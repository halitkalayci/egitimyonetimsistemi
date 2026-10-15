using Application.Abstractions;
using Application.Common;

using Domain.People;

namespace Application.Users;

/// <summary>
/// <paramref name="MemberFirmId"/> is chosen by an Admin. A firm representative always creates in
/// their own firm: they may leave it empty or name their own, and naming any other firm is refused
/// (spec 0002, AC-19) - the body is never trusted to place a user elsewhere.
/// </summary>
public sealed record CreateUserCommand(string FullName, string Email, string Role, Guid? MemberFirmId);

public sealed class CreateUserCommandHandler
{
    private readonly IParticipantRepository _participants;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITemporaryPasswordGenerator _temporaryPasswords;
    private readonly IUnitOfWork _unitOfWork;

    public CreateUserCommandHandler(
        IParticipantRepository participants,
        IPasswordHasher passwordHasher,
        ITemporaryPasswordGenerator temporaryPasswords,
        IUnitOfWork unitOfWork)
    {
        _participants = participants;
        _passwordHasher = passwordHasher;
        _temporaryPasswords = temporaryPasswords;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UserWithTemporaryPasswordDto>> HandleAsync(
        CallerContext caller,
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(command);

        if (UserManagementPolicy.CheckCanManageUsers(caller) is Error notAllowed)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(notAllowed);
        }

        if (!UserManagementPolicy.TryParseRole(command.Role, out Role role))
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(IdentityErrors.UnknownRole);
        }

        if (UserManagementPolicy.CheckCanAssign(caller, role) is Error roleNotAllowed)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(roleNotAllowed);
        }

        Guid? firmId = caller.Role == Role.Admin ? command.MemberFirmId : caller.MemberFirmId;

        if (caller.Role == Role.FirmRepresentative
            && (firmId is null || (command.MemberFirmId is Guid requested && requested != firmId)))
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(IdentityErrors.NotAllowedForThisFirm);
        }

        if (UserManagementPolicy.CheckFirmFitsRole(firmId, role) is Error firmMismatch)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(firmMismatch);
        }

        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(
                Error.Validation("users.full_name_required", "Ad soyad zorunludur."));
        }

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(
                Error.Validation("users.email_required", "E-posta zorunludur."));
        }

        if (command.FullName.Trim().Length > Participant.MaxFullNameLength)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(IdentityErrors.FullNameTooLong);
        }

        string email = UserAccount.NormaliseEmail(command.Email);

        if (email.Length > UserAccount.MaxEmailLength)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(IdentityErrors.EmailTooLong);
        }

        if (!LooksLikeEmail(email))
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(IdentityErrors.EmailInvalid);
        }

        Result<bool> exists = await _participants.EmailExistsAsync(email, cancellationToken).ConfigureAwait(false);

        if (!exists.IsSuccess)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(exists.Error!);
        }

        if (exists.Value)
        {
            return Result<UserWithTemporaryPasswordDto>.Failure(IdentityErrors.EmailAlreadyUsed);
        }

        string temporaryPassword = _temporaryPasswords.Create();
        Participant person = Participant.CreateWithAccount(
            command.FullName, firmId, email, _passwordHasher.Hash(temporaryPassword), role);

        _participants.Add(person);

        Result<int> saved = await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!saved.IsSuccess)
        {
            // The existence check above and the insert are not atomic; a creation racing ours with
            // the same e-mail ends at the unique index, and that is still "e-mail already used".
            return Result<UserWithTemporaryPasswordDto>.Failure(
                saved.Error == PersistenceErrors.UniqueViolation ? IdentityErrors.EmailAlreadyUsed : saved.Error!);
        }

        return Result<UserWithTemporaryPasswordDto>.Success(new UserWithTemporaryPasswordDto(
            UserManagementPolicy.ToDto(person, person.Account!), temporaryPassword));
    }

    /// <summary>
    /// A deliberately small check - one @, something on both sides, a dot in the domain, no spaces.
    /// It catches typing mistakes; whether the address exists is not knowable here.
    /// </summary>
    private static bool LooksLikeEmail(string email)
    {
        int at = email.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at == email.LastIndexOf('@')
            && email.IndexOf('.', at) > at + 1
            && !email.EndsWith('.')
            && !email.Any(char.IsWhiteSpace);
    }
}
