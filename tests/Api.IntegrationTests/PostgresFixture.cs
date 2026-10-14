using Testcontainers.PostgreSql;

namespace Api.IntegrationTests;

/// <summary>
/// One real PostgreSQL container shared by every integration test class (plan 0001, risk 3):
/// a real database, because EF InMemory and SQLite do not reproduce timestamptz, numeric or
/// constraint behaviour (docs/testing.md). The container is disposed at the end of the run, so
/// check leaves nothing behind (docs/testing.md, Resource hygiene).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("egitimyonetim_test")
        .WithUsername("egitim_test")
        .WithPassword("egitim_test")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresDatabase : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
