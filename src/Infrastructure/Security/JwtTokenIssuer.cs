using System.Security.Claims;
using System.Text;

using Application.Abstractions;
using Application.Identity;

using Domain.People;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Security;

/// <summary>
/// Mints the short lived access token. The role and the account's session version travel inside it,
/// so the HTTP layer can authorize without a database read - at the documented cost that a role
/// change only takes full effect when the token expires (spec 0002, AC-16).
/// </summary>
internal sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public JwtTokenIssuer(JwtOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public AccessToken IssueAccessToken(
        Guid participantId,
        Role role,
        Guid? memberFirmId,
        int sessionVersion,
        bool mustChangePassword)
    {
        DateTimeOffset issuedAt = _clock.UtcNow;
        DateTimeOffset expiresAt = issuedAt.Add(_options.AccessTokenLifetime);

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, participantId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(AccessTokenClaims.SessionVersion, sessionVersion.ToString(provider: null)),
            new Claim(AccessTokenClaims.MustChangePassword, mustChangePassword ? "true" : "false")
        ];

        if (memberFirmId is Guid firmId)
        {
            // Without this the firm scope of BR-8 would be invisible to the HTTP layer.
            claims.Add(new Claim(AccessTokenClaims.MemberFirm, firmId.ToString()));
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = JwtOptions.Issuer,
            Audience = JwtOptions.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials
        };

        string token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new AccessToken(token, expiresAt);
    }
}
