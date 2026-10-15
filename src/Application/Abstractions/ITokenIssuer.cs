using Domain.People;

namespace Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public interface ITokenIssuer
{
    /// <summary>
    /// Mints a short lived access token carrying the person's id, role and session version.
    /// </summary>
    AccessToken IssueAccessToken(
        Guid participantId,
        Role role,
        Guid? memberFirmId,
        int sessionVersion,
        bool mustChangePassword);
}
