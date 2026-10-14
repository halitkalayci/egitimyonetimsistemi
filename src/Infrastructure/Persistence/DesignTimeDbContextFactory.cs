using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` only. The connection string comes from the environment so no credential is
/// ever written into the repository (docs/security.md, Secrets).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimeFallback =
        "Host=localhost;Port=5432;Database=egitimyonetim;Username=egitim;Password=design-time-only";

    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? DesignTimeFallback;

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
