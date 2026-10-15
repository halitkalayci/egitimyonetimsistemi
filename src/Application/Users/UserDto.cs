namespace Application.Users;

public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    Guid? MemberFirmId,
    bool IsDisabled,
    bool MustChangePassword);

/// <summary>
/// Returned once, when an account is created or its password is reset. The temporary password is
/// never readable afterwards (spec 0002, AC-18).
/// </summary>
public sealed record UserWithTemporaryPasswordDto(UserDto User, string TemporaryPassword);
