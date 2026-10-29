using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.PaymentEndpoints;

namespace MsrEdu.Api.Endpoints;

/// <summary>
/// Running the business: revenue reports, payment reconciliation, the audit log, business settings and the setup
/// checklist. Revenue comes from verified transactions and completed refunds only.
/// </summary>
public static class OpsEndpoints
{
    /// <summary>Reports group days in Bangladesh time, where the business operates.</summary>
    public static readonly TimeZoneInfo ReportZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    public record RevenueRow(string Period, int Payments, long GrossMinor, long RefundedMinor, long NetMinor, long CourseMinor, long SubscriptionMinor);
    public record ProductRow(string Name, string Kind, int Sales, long GrossMinor);
    public record RevenueReport(string Currency, DateOnly From, DateOnly To, string Group, RevenueRow Totals, IReadOnlyList<RevenueRow> Rows, IReadOnlyList<ProductRow> Products);

    public record IssueView(Guid Id, string Issue, string Status, JsonElement? Details, Guid? OrderId, string? OrderReference, string? UserEmail,
        DateTimeOffset CreatedAt, string? ResolvedBy, DateTimeOffset? ResolvedAt, string? ResolutionNote);
    public record IssueUpdate(string Status, string? Note);

    public record AuditRow(long Id, DateTimeOffset CreatedAt, string? Actor, string Action, string EntityType, string? EntityId, JsonElement? Before, JsonElement? After);
    public record AuditPage(IReadOnlyList<AuditRow> Rows, long? Next);

    public record SettingView(string Key, JsonElement Value, string? Description, bool IsPublic, DateTimeOffset UpdatedAt, string? UpdatedBy);
    public record SettingUpdate(JsonElement Value);
    public record SetupStatus(bool Database, bool GoogleSignIn, bool DefaultsConfirmed, string? Gateway, bool GatewayIsSandbox, bool Drive, bool Ai);

    public const string DefaultsConfirmedKey = "business.defaults_confirmed";

