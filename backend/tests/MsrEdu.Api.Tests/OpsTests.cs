using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;
using MsrEdu.Api.Services;
using static MsrEdu.Api.Endpoints.FinanceEndpoints;
using static MsrEdu.Api.Endpoints.OpsEndpoints;
using static MsrEdu.Api.Endpoints.PaymentEndpoints;

namespace MsrEdu.Api.Tests;

public class OpsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Started(string Reference, string RedirectUrl);

    private static string Email() => $"{Guid.NewGuid():N}@x.test";

    private static async Task<T> Ok<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Guid> ProductAsync(string name, long price)
    {
        await using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (course, _, _) = await new Seed(db).CourseAsync($"c-{Guid.NewGuid():N}"[..14]);
        var product = new Product { Kind = ProductKind.Course, CourseId = course.Id, Name = name, PriceMinor = price, Status = SaleStatus.Active };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>Buys through the sandbox; returns the order reference and the sandbox payment id.</summary>
    private static async Task<(string Reference, string TxnId)> BuyAsync(HttpClient learner, Guid productId, bool pay = true)
    {
        var started = await Ok<Started>(await learner.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productId)));
        var txnId = started.RedirectUrl.Split('/').Last();
        if (pay) (await learner.PostAsJsonAsync($"/api/sandbox/payments/{txnId}", new SandboxOutcome("success"))).EnsureSuccessStatusCode();
        return (started.Reference, txnId);
    }

    [Fact]
    public async Task Revenue_report_counts_verified_payments_and_completed_refunds()
    {
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        var before = await Ok<RevenueReport>(await finance.GetAsync("/api/admin/reports/revenue"));

        var name = $"Report course {Guid.NewGuid():N}"[..24];
        var learner = await factory.SignedIn(Email());
        var (paidRef, _) = await BuyAsync(learner, await ProductAsync(name, 100_000));
        await BuyAsync(await factory.SignedIn(Email()), await ProductAsync("=HYPERLINK(\"x\")", 50_000));
        await BuyAsync(await factory.SignedIn(Email()), await ProductAsync("Never paid", 70_000), pay: false);

        var orderId = (await finance.GetFromJsonAsync<List<OrderRow>>($"/api/admin/orders?q={paidRef}"))!.Single().Id;
        await Ok<RefundRow>(await finance.PostAsJsonAsync($"/api/admin/orders/{orderId}/refunds", new RefundRequest(30_000, "partial", false)));

        var report = await Ok<RevenueReport>(await finance.GetAsync("/api/admin/reports/revenue"));
        Assert.Equal(30, report.Rows.Count);
        Assert.Equal(before.Totals.Payments + 2, report.Totals.Payments);
        Assert.Equal(before.Totals.GrossMinor + 150_000, report.Totals.GrossMinor);
        Assert.Equal(before.Totals.RefundedMinor + 30_000, report.Totals.RefundedMinor);
        Assert.Equal(report.Totals.GrossMinor - report.Totals.RefundedMinor, report.Totals.NetMinor);
        Assert.Equal(report.Totals.GrossMinor, report.Rows.Sum(r => r.GrossMinor));
        Assert.Equal(100_000, Assert.Single(report.Products, p => p.Name == name).GrossMinor);
        Assert.DoesNotContain(report.Products, p => p.Name == "Never paid");

        var monthly = await Ok<RevenueReport>(await finance.GetAsync("/api/admin/reports/revenue?group=month"));
        Assert.Equal(12, monthly.Rows.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.GetAsync("/api/admin/reports/revenue?from=2020-01-01&to=2026-01-01")).StatusCode);

        var csv = await finance.GetStringAsync("/api/admin/reports/revenue.csv");
        Assert.Contains($"{paidRef},", csv);
        Assert.Contains("-300.00", csv);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);

        var content = await factory.SignedIn(Email(), RoleKeys.ContentManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await content.GetAsync("/api/admin/reports/revenue")).StatusCode);
    }

    [Fact]
    public async Task Payment_discrepancies_are_queued_and_closed_with_a_note()
    {
        var learner = await factory.SignedIn(Email());
        var (reference, txnId) = await BuyAsync(learner, await ProductAsync("Mismatch course", 80_000), pay: false);
        var sandbox = (SandboxGateway)factory.Services.GetRequiredService<PaymentGateways>().Find("sandbox")!;
        var (body, signature) = sandbox.SignedEvent(txnId, "succeeded", 1, "BDT");
        var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sandbox") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        webhook.Headers.Add(SandboxGateway.SignatureHeader, signature);
        (await factory.CreateClient().SendAsync(webhook)).EnsureSuccessStatusCode();

        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        var issue = Assert.Single((await finance.GetFromJsonAsync<List<IssueView>>("/api/admin/reconciliation"))!, i => i.OrderReference == reference);
        Assert.Equal(("amount_mismatch", "open"), (issue.Issue, issue.Status));

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/admin/reconciliation/{issue.Id}", new IssueUpdate("resolved", " "))).StatusCode);
        await Ok<JsonElement>(await finance.PostAsJsonAsync($"/api/admin/reconciliation/{issue.Id}", new IssueUpdate("investigating", null)));
        Assert.Contains((await finance.GetFromJsonAsync<List<IssueView>>("/api/admin/reconciliation"))!, i => i.Id == issue.Id && i.Status == "investigating");
        await Ok<JsonElement>(await finance.PostAsJsonAsync($"/api/admin/reconciliation/{issue.Id}", new IssueUpdate("resolved", "Provider confirmed a test charge")));
        Assert.DoesNotContain((await finance.GetFromJsonAsync<List<IssueView>>("/api/admin/reconciliation"))!, i => i.Id == issue.Id);
        var closed = Assert.Single((await finance.GetFromJsonAsync<List<IssueView>>("/api/admin/reconciliation?status=closed"))!, i => i.Id == issue.Id);
        Assert.NotNull(closed.ResolvedBy);
    }

    [Fact]
    public async Task Audit_log_shows_who_changed_what_and_pages()
    {
        var email = Email();
        var finance = await factory.SignedIn(email, RoleKeys.FinanceManager);
        await using (var scope = factory.Scope())
        {
            var (course, _, _) = await new Seed(scope.ServiceProvider.GetRequiredService<AppDbContext>()).CourseAsync($"c-{Guid.NewGuid():N}"[..14]);
            for (var price = 1; price <= 3; price++)
                (await finance.PutAsJsonAsync($"/api/admin/courses/{course.Id}/price", new PriceRequest(price * 10_000, null, null, null, null, "active"))).EnsureSuccessStatusCode();
        }

        var page = await Ok<AuditPage>(await finance.GetAsync($"/api/admin/audit?entity=products&actor={email}"));
        Assert.Equal(3, page.Rows.Count);
        Assert.All(page.Rows, r => Assert.Equal(email, r.Actor));
        Assert.Equal(30_000, page.Rows[0].After!.Value.GetProperty("price_minor").GetInt64());
        Assert.Equal(20_000, page.Rows[0].Before!.Value.GetProperty("price_minor").GetInt64());
        Assert.Contains("products", (await finance.GetFromJsonAsync<List<string>>("/api/admin/audit/entities"))!);

        var first = await Ok<AuditPage>(await finance.GetAsync("/api/admin/audit"));
        if (first.Next is { } next)
            Assert.All((await Ok<AuditPage>(await finance.GetAsync($"/api/admin/audit?before={next}"))).Rows, r => Assert.True(r.Id < next));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignedIn(Email(), RoleKeys.SupportStaff)).GetAsync("/api/admin/audit")).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_edits_settings_and_values_are_checked()
    {
        var finance = await factory.SignedIn(Email(), RoleKeys.FinanceManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/admin/settings")).StatusCode);
        var owner = await factory.SignedIn(Email(), RoleKeys.OwnerAdmin);
        var settings = await Ok<List<SettingView>>(await owner.GetAsync("/api/admin/settings"));
        Assert.Contains(settings, s => s.Key == "order.expiry_minutes");

        static object Body(object value) => new { value };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/admin/settings/order.expiry_minutes", Body(1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/admin/settings/currency.default", Body("USD"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/admin/settings/refund.access_policy", Body("never"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/admin/settings/made.up", Body(true))).StatusCode);
        var saved = await Ok<SettingView>(await owner.PutAsJsonAsync("/api/admin/settings/order.expiry_minutes", Body(30)));
        Assert.Equal(30, saved.Value.GetInt32());
        await Ok<SettingView>(await owner.PutAsJsonAsync("/api/admin/settings/order.expiry_minutes", Body(60)));
        await Ok<SettingView>(await owner.PutAsJsonAsync("/api/admin/settings/course.default_access_days", Body(null!)));

        var setup = await Ok<SetupStatus>(await owner.GetAsync("/api/admin/setup"));
        Assert.True(setup.Database);
        Assert.Equal(("sandbox", true), (setup.Gateway, setup.GatewayIsSandbox));
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsync("/api/admin/settings/confirm-defaults", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/admin/settings/confirm-defaults", null)).StatusCode);
        Assert.True((await Ok<SetupStatus>(await finance.GetAsync("/api/admin/setup"))).DefaultsConfirmed);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void Csv_cells_are_quoted_and_formula_safe(string input, string expected) => Assert.Equal(expected, Cell(input));
}
