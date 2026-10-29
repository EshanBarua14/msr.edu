using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Services;

/// <summary>Writes an audit_logs row for every change to sensitive entities, with the acting user.</summary>
public sealed class AuditInterceptor(ICurrentUser user) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> Audited =
    [
        typeof(UserRole), typeof(AppSetting), typeof(SubscriptionPlan), typeof(Product), typeof(Discount),
        typeof(Order), typeof(PaymentTransaction), typeof(Refund), typeof(Entitlement), typeof(Subscription),
        typeof(BankPaymentSubmission), typeof(ReconciliationRecord),
    ];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is { } ctx)
        {
            var logs = ctx.ChangeTracker.Entries()
                .Where(e => Audited.Contains(e.Entity.GetType())
                    && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(ToLog)
                .ToList();
            ctx.Set<AuditLog>().AddRange(logs);
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private AuditLog ToLog(EntityEntry e) => new()
    {
        ActorId = user.Id,
        Action = e.State.ToString().ToLowerInvariant() switch { "added" => "insert", "modified" => "update", _ => "delete" },
        EntityType = e.Metadata.GetTableName() ?? e.Metadata.ClrType.Name,
        EntityId = string.Join(":", e.Metadata.FindPrimaryKey()!.Properties.Select(p => e.Property(p.Name).CurrentValue)),
        Before = e.State == EntityState.Added ? null : Snapshot(e, original: true),
        After = e.State == EntityState.Deleted ? null : Snapshot(e, original: false),
    };

    private static JsonDocument Snapshot(EntityEntry e, bool original) =>
        JsonSerializer.SerializeToDocument(e.Properties.ToDictionary(
            p => p.Metadata.GetColumnName(),
            p => original ? p.OriginalValue : p.CurrentValue));
}
