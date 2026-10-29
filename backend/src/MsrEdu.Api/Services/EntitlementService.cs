using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Services;

public class EntitlementException(string message) : Exception(message);

public class EntitlementService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Grants access for a paid order. Requires a successful order with a verified transaction
    /// matching its amount and currency. Idempotent: replaying it (duplicate webhooks, retries)
    /// never creates a second entitlement. Returns the number of entitlements created.
    /// </summary>
    public async Task<int> GrantForOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var ownTx = db.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await db.Database.BeginTransactionAsync(ct) : null;

        // Row lock serialises concurrent grants for the same order.
        var order = await db.Orders.FromSql($"select * from orders where id = {orderId} for update")
            .AsTracking().SingleOrDefaultAsync(ct)
            ?? throw new EntitlementException($"order {orderId} not found");

        if (order.Status != PaymentState.Successful)
            throw new EntitlementException($"order {order.Reference} is {order.Status}; entitlements require a successful verified payment");

        var verified = await db.PaymentTransactions.AnyAsync(t =>
            t.OrderId == orderId && t.Status == PaymentState.Successful && t.VerifiedAt != null
            && t.AmountMinor == order.TotalMinor && t.Currency == order.Currency, ct);
        if (!verified)
            throw new EntitlementException($"order {order.Reference} has no verified transaction matching amount and currency");

        var items = await db.OrderItems.Where(i => i.OrderId == orderId).Include(i => i.Product).ToListAsync(ct);
        var granted = await db.Entitlements.Where(e => e.SourceOrderItemId != null && items.Select(i => i.Id).Contains(e.SourceOrderItemId.Value))
            .Select(e => e.SourceOrderItemId!.Value).ToListAsync(ct);

        var now = clock.GetUtcNow();
        var created = 0;
        foreach (var item in items.Where(i => !granted.Contains(i.Id)))
        {
            var product = item.Product!;
            if (product.Kind == ProductKind.Course)
            {
                db.Entitlements.Add(new Entitlement
                {
                    UserId = order.UserId,
                    CourseId = product.CourseId,
                    Source = EntitlementSource.Purchase,
                    SourceOrderItemId = item.Id,
                    StartsAt = now,
                    ExpiresAt = product.AccessDays is { } days ? now.AddDays(days) : null,
                });
            }
            else
            {
                var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == product.PlanId, ct);
                // Renewals extend from the end of the latest active period.
                var latestEnd = await db.Subscriptions
                    .Where(s => s.UserId == order.UserId && s.PlanId == plan.Id
                        && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace))
                    .MaxAsync(s => s.CurrentPeriodEnd, ct);
                var start = latestEnd is { } end && end > now ? end : now;
                var periodEnd = plan.BillingPeriod switch
                {
                    BillingPeriod.Month => start.AddMonths(1),
                    BillingPeriod.Year => start.AddYears(1),
                    _ => start.AddDays(plan.PeriodDays!.Value),
                };
                var subscription = new Subscription
                {
                    UserId = order.UserId,
                    PlanId = plan.Id,
                    Status = SubscriptionStatus.Active,
                    CurrentPeriodStart = start,
                    CurrentPeriodEnd = periodEnd,
                    GraceUntil = periodEnd.AddDays(plan.GraceDays),
                    SourceOrderId = order.Id,
                };
                db.Subscriptions.Add(subscription);
                db.Entitlements.Add(new Entitlement
                {
                    UserId = order.UserId,
                    PlanId = plan.Id,
                    Source = EntitlementSource.Subscription,
                    SourceOrderItemId = item.Id,
                    SubscriptionId = subscription.Id,
                    StartsAt = start,
                    ExpiresAt = periodEnd.AddDays(plan.GraceDays),
                });
            }
            created++;
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return created;
    }
}
