using Application.Common;

using Domain.System;

namespace Application.Abstractions;

public interface ISystemInfoRepository
{
    /// <summary>Every stored entry; an empty list is a valid, non-error answer.</summary>
    Task<Result<IReadOnlyList<SystemInfoEntry>>> GetAllAsync(CancellationToken cancellationToken);
}
