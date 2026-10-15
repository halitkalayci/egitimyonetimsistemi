using Application.Abstractions;

using Infrastructure.Persistence;
using Infrastructure.Security;
using Infrastructure.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

/// <summary>
/// Everything the application needs from the outside world. The signing key has no default on
/// purpose: a missing key must stop startup rather than let the application run with a guessable
/// secret (spec 0002, AC-27).
/// </summary>
public sealed record InfrastructureOptions(
    string ConnectionString,
    string JwtSigningKey,
    TimeSpan AccessTokenLifetime,
    SeedAdminOptions? SeedAdmin);

public static class DependencyInjection
{
    /// <summary>Minimum length for the signing key: 256 bits, as HMAC-SHA256 expects.</summary>
    public const int MinimumSigningKeyLength = 32;

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        if (string.IsNullOrWhiteSpace(options.JwtSigningKey)
            || options.JwtSigningKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey is missing or shorter than {MinimumSigningKeyLength} characters. "
                + "Provide it through configuration (see .env.example); it is never stored in the repository.");
        }

        services.AddDbContext<AppDbContext>(dbContext => dbContext
            .UseNpgsql(options.ConnectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ISystemInfoRepository, SystemInfoRepository>();
        services.AddScoped<IDatabaseTimeProvider, NpgsqlDatabaseTimeProvider>();
        services.AddScoped<IParticipantRepository, ParticipantRepository>();
        services.AddScoped<IRefreshChainRepository, RefreshChainRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
        services.AddSingleton(new JwtOptions(options.JwtSigningKey, options.AccessTokenLifetime));
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        if (options.SeedAdmin is SeedAdminOptions seedAdmin)
        {
            services.AddSingleton(seedAdmin);
        }

        return services;
    }

    /// <summary>
    /// Applies pending migrations, seeds the neutral system-info row and, when configured, the
    /// first administrator. Runs on start in Development; everywhere else it is the explicit
    /// deployment step `--initialize-database` (README, "Üretime kurulum"), never a side effect
    /// of serving requests (plan 0001, risk 5).
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

        if (scope.ServiceProvider.GetService<SeedAdminOptions>() is SeedAdminOptions seedAdmin)
        {
            await IdentitySeed.EnsureAdminAsync(
                context,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
                seedAdmin,
                cancellationToken).ConfigureAwait(false);
        }
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
