using Application.Abstractions;
using Application.Common;
using Application.System;

using Domain.System;

using NSubstitute;

using Shouldly;

namespace Application.Tests.System;

public sealed class GetSystemInfoQueryHandlerTests
{
    private static readonly DateTimeOffset ApplicationNow =
        new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DatabaseNow =
        new(2026, 10, 14, 9, 0, 1, TimeSpan.Zero);

    private readonly ISystemInfoRepository _repository = Substitute.For<ISystemInfoRepository>();
    private readonly IDatabaseTimeProvider _databaseTime = Substitute.For<IDatabaseTimeProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public GetSystemInfoQueryHandlerTests()
    {
        _clock.UtcNow.Returns(ApplicationNow);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnApplicationNameAndBothTimesInUtc_WhenDependenciesSucceed()
    {
        GivenDatabaseTime(DatabaseNow);
        GivenEntries(SystemInfoEntry.Create("app.name", "skeleton", DatabaseNow));

        Result<SystemInfoDto> result = await CreateHandler().HandleAsync(new GetSystemInfoQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ApplicationName.ShouldBe("Eğitim Yönetim Sistemi");
        result.Value.ApplicationTimeUtc.ShouldBe(ApplicationNow);
        result.Value.DatabaseTimeUtc.ShouldBe(DatabaseNow);
        result.Value.DatabaseTimeUtc.Offset.ShouldBe(TimeSpan.Zero);
        result.Value.Entries.Count.ShouldBe(1);
        result.Value.Entries[0].Key.ShouldBe("app.name");
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnEmptyList_WhenNoRecordsExist()
    {
        GivenDatabaseTime(DatabaseNow);
        GivenEntries();

        Result<SystemInfoDto> result = await CreateHandler().HandleAsync(new GetSystemInfoQuery(), TestContext.Current.CancellationToken);

        // An empty store is a successful answer, never an error (spec 0001, AC-7).
        result.IsSuccess.ShouldBeTrue();
        result.Value.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnUnavailable_WhenDatabaseTimeCannotBeRead()
    {
        _databaseTime.GetServerTimeUtcAsync(Arg.Any<CancellationToken>())
            .Returns(Result<DateTimeOffset>.Failure(
                Error.Unavailable("database.unavailable", "Veritabanına şu anda ulaşılamıyor.")));

        Result<SystemInfoDto> result = await CreateHandler().HandleAsync(new GetSystemInfoQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Unavailable);
        result.Error.Code.ShouldBe("database.unavailable");
    }

    [Fact]
    public async Task HandleAsync_ShouldNotQueryEntries_WhenDatabaseIsUnavailable()
    {
        _databaseTime.GetServerTimeUtcAsync(Arg.Any<CancellationToken>())
            .Returns(Result<DateTimeOffset>.Failure(
                Error.Unavailable("database.unavailable", "Veritabanına şu anda ulaşılamıyor.")));

        await CreateHandler().HandleAsync(new GetSystemInfoQuery(), TestContext.Current.CancellationToken);

        await _repository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldPropagateRepositoryFailure_WhenEntriesCannotBeRead()
    {
        GivenDatabaseTime(DatabaseNow);
        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SystemInfoEntry>>.Failure(
                Error.Unavailable("database.unavailable", "Veritabanına şu anda ulaşılamıyor.")));

        Result<SystemInfoDto> result = await CreateHandler().HandleAsync(new GetSystemInfoQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Unavailable);
    }

    private GetSystemInfoQueryHandler CreateHandler() => new(_repository, _databaseTime, _clock);

    private void GivenDatabaseTime(DateTimeOffset time) =>
        _databaseTime.GetServerTimeUtcAsync(Arg.Any<CancellationToken>())
            .Returns(Result<DateTimeOffset>.Success(time));

    private void GivenEntries(params SystemInfoEntry[] entries) =>
        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SystemInfoEntry>>.Success(entries));
}
