using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.FinanceEndpoints;
using static MsrEdu.Api.Endpoints.PaymentEndpoints;

namespace MsrEdu.Api.Tests;

/// <summary>Checkout through the sandbox gateway (enabled by appsettings.Development.json) end to end over HTTP.</summary>
public class PaymentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Started(string Reference, string RedirectUrl);
    private sealed record Course(Guid Id, string Slug, Guid ProductId, Guid PaidLessonId);

    private static string Email() => $"{Guid.NewGuid():N}@x.test";
    private static string Slug() => $"c-{Guid.NewGuid():N}"[..14];

    private static async Task<T> Ok<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Course> CourseAsync(long price = 150_000, long? promo = null)
    {
        await using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (course, _, paid) = await new Seed(db).CourseAsync(Slug());
        var product = new Product { Kind = ProductKind.Course, CourseId = course.Id, Name = course.Title, PriceMinor = price, PromoPriceMinor = promo, Status = SaleStatus.Active };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return new Course(course.Id, course.Slug, product.Id, paid.Id);
    }

    private static async Task<(Started Started, string TxnId)> CheckoutAsync(HttpClient client, Guid productId)
    {
        var started = await Ok<Started>(await client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productId)));
        Assert.StartsWith("/checkout/sandbox/sbx_", started.RedirectUrl);
        return (started, started.RedirectUrl.Split('/').Last());
    }

    private static async Task PayAsync(HttpClient client, string txnId, string outcome = "success") =>
        (await client.PostAsJsonAsync($"/api/sandbox/payments/{txnId}", new SandboxOutcome(outcome))).EnsureSuccessStatusCode();

    private static Task<OrderView> OrderAsync(HttpClient client, string reference) =>
        client.GetFromJsonAsync<OrderView>($"/api/orders/{reference}")!;

    private HttpRequestMessage Webhook(string body, string? signature = null)
    {
        var sandbox = (SandboxGateway)factory.Services.GetRequiredService<PaymentGateways>().Find("sandbox")!;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sandbox") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add(SandboxGateway.SignatureHeader, signature ?? sandbox.Sign(body));
        return req;
    }

    private (string Body, string Signature) Signed(string txnId, string outcome, long amount) =>
        ((SandboxGateway)factory.Services.GetRequiredService<PaymentGateways>().Find("sandbox")!).SignedEvent(txnId, outcome, amount, "BDT");

    [Fact]
    public async Task Access_is_granted_only_after_a_verified_webhook_at_the_server_price()
    {
        var course = await CourseAsync(150_000, promo: 120_000);
        var learner = await factory.SignedIn(Email());

        var (started, txnId) = await CheckoutAsync(learner, course.ProductId);
        var order = await OrderAsync(learner, started.Reference);
        Assert.Equal(("pending", 120_000L), (order.Status, order.TotalMinor));
        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);

        // A forged webhook is rejected and changes nothing.
        var (body, _) = Signed(txnId, "succeeded", 120_000);
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Webhook(body, "sha256=00"))).StatusCode);
        Assert.Equal("pending", (await OrderAsync(learner, started.Reference)).Status);

        // The real one, delivered twice, grants exactly once and issues one receipt.
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(body))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(body))).StatusCode);
        order = await OrderAsync(learner, started.Reference);
        Assert.Equal("successful", order.Status);
        Assert.StartsWith("R-", order.ReceiptNumber);
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);

        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orderId = await db.Orders.Where(o => o.Reference == started.Reference).Select(o => o.Id).SingleAsync();
            Assert.Equal(1, await db.Entitlements.CountAsync(e => e.CourseId == course.Id));
            Assert.Equal(1, await db.PaymentEvents.CountAsync(e => e.SignatureValid == false && e.Provider == "sandbox"));
            Assert.Equal(120_000, (await db.OrderItems.SingleAsync(i => i.OrderId == orderId)).UnitPriceMinor);
        }

        // Receipts are private to the buyer (and finance/support staff).
        var receipt = await learner.GetFromJsonAsync<ReceiptView>($"/api/receipts/{order.ReceiptNumber}");
        Assert.Equal((started.Reference, 120_000L), (receipt!.OrderReference, receipt.TotalMinor));
        Assert.Equal(HttpStatusCode.NotFound, (await (await factory.SignedIn(Email())).GetAsync($"/api/receipts/{order.ReceiptNumber}")).StatusCode);

        // Owning the course already: no second sale.
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/checkout", new CheckoutRequest(course.ProductId))).StatusCode);
        var mine = await learner.GetFromJsonAsync<List<OrderView>>("/api/me/orders");
        Assert.Equal(started.Reference, Assert.Single(mine!).Reference);
    }

    [Fact]
    public async Task Failed_cancelled_and_mismatched_payments_never_grant()
    {
        var course = await CourseAsync();
        var learner = await factory.SignedIn(Email());

        var (failed, failedTxn) = await CheckoutAsync(learner, course.ProductId);
        await PayAsync(learner, failedTxn, "fail");
        Assert.Equal("failed", (await OrderAsync(learner, failed.Reference)).Status);
        // A success arriving after a failure is not applied, but is flagged for a person to check.
        var (late, _) = Signed(failedTxn, "succeeded", 150_000);
        await factory.CreateClient().SendAsync(Webhook(late));
        Assert.Equal("failed", (await OrderAsync(learner, failed.Reference)).Status);

        var (cancelled, cancelledTxn) = await CheckoutAsync(learner, course.ProductId);
        await PayAsync(learner, cancelledTxn, "cancel");
        Assert.Equal("cancelled", (await OrderAsync(learner, cancelled.Reference)).Status);

        var (mismatch, mismatchTxn) = await CheckoutAsync(learner, course.ProductId);
        var (body, _) = Signed(mismatchTxn, "succeeded", 1);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(Webhook(body))).StatusCode);
        Assert.Equal("pending", (await OrderAsync(learner, mismatch.Reference)).Status);

        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
        await using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issues = await db.ReconciliationRecords.Where(r => r.TransactionId != null).Join(db.PaymentTransactions, r => r.TransactionId, t => t.Id, (r, t) => new { r.Issue, t.ProviderTxnId })
            .Where(x => x.ProviderTxnId == failedTxn || x.ProviderTxnId == mismatchTxn).Select(x => x.Issue).OrderBy(x => x).ToListAsync();
        Assert.Equal(["amount_mismatch", "paid_after_close"], issues);
    }

    [Fact]
    public async Task Expired_orders_still_honour_a_late_verified_payment()
    {
        var course = await CourseAsync();
        var learner = await factory.SignedIn(Email());
        var (started, txnId) = await CheckoutAsync(learner, course.ProductId);
        await using (var scope = factory.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Orders.Where(o => o.Reference == started.Reference).ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        Assert.Equal("expired", (await OrderAsync(learner, started.Reference)).Status);
        await PayAsync(learner, txnId);
        Assert.Equal("successful", (await OrderAsync(learner, started.Reference)).Status);
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
    }

    [Fact]
    public async Task Refunds_are_finance_only_capped_and_a_full_refund_revokes_access()
    {
        var course = await CourseAsync(100_000);
        var learner = await factory.SignedIn(Email());
        var (started, txnId) = await CheckoutAsync(learner, course.ProductId);
        await PayAsync(learner, txnId);

        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        var orders = await finance.GetFromJsonAsync<List<OrderRow>>($"/api/admin/orders?q={started.Reference}");
        var orderId = Assert.Single(orders!).Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await learner.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(100, "x", false))).StatusCode);
        var support = await factory.SignedIn(Email(), RoleKeys.SupportStaff);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(100, "x", false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync($"/api/admin/orders/{orderId}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(100_001, "too much", false))).StatusCode);
        await Ok<RefundRow>(await finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(40_000, "partial", false)));
        Assert.Equal("partially_refunded", (await OrderAsync(learner, started.Reference)).Status);
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(60_001, "over", false))).StatusCode);
        var rest = await Ok<RefundRow>(await finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(60_000, "rest", true)));
        Assert.True(rest.IsManual);
        var order = await OrderAsync(learner, started.Reference);
        Assert.Equal(("refunded", 100_000L), (order.Status, order.RefundedMinor));
        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);

        var detail = await finance.GetFromJsonAsync<OrderDetail>($"/api/admin/orders/{orderId}");
        Assert.Equal(2, detail!.Refunds.Count);
        Assert.Equal("revoked", Assert.Single(detail.Access).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(1, "again", false))).StatusCode);
    }

    [Fact]
    public async Task Plans_are_priced_by_finance_and_a_subscription_unlocks_covered_courses()
    {
        var course = await CourseAsync();
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        var slug = Slug();
        var plan = new PlanRequest(slug, "Monthly", "All courses", 50_000, "month", null, 3, false, [course.Id], [], "active", 0);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignedIn(Email(), RoleKeys.ContentManager)).PostAsJsonAsync("/api/admin/plans", plan)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync("/api/admin/plans", plan with { CourseIds = [] })).StatusCode);
        var created = await Ok<PlanEditor>(await finance.PostAsJsonAsync("/api/admin/plans", plan));
        Assert.Equal(HttpStatusCode.Conflict, (await finance.PostAsJsonAsync("/api/admin/plans", plan)).StatusCode);
        var updated = await Ok<PlanEditor>(await finance.PutAsJsonAsync($"/api/admin/plans/{created.Id}", plan with { PriceMinor = 45_000 }));
        Assert.Equal([course.Id], updated.CourseIds);

        var offer = Assert.Single((await factory.Client().GetFromJsonAsync<List<PlanOffer>>("/api/plans"))!, p => p.Slug == slug);
        Assert.Equal(45_000, offer.PriceMinor);

        var learner = await factory.SignedIn(Email());
        var (_, txnId) = await CheckoutAsync(learner, offer.ProductId);
        await PayAsync(learner, txnId);
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
        var membership = await learner.GetFromJsonAsync<Membership>("/api/me/membership");
        var sub = Assert.Single(membership!.Subscriptions);
        Assert.Equal(("Monthly", "active"), (sub.PlanName, sub.Status));

        // Archiving the plan stops new sales but keeps existing members.
        await Ok<PlanEditor>(await finance.PutAsJsonAsync($"/api/admin/plans/{created.Id}", plan with { Status = "archived" }));
        Assert.DoesNotContain((await factory.Client().GetFromJsonAsync<List<PlanOffer>>("/api/plans"))!, p => p.Slug == slug);
        Assert.Equal(HttpStatusCode.NotFound, (await learner.PostAsJsonAsync("/api/checkout", new CheckoutRequest(offer.ProductId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
    }

    [Fact]
    public async Task Course_price_is_set_by_finance_and_shown_on_the_course_page()
    {
        await using var scope = factory.Scope();
        var (course, _, _) = await new Seed(scope.ServiceProvider.GetRequiredService<AppDbContext>()).CourseAsync(Slug());
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PutAsJsonAsync($"/api/admin/courses/{course.Id}/price", new PriceRequest(100, 200, null, null, null, "active"))).StatusCode);
        await finance.PutAsJsonAsync($"/api/admin/courses/{course.Id}/price", new PriceRequest(200_000, 150_000, null, DateTimeOffset.UtcNow.AddDays(1), 365, "active"));

        var detail = await factory.Client().GetFromJsonAsync<Endpoints.CatalogEndpoints.CourseDetail>($"/api/courses/{course.Slug}");
        Assert.Equal((150_000L, 200_000L, (int?)365), (detail!.Price!.PriceMinor, detail.Price.RegularMinor, detail.Price.AccessDays));
        var row = Assert.Single((await finance.GetFromJsonAsync<List<CoursePrice>>("/api/admin/pricing"))!, p => p.CourseId == course.Id);
        Assert.Equal("active", row.SaleStatus);
    }

    [Fact]
    public async Task Finance_can_grant_and_revoke_access_by_hand()
    {
        var course = await CourseAsync();
        var email = Email();
        var learner = await factory.SignedIn(email);
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync("/api/admin/entitlements", new GrantRequest("nobody@x.test", course.Id, null, null, "gift"))).StatusCode);
        var grant = await Ok<AdminEntitlement>(await finance.PostAsJsonAsync("/api/admin/entitlements", new GrantRequest(email, course.Id, null, null, "scholarship")));
        Assert.Equal(("manual_grant", "active"), (grant.Source, grant.Status));
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);

        var revoked = await Ok<AdminEntitlement>(await finance.PostAsJsonAsync($"/api/admin/entitlements/{grant.Id}/revoke", new RevokeRequest("ended")));
        Assert.Equal("revoked", revoked.Status);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
        Assert.Single((await finance.GetFromJsonAsync<List<AdminEntitlement>>($"/api/admin/entitlements?email={email}"))!);
    }
}
