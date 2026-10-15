using Application.Common;

using Domain.People;

namespace Application.Users;

/// <summary>
/// The authorization rules of user management, in one place and in the Application layer so they
/// hold whatever the HTTP surface looks like (BR-8, docs/security.md).
/// </summary>
internal static class UserManagementPolicy
{
    /// <summary>
    /// Accepts a role by its exact name only. Enum.TryParse alone also takes numbers ("99"), which
    /// would store a role that does not exist.
    /// </summary>
    internal static bool TryParseRole(string? value, out Role role)
    {
        role = default;

        return value is not null
            && Enum.GetNames<Role>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out role);
    }

    internal static Error? CheckCanManageUsers(CallerContext caller) =>
        caller.Role is Role.Admin or Role.FirmRepresentative
            ? null
            : IdentityErrors.NotAllowed;

    /// <summary>
    /// A firm representative may only touch Participant accounts of their own firm. Someone with no
    /// firm (an Admin or Instructor record) is never visible to them, and neither is a fellow
    /// representative: resetting their password would hand over their account.
    /// </summary>
    internal static Error? CheckCanTarget(CallerContext caller, Participant target)
    {
        if (caller.Role == Role.Admin)
        {
            return null;
        }

        if (caller.Role != Role.FirmRepresentative)
        {
            return IdentityErrors.NotAllowed;
        }

        if (caller.MemberFirmId is not Guid firmId || !target.BelongsToFirm(firmId))
        {
            return IdentityErrors.NotAllowedForThisFirm;
        }

        return target.Account is { Role: not Role.Participant } ? IdentityErrors.NotAllowed : null;
    }

    /// <summary>Staff roles carry no firm (see <see cref="Participant.RoleMayBelongToFirm"/>).</summary>
    internal static Error? CheckFirmFitsRole(Guid? memberFirmId, Role role) =>
        memberFirmId is not null && !Participant.RoleMayBelongToFirm(role)
            ? IdentityErrors.StaffRoleCannotBelongToFirm
            : null;

    internal static Error? CheckCanAssign(CallerContext caller, Role role) => caller.Role switch
    {
        Role.Admin => null,
        Role.FirmRepresentative when role == Role.Participant => null,
        _ => IdentityErrors.RoleNotAssignableByCaller
    };

    /// <summary>The firm scope a listing must be built with; null means "everything" (Admin only).</summary>
    internal static Guid? ScopeFor(CallerContext caller) =>
        caller.Role == Role.Admin ? null : caller.MemberFirmId;

    internal static UserDto ToDto(Participant person, UserAccount account) => new(
        person.Id,
        person.FullName,
        account.Email,
        account.Role.ToString(),
        person.MemberFirmId,
        account.IsDisabled,
        account.MustChangePassword);
}
