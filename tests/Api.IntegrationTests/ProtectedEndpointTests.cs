using System.Net;

using Shouldly;

namespace Api.IntegrationTests;

[Collection(PostgresDatabase.Name)]
public sealed class ProtectedEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public ProtectedEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    public ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Ping_ShouldReturn401_WhenRequestCarriesNoCredentials()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        // Default-deny: an endpoint that says nothing about authorization is closed, not open
        // (docs/security.md, AuthN / AuthZ).
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_ShouldReturn401_WhenBearerTokenIsGarbage()
    {
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-real-token");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
