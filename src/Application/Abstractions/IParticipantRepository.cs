using Application.Common;

using Domain.People;

namespace Application.Abstractions;

public interface IParticipantRepository
{
    Task<Result<Participant?>> FindByEmailAsync(string normalisedEmail, CancellationToken cancellationToken);

    Task<Result<Participant?>> FindByIdAsync(Guid participantId, CancellationToken cancellationToken);

    Task<Result<bool>> EmailExistsAsync(string normalisedEmail, CancellationToken cancellationToken);

    /// <summary>Firm scope is a parameter, never optional: BR-8 is enforced in this layer.</summary>
    Task<Result<IReadOnlyList<Participant>>> ListWithAccountsAsync(
        Guid? memberFirmId,
        CancellationToken cancellationToken);

    /// <summary>Admin accounts that are not disabled - the people who can still run the system.</summary>
    Task<Result<int>> CountActiveAdminsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Counts one wrong password in a single atomic statement - the counter is incremented, and the
    /// lock set, by the database rather than by writing back a value read earlier. Parallel wrong
    /// passwords therefore never conflict, so none of them can answer differently from an unknown
    /// e-mail (spec 0002, AC-2), and none of them is lost (AC-3). A locked or disabled account is
    /// left untouched, as <see cref="UserAccount.TryLogin"/> does.
    /// </summary>
    Task<Result<int>> RegisterFailedLoginAsync(
        Guid participantId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    void Add(Participant participant);
}
