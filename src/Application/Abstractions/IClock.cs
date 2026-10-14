namespace Application.Abstractions;

/// <summary>
/// The application clock. Time is never read from DateTimeOffset.UtcNow directly in Application
/// code (docs/conventions.md, Data rules) so behaviour stays testable.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
