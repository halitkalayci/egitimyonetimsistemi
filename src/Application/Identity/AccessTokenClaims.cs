namespace Application.Identity;

/// <summary>
/// The custom claim names of the access token, defined once: Infrastructure writes them when it
/// mints a token and the HTTP layer reads them back, so the two sides cannot drift apart.
/// </summary>
public static class AccessTokenClaims
{
    public const string SessionVersion = "session_version";

    public const string MustChangePassword = "must_change_password";

    public const string MemberFirm = "member_firm";
}
