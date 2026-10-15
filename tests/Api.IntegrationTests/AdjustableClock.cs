using Application.Abstractions;

namespace Api.IntegrationTests;

/// <summary>A clock the test moves forward, for rules that depend on time passing.</summary>
internal sealed class AdjustableClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
