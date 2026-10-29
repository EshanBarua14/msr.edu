using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;

namespace MsrEdu.Api.Endpoints;

/// <summary>
/// Public offers, checkout, the learner's orders, membership and receipts, and gateway webhooks.
/// The return page polls <c>/api/orders/{reference}</c>; only the webhook changes payment status.
/// </summary>
public static class PaymentEndpoints
{
    public record PlanOffer(Guid Id, string Slug, string Name, string? Description, Guid ProductId, long PriceMinor, long RegularMinor,
        string Currency, string BillingPeriod, int? PeriodDays, bool AllCourses, IReadOnlyList<string> Courses, IReadOnlyList<string> Categories);
    public record CheckoutRequest(Guid ProductId);
    public record OrderLine(string Name, string Kind, long AmountMinor, string? CourseSlug);
    public record OrderView(string Reference, string Status, long TotalMinor, string Currency, DateTimeOffset CreatedAt,
        DateTimeOffset? PaidAt, DateTimeOffset? ExpiresAt, IReadOnlyList<OrderLine> Items, string? ReceiptNumber, long RefundedMinor);
    public record SubscriptionView(Guid Id, string PlanName, string Status, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd, DateTimeOffset? GraceUntil);
    public record AccessView(Guid Id, string Kind, string Title, string? CourseSlug, string Source, string Status, string? StatusReason,
        DateTimeOffset StartsAt, DateTimeOffset? ExpiresAt);
    public record Membership(IReadOnlyList<SubscriptionView> Subscriptions, IReadOnlyList<AccessView> Access);
    public record ReceiptView(string Number, DateTimeOffset IssuedAt, string OrderReference, string? CustomerName, string CustomerEmail,
        IReadOnlyList<OrderLine> Items, long TotalMinor, string Currency, string Provider, string? ProviderTxnId, DateTimeOffset? PaidAt,
        string OrderStatus, long RefundedMinor);
    public record SandboxPayment(string ProviderTxnId, string OrderReference, string Description, long AmountMinor, string Currency, string Status);
    public record SandboxOutcome(string Outcome);
    public record PaymentsStatus(bool Enabled, string? Gateway, string? GatewayName, bool IsSandbox);

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api");

        g.MapGet("/payments/status", (PaymentGateways gateways) =>
            gateways.Default is { } d ? new PaymentsStatus(true, d.Key, d.DisplayName, d.IsSandbox) : new PaymentsStatus(false, null, null, false));

        g.MapGet("/plans", async (AppDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            var plans = await db.SubscriptionPlans.AsNoTracking()
                .Where(p => p.Status == SaleStatus.Active && (p.AvailableFrom == null || p.AvailableFrom <= now) && (p.AvailableUntil == null || p.AvailableUntil > now))
                .OrderBy(p => p.Position).ThenBy(p => p.PriceMinor).ToListAsync();
            var ids = plans.Select(p => p.Id).ToList();
            var products = await db.Products.AsNoTracking().Where(p => p.PlanId != null && ids.Contains(p.PlanId.Value) && p.Status == SaleStatus.Active)
                .ToDictionaryAsync(p => p.PlanId!.Value);
            var courses = await (from pc in db.PlanCourses join c in db.Courses on pc.CourseId equals c.Id
                                 where ids.Contains(pc.PlanId) && c.Status == PublishStatus.Published
                                 select new { pc.PlanId, c.Title }).ToListAsync();
            var categories = await (from pc in db.PlanCategories join c in db.Categories on pc.CategoryId equals c.Id
                                    where ids.Contains(pc.PlanId) select new { pc.PlanId, c.Name }).ToListAsync();
            return plans.Where(p => products.ContainsKey(p.Id)).Select(p =>
            {
                var product = products[p.Id];
                return new PlanOffer(p.Id, p.Slug, p.Name, p.Description, product.Id, product.CurrentPriceMinor(now), product.PriceMinor,
                    product.Currency, Label(p.BillingPeriod), p.PeriodDays, p.AllCourses,
                    courses.Where(c => c.PlanId == p.Id).Select(c => c.Title).ToList(),
                    categories.Where(c => c.PlanId == p.Id).Select(c => c.Name).ToList());
            }).ToList();
        });

        var me = g.MapGroup("").RequireAuthorization();

