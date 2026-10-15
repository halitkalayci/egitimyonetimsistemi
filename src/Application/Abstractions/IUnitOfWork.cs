using Application.Common;

namespace Application.Abstractions;

/// <summary>A decision that two requests must never make at the same time.</summary>
public enum ExclusiveSection
{
    /// <summary>
    /// Taking an Admin's access away (disable, re-role). The "last active Admin" rule reads other
    /// rows than it writes, so row versions cannot catch two Admins removing each other at once.
    /// </summary>
    AdminAccess
}

public interface IUnitOfWork
{
    /// <summary>One user action, one transaction (docs/architecture.md, Communication rules).</summary>
    /// <remarks>
    /// Fails with <see cref="PersistenceErrors.ConcurrentUpdate"/> when a row read in this unit was
    /// changed by someone else in the meantime; nothing is saved then.
    /// </remarks>
    Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Forgets every loaded and pending change, so the next read sees the database as it is now.
    /// Used to retry a unit of work after <see cref="PersistenceErrors.ConcurrentUpdate"/>.
    /// </summary>
    void DiscardChanges();

    /// <summary>
    /// Runs <paramref name="work"/> - its reads and its <see cref="SaveChangesAsync"/> - in one
    /// transaction that holds <paramref name="section"/> exclusively until it ends, so a second
    /// caller waits and then decides on what the first one committed. Nothing is committed unless
    /// <paramref name="work"/> succeeds.
    /// </summary>
    Task<Result<T>> RunExclusivelyAsync<T>(
        ExclusiveSection section,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken);
}
