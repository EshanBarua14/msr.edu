using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.FinanceEndpoints;
using static MsrEdu.Api.Endpoints.PaymentEndpoints;

namespace MsrEdu.Api.Tests;

/// <summary>Phase 8: tampering, replays, races and cross-account access on the payment path.</summary>
public class SecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Started(string Reference, string RedirectUrl);
    private sealed record Paid(Guid ProductId, Guid CourseId, Guid PaidLessonId);

    private static string Email() => $"{Guid.NewGuid():N}@x.test";

    private SandboxGateway Sandbox => (SandboxGateway)factory.Services.GetRequiredService<PaymentGateways>().Find("sandbox")!;

    private async Task<Paid> CourseAsync(long price = 120_000)
    {
        await using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (course, _, paid) = await new Seed(db).CourseAsync($"c-{Guid.NewGuid():N}"[..14]);
        var product = new Product { Kind = ProductKind.Course, CourseId = course.Id, Name = course.Title, PriceMinor = price, Status = SaleStatus.Active };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return new Paid(product.Id, course.Id, paid.Id);
    }

    private static async Task<(string Reference, string TxnId)> CheckoutAsync(HttpClient client, Guid productId)
    {
        var res = await client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productId));
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var started = (await res.Content.ReadFromJsonAsync<Started>())!;
        return (started.Reference, started.RedirectUrl.Split('/').Last());
    }

    private static HttpRequestMessage Webhook(string body, string signature)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sandbox") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add(SandboxGateway.SignatureHeader, signature);
        return req;
    }

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Scope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Checkout_ignores_prices_sent_by_the_browser_and_needs_the_csrf_header()
    {
        var course = await CourseAsync(120_000);
        var learner = await factory.SignedIn(Email());
        var res = await learner.PostAsJsonAsync("/api/checkout", new { productId = course.ProductId, priceMinor = 1, totalMinor = 1, currency = "USD" });
        var started = (await res.Content.ReadFromJsonAsync<Started>())!;
        var order = await learner.GetFromJsonAsync<OrderView>($"/api/orders/{started.Reference}");
        Assert.Equal((120_000L, "BDT"), (order!.TotalMinor, order.Currency.Trim()));

        var noCsrf = factory.CreateClient();
        var csrf = await noCsrf.PostAsJsonAsync("/api/checkout", new CheckoutRequest(course.ProductId));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }

    [Fact]
    public async Task A_body_changed_after_signing_is_rejected()
    {
        var course = await CourseAsync(120_000);
        var learner = await factory.SignedIn(Email());
        var (reference, txnId) = await CheckoutAsync(learner, course.ProductId);
        var (body, signature) = Sandbox.SignedEvent(txnId, "succeeded", 120_000, "BDT");

        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Webhook(body.Replace("120000", "100"), signature))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Webhook(body, signature.ToUpperInvariant()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Webhook(body, ""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/madeup") { Content = new StringContent(body) })).StatusCode);
        Assert.Equal("pending", (await learner.GetFromJsonAsync<OrderView>($"/api/orders/{reference}"))!.Status);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
    }

    [Fact]
    public async Task Ten_simultaneous_deliveries_of_one_event_grant_once()
    {
        var course = await CourseAsync();
        var learner = await factory.SignedIn(Email());
        var (reference, txnId) = await CheckoutAsync(learner, course.ProductId);
        var (body, signature) = Sandbox.SignedEvent(txnId, "succeeded", 120_000, "BDT");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => factory.CreateClient().SendAsync(Webhook(body, signature))));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var (grants, receipts, events) = await InDbAsync(async db =>
        {
            var orderId = await db.Orders.Where(o => o.Reference == reference).Select(o => o.Id).SingleAsync();
            return (await db.Entitlements.CountAsync(e => e.CourseId == course.CourseId),
                await db.Receipts.CountAsync(r => r.OrderId == orderId),
                await db.PaymentEvents.CountAsync(e => e.Provider == "sandbox" && e.ProcessedAt != null && e.TransactionId != null
                    && db.PaymentTransactions.Any(t => t.Id == e.TransactionId && t.ProviderTxnId == txnId)));
        });
        Assert.Equal((1, 1, 1), (grants, receipts, events));
    }

    [Fact]
    public async Task Racing_success_and_failure_events_leave_one_consistent_outcome()
    {
        for (var round = 0; round < 5; round++)
        {
            var course = await CourseAsync();
            var learner = await factory.SignedIn(Email());
            var (reference, txnId) = await CheckoutAsync(learner, course.ProductId);
            var ok = Sandbox.SignedEvent(txnId, "succeeded", 120_000, "BDT");
            var failed = Sandbox.SignedEvent(txnId, "failed", 120_000, "BDT");
            await Task.WhenAll(factory.CreateClient().SendAsync(Webhook(ok.Body, ok.Signature)), factory.CreateClient().SendAsync(Webhook(failed.Body, failed.Signature)));

            var order = (await learner.GetFromJsonAsync<OrderView>($"/api/orders/{reference}"))!;
            var hasAccess = (await learner.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode == HttpStatusCode.OK;
            var txnStatus = await InDbAsync(db => db.PaymentTransactions.Where(t => t.ProviderTxnId == txnId).Select(t => t.Status).SingleAsync());
            Assert.Contains(order.Status, new[] { "successful", "failed" });
            Assert.Equal(order.Status == "successful", hasAccess);
            Assert.Equal(order.Status == "successful" ? PaymentState.Successful : PaymentState.Failed, txnStatus);
        }
    }

    [Fact]
    public async Task Two_simultaneous_full_refunds_refund_once()
    {
        var course = await CourseAsync(90_000);
        var learner = await factory.SignedIn(Email());
        var (reference, txnId) = await CheckoutAsync(learner, course.ProductId);
        (await learner.PostAsJsonAsync($"/api/sandbox/payments/{txnId}", new SandboxOutcome("success"))).EnsureSuccessStatusCode();
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        var orderId = (await finance.GetFromJsonAsync<List<OrderRow>>($"/api/admin/orders?q={reference}"))!.Single().Id;

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(90_000, "duplicate click", false))));
        Assert.Single(results, r => r.IsSuccessStatusCode);
        var order = (await learner.GetFromJsonAsync<OrderView>($"/api/orders/{reference}"))!;
        Assert.Equal(("refunded", 90_000L), (order.Status, order.RefundedMinor));
    }

    [Fact]
    public async Task Nobody_else_can_see_or_pay_my_order()
    {
        var course = await CourseAsync();
        var me = await factory.SignedIn(Email());
        var (reference, txnId) = await CheckoutAsync(me, course.ProductId);
        var other = await factory.SignedIn(Email());

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{reference}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/sandbox/payments/{txnId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/sandbox/payments/{txnId}", new SandboxOutcome("success"))).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<OrderView>>("/api/me/orders"))!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().GetAsync($"/api/orders/{reference}")).StatusCode);

        (await me.PostAsJsonAsync($"/api/sandbox/payments/{txnId}", new SandboxOutcome("success"))).EnsureSuccessStatusCode();
        var receipt = (await me.GetFromJsonAsync<OrderView>($"/api/orders/{reference}"))!.ReceiptNumber;
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/receipts/{receipt}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/admin/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await other.GetAsync($"/api/lessons/{course.PaidLessonId}/content")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var res = await factory.Client().GetAsync("/api/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(res.Headers.Contains("Content-Security-Policy"));

        var page = await factory.Client().GetAsync("/courses");
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
    }
}

/// <summary>Runs with the production limits turned down so they trip quickly.</summary>
public class RateLimitTests(RateLimitTests.LowLimits factory) : IClassFixture<RateLimitTests.LowLimits>
{
    public class LowLimits : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimits:CheckoutPerMinute", "2");
            builder.UseSetting("RateLimits:SignInPerMinute", "3");
        }
    }

    [Fact]
    public async Task Checkout_and_sign_in_are_rate_limited()
    {
        var learner = await factory.SignedIn($"{Guid.NewGuid():N}@x.test");
        var any = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await learner.PostAsJsonAsync("/api/checkout", new CheckoutRequest(any))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await learner.PostAsJsonAsync("/api/checkout", new CheckoutRequest(any))).StatusCode);
        var limited = await learner.PostAsJsonAsync("/api/checkout", new CheckoutRequest(any));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());

        // Checkout limits are per account: another learner is unaffected.
        var other = await factory.SignedIn($"{Guid.NewGuid():N}@x.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/checkout", new CheckoutRequest(any))).StatusCode);

        await factory.SignedIn($"{Guid.NewGuid():N}@x.test");
        var res = await factory.Client().PostAsJsonAsync("/api/auth/dev-login", new { email = "x@x.test", roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }
}