    private static IResult Bad(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    private static readonly PaymentState[] Collected =
        [PaymentState.Successful, PaymentState.RefundPending, PaymentState.PartiallyRefunded, PaymentState.Refunded];

    public static void MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var finance = app.MapGroup("/api/admin").RequireAuthorization(Policies.Finance);
        var owner = app.MapGroup("/api/admin").RequireAuthorization(Policies.Owner);

        // ------------------------------------------------------------ revenue
        finance.MapGet("/reports/revenue", async (DateOnly? from, DateOnly? to, string? group, AppDbContext db, TimeProvider clock) =>
        {
            if (Range(from, to, group, clock) is not { } r) return Bad("Pick a range of up to 2 years, grouped by day or month.");
            var currency = await Settings.GetAsync(db, "currency.default", "BDT");
            var (sales, refunds) = await LoadAsync(db, r.Start, r.End);

            var periods = new List<string>();
            for (var d = r.From; d <= r.To; d = r.Group == "month" ? d.AddMonths(1) : d.AddDays(1))
                periods.Add(Key(d, r.Group));
            periods = periods.Distinct().ToList();

            RevenueRow Row(string period, IEnumerable<Sale> s, IEnumerable<RefundLine> f)
            {
                var gross = s.Sum(x => x.AmountMinor);
                var refunded = f.Sum(x => x.AmountMinor);
                return new RevenueRow(period, s.Count(), gross, refunded, gross - refunded,
                    s.Where(x => x.Kind == ProductKind.Course).Sum(x => x.AmountMinor),
                    s.Where(x => x.Kind == ProductKind.SubscriptionPlan).Sum(x => x.AmountMinor));
            }

            var rows = periods.Select(p => Row(p, sales.Where(s => Key(Local(s.At), r.Group) == p), refunds.Where(f => Key(Local(f.At), r.Group) == p))).ToList();
            var products = sales.GroupBy(s => (s.Name, s.Kind))
                .Select(g => new ProductRow(g.Key.Name, g.Key.Kind == ProductKind.Course ? "course" : "subscription_plan", g.Count(), g.Sum(s => s.AmountMinor)))
                .OrderByDescending(p => p.GrossMinor).ToList();
            return Results.Ok(new RevenueReport(currency, r.From, r.To, r.Group, Row("total", sales, refunds), rows, products));
        });

        finance.MapGet("/reports/revenue.csv", async (DateOnly? from, DateOnly? to, AppDbContext db, TimeProvider clock) =>
        {
            if (Range(from, to, "day", clock) is not { } r) return Bad("Pick a range of up to 2 years.");
            var (sales, refunds) = await LoadAsync(db, r.Start, r.End);
            var csv = new StringBuilder("type,date_bd,order_reference,customer_email,item,kind,amount,currency,provider,provider_reference\n");
            var lines = sales.Select(s => (Type: "payment", s.At, Sale: s, Amount: s.AmountMinor))
                .Concat(refunds.Select(f => (Type: "refund", f.At, f.Sale, Amount: -f.AmountMinor)))
                .OrderBy(x => x.At);
            foreach (var (type, at, s, amount) in lines)
                csv.AppendJoin(',', type, Local(at).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Cell(s.Reference), Cell(s.Email), Cell(s.Name),
                    s.Kind == ProductKind.Course ? "course" : "subscription_plan", (amount / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                    s.Currency, Cell(s.Provider), Cell(s.ProviderTxnId ?? "")).Append('\n');
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv",
                $"msr-revenue-{r.From:yyyyMMdd}-{r.To:yyyyMMdd}.csv");
        });

        // ------------------------------------------------------------ reconciliation
        finance.MapGet("/reconciliation", async (string? status, AppDbContext db) =>
        {
            var open = string.IsNullOrEmpty(status) || status == "open";
            var rows = await (from r in db.ReconciliationRecords
                              join o in db.Orders on r.OrderId equals o.Id into os from o in os.DefaultIfEmpty()
                              join u in db.Users on o.UserId equals u.Id into us from u in us.DefaultIfEmpty()
                              join a in db.Users on r.ResolvedBy equals a.Id into rs from a in rs.DefaultIfEmpty()
                              where open ? r.Status == ReconciliationStatus.Open || r.Status == ReconciliationStatus.Investigating
                                         : r.Status == ReconciliationStatus.Resolved || r.Status == ReconciliationStatus.Ignored
                              orderby r.CreatedAt descending
                              select new { r, Ref = o != null ? o.Reference : null, Email = u != null ? u.Email : null, By = a != null ? a.Email : null })
                .Take(200).AsNoTracking().ToListAsync();
            return rows.Select(x => new IssueView(x.r.Id, x.r.Issue, Snake(x.r.Status.ToString()), x.r.Details?.RootElement.Clone(), x.r.OrderId, x.Ref, x.Email,
                x.r.CreatedAt, x.By, x.r.ResolvedAt, x.r.ResolutionNote)).ToList();
        });

        finance.MapPost("/reconciliation/{id:guid}", async (Guid id, IssueUpdate req, AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            if (!Enum.TryParse<ReconciliationStatus>(req.Status, true, out var status) || status == ReconciliationStatus.Open || !Enum.IsDefined(status))
                return Bad("Status must be investigating, resolved or ignored.");
            var closing = status is ReconciliationStatus.Resolved or ReconciliationStatus.Ignored;
            if (closing && string.IsNullOrWhiteSpace(req.Note)) return Bad("Say what was done before closing it.");
            var record = await db.ReconciliationRecords.FirstOrDefaultAsync(r => r.Id == id);
            if (record is null) return Results.NotFound();
            record.Status = status;
            record.ResolutionNote = req.Note?.Trim();
            record.ResolvedBy = closing ? user.Id : null;
            record.ResolvedAt = closing ? clock.GetUtcNow() : null;
            await db.SaveChangesAsync();
            return Results.Ok(new { status = Snake(status.ToString()) });
        });

        // ------------------------------------------------------------ audit log
        finance.MapGet("/audit", async (string? entity, string? actor, string? entityId, long? before, AppDbContext db) =>
        {
            var q = db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.EntityType == entity);
            if (!string.IsNullOrWhiteSpace(entityId)) q = q.Where(a => a.EntityId == entityId);
            if (!string.IsNullOrWhiteSpace(actor))
            {
                var term = actor.Trim().ToLower();
                var ids = await db.Users.Where(u => u.Email.ToLower().Contains(term)).Select(u => (Guid?)u.Id).ToListAsync();
                q = q.Where(a => ids.Contains(a.ActorId));
            }
            if (before is { } b) q = q.Where(a => a.Id < b);
            const int size = 50;
            var rows = await (from a in q.OrderByDescending(a => a.Id).Take(size + 1)
                              join u in db.Users on a.ActorId equals u.Id into us from u in us.DefaultIfEmpty()
                              select new { a, Email = u != null ? u.Email : null }).ToListAsync();
            return new AuditPage(
                rows.Take(size).Select(x => new AuditRow(x.a.Id, x.a.CreatedAt, x.Email, x.a.Action, x.a.EntityType, x.a.EntityId,
                    x.a.Before?.RootElement.Clone(), x.a.After?.RootElement.Clone())).ToList(),
                rows.Count > size ? rows[size - 1].a.Id : null);
        });

