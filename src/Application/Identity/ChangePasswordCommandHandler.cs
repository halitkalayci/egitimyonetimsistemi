using Application.Abstractions;
using Application.Common;

using Domain.People;

namespace Application.Identity;

public sealed record ChangePasswordCommand(Guid ParticipantId, string CurrentPassword, string NewPassword);

public sealed class ChangePasswordCommandHandler
{
    private readonly IParticipantRepository _participants;
    private readonly IRefreshChainRepository _chains;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ChangePasswordCommandHandler(
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

    /// <summary>
    /// Changes the password, ends every session opened with the old one and hands back a fresh
    /// session for the device that made the change, so the user lands on the welcome page instead
    /// of logging in again (spec 0002 UX, "zorunlu şifre değiştirme - Success").
    /// </summary>
    public async Task<Result<AuthenticationResultDto>> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<Participant?> lookup = await _participants
            .FindByIdAsync(command.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(lookup.Error!);
        }

        Participant? person = lookup.Value;
        UserAccount? account = person?.Account;

        if (person is null || account is null)
        {
            return Result<AuthenticationResultDto>.Failure(IdentityErrors.PersonHasNoAccount);
        }

        string current = command.CurrentPassword ?? string.Empty;
        string next = command.NewPassword ?? string.Empty;
        DateTimeOffset now = _clock.UtcNow;

        // The current password is checked under the same lock rules as a login: a stolen access
        // token must not become a way to guess it without limit.
        LoginOutcome outcome = account.TryLogin(_passwordHasher.Verify(current, account.PasswordHash), now);

        if (outcome is LoginOutcome.InvalidCredentials or LoginOutcome.Locked or LoginOutcome.Disabled)
        {
            Result<int> counted = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result<AuthenticationResultDto>.Failure(
                !counted.IsSuccess ? counted.Error! : outcome switch
                {
                    LoginOutcome.Locked => IdentityErrors.AccountLocked,
                    LoginOutcome.Disabled => IdentityErrors.AccountDisabled,
                    _ => IdentityErrors.InvalidCredentials
                });
        }

        if (next.Length < UserAccount.MinimumPasswordLength)
        {
            return Result<AuthenticationResultDto>.Failure(IdentityErrors.PasswordTooShort);
        }

        if (_passwordHasher.Verify(next, account.PasswordHash))
        {
            return Result<AuthenticationResultDto>.Failure(IdentityErrors.PasswordSameAsCurrent);
        }

        // Bumps SessionVersion, which already makes every existing chain stale; they are revoked
        // explicitly as well so the stored state says what happened.
        account.ChangePassword(_passwordHasher.Hash(next));

        Result<IReadOnlyList<RefreshChain>> chains = await _chains
            .ListActiveForParticipantAsync(person.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!chains.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(chains.Error!);
        }

        foreach (RefreshChain chain in chains.Value)
        {
            chain.Revoke(now);
        }

        RefreshTokenPair refresh = _refreshTokens.Create();
        RefreshChain fresh = RefreshChain.Start(person.Id, refresh.Hash, account.SessionVersion, now);
        _chains.Add(fresh);

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!saved.IsSuccess)
        {
            return Result<AuthenticationResultDto>.Failure(saved.Error!);
        }

        AccessToken accessToken = _tokenIssuer.IssueAccessToken(
            person.Id, account.Role, person.MemberFirmId, account.SessionVersion, mustChangePassword: false);

        return Result<AuthenticationResultDto>.Success(new AuthenticationResultDto(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            refresh.Secret,
            fresh.ExpiresAtUtc,
            account.Role.ToString(),
            MustChangePassword: false));
    }
}
