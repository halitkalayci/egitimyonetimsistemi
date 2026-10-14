using System.Data.Common;

using Application.Abstractions;
using Application.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

/// <summary>
/// Reads the time from the PostgreSQL server itself: this is the assertion that the connection,
/// credentials and network path really work, not just that the application started.
/// </summary>
internal sealed class NpgsqlDatabaseTimeProvider : IDatabaseTimeProvider
{
    private readonly AppDbContext _context;
    private readonly ILogger<NpgsqlDatabaseTimeProvider> _logger;

    public NpgsqlDatabaseTimeProvider(AppDbContext context, ILogger<NpgsqlDatabaseTimeProvider> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<DateTimeOffset>> GetServerTimeUtcAsync(CancellationToken cancellationToken)
    {
        try
        {
            DbConnection connection = _context.Database.GetDbConnection();
            await _context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = "SELECT now() AT TIME ZONE 'UTC'";

                object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                return value is DateTime utc
                    ? Result<DateTimeOffset>.Success(new DateTimeOffset(utc, TimeSpan.Zero))
                    : Result<DateTimeOffset>.Failure(DatabaseErrors.Unavailable);
            }
            finally
            {
                await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.DatabaseTimeReadFailed(_logger, exception);

            return Result<DateTimeOffset>.Failure(DatabaseErrors.Unavailable);
        }
    }
}
