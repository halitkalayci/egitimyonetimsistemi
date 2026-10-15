using Application.Abstractions;
using Application.Common;

using Domain.People;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

internal sealed class ParticipantRepository : IParticipantRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<ParticipantRepository> _logger;

    public ParticipantRepository(AppDbContext context, ILogger<ParticipantRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<Participant?>> FindByEmailAsync(
        string normalisedEmail,
        CancellationToken cancellationToken)
    {
        try
        {
            Participant? person = await _context.Participants
                .FirstOrDefaultAsync(
                    candidate => candidate.Account != null && candidate.Account.Email == normalisedEmail,
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<Participant?>.Success(person);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.ParticipantReadFailed(_logger, exception);

            return Result<Participant?>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public async Task<Result<Participant?>> FindByIdAsync(Guid participantId, CancellationToken cancellationToken)
    {
        try
        {
            Participant? person = await _context.Participants
                .FirstOrDefaultAsync(candidate => candidate.Id == participantId, cancellationToken)
                .ConfigureAwait(false);

            return Result<Participant?>.Success(person);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.ParticipantReadFailed(_logger, exception);

            return Result<Participant?>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public async Task<Result<bool>> EmailExistsAsync(string normalisedEmail, CancellationToken cancellationToken)
    {
        try
        {
            bool exists = await _context.Participants
                .AnyAsync(
                    candidate => candidate.Account != null && candidate.Account.Email == normalisedEmail,
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<bool>.Success(exists);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.ParticipantReadFailed(_logger, exception);

            return Result<bool>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public async Task<Result<IReadOnlyList<Participant>>> ListWithAccountsAsync(
        Guid? memberFirmId,
        CancellationToken cancellationToken)
    {
        try
        {
            IQueryable<Participant> query = _context.Participants
                .AsNoTracking()
                .Where(candidate => candidate.Account != null);

            // The firm scope is applied in the query, so a listing can never contain a row the
            // caller may not see (BR-8).
            if (memberFirmId is Guid firmId)
            {
                query = query.Where(candidate => candidate.MemberFirmId == firmId);
            }

            List<Participant> people = await query
                .OrderBy(candidate => candidate.FullName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result<IReadOnlyList<Participant>>.Success(people);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.ParticipantReadFailed(_logger, exception);

            return Result<IReadOnlyList<Participant>>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public async Task<Result<int>> CountActiveAdminsAsync(CancellationToken cancellationToken)
    {
        try
        {
            int count = await _context.Participants
                .CountAsync(
                    candidate => candidate.Account != null
                        && candidate.Account.Role == Role.Admin
                        && !candidate.Account.IsDisabled,
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<int>.Success(count);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.ParticipantReadFailed(_logger, exception);

            return Result<int>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public async Task<Result<int>> RegisterFailedLoginAsync(
        Guid participantId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        DateTimeOffset lockedUntil = now.Add(UserAccount.LockDuration);

        try
        {
            // Both SET expressions read the row as it was before this statement, and PostgreSQL
            // serialises concurrent UPDATEs of one row - so N parallel calls add exactly N.
            int affected = await _context.Participants
                .Where(candidate => candidate.Id == participantId
                    && candidate.Account != null
                    && !candidate.Account.IsDisabled
                    && !(candidate.Account.LockedUntilUtc > now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            candidate => candidate.Account!.FailedAttempts,
                            candidate => candidate.Account!.FailedAttempts + 1 >= UserAccount.MaxFailedAttempts
                                ? 0
                                : candidate.Account!.FailedAttempts + 1)
                        .SetProperty(
                            candidate => candidate.Account!.LockedUntilUtc,
                            candidate => candidate.Account!.FailedAttempts + 1 >= UserAccount.MaxFailedAttempts
                                ? lockedUntil
                                : candidate.Account!.LockedUntilUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<int>.Success(affected);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.SaveFailed(_logger, exception);

            return Result<int>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public void Add(Participant participant) => _context.Participants.Add(participant);
}
