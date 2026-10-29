using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.PaymentEndpoints;

namespace MsrEdu.Api.Endpoints;

/// <summary>
/// Admin commerce: course prices, subscription plans, orders, refunds and manual access. Changing a price never
/// touches past orders (their items keep a snapshot). Financial rows are never deleted.
/// </summary>
public static partial class FinanceEndpoints
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public record CoursePrice(Guid CourseId, string Title, string Slug, string CourseStatus, bool IsFree, Guid? ProductId, long? PriceMinor,
        long? PromoPriceMinor, DateTimeOffset? PromoStartsAt, DateTimeOffset? PromoEndsAt, int? AccessDays, string? SaleStatus, string Currency);
    public record PriceRequest(long PriceMinor, long? PromoPriceMinor, DateTimeOffset? PromoStartsAt, DateTimeOffset? PromoEndsAt, int? AccessDays, string Status);

    public record PlanEditor(Guid Id, string Slug, string Name, string? Description, long PriceMinor, string Currency, string BillingPeriod,
        int? PeriodDays, int GraceDays, bool AllCourses, IReadOnlyList<Guid> CourseIds, IReadOnlyList<Guid> CategoryIds, string Status,
        int Position, int ActiveSubscribers);
    public record PlanRequest(string Slug, string Name, string? Description, long PriceMinor, string BillingPeriod, int? PeriodDays,
        int GraceDays, bool AllCourses, IReadOnlyList<Guid> CourseIds, IReadOnlyList<Guid> CategoryIds, string Status, int Position);

    public record OrderRow(Guid Id, string Reference, string UserEmail, string Status, long TotalMinor, string Currency,
        DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, string Items, string? Provider);
    public record TxnRow(Guid Id, string Provider, string Method, string? ProviderTxnId, long AmountMinor, string Currency, string Status,
        string? FailureReason, DateTimeOffset? VerifiedAt, DateTimeOffset CreatedAt);
    public record EventRow(long Id, string Provider, string ProviderEventId, string EventType, bool? SignatureValid,
        DateTimeOffset ReceivedAt, DateTimeOffset? ProcessedAt, string? ProcessingError, JsonElement? Payload);
    public record RefundRow(Guid Id, long AmountMinor, string Currency, string Reason, string Status, bool IsManual, string? ProviderRefundId,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
    public record IssueRow(Guid Id, string Issue, string Status, DateTimeOffset CreatedAt);
    public record OrderDetail(Guid Id, string UserEmail, Guid UserId, OrderView Order, IReadOnlyList<TxnRow> Transactions,
        IReadOnlyList<EventRow> Events, IReadOnlyList<RefundRow> Refunds, IReadOnlyList<AccessView> Access, IReadOnlyList<IssueRow> Issues);
    public record RefundRequest(long AmountMinor, string Reason, bool Manual);

    public record AdminEntitlement(Guid Id, Guid UserId, string UserEmail, string Kind, string Title, string Source, string Status,
        string? StatusReason, string? GrantReason, DateTimeOffset StartsAt, DateTimeOffset? ExpiresAt);
    public record GrantRequest(string Email, Guid? CourseId, Guid? PlanId, DateTimeOffset? ExpiresAt, string Reason);
    public record RevokeRequest(string Reason);

    private static IResult Bad(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var finance = app.MapGroup("/api/admin").RequireAuthorization(Policies.Finance);
        var support = app.MapGroup("/api/admin").RequireAuthorization(Policies.Support);

        // ------------------------------------------------------------ course pricing
        finance.MapGet("/pricing", async (AppDbContext db) =>
        {
            var currency = await Settings.GetAsync(db, "currency.default", "BDT");
            return await (from c in db.Courses
                          join p in db.Products on c.Id equals p.CourseId into ps from p in ps.DefaultIfEmpty()
                          where c.Status != PublishStatus.Archived
                          orderby c.Position, c.Title
                          select new CoursePrice(c.Id, c.Title, c.Slug, c.Status.ToString().ToLower(), c.IsFree, p != null ? p.Id : null,
                              p != null ? p.PriceMinor : null, p != null ? p.PromoPriceMinor : null, p != null ? p.PromoStartsAt : null,
                              p != null ? p.PromoEndsAt : null, p != null ? p.AccessDays : null, p != null ? p.Status.ToString().ToLower() : null,
                              p != null ? p.Currency : currency)).AsNoTracking().ToListAsync();
        });

        finance.MapPut("/courses/{id:guid}/price", async (Guid id, PriceRequest req, AppDbContext db, TimeProvider clock) =>
        {
            var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (course is null) return Results.NotFound();
            if (ParseSale(req.Status) is not { } status) return Bad("Status must be active, inactive or archived.");
            if (req.PriceMinor <= 0) return Bad("Price must be more than zero. Mark the course free instead.");
            if (req.PromoPriceMinor is { } promo && (promo < 0 || promo >= req.PriceMinor)) return Bad("The promotional price must be below the regular price.");
            if (req.PromoStartsAt is { } a && req.PromoEndsAt is { } b && b <= a) return Bad("The promotion must end after it starts.");
            if (req.AccessDays is <= 0) return Bad("Access days must be positive, or empty for lifetime access.");

            var product = await db.Products.FirstOrDefaultAsync(p => p.CourseId == id);
            if (product is null)
            {
                product = new Product { Kind = ProductKind.Course, CourseId = id, Name = course.Title, Currency = await Settings.GetAsync(db, "currency.default", "BDT") };
                db.Products.Add(product);
            }
            (product.Name, product.PriceMinor, product.PromoPriceMinor, product.PromoStartsAt, product.PromoEndsAt, product.AccessDays, product.Status) =
                (course.Title, req.PriceMinor, req.PromoPriceMinor, req.PromoPriceMinor is null ? null : req.PromoStartsAt,
                 req.PromoPriceMinor is null ? null : req.PromoEndsAt, req.AccessDays, status);
            product.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
            return Results.Ok(new { product.Id });
        });

        // ------------------------------------------------------------ subscription plans
        finance.MapGet("/plans", async (AppDbContext db) => await PlansAsync(db));

        finance.MapPost("/plans", async (PlanRequest req, AppDbContext db, TimeProvider clock) =>
            await SavePlanAsync(db, clock, null, req));
        finance.MapPut("/plans/{id:guid}", async (Guid id, PlanRequest req, AppDbContext db, TimeProvider clock) =>
            await SavePlanAsync(db, clock, id, req));

        // ------------------------------------------------------------ orders and refunds
        support.MapGet("/orders", async (string? status, string? q, AppDbContext db, PaymentService payments) =>
        {
            await payments.ExpireStaleOrdersAsync();
            var query = from o in db.Orders join u in db.Users on o.UserId equals u.Id select new { o, u.Email };
            if (!string.IsNullOrWhiteSpace(status))
            {
                var states = status.Split(',').Select(s => Enum.TryParse<PaymentState>(s.Replace("_", ""), true, out var st) ? st : (PaymentState?)null)
                    .OfType<PaymentState>().ToList();
                query = query.Where(x => states.Contains(x.o.Status));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(x => x.o.Reference.ToLower().Contains(term) || x.Email.ToLower().Contains(term));
            }
            var rows = await query.OrderByDescending(x => x.o.CreatedAt).Take(200).AsNoTracking().ToListAsync();
            var ids = rows.Select(r => r.o.Id).ToList();
            var names = await db.OrderItems.Where(i => ids.Contains(i.OrderId)).Select(i => new { i.OrderId, Name = i.Product!.Name }).ToListAsync();
            var providers = await db.PaymentTransactions.Where(t => ids.Contains(t.OrderId)).Select(t => new { t.OrderId, t.Provider }).ToListAsync();
            return rows.Select(r => new OrderRow(r.o.Id, r.o.Reference, r.Email, Snake(r.o.Status.ToString()), r.o.TotalMinor, r.o.Currency,
                r.o.CreatedAt, r.o.PaidAt, string.Join(", ", names.Where(n => n.OrderId == r.o.Id).Select(n => n.Name)),
                providers.FirstOrDefault(p => p.OrderId == r.o.Id)?.Provider)).ToList();
        });

        support.MapGet("/orders/{id:guid}", async (Guid id, AppDbContext db, TimeProvider clock) =>
        {
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
            if (order is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            var email = await db.Users.Where(u => u.Id == order.UserId).Select(u => u.Email).SingleAsync();
            var txns = await db.PaymentTransactions.AsNoTracking().Where(t => t.OrderId == id).OrderBy(t => t.CreatedAt).ToListAsync();
            var txnIds = txns.Select(t => t.Id).ToList();
            var events = await db.PaymentEvents.AsNoTracking().Where(e => e.TransactionId != null && txnIds.Contains(e.TransactionId.Value))
                .OrderBy(e => e.ReceivedAt).ToListAsync();
            var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == id).OrderBy(r => r.CreatedAt).ToListAsync();
            var itemIds = await db.OrderItems.Where(i => i.OrderId == id).Select(i => i.Id).ToListAsync();
            var access = await EntitlementsAsync(db, now, e => e.SourceOrderItemId != null && itemIds.Contains(e.SourceOrderItemId.Value));
            var issues = await db.ReconciliationRecords.AsNoTracking().Where(r => r.OrderId == id)
                .Select(r => new IssueRow(r.Id, r.Issue, r.Status.ToString().ToLower(), r.CreatedAt)).ToListAsync();
            return Results.Ok(new OrderDetail(order.Id, email, order.UserId, (await ToViewsAsync(db, [order]))[0],
                txns.Select(t => new TxnRow(t.Id, t.Provider, Snake(t.Method.ToString()), t.ProviderTxnId, t.AmountMinor, t.Currency,
                    Snake(t.Status.ToString()), t.FailureReason, t.VerifiedAt, t.CreatedAt)).ToList(),
                events.Select(e => new EventRow(e.Id, e.Provider, e.ProviderEventId, e.EventType, e.SignatureValid, e.ReceivedAt, e.ProcessedAt,
                    e.ProcessingError, e.PayloadRedacted?.RootElement.Clone())).ToList(),
                refunds.Select(ToRow).ToList(),
                access.Select(a => new AccessView(a.Id, a.Kind, a.Title, null, a.Source, a.Status, a.StatusReason, a.StartsAt, a.ExpiresAt)).ToList(),
                issues));
        });

        finance.MapPost("/orders/{id:guid}/refunds", async (Guid id, RefundRequest req, ICurrentUser user, PaymentService payments) =>
        {
            try { return Results.Ok(ToRow(await payments.RefundAsync(id, req.AmountMinor, req.Reason, req.Manual, user.Id!.Value))); }
            catch (PaymentException e) { return Results.Problem(e.Message, statusCode: e.StatusCode); }
        });

        // ------------------------------------------------------------ manual access
        support.MapGet("/entitlements", async (string? email, AppDbContext db, TimeProvider clock) =>
        {
            var term = email?.Trim().ToLower();
            var userIds = string.IsNullOrEmpty(term) ? null
                : await db.Users.Where(u => u.Email.ToLower().Contains(term)).Select(u => u.Id).ToListAsync();
            return await EntitlementsAsync(db, clock.GetUtcNow(), e => userIds == null || userIds.Contains(e.UserId), 200);
        });

        finance.MapPost("/entitlements", async (GrantRequest req, AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) return Bad("Give a reason for the grant.");
            if ((req.CourseId is null) == (req.PlanId is null)) return Bad("Pick one course or one plan.");
            var now = clock.GetUtcNow();
            if (req.ExpiresAt is { } exp && exp <= now) return Bad("The end date must be in the future.");
            var learner = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.Trim().ToLower());
            if (learner is null) return Bad("No account with that email. The learner must sign in once first.");
            if (req.CourseId is { } cid && !await db.Courses.AnyAsync(c => c.Id == cid)) return Bad("Unknown course.");
            if (req.PlanId is { } pid && !await db.SubscriptionPlans.AnyAsync(p => p.Id == pid)) return Bad("Unknown plan.");
            var e = new Entitlement
            {
                UserId = learner.Id, CourseId = req.CourseId, PlanId = req.PlanId, Source = EntitlementSource.ManualGrant,
                GrantedBy = user.Id, GrantReason = req.Reason.Trim(), StartsAt = now, ExpiresAt = req.ExpiresAt, CreatedAt = now, UpdatedAt = now,
            };
            db.Entitlements.Add(e);
            await db.SaveChangesAsync();
            return Results.Ok((await EntitlementsAsync(db, now, x => x.Id == e.Id))[0]);
        });

        finance.MapPost("/entitlements/{id:guid}/revoke", async (Guid id, RevokeRequest req, AppDbContext db, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) return Bad("Give a reason.");
            var e = await db.Entitlements.FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            if (e.Status == EntitlementStatus.Active)
            {
                (e.Status, e.StatusReason, e.UpdatedAt) = (EntitlementStatus.Revoked, req.Reason.Trim(), now);
                await db.SaveChangesAsync();
            }
            return Results.Ok((await EntitlementsAsync(db, now, x => x.Id == id))[0]);
        });
    }

    private static SaleStatus? ParseSale(string? s) =>
        Enum.TryParse<SaleStatus>(s, true, out var v) && Enum.IsDefined(v) ? v : null;

    private static RefundRow ToRow(Refund r) => new(r.Id, r.AmountMinor, r.Currency, r.Reason, Snake(r.Status.ToString()), r.IsManual,
        r.ProviderRefundId, r.CreatedAt, r.CompletedAt);

    private static async Task<List<AdminEntitlement>> EntitlementsAsync(AppDbContext db, DateTimeOffset now,
        System.Linq.Expressions.Expression<Func<Entitlement, bool>> filter, int take = 500)
    {
        var rows = await (from e in db.Entitlements.Where(filter)
                          join u in db.Users on e.UserId equals u.Id
                          join c in db.Courses on e.CourseId equals c.Id into cs from c in cs.DefaultIfEmpty()
                          join p in db.SubscriptionPlans on e.PlanId equals p.Id into ps from p in ps.DefaultIfEmpty()
                          orderby e.CreatedAt descending
                          select new { e, u.Email, Title = c != null ? c.Title : p != null ? p.Name : "" }).Take(take).AsNoTracking().ToListAsync();
        return rows.Select(r => new AdminEntitlement(r.e.Id, r.e.UserId, r.Email, r.e.CourseId is null ? "plan" : "course", r.Title,
            Snake(r.e.Source.ToString()), EntitlementStatusAt(r.e, now), r.e.StatusReason, r.e.GrantReason, r.e.StartsAt, r.e.ExpiresAt)).ToList();
    }

    private static async Task<List<PlanEditor>> PlansAsync(AppDbContext db, Guid? only = null)
    {
        var plans = await db.SubscriptionPlans.AsNoTracking().Where(p => only == null || p.Id == only)
            .OrderBy(p => p.Position).ThenBy(p => p.Name).ToListAsync();
        var courses = await db.PlanCourses.AsNoTracking().ToListAsync();
        var categories = await db.PlanCategories.AsNoTracking().ToListAsync();
        var subscribers = await db.Subscriptions.Where(s => s.Status == SubscriptionStatus.Active)
            .GroupBy(s => s.PlanId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return plans.Select(p => new PlanEditor(p.Id, p.Slug, p.Name, p.Description, p.PriceMinor, p.Currency, Label(p.BillingPeriod),
            p.PeriodDays, p.GraceDays, p.AllCourses, courses.Where(c => c.PlanId == p.Id).Select(c => c.CourseId).ToList(),
            categories.Where(c => c.PlanId == p.Id).Select(c => c.CategoryId).ToList(), p.Status.ToString().ToLower(), p.Position,
            subscribers.GetValueOrDefault(p.Id))).ToList();
    }

    private static async Task<IResult> SavePlanAsync(AppDbContext db, TimeProvider clock, Guid? id, PlanRequest req)
    {
        var slug = req.Slug?.Trim().ToLowerInvariant() ?? "";
        if (!SlugPattern().IsMatch(slug)) return Bad("Slug: lowercase letters, numbers and dashes.");
        if (string.IsNullOrWhiteSpace(req.Name)) return Bad("Name is required.");
        if (req.PriceMinor <= 0) return Bad("Price must be more than zero.");
        if (ParseSale(req.Status) is not { } status) return Bad("Status must be active, inactive or archived.");
        BillingPeriod? period = req.BillingPeriod switch
        {
            "month" => BillingPeriod.Month, "year" => BillingPeriod.Year, "custom_days" => BillingPeriod.CustomDays, _ => null,
        };
        if (period is null) return Bad("Billing period must be month, year or custom_days.");
        if (period == BillingPeriod.CustomDays && req.PeriodDays is not > 0) return Bad("Custom periods need a number of days.");
        if (req.GraceDays < 0) return Bad("Grace days can't be negative.");
        if (!req.AllCourses && req.CourseIds.Count == 0 && req.CategoryIds.Count == 0)
            return Bad("Include all courses, or pick courses or categories.");
        if (await db.SubscriptionPlans.AnyAsync(p => p.Slug == slug && p.Id != id))
            return Results.Problem("Another plan uses that slug.", statusCode: StatusCodes.Status409Conflict);
        if (req.CourseIds.Distinct().Count() != await db.Courses.CountAsync(c => req.CourseIds.Contains(c.Id))
            || req.CategoryIds.Distinct().Count() != await db.Categories.CountAsync(c => req.CategoryIds.Contains(c.Id)))
            return Bad("Unknown course or category.");

        var now = clock.GetUtcNow();
        SubscriptionPlan plan;
        Product product;
        if (id is null)
        {
            plan = new SubscriptionPlan { Slug = slug, Name = req.Name, Currency = await Settings.GetAsync(db, "currency.default", "BDT"), CreatedAt = now };
            product = new Product { Kind = ProductKind.SubscriptionPlan, PlanId = plan.Id, Name = req.Name, Currency = plan.Currency, CreatedAt = now };
            db.SubscriptionPlans.Add(plan);
            db.Products.Add(product);
        }
        else
        {
            var existing = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (existing is null) return Results.NotFound();
            plan = existing;
            product = await db.Products.SingleAsync(p => p.PlanId == id);
        }
        (plan.Slug, plan.Name, plan.Description, plan.PriceMinor, plan.BillingPeriod, plan.PeriodDays, plan.GraceDays, plan.AllCourses, plan.Status, plan.Position, plan.UpdatedAt) =
            (slug, req.Name.Trim(), req.Description?.Trim(), req.PriceMinor, period.Value,
             period == BillingPeriod.CustomDays ? req.PeriodDays : null, req.GraceDays, req.AllCourses, status, req.Position, now);
        // The plan's product is what checkout sells; keep it in step with the plan.
        (product.Name, product.PriceMinor, product.Status, product.UpdatedAt) = (plan.Name, plan.PriceMinor, status, now);
        var wantCourses = req.AllCourses ? [] : req.CourseIds.ToHashSet();
        var wantCategories = req.AllCourses ? [] : req.CategoryIds.ToHashSet();
        var haveCourses = await db.PlanCourses.Where(p => p.PlanId == plan.Id).ToListAsync();
        var haveCategories = await db.PlanCategories.Where(p => p.PlanId == plan.Id).ToListAsync();
        db.PlanCourses.RemoveRange(haveCourses.Where(p => !wantCourses.Contains(p.CourseId)));
        db.PlanCourses.AddRange(wantCourses.Except(haveCourses.Select(p => p.CourseId)).Select(c => new PlanCourse { PlanId = plan.Id, CourseId = c }));
        db.PlanCategories.RemoveRange(haveCategories.Where(p => !wantCategories.Contains(p.CategoryId)));
        db.PlanCategories.AddRange(wantCategories.Except(haveCategories.Select(p => p.CategoryId)).Select(c => new PlanCategory { PlanId = plan.Id, CategoryId = c }));
        await db.SaveChangesAsync();
        return Results.Ok((await PlansAsync(db, plan.Id))[0]);
    }
}
