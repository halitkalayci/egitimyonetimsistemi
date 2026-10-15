using Application.Common;

using Domain.People;

namespace Application.Abstractions;

public interface IRefreshChainRepository
{
    Task<Result<RefreshChain?>> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<RefreshChain>>> ListActiveForParticipantAsync(
        Guid participantId,
        CancellationToken cancellationToken);

    void Add(RefreshChain chain);
}
