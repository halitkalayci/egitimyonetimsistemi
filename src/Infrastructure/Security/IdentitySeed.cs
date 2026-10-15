using Application.Abstractions;

using Domain.People;

using Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Security;

/// <summary>
/// The first administrator, so a fresh database is usable at all. Credentials come from
/// configuration and never from the repository (docs/security.md, Secrets); the account is created
/// with the temporary-password flag, so whoever receives it must replace it on first login.
/// </summary>
public sealed record SeedAdminOptions(string Email, string Password);

internal static class IdentitySeed
{
    internal static async Task EnsureAdminAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        SeedAdminOptions options,
        CancellationToken cancellationToken)
    {
        string email = UserAccount.NormaliseEmail(options.Email);

        bool anyAccountExists = await context.Participants
            .AnyAsync(person => person.Account != null, cancellationToken)
            .ConfigureAwait(false);

        if (anyAccountExists)
        {
            // Never touch an existing installation: a seeded password would be a known credential.
            return;
        }

        context.Participants.Add(Participant.CreateWithAccount(
            "Eğitim Sorumlusu",
            memberFirmId: null,
            email,
            passwordHasher.Hash(options.Password),
            Role.Admin));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
