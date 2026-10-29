using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Endpoints;

public static class AdminEndpoints
{
    public record Overview(
        int VerifiedPayments, long GrossMinor, long RefundedMinor, long NetMinor, long SubscriptionMinor, long CourseMinor,
        int PendingPayments, int FailedPayments, int RefundsAwaitingAction, int OpenReconciliation, string Currency);
    public record AdminUser(Guid Id, string Email, string? FullName, DateTimeOffset? LastLoginAt, IReadOnlyCollection<string> Roles);
    public record SetRolesRequest(string[] Roles);

    private static readonly PaymentState[] Collected =
        [PaymentState.Successful, PaymentState.RefundPending, PaymentState.PartiallyRefunded, PaymentState.Refunded];

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization(Policies.Staff);

        // Totals come from verified transaction records only, never client analytics.
        g.MapGet("/overview", async (AppDbContext db) =>
        {
            var verified = db.PaymentTransactions.Where(t => t.VerifiedAt != null && Collected.Contains(t.Status));
            var gross = await verified.SumAsync(t => (long?)t.AmountMinor) ?? 0;
            var refunded = await db.Refunds.Where(r => r.Status == RefundStatus.Completed).SumAsync(r => (long?)r.AmountMinor) ?? 0;
            var byKind = await (
                from t in verified
                join i in db.OrderItems on t.OrderId equals i.OrderId
                join p in db.Products on i.ProductId equals p.Id
                group i.LineTotalMinor by p.Kind into k
                select new { Kind = k.Key, Total = k.Sum() }).ToListAsync();
            return new Overview(
                await verified.CountAsync(), gross, refunded, gross - refunded,
                byKind.FirstOrDefault(k => k.Kind == ProductKind.SubscriptionPlan)?.Total ?? 0,
                byKind.FirstOrDefault(k => k.Kind == ProductKind.Course)?.Total ?? 0,
                await db.PaymentTransactions.CountAsync(t => t.Status == PaymentState.Pending || t.Status == PaymentState.Processing),
                await db.PaymentTransactions.CountAsync(t => t.Status == PaymentState.Failed),
                await db.Refunds.CountAsync(r => r.Status == RefundStatus.Requested || r.Status == RefundStatus.Approved),
                await db.ReconciliationRecords.CountAsync(r => r.Status == ReconciliationStatus.Open || r.Status == ReconciliationStatus.Investigating),
                "BDT");
        }).RequireAuthorization(Policies.Finance);

        g.MapGet("/users", async (AppDbContext db) =>
            await db.Users.AsNoTracking().OrderBy(u => u.Email)
                .Select(u => new AdminUser(u.Id, u.Email, u.FullName, u.LastLoginAt, u.Roles.Select(r => r.RoleKey).ToList()))
                .ToListAsync())
            .RequireAuthorization(Policies.Support);

        g.MapPut("/users/{id:guid}/roles", async (Guid id, SetRolesRequest req, AppDbContext db, ICurrentUser current) =>
        {
            var valid = req.Roles.Append(RoleKeys.Learner).Intersect([.. RoleKeys.Staff, RoleKeys.Learner]).ToHashSet();
            var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            if (!valid.Contains(RoleKeys.OwnerAdmin) && user.Roles.Any(r => r.RoleKey == RoleKeys.OwnerAdmin)
                && await db.UserRoles.CountAsync(r => r.RoleKey == RoleKeys.OwnerAdmin) <= 1)
                return Results.Problem("Cannot remove the last owner_admin.", statusCode: StatusCodes.Status409Conflict);

            user.Roles.RemoveAll(r => !valid.Contains(r.RoleKey));
            foreach (var role in valid.Where(v => user.Roles.All(r => r.RoleKey != v)))
                user.Roles.Add(new UserRole { RoleKey = role, GrantedBy = current.Id });
            await db.SaveChangesAsync();
            return Results.Ok(user.Roles.Select(r => r.RoleKey));
        }).RequireAuthorization(Policies.Owner);
    }
}
