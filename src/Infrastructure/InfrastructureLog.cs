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

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "A person record could not be read.")]
    internal static partial void ParticipantReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "A refresh chain could not be read.")]
    internal static partial void RefreshChainReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Changes could not be saved.")]
    internal static partial void SaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "A save lost an optimistic concurrency race.")]
    internal static partial void ConcurrentUpdate(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "The database refused the data of a save.")]
    internal static partial void SaveRefused(ILogger logger, Exception exception);
}