        finance.MapGet("/audit/entities", async (AppDbContext db) =>
            await db.AuditLogs.Select(a => a.EntityType).Distinct().OrderBy(e => e).ToListAsync());

        // ------------------------------------------------------------ settings
        owner.MapGet("/settings", async (AppDbContext db) =>
            (await (from s in db.AppSettings
                    join u in db.Users on s.UpdatedBy equals u.Id into us from u in us.DefaultIfEmpty()
                    orderby s.Key
                    select new { s, By = u != null ? u.Email : null }).AsNoTracking().ToListAsync())
                .Select(x => new SettingView(x.s.Key, x.s.Value.RootElement.Clone(), x.s.Description, x.s.IsPublic, x.s.UpdatedAt, x.By)).ToList());

        owner.MapPut("/settings/{key}", async (string key, SettingUpdate req, AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting is null || key == DefaultsConfirmedKey) return Results.NotFound();
            var allowed = await Settings.GetAsync(db, "currency.allowed", new[] { "BDT" });
            if (SettingRules.Validate(key, req.Value, allowed) is { } error) return Bad(error);
            setting.Value = JsonDocument.Parse(req.Value.GetRawText());
            (setting.UpdatedAt, setting.UpdatedBy) = (clock.GetUtcNow(), user.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new SettingView(setting.Key, setting.Value.RootElement, setting.Description, setting.IsPublic, setting.UpdatedAt, null));
        });

        // The owner signs off the placeholder defaults (blueprint section 18) once they have reviewed them.
        owner.MapPost("/settings/confirm-defaults", async (AppDbContext db, ICurrentUser user, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            var value = JsonSerializer.SerializeToDocument(new { confirmed_at = now, by = user.Id });
            var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == DefaultsConfirmedKey);
            if (setting is null)
                db.AppSettings.Add(new AppSetting { Key = DefaultsConfirmedKey, Value = value, Description = "When the owner confirmed the business defaults", UpdatedAt = now, UpdatedBy = user.Id });
            else (setting.Value, setting.UpdatedAt, setting.UpdatedBy) = (value, now, user.Id);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapGet("/api/admin/setup", async (AppDbContext db, IConfiguration config, PaymentGateways gateways, IDriveClient drive, IQuestionGenerator ai) =>
            new SetupStatus(
                await db.Database.CanConnectAsync(),
                !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientSecret"]),
                await db.AppSettings.AnyAsync(s => s.Key == DefaultsConfirmedKey),
                gateways.Default?.Key, gateways.Default?.IsSandbox ?? false, drive.IsConfigured, ai.IsConfigured))
            .RequireAuthorization(Policies.Staff);
    }

    private sealed record Sale(DateTimeOffset At, string Reference, string Email, string Name, ProductKind Kind, long AmountMinor, string Currency, string Provider, string? ProviderTxnId, Guid TxnId);
    private sealed record RefundLine(DateTimeOffset At, long AmountMinor, Sale Sale);
    private sealed record ReportRange(DateOnly From, DateOnly To, string Group, DateTimeOffset Start, DateTimeOffset End);

    private static ReportRange? Range(DateOnly? from, DateOnly? to, string? group, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(Local(clock.GetUtcNow()).DateTime);
        var g = string.IsNullOrEmpty(group) ? "day" : group;
        var end = to ?? today;
        var start = from ?? (g == "month" ? new DateOnly(end.Year, end.Month, 1).AddMonths(-11) : end.AddDays(-29));
        if (g is not ("day" or "month") || start > end || end.DayNumber - start.DayNumber > 731) return null;
        if (g == "month") start = new DateOnly(start.Year, start.Month, 1);
        return new ReportRange(start, end, g, Utc(start), Utc(end.AddDays(1)));
    }

    private static DateTimeOffset Utc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ReportZone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, ReportZone);
    private static string Key(DateOnly d, string group) => group == "month" ? d.ToString("yyyy-MM", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Key(DateTimeOffset local, string group) => Key(DateOnly.FromDateTime(local.DateTime), group);

    /// <summary>Spreadsheet-safe CSV cell: quoted, and never starting with a formula character.</summary>
    public static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith('\'') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static async Task<(List<Sale> Sales, List<RefundLine> Refunds)> LoadAsync(AppDbContext db, DateTimeOffset start, DateTimeOffset end)
    {
        Sale ToSale(DateTimeOffset at, PaymentTransaction t, string reference, string email, string name, ProductKind kind) =>
            new(at, reference, email, name, kind, t.AmountMinor, t.Currency, t.Provider, t.ProviderTxnId, t.Id);

        // One product per order today; the first item names the sale.
        var query = from t in db.PaymentTransactions
                    join o in db.Orders on t.OrderId equals o.Id
                    join u in db.Users on o.UserId equals u.Id
                    where t.VerifiedAt != null && Collected.Contains(t.Status)
                    select new { t, o.Reference, u.Email, Item = o.Items.OrderBy(i => i.CreatedAt).Select(i => new { i.Product!.Name, i.Product.Kind }).First() };
        var paid = await query.Where(x => x.t.VerifiedAt >= start && x.t.VerifiedAt < end).AsNoTracking().ToListAsync();
        var sales = paid.Select(x => ToSale(x.t.VerifiedAt!.Value, x.t, x.Reference, x.Email, x.Item.Name, x.Item.Kind)).ToList();

        var refundRows = await (from r in db.Refunds
                                join x in query on r.TransactionId equals x.t.Id
                                where r.Status == RefundStatus.Completed && r.CompletedAt >= start && r.CompletedAt < end
                                select new { r.CompletedAt, r.AmountMinor, x }).AsNoTracking().ToListAsync();
        var refunds = refundRows.Select(r => new RefundLine(r.CompletedAt!.Value, r.AmountMinor,
            ToSale(r.x.t.VerifiedAt!.Value, r.x.t, r.x.Reference, r.x.Email, r.x.Item.Name, r.x.Item.Kind))).ToList();
        return (sales, refunds);
    }
}

