using Application.Abstractions;
using Application.Common;

using Domain.People;

namespace Application.Identity;

/// <summary>Ends the session the presented secret belongs to - that device only (AC-14).</summary>
public sealed record LogoutCommand(string RefreshToken);

/// <summary>Ends every session of the caller, on every device (AC-15).</summary>
public sealed record LogoutAllCommand(Guid ParticipantId);

public sealed class LogoutCommandHandler
{
    private readonly IRefreshChainRepository _chains;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public LogoutCommandHandler(
        IRefreshChainRepository chains,
        IRefreshTokenGenerator refreshTokens,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _chains = chains;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>
    /// Idempotent and silent: an unknown or already dead secret still answers success, so logging
    /// out cannot be used to probe which secrets exist.
    /// </summary>
    public async Task<Result<bool>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result<bool>.Success(true);
        }

        Result<RefreshChain?> lookup = await _chains
            .FindByTokenHashAsync(_refreshTokens.HashOf(command.RefreshToken), cancellationToken)
            .ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return Result<bool>.Failure(lookup.Error!);
        }

        if (lookup.Value is not RefreshChain chain)
        {
            return Result<bool>.Success(true);
        }

        chain.Revoke(_clock.UtcNow);

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return saved.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(saved.Error!);
    }
}

public sealed class LogoutAllCommandHandler
{
    private readonly IParticipantRepository _participants;
    private readonly IRefreshChainRepository _chains;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public LogoutAllCommandHandler(
        IParticipantRepository participants,
        IRefreshChainRepository chains,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _participants = participants;
        _chains = chains;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<bool>> HandleAsync(LogoutAllCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<Participant?> personLookup = await _participants
            .FindByIdAsync(command.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (!personLookup.IsSuccess)
        {
            return Result<bool>.Failure(personLookup.Error!);
        }

        if (personLookup.Value?.Account is not UserAccount account)
        {
            return Result<bool>.Failure(IdentityErrors.PersonHasNoAccount);
        }

        Result<IReadOnlyList<RefreshChain>> chains = await _chains
            .ListActiveForParticipantAsync(command.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (!chains.IsSuccess)
        {
            return Result<bool>.Failure(chains.Error!);
        }

        DateTimeOffset now = _clock.UtcNow;

        foreach (RefreshChain chain in chains.Value)
        {
            chain.Revoke(now);
        }

        // Belt and braces: the version bump makes any chain we did not see stale as well.
        account.EndAllSessions();

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return saved.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(saved.Error!);
    }
}
