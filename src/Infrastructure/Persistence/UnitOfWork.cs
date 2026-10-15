using Application.Abstractions;
using Application.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly ILogger<UnitOfWork> _logger;

    public UnitOfWork(AppDbContext context, ILogger<UnitOfWork> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            int affected = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result<int>.Success(affected);
        }
        catch (DbUpdateException exception) when (DatabaseFailure.SqlStateOf(exception) == PostgresErrorCodes.UniqueViolation)
        {
            InfrastructureLog.SaveRefused(_logger, exception);

            return Result<int>.Failure(PersistenceErrors.UniqueViolation);
        }
        catch (DbUpdateException exception) when (DatabaseFailure.SqlStateOf(exception) == PostgresErrorCodes.StringDataRightTruncation)
        {
            InfrastructureLog.SaveRefused(_logger, exception);

            return Result<int>.Failure(PersistenceErrors.ValueTooLong);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // The row's xmin moved since we read it: another request got there first.
            InfrastructureLog.ConcurrentUpdate(_logger, exception);

            return Result<int>.Failure(PersistenceErrors.ConcurrentUpdate);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.SaveFailed(_logger, exception);

            return Result<int>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public void DiscardChanges() => _context.ChangeTracker.Clear();

    public async Task<Result<T>> RunExclusivelyAsync<T>(
        ExclusiveSection section,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        long key = AdvisoryLockKeyOf(section);
        IDbContextTransaction transaction;

        try
        {
            transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Released by PostgreSQL itself when the transaction ends, whichever way it ends.
            await _context.Database
                .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.SaveFailed(_logger, exception);

            return Result<T>.Failure(DatabaseErrors.Unavailable);
        }

        await using (transaction.ConfigureAwait(false))
        {
            Result<T> result = await work(cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return result;
            }

            try
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
            {
                InfrastructureLog.SaveFailed(_logger, exception);

                return Result<T>.Failure(DatabaseErrors.Unavailable);
            }

            return result;
        }
    }

    /// <summary>One fixed key per section; the values are arbitrary but must never be reused.</summary>
    private static long AdvisoryLockKeyOf(ExclusiveSection section) => section switch
    {
        ExclusiveSection.AdminAccess => 2_0002_0001,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
    };
}
