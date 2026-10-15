using Application.Abstractions;
using Application.Common;

using Domain.People;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// The database refusing data is a request problem, not an outage: it must never come back as the
/// 503 "unreachable" answer. These tests go below the use cases on purpose, straight to the unit of
/// work, because the use cases now validate first and would hide the mapping.
/// </summary>
[Collection(PostgresDatabase.Name)]
public sealed class PersistenceErrorMappingTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public PersistenceErrorMappingTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Save_ShouldReportUniqueViolation_WhenTwoAccountsShareAnEmail()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync("ayni@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        Result<int> saved = await SaveAsync(
            Participant.CreateWithAccount("İkinci", null, "ayni@immib.org", "hash", Role.Participant), token);

        saved.Error.ShouldBe(PersistenceErrors.UniqueViolation);
    }

    [Fact]
    public async Task Save_ShouldReportValueTooLong_WhenAValueDoesNotFitItsColumn()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        Result<int> saved = await SaveAsync(
            Participant.CreateWithoutAccount(new string('a', Participant.MaxFullNameLength + 1), null), token);

        saved.Error.ShouldBe(PersistenceErrors.ValueTooLong);
        saved.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    private async Task<Result<int>> SaveAsync(Participant person, CancellationToken token)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IParticipantRepository>().Add(person);

        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(token);
    }
}
