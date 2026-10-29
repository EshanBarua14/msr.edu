using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;

namespace MsrEdu.Api.Tests;

public class AccessAndEntitlementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Ctx(AppDbContext Db, Seed Seed, AccessService Access, EntitlementService Grants);

    private async Task<T> With<T>(Func<Ctx, Task<T>> body)
    {
        await using var scope = factory.Scope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        return await body(new Ctx(db, new Seed(db), sp.GetRequiredService<AccessService>(), sp.GetRequiredService<EntitlementService>()));
    }

    private static string Slug() => $"c-{Guid.NewGuid():N}"[..14];

    [Fact]
    public async Task Preview_is_public_but_paid_content_needs_access() => await With(async c =>
    {
        var (_, preview, paid) = await c.Seed.CourseAsync(Slug());
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        Assert.True(await c.Access.CanViewLessonAsync(null, false, preview.Id));
        Assert.False(await c.Access.CanViewLessonAsync(null, false, paid.Id));
        Assert.False(await c.Access.CanViewLessonAsync(learner.Id, false, paid.Id));
        Assert.True(await c.Access.CanViewLessonAsync(learner.Id, isStaff: true, paid.Id));
        return 0;
    });

    [Fact]
    public async Task Draft_course_content_is_hidden_even_for_previews() => await With(async c =>
    {
        var (_, preview, _) = await c.Seed.CourseAsync(Slug(), PublishStatus.Draft);
        Assert.False(await c.Access.CanViewLessonAsync(null, false, preview.Id));
        return 0;
    });

    [Fact]
    public async Task Free_course_needs_sign_in_only() => await With(async c =>
    {
        var (_, _, paid) = await c.Seed.CourseAsync(Slug(), free: true);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        Assert.False(await c.Access.CanViewLessonAsync(null, false, paid.Id));
        Assert.True(await c.Access.CanViewLessonAsync(learner.Id, false, paid.Id));
        return 0;
    });

    [Fact]
    public async Task Pending_order_cannot_grant_access() => await With(async c =>
    {
        var (course, _, _) = await c.Seed.CourseAsync(Slug());
        var product = await c.Seed.CourseProductAsync(course);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, _) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await Assert.ThrowsAsync<EntitlementException>(() => c.Grants.GrantForOrderAsync(order.Id));
        Assert.False(await c.Access.HasCourseAccessAsync(learner.Id, course.Id));
        return 0;
    });

    [Fact]
    public async Task Successful_order_without_matching_verified_transaction_cannot_grant() => await With(async c =>
    {
        var (course, _, _) = await c.Seed.CourseAsync(Slug());
        var product = await c.Seed.CourseProductAsync(course);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, _) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        order.Status = PaymentState.Successful;   // success flag alone, transaction still pending
        await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<EntitlementException>(() => c.Grants.GrantForOrderAsync(order.Id));
        return 0;
    });

    [Fact]
    public async Task Verified_purchase_grants_once_even_when_replayed() => await With(async c =>
    {
        var (course, _, paid) = await c.Seed.CourseAsync(Slug());
        var product = await c.Seed.CourseProductAsync(course);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, txn) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await c.Seed.MarkPaidAsync(order, txn);

        Assert.Equal(1, await c.Grants.GrantForOrderAsync(order.Id));
        Assert.Equal(0, await c.Grants.GrantForOrderAsync(order.Id));
        Assert.Equal(1, await c.Db.Entitlements.CountAsync(e => e.UserId == learner.Id));
        Assert.True(await c.Access.CanViewLessonAsync(learner.Id, false, paid.Id));
        Assert.Null((await c.Db.Entitlements.SingleAsync(e => e.UserId == learner.Id)).ExpiresAt); // lifetime by default
        return 0;
    });

    [Fact]
    public async Task Concurrent_grants_for_the_same_order_create_one_entitlement() => await With(async c =>
    {
        var (course, _, _) = await c.Seed.CourseAsync(Slug());
        var product = await c.Seed.CourseProductAsync(course);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, txn) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await c.Seed.MarkPaidAsync(order, txn);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            With(other => other.Grants.GrantForOrderAsync(order.Id))));
        Assert.Equal(1, results.Sum());
        Assert.Equal(1, await c.Db.Entitlements.CountAsync(e => e.UserId == learner.Id));
        return 0;
    });

    [Fact]
    public async Task Course_access_days_set_expiry_and_expiry_is_enforced() => await With(async c =>
    {
        var (course, _, _) = await c.Seed.CourseAsync(Slug());
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var owner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test", RoleKeys.OwnerAdmin);
        c.Db.Entitlements.Add(new Entitlement
        {
            UserId = learner.Id, CourseId = course.Id, Source = EntitlementSource.ManualGrant, GrantedBy = owner.Id, GrantReason = "trial",
            StartsAt = DateTimeOffset.UtcNow.AddDays(-2), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await c.Db.SaveChangesAsync();
        Assert.False(await c.Access.HasCourseAccessAsync(learner.Id, course.Id));
        return 0;
    });

    [Fact]
    public async Task Revoked_entitlement_removes_access() => await With(async c =>
    {
        var (course, _, _) = await c.Seed.CourseAsync(Slug());
        var product = await c.Seed.CourseProductAsync(course);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, txn) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await c.Seed.MarkPaidAsync(order, txn);
        await c.Grants.GrantForOrderAsync(order.Id);

        var ent = await c.Db.Entitlements.SingleAsync(e => e.UserId == learner.Id);
        ent.Status = EntitlementStatus.Revoked;
        ent.StatusReason = "refunded";
        await c.Db.SaveChangesAsync();
        Assert.False(await c.Access.HasCourseAccessAsync(learner.Id, course.Id));
        return 0;
    });

    [Fact]
    public async Task Subscription_covers_courses_and_renewal_stacks_periods() => await With(async c =>
    {
        var (course, _, paid) = await c.Seed.CourseAsync(Slug());
        var (plan, product) = await c.Seed.PlanAsync(Slug(), graceDays: 3);
        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");

        var (o1, t1) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await c.Seed.MarkPaidAsync(o1, t1);
        Assert.Equal(1, await c.Grants.GrantForOrderAsync(o1.Id));
        Assert.True(await c.Access.CanViewLessonAsync(learner.Id, false, paid.Id));

        var (o2, t2) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await c.Seed.MarkPaidAsync(o2, t2);
        await c.Grants.GrantForOrderAsync(o2.Id);
        var latestEnd = await c.Db.Subscriptions.Where(s => s.UserId == learner.Id).MaxAsync(s => s.CurrentPeriodEnd);
        Assert.True(latestEnd > DateTimeOffset.UtcNow.AddDays(55));
        var ents = await c.Db.Entitlements.Where(e => e.UserId == learner.Id).OrderBy(e => e.StartsAt).ToListAsync();
        Assert.Equal(2, ents.Count);
        Assert.Equal(ents[1].StartsAt.AddMonths(1).AddDays(plan.GraceDays), ents[1].ExpiresAt);
        return 0;
    });

    [Fact]
    public async Task Plan_scoped_to_a_category_covers_only_that_category() => await With(async c =>
    {
        var cat = new Category { Slug = Slug(), Name = "Maths" };
        c.Db.Categories.Add(cat);
        var (inCat, _, _) = await c.Seed.CourseAsync(Slug());
        var (outCat, _, _) = await c.Seed.CourseAsync(Slug());
        inCat.CategoryId = cat.Id;
        var (plan, product) = await c.Seed.PlanAsync(Slug(), allCourses: false);
        c.Db.PlanCategories.Add(new PlanCategory { PlanId = plan.Id, CategoryId = cat.Id });
        await c.Db.SaveChangesAsync();

        var learner = await c.Seed.UserAsync($"{Guid.NewGuid():N}@x.test");
        var (order, txn) = await c.Seed.PendingOrderAsync(learner, product, $"T-{Guid.NewGuid():N}");
        await c.Seed.MarkPaidAsync(order, txn);
        await c.Grants.GrantForOrderAsync(order.Id);

        Assert.True(await c.Access.HasCourseAccessAsync(learner.Id, inCat.Id));
        Assert.False(await c.Access.HasCourseAccessAsync(learner.Id, outCat.Id));
        return 0;
    });

    [Fact]
    public async Task Promo_price_applies_only_inside_its_window()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new Product { Name = "x", PriceMinor = 1000, PromoPriceMinor = 700, PromoStartsAt = now.AddDays(-1), PromoEndsAt = now.AddDays(1) };
        Assert.Equal(700, p.CurrentPriceMinor(now));
        Assert.Equal(1000, p.CurrentPriceMinor(now.AddDays(2)));
        await Task.CompletedTask;
    }
}
