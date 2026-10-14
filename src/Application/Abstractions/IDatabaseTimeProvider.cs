using Application.Common;

namespace Application.Abstractions;

/// <summary>
/// Reads the time from the database server, which is what proves the data path is really wired.
/// Returns a Result because an unreachable database is an expected failure, not a bug.
/// </summary>
public interface IDatabaseTimeProvider
{
    Task<Result<DateTimeOffset>> GetServerTimeUtcAsync(CancellationToken cancellationToken);
}
