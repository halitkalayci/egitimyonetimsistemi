namespace Application.System;

/// <summary>What the HTTP boundary returns. Domain entities never cross that boundary
/// (docs/architecture.md, forbidden dependency 4).</summary>
public sealed record SystemInfoDto(
    string ApplicationName,
    DateTimeOffset ApplicationTimeUtc,
    DateTimeOffset DatabaseTimeUtc,
    IReadOnlyList<SystemInfoEntryDto> Entries);

public sealed record SystemInfoEntryDto(Guid Id, string Key, string Value, DateTimeOffset RecordedAtUtc);
