using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MsrEdu.Api.Data;

/// <summary>Used by `dotnet ef` so migrations can be created without running the app.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=msr_edu_design", AppDbContext.MapEnums)
            .UseSnakeCaseNamingConvention()
            .Options);
}
