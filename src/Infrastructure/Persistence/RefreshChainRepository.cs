using Application.Abstractions;
using Application.Common;

using Domain.People;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

internal sealed class RefreshChainRepository : IRefreshChainRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<RefreshChainRepository> _logger;

    public RefreshChainRepository(AppDbContext context, ILogger<RefreshChainRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Looks in the live link AND the spent ones: a presented secret that matches a spent link is
    /// exactly the theft signal the domain needs to see (spec 0002, AC-12).
    /// </summary>
    public async Task<Result<RefreshChain?>> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        try
        {
            RefreshChain? chain = await _context.RefreshChains
                .FirstOrDefaultAsync(
                    candidate => candidate.CurrentTokenHash == tokenHash
                        || candidate.RotatedTokenHashes.Contains(tokenHash),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<RefreshChain?>.Success(chain);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.RefreshChainReadFailed(_logger, exception);

            return Result<RefreshChain?>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public async Task<Result<IReadOnlyList<RefreshChain>>> ListActiveForParticipantAsync(
        Guid participantId,
        CancellationToken cancellationToken)
    {
        try
        {
            List<RefreshChain> chains = await _context.RefreshChains
                .Where(candidate => candidate.ParticipantId == participantId && candidate.RevokedAtUtc == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result<IReadOnlyList<RefreshChain>>.Success(chains);
        }
        catch (Exception exception) when (DatabaseFailure.IsUnavailable(exception))
        {
            InfrastructureLog.RefreshChainReadFailed(_logger, exception);

            return Result<IReadOnlyList<RefreshChain>>.Failure(DatabaseErrors.Unavailable);
        }
    }

    public void Add(RefreshChain chain) => _context.RefreshChains.Add(chain);
}
