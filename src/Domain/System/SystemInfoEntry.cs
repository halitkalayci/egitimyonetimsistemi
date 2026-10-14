namespace Domain.System;

/// <summary>
/// A neutral record the walking skeleton reads so the whole data path is proven end to end.
/// Deliberately NOT part of the ubiquitous language (docs/domain.md): the real entities arrive
/// with their own specs and business rules.
/// </summary>
public sealed class SystemInfoEntry
{
    private SystemInfoEntry(Guid id, string key, string value, DateTimeOffset recordedAt)
    {
        Id = id;
        Key = key;
        Value = value;
        RecordedAt = recordedAt;
    }

    /// <summary>UUID v7: time ordered, index friendly, leaks no record count (docs/conventions.md).</summary>
    public Guid Id { get; }

    public string Key { get; }

    public string Value { get; }

    /// <summary>Always UTC - the domain refuses to hold a local timestamp.</summary>
    public DateTimeOffset RecordedAt { get; }

    public static SystemInfoEntry Create(string key, string value, DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new SystemInfoEntry(
            Guid.CreateVersion7(),
            key.Trim(),
            value.Trim(),
            recordedAt.ToUniversalTime());
    }

    /// <summary>Rehydration from storage: the identity and timestamp already exist.</summary>
    public static SystemInfoEntry FromStorage(Guid id, string key, string value, DateTimeOffset recordedAt) =>
        new(id, key, value, recordedAt.ToUniversalTime());
}
