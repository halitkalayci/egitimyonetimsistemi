using Application.Abstractions;
using Application.Common;

using Domain.System;

namespace Application.System;

public sealed class GetSystemInfoQueryHandler
{
    /// <summary>Not environment dependent, so it is a constant rather than configuration.</summary>
    private const string ApplicationName = "Eğitim Yönetim Sistemi";

    private readonly ISystemInfoRepository _repository;
    private readonly IDatabaseTimeProvider _databaseTime;
    private readonly IClock _clock;

    public GetSystemInfoQueryHandler(
        ISystemInfoRepository repository,
        IDatabaseTimeProvider databaseTime,
        IClock clock)
    {
        _repository = repository;
        _databaseTime = databaseTime;
        _clock = clock;
    }

    public async Task<Result<SystemInfoDto>> HandleAsync(
        GetSystemInfoQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result<DateTimeOffset> databaseTimeResult = await _databaseTime
            .GetServerTimeUtcAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!databaseTimeResult.IsSuccess)
        {
            return Result<SystemInfoDto>.Failure(databaseTimeResult.Error!);
        }

        Result<IReadOnlyList<SystemInfoEntry>> entriesResult = await _repository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!entriesResult.IsSuccess)
        {
            return Result<SystemInfoDto>.Failure(entriesResult.Error!);
        }

        SystemInfoEntryDto[] entries = [.. entriesResult.Value.Select(entry =>
            new SystemInfoEntryDto(entry.Id, entry.Key, entry.Value, entry.RecordedAt.ToUniversalTime()))];

        return Result<SystemInfoDto>.Success(new SystemInfoDto(
            ApplicationName,
            _clock.UtcNow.ToUniversalTime(),
            databaseTimeResult.Value.ToUniversalTime(),
            entries));
    }
}
