using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsrEdu.Api;
using MsrEdu.Api.Data;
using Npgsql;

namespace MsrEdu.Api.Tests;

/// <summary>
/// One throwaway PostgreSQL database per test class, migrated from scratch.
/// Server: MSR_TEST_DB (default: local postgres/postgres on 5432).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string DbServer =
        Environment.GetEnvironmentVariable("MSR_TEST_DB") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
    private readonly string _database = $"msr_test_{Guid.NewGuid():N}";

    public string ConnectionString => new NpgsqlConnectionStringBuilder(DbServer) { Database = _database }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Auth:EnableDevLogin", "true");
        builder.UseSetting("Auth:OwnerEmails:0", "owner@test.local");
    }

    public async ValueTask InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(DbServer) { Database = "postgres" }.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"drop database if exists \"{_database}\" with (force)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    /// <summary>A client that sends the CSRF header, like the real frontend.</summary>
    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add(Startup.CsrfHeader, "1");
        return client;
    }

    public async Task<HttpClient> SignedIn(string email, params string[] roles)
    {
        var client = Client();
        var res = await client.PostAsJsonAsync("/api/auth/dev-login", new { email, roles });
        res.EnsureSuccessStatusCode();
        return client;
    }
}
