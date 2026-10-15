namespace Infrastructure.Security;

/// <summary>
/// Access token parameters. The signing key never has a default: a missing key must stop the
/// application at startup rather than let it run insecurely (spec 0002, AC-27).
/// </summary>
public sealed record JwtOptions(string SigningKey, TimeSpan AccessTokenLifetime)
{
    /// <summary>Who mints the token. Checked on every request, so a token made for another
    /// system that happens to share the key is still refused.</summary>
    public const string Issuer = "egitimyonetimsistemi-api";

    /// <summary>Who the token is for.</summary>
    public const string Audience = "egitimyonetimsistemi-web";
}
