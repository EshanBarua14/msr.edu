using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Endpoints;
using MsrEdu.Api.Services;

namespace MsrEdu.Api.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record Me(Guid Id, string Email, string[] Roles);

    private static string Email() => $"{Guid.NewGuid():N}@x.test";

    [Fact]
    public async Task Health_is_public()
    {
        var res = await factory.Client().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Me_requires_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task State_changing_calls_need_the_csrf_header()
    {
        var bare = factory.CreateClient();
        var res = await bare.PostAsJsonAsync("/api/auth/dev-login", new { email = Email(), roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Sign_in_returns_profile_and_roles()
    {
        var client = await factory.SignedIn(Email(), RoleKeys.ContentManager);
        var me = await client.GetFromJsonAsync<Me>("/api/auth/me");
        Assert.Contains(RoleKeys.Learner, me!.Roles);
        Assert.Contains(RoleKeys.ContentManager, me.Roles);

        await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Configured_owner_email_is_promoted_on_google_sign_in()
    {
        await using var scope = factory.Scope();
        var provisioning = scope.ServiceProvider.GetRequiredService<Auth.UserProvisioning>();
        var user = await provisioning.UpsertGoogleUserAsync($"g-{Guid.NewGuid():N}", "OWNER@test.local", "Owner", null);
        Assert.Contains(user.Roles, r => r.RoleKey == RoleKeys.OwnerAdmin);
        var again = await provisioning.UpsertGoogleUserAsync(user.GoogleSubject!, "owner@test.local", null, null);
        Assert.Equal(user.Id, again.Id);
    }

    [Fact]
    public async Task Lesson_content_is_gated()
    {
        Guid previewId, paidId, orderId, userId;
        var learnerEmail = Email();
        var learner = await factory.SignedIn(learnerEmail);
        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seed = new Seed(db);
            var (course, preview, paid) = await seed.CourseAsync($"api-{Guid.NewGuid():N}"[..14]);
            previewId = preview.Id;
            paidId = paid.Id;
            var product = await seed.CourseProductAsync(course);
            var user = db.Users.Single(u => u.Email == learnerEmail);
            userId = user.Id;
            var (order, _) = await seed.PendingOrderAsync(user, product, $"T-{Guid.NewGuid():N}");
            orderId = order.Id;
        }

        var anon = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/lessons/{previewId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/lessons/{paidId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/lessons/{paidId}/content")).StatusCode);

        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = db.Orders.Single(o => o.Id == orderId);
            var txn = db.PaymentTransactions.Single(t => t.OrderId == orderId);
            await new Seed(db).MarkPaidAsync(order, txn);
            await scope.ServiceProvider.GetRequiredService<EntitlementService>().GrantForOrderAsync(orderId);
        }

        var res = await learner.GetAsync($"/api/lessons/{paidId}/content");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("paid body", await res.Content.ReadAsStringAsync());
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Catalogue_hides_drafts_and_marks_locked_lessons()
    {
        var slug = $"cat-{Guid.NewGuid():N}"[..14];
        var draft = $"dft-{Guid.NewGuid():N}"[..14];
        await using (var scope = factory.Scope())
        {
            var seed = new Seed(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            await seed.CourseAsync(slug);
            await seed.CourseAsync(draft, PublishStatus.Draft);
        }
        var anon = factory.Client();
        var list = await anon.GetStringAsync("/api/courses");
        Assert.Contains(slug, list);
        Assert.DoesNotContain(draft, list);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/courses/{draft}")).StatusCode);

        var detail = await anon.GetFromJsonAsync<CatalogEndpoints.CourseDetail>($"/api/courses/{slug}");
        var lessons = detail!.Modules.SelectMany(m => m.Lessons).ToList();
        Assert.False(detail.HasAccess);
        Assert.False(lessons.Single(l => l.IsPreview).Locked);
        Assert.True(lessons.Single(l => !l.IsPreview).Locked);
    }

    [Fact]
    public async Task Admin_endpoints_follow_roles()
    {
        var learner = await factory.SignedIn(Email());
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/admin/overview")).StatusCode);

        var content = await factory.SignedIn(Email(), RoleKeys.ContentManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await content.GetAsync("/api/admin/overview")).StatusCode);

        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/roles", new { roles = new[] { "owner_admin" } })).StatusCode);
    }

    [Fact]
    public async Task Owner_manages_roles_but_cannot_remove_the_last_owner()
    {
        var ownerEmail = Email();
        var owner = await factory.SignedIn(ownerEmail, RoleKeys.OwnerAdmin);
        var me = await owner.GetFromJsonAsync<Me>("/api/auth/me");

        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Make this the only owner for the test.
            foreach (var r in db.UserRoles.Where(r => r.RoleKey == RoleKeys.OwnerAdmin && r.UserId != me!.Id)) db.UserRoles.Remove(r);
            await db.SaveChangesAsync();
        }

        var target = await factory.SignedIn(Email());
        var targetMe = await target.GetFromJsonAsync<Me>("/api/auth/me");
        var res = await owner.PutAsJsonAsync($"/api/admin/users/{targetMe!.Id}/roles", new { roles = new[] { RoleKeys.FinanceManager } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(RoleKeys.FinanceManager, (await target.GetFromJsonAsync<Me>("/api/auth/me"))!.Roles);

        var demote = await owner.PutAsJsonAsync($"/api/admin/users/{me!.Id}/roles", new { roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
    }

    [Theory]
    [InlineData("/app", "/app")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("https://evil.com", "/")]
    [InlineData(null, "/")]
    public void Return_paths_cannot_leave_the_site(string? raw, string expected) =>
        Assert.Equal(expected, AuthEndpoints.SafeReturnPath(raw));

    [Fact]
    public async Task Unknown_api_routes_are_404_not_the_spa()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client().GetAsync("/api/nope")).StatusCode);
    }
}
