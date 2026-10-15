using System.Security.Claims;

using Application.Identity;
using Application.Users;

using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Auth;

/// <summary>
/// Builds the caller's identity from verified token claims only. Nothing here reads the request
/// body or query string: an authorization decision must never depend on what the client says about
/// itself (docs/security.md, AuthN / AuthZ).
/// </summary>
internal static class CallerContextFactory
{
    internal static CallerContext? From(HttpContext context)
    {
        ClaimsPrincipal principal = context.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out Guid participantId))
        {
            return null;
        }

        Guid? memberFirmId = Guid.TryParse(principal.FindFirstValue(AccessTokenClaims.MemberFirm), out Guid firmId)
            ? firmId
            : null;

        // The role arrives as the string it was minted as; Application turns it back into its own
        // type, so no Domain type is named here.
        return CallerContext.TryCreate(participantId, principal.FindFirstValue(ClaimTypes.Role), memberFirmId);
    }

    internal static bool MustChangePassword(HttpContext context) =>
        string.Equals(
            context.User.FindFirstValue(AccessTokenClaims.MustChangePassword),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
