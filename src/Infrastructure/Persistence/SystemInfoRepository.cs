using Application.Abstractions;
using Application.Common;

using Domain.System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

internal sealed class SystemInfoRepository : ISystemInfoRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<SystemInfoRepository> _logger;

    public SystemInfoRepository(AppDbContext context, ILogger<SystemInfoRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SystemInfoEntry>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            List<SystemInfoEntry> entries = await _context.SystemInfoEntries
                .AsNoTracking()
                .OrderBy(entry => entry.Key)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result<IReadOnlyList<SystemInfoEntry>>.Success(entries);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            // The provider exception stays here: it never reaches the HTTP boundary, and nothing
            // from it is put in the user-facing message (docs/security.md, Input & output).
            InfrastructureLog.SystemInfoReadFailed(_logger, exception);

            return Result<IReadOnlyList<SystemInfoEntry>>.Failure(DatabaseErrors.Unavailable);
        }
    }
}
