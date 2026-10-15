namespace Application.Common;

/// <summary>
/// Persistence outcomes a use case may react to. Defined here, not in Infrastructure, because the
/// Application layer decides what a conflict means for its own flow (retry, refuse, report).
/// </summary>
public static class PersistenceErrors
{
    /// <summary>
    /// Someone else changed the same row between our read and our write (optimistic concurrency).
    /// Nothing of ours was saved.
    /// </summary>
    public static Error ConcurrentUpdate { get; } = Error.BusinessRule(
        "database.concurrent_update",
        "Kayıt aynı anda başka bir işlemle değişti. Lütfen tekrar deneyin.");

    /// <summary>A unique index refused the row (PostgreSQL 23505), e.g. two creations racing.</summary>
    public static Error UniqueViolation { get; } = Error.BusinessRule(
        "database.duplicate",
        "Bu kayıt zaten mevcut.");

    /// <summary>A value did not fit its column (PostgreSQL 22001): bad input, not an outage.</summary>
    public static Error ValueTooLong { get; } = Error.Validation(
        "database.value_too_long",
        "Girilen değerlerden biri izin verilen uzunluğu aşıyor.");
}
