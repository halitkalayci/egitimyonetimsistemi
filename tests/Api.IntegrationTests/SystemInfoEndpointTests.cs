using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Shouldly;

namespace Api.IntegrationTests;

[Collection(PostgresDatabase.Name)]
public sealed class SystemInfoEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public SystemInfoEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GetSystemInfo_ShouldReturnApplicationNameAndDatabaseTimeInUtc_WhenDatabaseAvailable()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await _factory.AddEntryAsync("skeleton.status", "çalışıyor", cancellationToken);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/system-info", UriKind.Relative), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        body.GetProperty("applicationName").GetString().ShouldBe("Eğitim Yönetim Sistemi");

        // The database time really comes from the server and arrives as UTC (docs/conventions.md).
        DateTimeOffset databaseTime = body.GetProperty("databaseTimeUtc").GetDateTimeOffset();
        databaseTime.Offset.ShouldBe(TimeSpan.Zero);
        databaseTime.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        JsonElement entries = body.GetProperty("entries");
        entries.GetArrayLength().ShouldBe(1);
        entries[0].GetProperty("key").GetString().ShouldBe("skeleton.status");
        entries[0].GetProperty("recordedAtUtc").GetDateTimeOffset().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetSystemInfo_ShouldReturnEmptyEntryList_WhenNothingIsStored()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/system-info", UriKind.Relative), cancellationToken);

        // Empty is a successful answer, not an error (spec 0001, AC-7).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        body.GetProperty("entries").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GetSystemInfo_ShouldAnswer_WhenRequestCarriesNoCredentials()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/system-info", UriKind.Relative), cancellationToken);

        // This endpoint is the one deliberate exception to default-deny (spec 0001, AC-4).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
