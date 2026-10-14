using Domain.System;

using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

internal static class SystemInfoSeed
{
    /// <summary>
    /// One neutral row so the running application shows a filled list. The empty-list behaviour
    /// (AC-7) is covered by tests, not by leaving the database empty.
    /// </summary>
    internal static async Task EnsureSeededAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.SystemInfoEntries.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        context.SystemInfoEntries.Add(SystemInfoEntry.Create(
            "skeleton.status",
            "Yürüyen iskelet çalışıyor",
            DateTimeOffset.UtcNow));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
