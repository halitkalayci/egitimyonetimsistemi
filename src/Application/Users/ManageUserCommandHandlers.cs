using Application.Abstractions;
using Application.Common;

using Domain.People;

namespace Application.Users;

public sealed record AssignRoleCommand(Guid TargetParticipantId, string Role);

/// <summary><paramref name="Disabled"/> is required: a missing value must not silently re-open an account.</summary>
public sealed record SetAccountStatusCommand(Guid TargetParticipantId, bool? Disabled);

public sealed record ResetPasswordCommand(Guid TargetParticipantId);

public sealed record ListUsersQuery;

/// <summary>
/// The three administrative commands and the listing. Every one of them resolves the target first
/// and then asks <see cref="UserManagementPolicy"/> whether this caller may touch it, so firm
/// isolation (BR-8) cannot be bypassed by guessing an id.
/// </summary>
public sealed class UserAdministrationHandlers
{
    private readonly IParticipantRepository _participants;
    private readonly IRefreshChainRepository _chains;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITemporaryPasswordGenerator _temporaryPasswords;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UserAdministrationHandlers(
        IParticipantRepository participants,
        IRefreshChainRepository chains,
        IPasswordHasher passwordHasher,
        ITemporaryPasswordGenerator temporaryPasswords,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _participants = participants;
        _chains = chains;
        _passwordHasher = passwordHasher;
        _temporaryPasswords = temporaryPasswords;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<UserDto>> AssignRoleAsync(
        CallerContext caller,
        AssignRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!UserManagementPolicy.TryParseRole(command.Role, out Role role))
        {
            return Result<UserDto>.Failure(IdentityErrors.UnknownRole);
        }

        Result<(Participant Person, UserAccount Account)> target =
            await ResolveTargetAsync(caller, command.TargetParticipantId, cancellationToken).ConfigureAwait(false);

        if (!target.IsSuccess)
        {
            return Result<UserDto>.Failure(target.Error!);
        }

        if (UserManagementPolicy.CheckCanAssign(caller, role) is Error roleNotAllowed)
        {
            return Result<UserDto>.Failure(roleNotAllowed);
        }

        if (UserManagementPolicy.CheckFirmFitsRole(target.Value.Person.MemberFirmId, role) is Error firmMismatch)
        {
            return Result<UserDto>.Failure(firmMismatch);
        }

        if (role == target.Value.Account.Role)
        {
            return await SaveAndProjectAsync(target.Value, cancellationToken).ConfigureAwait(false);
        }

        return await _unitOfWork.RunExclusivelyAsync(
            ExclusiveSection.AdminAccess,
            async token =>
            {
                if (await CheckKeepsAccessAsync(caller, target.Value, token).ConfigureAwait(false) is Error lockout)
                {
                    return Result<UserDto>.Failure(lockout);
                }

                // Bumps SessionVersion, so the user's refresh chains become stale at once (AC-16).
                target.Value.Person.AssignRole(role);

                return await SaveAndProjectAsync(target.Value, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserDto>> SetAccountStatusAsync(
        CallerContext caller,
        SetAccountStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Disabled is not bool disabled)
        {
            return Result<UserDto>.Failure(IdentityErrors.StatusRequired);
        }

        Result<(Participant Person, UserAccount Account)> target =
            await ResolveTargetAsync(caller, command.TargetParticipantId, cancellationToken).ConfigureAwait(false);

        if (!target.IsSuccess)
        {
            return Result<UserDto>.Failure(target.Error!);
        }

        if (!disabled)
        {
            target.Value.Account.Enable();

            return await SaveAndProjectAsync(target.Value, cancellationToken).ConfigureAwait(false);
        }

        return await _unitOfWork.RunExclusivelyAsync(
            ExclusiveSection.AdminAccess,
            async token =>
            {
                if (!target.Value.Account.IsDisabled
                    && await CheckKeepsAccessAsync(caller, target.Value, token).ConfigureAwait(false) is Error lockout)
                {
                    return Result<UserDto>.Failure(lockout);
                }

                target.Value.Account.Disable();

                if (await RevokeAllChainsAsync(target.Value.Person.Id, token).ConfigureAwait(false) is Error revokeFailed)
                {
                    return Result<UserDto>.Failure(revokeFailed);
                }

                return await SaveAndProjectAsync(target.Value, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserWithTemporaryPasswordDto>> ResetPasswordAsync(
        CallerContext caller,
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<(Participant Person, UserAccount Account)> target =
            await ResolveTargetAsync(caller, command.TargetParticipantId, cancellationToken).ConfigureAwait(false);

        if (!target.IsSuccess)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(target.Error!);
        }

        string temporaryPassword = _temporaryPasswords.Create();
        target.Value.Account.ResetToTemporaryPassword(_passwordHasher.Hash(temporaryPassword));

        if (await RevokeAllChainsAsync(target.Value.Person.Id, cancellationToken).ConfigureAwait(false) is Error revokeFailed)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(revokeFailed);
        }

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return saved.IsSuccess
            ? Result<UserWithTemporaryPasswordDto>.Success(new UserWithTemporaryPasswordDto(
                UserManagementPolicy.ToDto(target.Value.Person, target.Value.Account), temporaryPassword))
            : Result<UserWithTemporaryPasswordDto>.Failure(saved.Error!);
    }

    public async Task<Result<IReadOnlyList<UserDto>>> ListAsync(
        CallerContext caller,
        ListUsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(query);

        if (UserManagementPolicy.CheckCanManageUsers(caller) is Error notAllowed)
        {
            return Result<IReadOnlyList<UserDto>>.Failure(notAllowed);
        }

        if (caller.Role == Role.FirmRepresentative && caller.MemberFirmId is null)
        {
            return Result<IReadOnlyList<UserDto>>.Failure(IdentityErrors.NotAllowedForThisFirm);
        }

        Result<IReadOnlyList<Participant>> people = await _participants
            .ListWithAccountsAsync(UserManagementPolicy.ScopeFor(caller), cancellationToken)
            .ConfigureAwait(false);

        if (!people.IsSuccess)
        {
            return Result<IReadOnlyList<UserDto>>.Failure(people.Error!);
        }

        UserDto[] users =
        [
            .. people.Value
                .Where(person => person.Account is not null)
                .Select(person => UserManagementPolicy.ToDto(person, person.Account!))
        ];

        return Result<IReadOnlyList<UserDto>>.Success(users);
    }

    private async Task<Result<(Participant Person, UserAccount Account)>> ResolveTargetAsync(
        CallerContext caller,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (UserManagementPolicy.CheckCanManageUsers(caller) is Error notAllowed)
        {
            return Result<(Participant, UserAccount)>.Failure(notAllowed);
        }

        Result<Participant?> lookup = await _participants
            .FindByIdAsync(targetId, cancellationToken)
            .ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return Result<(Participant, UserAccount)>.Failure(lookup.Error!);
        }

        if (lookup.Value is not Participant person)
        {
            return Result<(Participant, UserAccount)>.Failure(IdentityErrors.UserNotFound);
        }

        if (UserManagementPolicy.CheckCanTarget(caller, person) is Error outOfScope)
        {
            return Result<(Participant, UserAccount)>.Failure(outOfScope);
        }

        return person.Account is UserAccount account
            ? Result<(Participant, UserAccount)>.Success((person, account))
            : Result<(Participant, UserAccount)>.Failure(IdentityErrors.PersonHasNoAccount);
    }

    /// <summary>
    /// Disabling or re-roling must not lock the system out of its own administration: nobody may
    /// do it to themselves, and the last active Admin keeps their access. Called inside
    /// <see cref="ExclusiveSection.AdminAccess"/>, so the count cannot be overtaken by a parallel
    /// change before this one is saved.
    /// </summary>
    private async Task<Error?> CheckKeepsAccessAsync(
        CallerContext caller,
        (Participant Person, UserAccount Account) target,
        CancellationToken cancellationToken)
    {
        if (target.Person.Id == caller.ParticipantId)
        {
            return IdentityErrors.CannotChangeOwnAccess;
        }

        if (target.Account.Role != Role.Admin || target.Account.IsDisabled)
        {
            return null;
        }

        Result<int> admins = await _participants.CountActiveAdminsAsync(cancellationToken).ConfigureAwait(false);

        if (!admins.IsSuccess)
        {
            return admins.Error;
        }

        return admins.Value <= 1 ? IdentityErrors.LastActiveAdmin : null;
    }

    /// <summary>
    /// A failure here is reported, not swallowed: nothing is saved then, so the account change and
    /// the revocation happen together or not at all.
    /// </summary>
    private async Task<Error?> RevokeAllChainsAsync(Guid participantId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RefreshChain>> chains = await _chains
            .ListActiveForParticipantAsync(participantId, cancellationToken)
            .ConfigureAwait(false);

        if (!chains.IsSuccess)
        {
            return chains.Error;
        }

        foreach (RefreshChain chain in chains.Value)
        {
            chain.Revoke(_clock.UtcNow);
        }

        return null;
    }

    private async Task<Result<UserDto>> SaveAndProjectAsync(
        (Participant Person, UserAccount Account) target,
        CancellationToken cancellationToken)
    {
        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return saved.IsSuccess
            ? Result<UserDto>.Success(UserManagementPolicy.ToDto(target.Person, target.Account))
            : Result<UserDto>.Failure(saved.Error!);
    }
}
