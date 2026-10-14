using Application.Abstractions;

using Infrastructure.Persistence;
using Infrastructure.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Composition root for everything that touches the outside world. Api calls this and stays
    /// free of EF Core itself (docs/architecture.md, forbidden dependency 4).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ISystemInfoRepository, SystemInfoRepository>();
        services.AddScoped<IDatabaseTimeProvider, NpgsqlDatabaseTimeProvider>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>
    /// Applies pending migrations and seeds the neutral entry the skeleton reads. Development only:
    /// production applies migrations as a deployment step (plan 0001, risk 5).
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await SystemInfoSeed.EnsureSeededAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Schema only, no seed data: integration tests need a deterministic, empty database
    /// (docs/testing.md, Determinism).
    /// </summary>
    public static async Task MigrateDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
