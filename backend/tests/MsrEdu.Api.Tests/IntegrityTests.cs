using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using Npgsql;

namespace MsrEdu.Api.Tests;

/// <summary>Database guards that hold even for clients that bypass the API (pgAdmin, scripts).</summary>
public class IntegrityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task Run(Func<AppDbContext, Seed, Task> body)
    {
        await using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await body(db, new Seed(db));
    }

    private static async Task Fails(AppDbContext db, string sql, params object[] args)
    {
        db.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, args));
    }

    private static string Slug() => $"c-{Guid.NewGuid():N}"[..14];

    private static async Task<(Order Order, PaymentTransaction Txn, User User)> PaidOrder(AppDbContext db, Seed seed)
    {
        var (course, _, _) = await seed.CourseAsync(Slug());
        var product = await seed.CourseProductAsync(course);
        var user = await seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, txn) = await seed.PendingOrderAsync(user, product, $"T-{Guid.NewGuid():N}");
        await seed.MarkPaidAsync(order, txn);
        return (order, txn, user);
    }

    [Fact]
    public Task Order_reference_and_amounts_are_generated_and_immutable() => Run(async (db, seed) =>
    {
        var (order, _, _) = await PaidOrder(db, seed);
        Assert.Matches(@"^MSR-\d{6}-[0-9A-F]{8}$", (await seed.ReloadAsync(order)).Reference);
        await Fails(db, "update orders set total_minor = 1, subtotal_minor = 1 where id = {0}", order.Id);
        await Fails(db, "update order_items set unit_price_minor = 1 where order_id = {0}", order.Id);
        await Fails(db, "delete from orders where id = {0}", order.Id);
    });

    [Fact]
    public Task Successful_transaction_requires_verification() => Run(async (db, seed) =>
    {
        var (course, _, _) = await seed.CourseAsync(Slug());
        var product = await seed.CourseProductAsync(course);
        var user = await seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (_, txn) = await seed.PendingOrderAsync(user, product, $"T-{Guid.NewGuid():N}");
        await Fails(db, "update payment_transactions set status = 'successful' where id = {0}", txn.Id);
    });

    [Fact]
    public Task Status_follows_the_state_machine() => Run(async (db, seed) =>
    {
        var (order, txn, _) = await PaidOrder(db, seed);
        await Fails(db, "update orders set status = 'pending' where id = {0}", order.Id);
        await Fails(db, "update payment_transactions set status = 'failed' where id = {0}", txn.Id);
        await Fails(db, "delete from payment_transactions where id = {0}", txn.Id);
    });

    [Fact]
    public Task State_machine_in_code_matches_the_database() => Run(async (db, _) =>
    {
        foreach (var from in Enum.GetValues<PaymentState>())
        foreach (var to in Enum.GetValues<PaymentState>())
        {
            var allowed = await db.Database
                .SqlQueryRaw<bool>("select payment_transition_allowed({0}, {1}) as \"Value\"", from, to)
                .SingleAsync();
            Assert.True(allowed == PaymentStateMachine.CanTransition(from, to), $"{from} -> {to}");
        }
    });

    [Fact]
    public Task Duplicate_webhooks_and_gateway_ids_are_rejected() => Run(async (db, seed) =>
    {
        var (order, txn, _) = await PaidOrder(db, seed);
        var evt = $"E-{Guid.NewGuid():N}";
        await db.Database.ExecuteSqlRawAsync("insert into payment_events (provider, provider_event_id, event_type, received_at, attempts) values ('sandbox', {0}, 'paid', now(), 0)", evt);
        await Fails(db, "insert into payment_events (provider, provider_event_id, event_type, received_at, attempts) values ('sandbox', {0}, 'paid', now(), 0)", evt);
        await Fails(db,
            "insert into payment_transactions (id, order_id, provider, method, provider_txn_id, amount_minor, currency, status, created_at, updated_at) " +
            "values (gen_random_uuid(), {0}, 'sandbox', 'card', {1}, 1, 'BDT', 'created', now(), now())", order.Id, txn.ProviderTxnId!);
    });

    [Fact]
    public Task Refunds_cannot_exceed_the_paid_amount() => Run(async (db, seed) =>
    {
        var (order, txn, _) = await PaidOrder(db, seed);
        db.Refunds.Add(new Refund { OrderId = order.Id, TransactionId = txn.Id, AmountMinor = 100_000, Currency = "BDT", Reason = "partial" });
        await db.SaveChangesAsync();
        db.Refunds.Add(new Refund { OrderId = order.Id, TransactionId = txn.Id, AmountMinor = 60_000, Currency = "BDT", Reason = "too much" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    });

    [Fact]
    public Task Receipts_require_a_verified_payment() => Run(async (db, seed) =>
    {
        var (course, _, _) = await seed.CourseAsync(Slug());
        var product = await seed.CourseProductAsync(course);
        var user = await seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, txn) = await seed.PendingOrderAsync(user, product, $"T-{Guid.NewGuid():N}");
        db.Receipts.Add(new Receipt { OrderId = order.Id, TransactionId = txn.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var paid = await PaidOrder(db, seed);
        var receipt = new Receipt { OrderId = paid.Order.Id, TransactionId = paid.Txn.Id };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync();
        Assert.Matches(@"^R-\d{4}-\d{6}$", (await db.Receipts.AsNoTracking().SingleAsync(r => r.Id == receipt.Id)).ReceiptNumber);
    });

    [Fact]
    public Task Entitlements_need_reasons_and_keep_their_scope() => Run(async (db, seed) =>
    {
        var (order, _, user) = await PaidOrder(db, seed);
        await scopeGrant(order.Id);
        var ent = await db.Entitlements.AsNoTracking().SingleAsync(e => e.UserId == user.Id);
        await Fails(db, "update entitlements set status = 'revoked' where id = {0}", ent.Id);
        await Fails(db, "update entitlements set starts_at = now() - interval '1 year' where id = {0}", ent.Id);
        await Fails(db, "delete from entitlements where id = {0}", ent.Id);

        async Task scopeGrant(Guid id)
        {
            await using var scope = factory.Scope();
            await scope.ServiceProvider.GetRequiredService<EntitlementService>().GrantForOrderAsync(id);
        }
    });

    [Fact]
    public Task Audit_log_records_changes_and_is_append_only() => Run(async (db, seed) =>
    {
        var (order, _, _) = await PaidOrder(db, seed);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == "orders" && a.EntityId == order.Id.ToString() && a.Action == "update"));
        await Fails(db, "delete from audit_logs");
        await Fails(db, "update audit_logs set action = 'x'");
    });

    [Fact]
    public Task Last_owner_cannot_be_removed() => Run(async (db, seed) =>
    {
        await db.Database.ExecuteSqlRawAsync("delete from user_roles where role_key = 'owner_admin'");
        var owner = await seed.UserAsync($"{Guid.NewGuid():N}@x.test", RoleKeys.OwnerAdmin);
        await Fails(db, "delete from user_roles where role_key = 'owner_admin'");
        await Fails(db, "update user_roles set role_key = 'learner' where user_id = {0} and role_key = 'owner_admin'", owner.Id);
    });

    [Fact]
    public Task Business_defaults_are_seeded() => Run(async (db, _) =>
    {
        var settings = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value.RootElement.GetRawText());
        Assert.Equal("\"BDT\"", settings["currency.default"]);
        Assert.Equal("\"manual\"", settings["subscription.renewal_mode"]);
        Assert.Equal("false", settings["discounts.enabled"]);
    });
}
