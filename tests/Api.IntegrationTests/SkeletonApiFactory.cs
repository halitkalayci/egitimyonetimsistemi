using Infrastructure;
using Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.IntegrationTests;

/// <summary>
/// Boots the real application against a given connection string. The environment is deliberately
/// NOT Development, so the application does not migrate or seed on start and each test decides
/// what the database contains.
/// </summary>
public sealed class SkeletonApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public SkeletonApiFactory(string connectionString) => _connectionString = connectionString;

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await Services.MigrateDatabaseAsync(cancellationToken);

        using IServiceScope scope = Services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.SystemInfoEntries.RemoveRange(context.SystemInfoEntries);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddEntryAsync(string key, string value, CancellationToken cancellationToken)
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.SystemInfoEntries.Add(
            Domain.System.SystemInfoEntry.Create(key, value, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync(cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Staging);
        builder.UseSetting("ConnectionStrings:Database", _connectionString);
    }
}
