using System.Net;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// The unreachable database is produced with a connection string pointing at a closed port rather
/// than by stopping the shared container: deterministic, order independent and safe to run in
/// parallel with the other classes (plan 0001, risk 4).
/// </summary>
public sealed class DatabaseUnavailableTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=nope;Username=nobody;Password=nothing;Timeout=1;Command Timeout=1";

    private SkeletonApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(UnreachableConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GetSystemInfo_ShouldReturn503ProblemDetails_WhenDatabaseIsUnreachable()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/system-info", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task GetSystemInfo_ShouldCarryCodeAndTraceId_WhenDatabaseIsUnreachable()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/system-info", UriKind.Relative), TestContext.Current.CancellationToken);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("database.unavailable");
        body.ShouldContain("traceId");
    }

    [Fact]
    public async Task GetSystemInfo_ShouldLeakNoInternals_WhenDatabaseIsUnreachable()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/system-info", UriKind.Relative), TestContext.Current.CancellationToken);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // docs/security.md, Input & output: no connection string, no SQL, no provider or type
        // names, no stack frames in anything the caller sees.
        foreach (string forbidden in new[]
                 {
                     "Password", "Username", "Host=", "127.0.0.1", "Npgsql", "SELECT",
                     "DbException", "SocketException", "   at "
                 })
        {
            body.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }
}
