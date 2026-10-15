using Application.Abstractions;
using Application.Common;

using Domain.People;

namespace Application.Identity;

public sealed record LoginCommand(string Email, string Password);

/// <summary>
/// What the caller gets after a successful login. <paramref name="MustChangePassword"/> sessions are
/// real sessions on purpose - the user has to authenticate in order to change the password - but the
/// HTTP boundary refuses everything except the change-password endpoint for them (spec 0002, AC-6).
/// </summary>
public sealed record AuthenticationResultDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    string Role,
    bool MustChangePassword);

public sealed class LoginCommandHandler
{
    private readonly IParticipantRepository _participants;
    private readonly IRefreshChainRepository _chains;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public LoginCommandHandler(
        IParticipantRepository participants,
        IRefreshChainRepository chains,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer,
        IRefreshTokenGenerator refreshTokens,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _participants = participants;
        _chains = chains;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <remarks>
    /// A wrong password is counted by one atomic statement, so parallel wrong passwords never
    /// conflict (see <see cref="IParticipantRepository.RegisterFailedLoginAsync"/>). Only the
    /// success path writes the account back; if a parallel attempt moved the row in between, that
    /// write is refused (optimistic concurrency) and the login is replayed once on fresh state.
    /// </remarks>
    public async Task<Result<AuthenticationResultDto>> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        Result<AuthenticationResultDto> result = await TryHandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess || result.Error != PersistenceErrors.ConcurrentUpdate)
        {
            return result;
        }

        _unitOfWork.DiscardChanges();

        return await TryHandleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AuthenticationResultDto>> TryHandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string email = UserAccount.NormaliseEmail(command.Email ?? string.Empty);

        Result<Participant?> lookup = await _participants
            .FindByEmailAsync(email, cancellationToken)
            .ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(lookup.Error!);
        }

        UserAccount? account = lookup.Value?.Account;

        if (account is null)
        {
            // Pay the verification cost anyway, then answer exactly as for a wrong password. The
            // decoy comes from the hasher, so its cost tracks the real parameters (AC-29).
            _passwordHasher.Verify(command.Password ?? string.Empty, _passwordHasher.DecoyHash);

            return Result<AuthenticationResultDto>.Failure(IdentityErrors.InvalidCredentials);
        }

        bool passwordMatches = _passwordHasher.Verify(command.Password ?? string.Empty, account.PasswordHash);
        DateTimeOffset now = _clock.UtcNow;
        LoginOutcome outcome = account.TryLogin(passwordMatches, now);

        if (outcome == LoginOutcome.InvalidCredentials)
        {
            return await RegisterFailedLoginAsync(lookup.Value!.Id, now, cancellationToken).ConfigureAwait(false);
        }

        // A successful login resets the attempt counter, so it is persisted before the session.
        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!saved.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(saved.Error!);
        }

        switch (outcome)
        {
            case LoginOutcome.Locked:
                return Result<AuthenticationResultDto>.Failure(IdentityErrors.AccountLocked);
            case LoginOutcome.Disabled:
                return Result<AuthenticationResultDto>.Failure(IdentityErrors.AccountDisabled);
            default:
                break;
        }

        return await IssueSessionAsync(lookup.Value!, account, outcome, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AuthenticationResultDto>> RegisterFailedLoginAsync(
        Guid participantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The decision was made on the tracked account; the count itself is written atomically by
        // the database, never by saving the value read above, which a parallel attempt may have
        // already moved. A disabled account is not counted - the statement skips it, as TryLogin does.
        _unitOfWork.DiscardChanges();

        Result<int> counted = await _participants
            .RegisterFailedLoginAsync(participantId, now, cancellationToken)
            .ConfigureAwait(false);

        return Result<AuthenticationResultDto>.Failure(
            counted.IsSuccess ? IdentityErrors.InvalidCredentials : counted.Error!);
    }

    private async Task<Result<AuthenticationResultDto>> IssueSessionAsync(
        Participant person,
        UserAccount account,
        LoginOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool mustChangePassword = outcome == LoginOutcome.MustChangePassword;

        AccessToken accessToken = _tokenIssuer.IssueAccessToken(
            person.Id, account.Role, person.MemberFirmId, account.SessionVersion, mustChangePassword);

        RefreshTokenPair refresh = _refreshTokens.Create();
        RefreshChain chain = RefreshChain.Start(person.Id, refresh.Hash, account.SessionVersion, now);
        _chains.Add(chain);

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return saved.IsSuccess
            ? Result<AuthenticationResultDto>.Success(new AuthenticationResultDto(
                accessToken.Value,
                accessToken.ExpiresAtUtc,
                refresh.Secret,
                chain.ExpiresAtUtc,
                account.Role.ToString(),
                mustChangePassword))
            : Result<AuthenticationResultDto>.Failure(saved.Error!);
    }
}
