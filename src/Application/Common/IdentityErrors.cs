namespace Application.Common;

/// <summary>
/// Every identity failure the HTTP boundary may return. Messages are user facing and Turkish;
/// codes are stable machine keys. <see cref="InvalidCredentials"/> is deliberately shared by
/// "no such e-mail" and "wrong password" so the answer cannot be used to enumerate accounts
/// (spec 0002, AC-2).
/// </summary>
public static class IdentityErrors
{
    public static Error InvalidCredentials { get; } = Error.Validation(
        "auth.invalid_credentials",
        "E-posta veya şifre hatalı.");

    public static Error AccountLocked { get; } = Error.BusinessRule(
        "auth.account_locked",
        "Hesabınız geçici olarak kilitlendi. Lütfen daha sonra tekrar deneyin.");

    public static Error AccountDisabled { get; } = Error.BusinessRule(
        "auth.account_disabled",
        "Hesabınız kullanımda değil. Eğitim sorumlusuna başvurun.");

    public static Error PasswordChangeRequired { get; } = Error.Forbidden(
        "auth.password_change_required",
        "Devam etmek için şifrenizi değiştirmeniz gerekiyor.");

    public static Error PasswordTooShort { get; } = Error.Validation(
        "auth.password_too_short",
        $"Şifre en az {Domain.People.UserAccount.MinimumPasswordLength} karakter olmalıdır.");

    public static Error PasswordSameAsCurrent { get; } = Error.Validation(
        "auth.password_same_as_current",
        "Yeni şifre mevcut şifreden farklı olmalıdır.");

    public static Error SessionNotRenewable { get; } = Error.Unauthorized(
        "auth.session_not_renewable",
        "Oturumunuz sona erdi. Lütfen tekrar giriş yapın.");

    public static Error EmailAlreadyUsed { get; } = Error.BusinessRule(
        "users.email_already_used",
        "Bu e-posta adresi başka bir hesapta kullanılıyor.");

    public static Error NotAllowedForThisFirm { get; } = Error.Forbidden(
        "users.other_firm",
        "Yalnızca kendi firmanızın kullanıcıları üzerinde işlem yapabilirsiniz.");

    public static Error RoleNotAssignableByCaller { get; } = Error.Forbidden(
        "users.role_not_assignable",
        "Bu rolü atama yetkiniz yok.");

    public static Error NotAllowed { get; } = Error.Forbidden(
        "users.not_allowed",
        "Bu işlem için yetkiniz yok.");

    public static Error StaffRoleCannotBelongToFirm { get; } = Error.Validation(
        "users.staff_role_with_firm",
        "Eğitim sorumlusu ve eğitmen hesapları bir üye firmaya bağlı olamaz.");

    public static Error UnknownRole { get; } = Error.Validation(
        "users.unknown_role",
        "Geçersiz rol.");

    public static Error FullNameTooLong { get; } = Error.Validation(
        "users.full_name_too_long",
        $"Ad soyad en fazla {Domain.People.Participant.MaxFullNameLength} karakter olabilir.");

    public static Error EmailInvalid { get; } = Error.Validation(
        "users.email_invalid",
        "Geçerli bir e-posta adresi girin.");

    public static Error EmailTooLong { get; } = Error.Validation(
        "users.email_too_long",
        $"E-posta en fazla {Domain.People.UserAccount.MaxEmailLength} karakter olabilir.");

    public static Error StatusRequired { get; } = Error.Validation(
        "users.status_required",
        "Hesabın devre dışı mı yoksa açık mı olacağı belirtilmelidir.");

    public static Error CannotChangeOwnAccess { get; } = Error.BusinessRule(
        "users.own_access",
        "Kendi hesabınızı devre dışı bırakamaz veya kendi rolünüzü değiştiremezsiniz.");

    public static Error LastActiveAdmin { get; } = Error.BusinessRule(
        "users.last_admin",
        "Sistemdeki son etkin eğitim sorumlusu hesabı kapatılamaz veya rolü değiştirilemez.");

    public static Error UserNotFound { get; } = Error.NotFound(
        "users.not_found",
        "Kullanıcı bulunamadı.");

    public static Error PersonHasNoAccount { get; } = Error.BusinessRule(
        "users.no_account",
        "Bu kişinin sistem hesabı yok.");
}
