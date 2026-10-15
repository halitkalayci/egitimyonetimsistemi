using Domain.People;

namespace Application.Users;

/// <summary>
/// Who is asking, as established from the verified token - never from the request body
/// (docs/security.md: "Yetki kararı hiçbir zaman istemciden gelen rol/firma bilgisine dayanmaz").
/// </summary>
public sealed record CallerContext(Guid ParticipantId, Role Role, Guid? MemberFirmId)
{
    /// <summary>
    /// Builds a caller from raw claim values. This conversion lives here so the HTTP layer never
    /// touches the Domain's Role type (docs/architecture.md, forbidden dependency 4).
    /// </summary>
    public static CallerContext? TryCreate(Guid participantId, string? role, Guid? memberFirmId) =>
        Enum.TryParse(role, ignoreCase: false, out Role parsed)
            ? new CallerContext(participantId, parsed, memberFirmId)
            : null;
}
