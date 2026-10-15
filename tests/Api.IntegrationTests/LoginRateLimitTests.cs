using System.Net;
using System.Net.Http.Json;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// The rate limit is per network address and independent of the per-account lock, which is why
/// every attempt here uses a different e-mail: five failures against one account would lock that
/// account, but eleven attempts from one address must be refused with 429 regardless (AC-30).
/// </summary>
[Collection(PostgresDatabase.Name)]
public sealed class LoginRateLimitTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public LoginRateLimitTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Login_ShouldReturn429_WhenMoreThanTenAttemptsPerMinuteFromSameAddress()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();
        List<HttpStatusCode> codes = [];

        for (int attempt = 1; attempt <= 11; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { email = $"attempt-{attempt}@immib.org", password = "bir-sifre-123" },
                token);

            codes.Add(response.StatusCode);
        }

        codes.Take(10).ShouldAllBe(code => code == HttpStatusCode.BadRequest);
        codes[10].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_ShouldReturnProblemDetailsWithCode_WhenRateLimited()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            using HttpResponseMessage warmup = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { email = $"warm-{attempt}@immib.org", password = "bir-sifre-123" },
                token);
        }

        using HttpResponseMessage rejected = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { email = "one-too-many@immib.org", password = "bir-sifre-123" },
            token);
        string body = await rejected.Content.ReadAsStringAsync(token);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldContain("auth.too_many_attempts");
        body.ShouldContain("traceId");
    }
}