        me.MapPost("/checkout", async (CheckoutRequest req, ICurrentUser user, PaymentService payments) =>
        {
            try { return Results.Ok(await payments.CheckoutAsync(user.Id!.Value, req.ProductId)); }
            catch (PaymentException e) { return Results.Problem(e.Message, statusCode: e.StatusCode); }
        });

        me.MapGet("/orders/{reference}", async (string reference, AppDbContext db, ICurrentUser user, PaymentService payments) =>
        {
            await payments.ExpireStaleOrdersAsync();
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Reference == reference && o.UserId == user.Id);
            return order is null ? Results.NotFound() : Results.Ok((await ToViewsAsync(db, [order]))[0]);
        });

        me.MapGet("/me/orders", async (AppDbContext db, ICurrentUser user, PaymentService payments) =>
        {
            await payments.ExpireStaleOrdersAsync();
            var orders = await db.Orders.AsNoTracking().Where(o => o.UserId == user.Id).OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync();
            return await ToViewsAsync(db, orders);
        });

        me.MapGet("/me/membership", async (AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            var subs = await (from s in db.Subscriptions join p in db.SubscriptionPlans on s.PlanId equals p.Id
                              where s.UserId == user.Id orderby s.CurrentPeriodEnd descending
                              select new { s, p.Name }).AsNoTracking().ToListAsync();
            var access = await (from e in db.Entitlements
                                join c in db.Courses on e.CourseId equals c.Id into cs from c in cs.DefaultIfEmpty()
                                join p in db.SubscriptionPlans on e.PlanId equals p.Id into ps from p in ps.DefaultIfEmpty()
                                where e.UserId == user.Id orderby e.StartsAt descending
                                select new { e, Course = c, PlanName = p != null ? p.Name : null }).AsNoTracking().ToListAsync();
            return new Membership(
                subs.Select(x => new SubscriptionView(x.s.Id, x.Name, SubscriptionStatusAt(x.s, now), x.s.CurrentPeriodStart, x.s.CurrentPeriodEnd, x.s.GraceUntil)).ToList(),
                access.Select(x => new AccessView(x.e.Id, x.e.CourseId is null ? "plan" : "course", x.Course?.Title ?? x.PlanName ?? "",
                    x.Course?.Slug, Snake(x.e.Source.ToString()), EntitlementStatusAt(x.e, now), x.e.StatusReason, x.e.StartsAt, x.e.ExpiresAt)).ToList());
        });

        me.MapGet("/receipts/{number}", async (string number, AppDbContext db, ICurrentUser user) =>
        {
            var receipt = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.ReceiptNumber == number);
            if (receipt is null) return Results.NotFound();
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == receipt.OrderId);
            if (order.UserId != user.Id && !user.HasRole(RoleKeys.FinanceManager) && !user.HasRole(RoleKeys.SupportStaff)) return Results.NotFound();
            var customer = await db.Users.AsNoTracking().SingleAsync(u => u.Id == order.UserId);
            var txn = await db.PaymentTransactions.AsNoTracking().SingleAsync(t => t.Id == receipt.TransactionId);
            var view = (await ToViewsAsync(db, [order]))[0];
            return Results.Ok(new ReceiptView(receipt.ReceiptNumber, receipt.IssuedAt, order.Reference, customer.FullName, customer.Email,
                view.Items, order.TotalMinor, order.Currency, txn.Provider, txn.ProviderTxnId, order.PaidAt, view.Status, view.RefundedMinor));
        });

        // ------------------------------------------------------------ gateways
        // Webhooks are exempt from the CSRF header (providers can't send it) and authenticated by signature instead.
        g.MapPost("/webhooks/{provider}", async (string provider, HttpRequest request, PaymentService payments) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            return await payments.HandleWebhookAsync(provider, body, request.Headers) switch
            {
                WebhookOutcome.UnknownProvider => Results.NotFound(),
                WebhookOutcome.InvalidSignature => Results.Unauthorized(),
                _ => Results.Ok(new { received = true }),
            };
        });

        // The sandbox "provider's" payment page: the learner picks an outcome, the sandbox signs a webhook,
        // and it goes through exactly the same verification as a real gateway's would.
        me.MapGet("/sandbox/payments/{id}", async (string id, AppDbContext db, ICurrentUser user, PaymentGateways gateways) =>
        {
            if (gateways.Find("sandbox") is null) return Results.NotFound();
            var row = await SandboxRowAsync(db, id, user.Id!.Value);
            return row is null ? Results.NotFound()
                : Results.Ok(new SandboxPayment(id, row.Reference, row.Name, row.Txn.AmountMinor, row.Txn.Currency, Snake(row.Txn.Status.ToString())));
        });

        me.MapPost("/sandbox/payments/{id}", async (string id, SandboxOutcome req, AppDbContext db, ICurrentUser user, PaymentGateways gateways, PaymentService payments) =>
        {
            if (gateways.Find("sandbox") is not SandboxGateway sandbox) return Results.NotFound();
            var row = await SandboxRowAsync(db, id, user.Id!.Value);
            if (row is null) return Results.NotFound();
            var outcome = req.Outcome switch { "success" => "succeeded", "fail" => "failed", "cancel" => "cancelled", _ => null };
            if (outcome is null) return Results.Problem("Outcome must be success, fail or cancel.", statusCode: StatusCodes.Status400BadRequest);
            var (body, signature) = sandbox.SignedEvent(id, outcome, row.Txn.AmountMinor, row.Txn.Currency);
            await payments.HandleWebhookAsync("sandbox", body, new HeaderDictionary { [SandboxGateway.SignatureHeader] = signature });
            return Results.Ok(new { reference = row.Reference });
        });
    }

    private record SandboxRow(PaymentTransaction Txn, string Reference, string Name);

    private static async Task<SandboxRow?> SandboxRowAsync(AppDbContext db, string providerTxnId, Guid userId) =>
        await (from t in db.PaymentTransactions join o in db.Orders on t.OrderId equals o.Id
               where t.Provider == "sandbox" && t.ProviderTxnId == providerTxnId && o.UserId == userId
               select new SandboxRow(t, o.Reference, o.Items.Select(i => i.Product!.Name).FirstOrDefault() ?? o.Reference))
            .AsNoTracking().FirstOrDefaultAsync();

    internal static async Task<List<OrderView>> ToViewsAsync(AppDbContext db, IReadOnlyList<Order> orders)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var items = await (from i in db.OrderItems join p in db.Products on i.ProductId equals p.Id
                           join c in db.Courses on p.CourseId equals c.Id into cs from c in cs.DefaultIfEmpty()
                           where ids.Contains(i.OrderId)
                           select new { i.OrderId, i.ProductSnapshot, i.LineTotalMinor, p.Kind, Slug = c != null ? c.Slug : null })
            .AsNoTracking().ToListAsync();
        var receipts = await db.Receipts.AsNoTracking().Where(r => ids.Contains(r.OrderId)).ToDictionaryAsync(r => r.OrderId, r => r.ReceiptNumber);
        var refunded = await db.Refunds.Where(r => ids.Contains(r.OrderId) && r.Status == RefundStatus.Completed)
            .GroupBy(r => r.OrderId).Select(g => new { g.Key, Total = g.Sum(r => r.AmountMinor) }).ToDictionaryAsync(x => x.Key, x => x.Total);
        return orders.Select(o => new OrderView(o.Reference, Snake(o.Status.ToString()), o.TotalMinor, o.Currency, o.CreatedAt, o.PaidAt, o.ExpiresAt,
            items.Where(i => i.OrderId == o.Id).Select(i => new OrderLine(
                i.ProductSnapshot.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                i.Kind == ProductKind.Course ? "course" : "subscription_plan", i.LineTotalMinor, i.Slug)).ToList(),
            receipts.GetValueOrDefault(o.Id), refunded.GetValueOrDefault(o.Id))).ToList();
    }

    public static string Label(BillingPeriod p) => p switch { BillingPeriod.Month => "month", BillingPeriod.Year => "year", _ => "custom_days" };

    /// <summary>PascalCase enum name to the snake_case the database and frontend use.</summary>
    public static string Snake(string name) =>
        string.Concat(name.Select((ch, i) => char.IsUpper(ch) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(ch) : ch.ToString()));

    public static string SubscriptionStatusAt(Subscription s, DateTimeOffset now) =>
        s.Status != SubscriptionStatus.Active ? Snake(s.Status.ToString())
        : s.CurrentPeriodEnd is { } end && end <= now ? (s.GraceUntil is { } grace && grace > now ? "grace" : "expired")
        : "active";

    public static string EntitlementStatusAt(Entitlement e, DateTimeOffset now) =>
        e.Status != EntitlementStatus.Active ? Snake(e.Status.ToString())
        : e.ExpiresAt is { } end && end <= now ? "expired"
        : e.StartsAt > now ? "scheduled" : "active";
}