/// <summary>What each business setting may hold. Unknown keys can't be edited from the console.</summary>
public static class SettingRules
{
    public static string? Validate(string key, JsonElement v, IReadOnlyCollection<string> allowedCurrencies)
    {
        bool Int(int min, int max) => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min && n <= max;
        bool OneOf(params string[] options) => v.ValueKind == JsonValueKind.String && options.Contains(v.GetString());
        return key switch
        {
            "currency.default" => v.ValueKind == JsonValueKind.String && allowedCurrencies.Contains(v.GetString()) ? null
                : $"Default currency must be one of: {string.Join(", ", allowedCurrencies)}.",
            "currency.allowed" => v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0
                && v.EnumerateArray().All(c => c.ValueKind == JsonValueKind.String && c.GetString() is { Length: 3 } s && s == s.ToUpperInvariant())
                ? null : "List 3-letter currency codes, like [\"BDT\"].",
            "subscription.renewal_mode" => OneOf("manual", "auto") ? null : "Renewal is manual or auto.",
            "subscription.grace_days" => Int(0, 90) ? null : "Grace days: 0 to 90.",
            "course.default_access_days" => v.ValueKind == JsonValueKind.Null || Int(1, 3650) ? null : "Access days: empty for lifetime, or 1 to 3650.",
            "refund.access_policy" => OneOf("revoke_immediately", "revoke_after_days") ? null : "Refund policy is revoke_immediately or revoke_after_days.",
            "refund.revoke_after_days" => Int(0, 365) ? null : "Days: 0 to 365.",
            "discounts.enabled" or "bank_transfer.enabled" => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "Turn it on or off.",
            "order.expiry_minutes" => Int(5, 1440) ? null : "Order expiry: 5 to 1440 minutes.",
            "admin.model" => OneOf("single_owner", "separate_roles") ? null : "Admin model is single_owner or separate_roles.",
            _ => "This setting can't be changed here.",
        };
    }
}
