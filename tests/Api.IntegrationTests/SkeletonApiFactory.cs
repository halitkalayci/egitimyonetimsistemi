using Infrastructure;
using Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
    /// <summary>A key only this test process knows; the application refuses to start without one
    /// (spec 0002, AC-27), which StartupConfigurationTests asserts separately.</summary>
    internal const string TestSigningKey = "integration-test-signing-key-0123456789";

    private readonly string _connectionString;

    public SkeletonApiFactory(string connectionString) => _connectionString = connectionString;

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await Services.MigrateDatabaseAsync(cancellationToken);

        using IServiceScope scope = Services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.RefreshChains.RemoveRange(context.RefreshChains);
        context.Participants.RemoveRange(context.Participants);
        context.SystemInfoEntries.RemoveRange(context.SystemInfoEntries);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Reads the stored password hashes straight out of the column (spec 0002, AC-9).</summary>
    public async Task<IReadOnlyList<string>> ReadPasswordHashesAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await context.Participants
            .AsNoTracking()
            .Where(person => person.Account != null)
            .OrderBy(person => person.Id)
            .Select(person => person.Account!.PasswordHash)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads the stored failed-attempt counter of one account (spec 0002, AC-3).</summary>
    public async Task<int> ReadFailedAttemptsAsync(Guid participantId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await context.Participants
            .AsNoTracking()
            .Where(person => person.Id == participantId)
            .Select(person => person.Account!.FailedAttempts)
            .SingleAsync(cancellationToken);
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
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
    }
}
