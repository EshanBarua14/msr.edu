using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MsrEdu.Api.Data;

/// <summary>
/// Used by `dotnet ef`. Reads ConnectionStrings:Default the same way the app does in Development
/// (appsettings, user secrets, then environment variables), so `dotnet ef database update` migrates your database.
/// </summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<DesignTimeFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connection = config.GetConnectionString("Default") is { Length: > 0 } c ? c : "Host=localhost;Database=msr_edu_design";
        return new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, AppDbContext.MapEnums)
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
