using Application.Abstractions;
using Application.Common;

using Domain.People;

namespace Application.Identity;

public sealed record RefreshSessionCommand(string RefreshToken);

public sealed class RefreshSessionCommandHandler
{
    private readonly IParticipantRepository _participants;
    private readonly IRefreshChainRepository _chains;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RefreshSessionCommandHandler(
        IParticipantRepository participants,
        IRefreshChainRepository chains,
        ITokenIssuer tokenIssuer,
        IRefreshTokenGenerator refreshTokens,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _participants = participants;
        _chains = chains;
        _tokenIssuer = tokenIssuer;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>
    /// Every refusal returns the same error on purpose: an unknown secret, a reused one, an expired
    /// window and a stale session version must not be distinguishable from outside (the client's
    /// only sensible reaction is the same in all four cases - log in again).
    /// </summary>
    /// <remarks>
    /// Two requests presenting the same secret at the same moment both see it as current; only one
    /// write survives (optimistic concurrency). The loser looks again and now finds its secret among
    /// the spent links - the theft signal - so the race ends in a revoked chain, never in two live
    /// successors (spec 0002, AC-12).
    /// </remarks>
    public async Task<Result<AuthenticationResultDto>> HandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        Result<AuthenticationResultDto> result = await TryHandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess || result.Error != PersistenceErrors.ConcurrentUpdate)
        {
            return result;
        }

        _unitOfWork.DiscardChanges();
        result = await TryHandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess || result.Error != PersistenceErrors.ConcurrentUpdate
            ? result
            : Result<AuthenticationResultDto>.Failure(IdentityErrors.SessionNotRenewable);
    }

    private async Task<Result<AuthenticationResultDto>> TryHandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result<AuthenticationResultDto>.Failure(IdentityErrors.SessionNotRenewable);
        }

        string presentedHash = _refreshTokens.HashOf(command.RefreshToken);

        Result<RefreshChain?> chainLookup = await _chains
            .FindByTokenHashAsync(presentedHash, cancellationToken)
            .ConfigureAwait(false);

        if (!chainLookup.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(chainLookup.Error!);
        }

        if (chainLookup.Value is not RefreshChain chain)
        {
            return Result<AuthenticationResultDto>.Failure(IdentityErrors.SessionNotRenewable);
        }

        Result<Participant?> personLookup = await _participants
            .FindByIdAsync(chain.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (!personLookup.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(personLookup.Error!);
        }

        DateTimeOffset now = _clock.UtcNow;
        UserAccount? account = personLookup.Value?.Account;

        if (account is null || account.IsDisabled)
        {
            // The account went away or was switched off: the chain has no reason to live on.
            chain.Revoke(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result<AuthenticationResultDto>.Failure(IdentityErrors.SessionNotRenewable);
        }

        RefreshTokenPair replacement = _refreshTokens.Create();
        RefreshOutcome outcome = chain.TryRotate(
            presentedHash, replacement.Hash, account.SessionVersion, now);

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!saved.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(saved.Error!);
        }

        if (outcome != RefreshOutcome.Rotated)
        {
            return Result<AuthenticationResultDto>.Failure(IdentityErrors.SessionNotRenewable);
        }

        AccessToken accessToken = _tokenIssuer.IssueAccessToken(
            chain.ParticipantId,
            account.Role,
            personLookup.Value!.MemberFirmId,
            account.SessionVersion,
            account.MustChangePassword);

        return Result<AuthenticationResultDto>.Success(new AuthenticationResultDto(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            replacement.Secret,
            chain.ExpiresAtUtc,
            account.Role.ToString(),
            account.MustChangePassword));
    }
}
