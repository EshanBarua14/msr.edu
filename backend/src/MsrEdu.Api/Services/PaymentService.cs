using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Services;

public class PaymentException(string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public enum WebhookOutcome { Processed, Ignored, Duplicate, InvalidSignature, UnknownProvider }

/// <summary>
/// Checkout, webhooks and refunds. Prices come from the database, never the browser; access is granted
/// only after the gateway's webhook is authenticated and matches the transaction's amount and currency.
/// </summary>
public class PaymentService(
    AppDbContext db, PaymentGateways gateways, EntitlementService entitlements, AccessService access,
    TimeProvider clock, ILogger<PaymentService> log)
{
    public record CheckoutResult(string Reference, string RedirectUrl);

    public async Task<CheckoutResult> CheckoutAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        var gateway = gateways.Default ?? throw new PaymentException("Payments are not set up yet.", StatusCodes.Status503ServiceUnavailable);
        var now = clock.GetUtcNow();
        await ExpireStaleOrdersAsync(ct);

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.Status == SaleStatus.Active, ct)
            ?? throw new PaymentException("This item is not on sale.", StatusCodes.Status404NotFound);
        SubscriptionPlan? plan = null;
        if (product.Kind == ProductKind.Course)
        {
            var course = await db.Courses.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == product.CourseId && c.Status == PublishStatus.Published, ct)
                ?? throw new PaymentException("This item is not on sale.", StatusCodes.Status404NotFound);
            if (course.IsFree) throw new PaymentException("This course is free. Sign in to start learning.", StatusCodes.Status409Conflict);
            if (await access.HasCourseAccessAsync(userId, course.Id, ct))
                throw new PaymentException("You already have access to this course.", StatusCodes.Status409Conflict);
        }
        else
        {
            plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.PlanId
                && p.Status == SaleStatus.Active
                && (p.AvailableFrom == null || p.AvailableFrom <= now) && (p.AvailableUntil == null || p.AvailableUntil > now), ct)
                ?? throw new PaymentException("This plan is not on sale.", StatusCodes.Status404NotFound);
        }

        var price = product.CurrentPriceMinor(now);
        if (price <= 0) throw new PaymentException("This item has no price yet.", StatusCodes.Status409Conflict);
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(ct);
        var expiryMinutes = await Settings.GetAsync(db, "order.expiry_minutes", 60, ct);

        var order = new Order
        {
            UserId = userId, Currency = product.Currency, SubtotalMinor = price, TotalMinor = price,
            Status = PaymentState.Pending, ExpiresAt = now.AddMinutes(expiryMinutes), CreatedAt = now, UpdatedAt = now,
        };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id, UnitPriceMinor = price, LineTotalMinor = price, Currency = product.Currency, CreatedAt = now,
            // What was sold, frozen: later price or plan edits never change past orders.
            ProductSnapshot = JsonSerializer.SerializeToDocument(new
            {
                name = product.Name,
                kind = product.Kind == ProductKind.Course ? "course" : "subscription_plan",
                course_id = product.CourseId,
                plan_id = product.PlanId,
                regular_price_minor = product.PriceMinor,
                charged_minor = price,
                access_days = product.AccessDays,
                billing_period = plan?.BillingPeriod.ToString().ToLowerInvariant(),
                period_days = plan?.PeriodDays,
            }),
        });
        var txn = new PaymentTransaction
        {
            OrderId = order.Id, Provider = gateway.Key, Method = gateway.Method, AmountMinor = price,
            Currency = product.Currency, Status = PaymentState.Pending, CreatedAt = now, UpdatedAt = now,
        };
        db.Orders.Add(order);
        db.PaymentTransactions.Add(txn);
        await db.SaveChangesAsync(ct);

        try
        {
            var payment = await gateway.CreatePaymentAsync(new PaymentRequest(txn.Id, order.Reference, price, product.Currency, product.Name, email), ct);
            txn.ProviderTxnId = payment.ProviderTxnId;
            txn.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new CheckoutResult(order.Reference, payment.RedirectUrl);
        }
        catch (Exception e) when (e is GatewayException or HttpRequestException)
        {
            log.LogWarning(e, "Gateway {Gateway} refused order {Reference}", gateway.Key, order.Reference);
            txn.Status = order.Status = PaymentState.Failed;
            txn.FailureReason = e.Message;
            await db.SaveChangesAsync(ct);
            throw new PaymentException("The payment provider is not responding. Try again in a few minutes.", StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Unpaid orders past their expiry stop being payable (a late verified payment is still honoured).</summary>
    public async Task<int> ExpireStaleOrdersAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            with expired as (
              update orders set status = 'expired', updated_at = {now}
               where status in ('created', 'pending') and expires_at < {now}
              returning id)
            update payment_transactions set status = 'expired', updated_at = {now}
             where order_id in (select id from expired) and status in ('created', 'pending')
            """, ct);
    }

    /// <summary>
    /// Idempotent: each provider event is processed once, under a row lock, so retries and duplicate deliveries
    /// can't double-grant. An exception rolls everything back and leaves the event for the provider's retry.
    /// </summary>
    public async Task<WebhookOutcome> HandleWebhookAsync(string provider, string body, IHeaderDictionary headers, CancellationToken ct = default)
    {
        if (gateways.Find(provider) is not { } gateway) return WebhookOutcome.UnknownProvider;
        var now = clock.GetUtcNow();

        GatewayEvent? evt;
        try { evt = await gateway.VerifyWebhookAsync(body, headers, ct); }
        catch (Exception e) when (e is FormatException or JsonException or KeyNotFoundException or InvalidOperationException) { evt = null; }
        if (evt is null)
        {
            log.LogWarning("Rejected {Provider} webhook: signature or payload invalid", provider);
            db.PaymentEvents.Add(new PaymentEvent
            {
                Provider = provider, ProviderEventId = $"invalid_{Guid.CreateVersion7():N}", EventType = "invalid",
                SignatureValid = false, PayloadRedacted = PaymentGateways.Redact(body), ReceivedAt = now, ProcessedAt = now,
                ProcessingError = "signature or payload invalid",
            });
            await db.SaveChangesAsync(ct);
            return WebhookOutcome.InvalidSignature;
        }

        var payload = evt.Redacted.RootElement.GetRawText();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into payment_events (provider, provider_event_id, event_type, signature_valid, payload_redacted, received_at, attempts)
            values ({provider}, {evt.EventId}, {evt.EventType}, true, {payload}::jsonb, {now}, 0)
            on conflict (provider, provider_event_id) do nothing
            """, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var stored = await db.PaymentEvents
            .FromSql($"select * from payment_events where provider = {provider} and provider_event_id = {evt.EventId} for update")
            .AsTracking().SingleAsync(ct);
        if (stored.ProcessedAt is not null) return WebhookOutcome.Duplicate;
        stored.Attempts++;

        var txn = await db.PaymentTransactions
            .FromSql($"select * from payment_transactions where provider = {provider} and provider_txn_id = {evt.ProviderTxnId} for update")
            .AsTracking().SingleOrDefaultAsync(ct);
        string? error;
        if (txn is null) error = "unknown transaction";
        else
        {
            stored.TransactionId = txn.Id;
            error = await ApplyAsync(txn, evt, now, ct);
        }
        stored.ProcessedAt = now;
        stored.ProcessingError = error;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (error is not null) log.LogWarning("{Provider} event {EventId} not applied: {Error}", provider, evt.EventId, error);
        return error is null ? WebhookOutcome.Processed : WebhookOutcome.Ignored;
    }

    private async Task<string?> ApplyAsync(PaymentTransaction txn, GatewayEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        var order = await db.Orders.FromSql($"select * from orders where id = {txn.OrderId} for update").AsTracking().SingleAsync(ct);

        if (evt.AmountMinor != txn.AmountMinor || !string.Equals(evt.Currency, txn.Currency, StringComparison.OrdinalIgnoreCase))
        {
            Reconcile(order, txn, "amount_mismatch", new { expected_minor = txn.AmountMinor, expected_currency = txn.Currency, received_minor = evt.AmountMinor, received_currency = evt.Currency });
            return "amount or currency does not match the transaction";
        }
        if (txn.Status == evt.Outcome) return null;
        if (!PaymentStateMachine.CanTransition(txn.Status, evt.Outcome))
        {
            // Money taken for a payment we already closed needs a person to look at it.
            if (evt.Outcome == PaymentState.Successful)
                Reconcile(order, txn, "paid_after_close", new { transaction_status = txn.Status.ToString(), event_id = evt.EventId });
            return $"transaction is {txn.Status}; {evt.Outcome} ignored";
        }

        if (evt.Outcome == PaymentState.Successful && order.Status == PaymentState.Expired)
            Reconcile(order, txn, "paid_after_expiry", new { event_id = evt.EventId });
        if (!PaymentStateMachine.CanTransition(order.Status, evt.Outcome))
        {
            if (evt.Outcome == PaymentState.Successful)
                Reconcile(order, txn, "paid_after_close", new { order_status = order.Status.ToString(), event_id = evt.EventId });
            return $"order is {order.Status}; {evt.Outcome} ignored";
        }

        txn.Status = order.Status = evt.Outcome;
        txn.FailureReason = evt.FailureReason;
        txn.UpdatedAt = order.UpdatedAt = now;
        if (evt.Outcome != PaymentState.Successful)
        {
            await db.SaveChangesAsync(ct);
            return null;
        }

        txn.VerifiedAt = now;
        order.PaidAt = now;
        await db.SaveChangesAsync(ct);
        await entitlements.GrantForOrderAsync(order.Id, ct);
        if (!await db.Receipts.AnyAsync(r => r.OrderId == order.Id, ct))
            db.Receipts.Add(new Receipt { OrderId = order.Id, TransactionId = txn.Id, IssuedAt = now });
        return null;
    }

    private void Reconcile(Order order, PaymentTransaction txn, string issue, object details) =>
        db.ReconciliationRecords.Add(new ReconciliationRecord
        {
            OrderId = order.Id, TransactionId = txn.Id, Issue = issue, Details = JsonSerializer.SerializeToDocument(details),
        });

    /// <summary>
    /// Refunds through the gateway (or records one made outside it when <paramref name="manual"/>). The refund row is
    /// committed before the gateway call so a crash can't lose a refund the provider already made.
    /// A full refund applies the refund.access_policy setting to the access the order granted.
    /// </summary>
    public async Task<Refund> RefundAsync(Guid orderId, long amountMinor, string reason, bool manual, Guid staffId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new PaymentException("Give a reason for the refund.");
        var now = clock.GetUtcNow();
        Refund refund;
        PaymentState before;
        PaymentTransaction txn;
        long alreadyRefunded;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var order = await db.Orders.FromSql($"select * from orders where id = {orderId} for update").AsTracking().SingleOrDefaultAsync(ct)
                ?? throw new PaymentException("Order not found.", StatusCodes.Status404NotFound);
            txn = await db.PaymentTransactions.FromSql($"""
                    select * from payment_transactions where order_id = {orderId} and verified_at is not null
                       and status in ('successful', 'partially_refunded') for update
                    """).AsTracking().SingleOrDefaultAsync(ct)
                ?? throw new PaymentException("Only paid orders without a refund in progress can be refunded.", StatusCodes.Status409Conflict);
            alreadyRefunded = await db.Refunds.Where(r => r.TransactionId == txn.Id && r.Status == RefundStatus.Completed)
                .SumAsync(r => (long?)r.AmountMinor, ct) ?? 0;
            if (amountMinor <= 0 || amountMinor > txn.AmountMinor - alreadyRefunded)
                throw new PaymentException($"Refund between 1 and {txn.AmountMinor - alreadyRefunded} (minor units).");
            if (!manual && gateways.Find(txn.Provider) is null)
                throw new PaymentException($"The {txn.Provider} gateway is not available. Refund outside the app and record it as manual.", StatusCodes.Status409Conflict);

            before = txn.Status;
            refund = new Refund
            {
                OrderId = orderId, TransactionId = txn.Id, AmountMinor = amountMinor, Currency = txn.Currency, Reason = reason.Trim(),
                Status = RefundStatus.Processing, IsManual = manual, RequestedBy = staffId, ApprovedBy = staffId, ApprovedAt = now,
                CreatedAt = now, UpdatedAt = now,
            };
            db.Refunds.Add(refund);
            txn.Status = order.Status = PaymentState.RefundPending;
            txn.UpdatedAt = order.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        string? providerRefundId = null;
        string? failure = null;
        if (!manual)
        {
            try { providerRefundId = await gateways.Find(txn.Provider)!.RefundAsync(txn.ProviderTxnId!, amountMinor, txn.Currency, reason, ct); }
            catch (Exception e) when (e is GatewayException or HttpRequestException) { failure = e.Message; }
        }

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var order = await db.Orders.FromSql($"select * from orders where id = {orderId} for update").AsTracking().SingleAsync(ct);
            now = clock.GetUtcNow();
            var full = failure is null && alreadyRefunded + amountMinor == txn.AmountMinor;
            var after = failure is not null ? before : full ? PaymentState.Refunded : PaymentState.PartiallyRefunded;
            refund.Status = failure is null ? RefundStatus.Completed : RefundStatus.Failed;
            refund.ProviderRefundId = providerRefundId;
            refund.CompletedAt = failure is null ? now : null;
            refund.UpdatedAt = now;
            txn.Status = order.Status = after;
            txn.UpdatedAt = order.UpdatedAt = now;
            if (full) await ApplyRefundAccessPolicyAsync(order, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (failure is not null) throw new PaymentException($"The gateway refused the refund: {failure}", StatusCodes.Status502BadGateway);
        return refund;
    }

    private async Task ApplyRefundAccessPolicyAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        var policy = await Settings.GetAsync(db, "refund.access_policy", "revoke_immediately", ct);
        var days = policy == "revoke_after_days" ? await Settings.GetAsync(db, "refund.revoke_after_days", 0, ct) : 0;
        var itemIds = await db.OrderItems.Where(i => i.OrderId == order.Id).Select(i => i.Id).ToListAsync(ct);
        var granted = await db.Entitlements
            .Where(e => e.SourceOrderItemId != null && itemIds.Contains(e.SourceOrderItemId.Value) && e.Status == EntitlementStatus.Active)
            .ToListAsync(ct);
        foreach (var e in granted)
        {
            var cutoff = now.AddDays(days);
            if (days > 0 && cutoff > e.StartsAt)
            {
                if (e.ExpiresAt is null || e.ExpiresAt > cutoff) e.ExpiresAt = cutoff;
                e.StatusReason = $"refunded; access ends {cutoff:yyyy-MM-dd}";
            }
            else
            {
                e.Status = EntitlementStatus.Revoked;
                e.StatusReason = "refunded";
            }
            e.UpdatedAt = now;
        }
        if (days == 0)
            foreach (var s in await db.Subscriptions.Where(s => s.SourceOrderId == order.Id && s.Status != SubscriptionStatus.Cancelled).ToListAsync(ct))
            {
                s.Status = SubscriptionStatus.Cancelled;
                s.UpdatedAt = now;
            }
    }
}

/// <summary>Reads business settings from app_settings (JSON values), with a fallback when unset.</summary>
public static class Settings
{
    public static async Task<T> GetAsync<T>(AppDbContext db, string key, T fallback, CancellationToken ct = default)
    {
        var value = await db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (value is null || value.RootElement.ValueKind == JsonValueKind.Null) return fallback;
        try { return value.RootElement.Deserialize<T>() ?? fallback; }
        catch (JsonException) { return fallback; }
    }
}
