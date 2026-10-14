using Microsoft.Extensions.Logging;

namespace Infrastructure;

/// <summary>
/// Source-generated log messages (CA1848). No personal data in any message - only the exception
/// and, where needed, record identifiers (docs/security.md, Personal data).
/// </summary>
internal static partial class InfrastructureLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "System info could not be read from the database.")]
    internal static partial void SystemInfoReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Database server time could not be read.")]
    internal static partial void DatabaseTimeReadFailed(ILogger logger, Exception exception);
}
