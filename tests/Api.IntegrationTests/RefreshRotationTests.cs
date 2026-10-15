using System.Net;
using System.Net.Http.Json;

using Application.Abstractions;

using Domain.People;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// Cookies are handled by hand here: these tests need to present a specific secret, including one
/// that has already been spent, which an automatic cookie container would quietly replace.
/// </summary>
[Collection(PostgresDatabase.Name)]
public sealed class RefreshRotationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public RefreshRotationTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Refresh_ShouldIssueNewAccessTokenAndRotateTheSecret_WhenSecretIsValid()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();
        string first = await LoginSecretAsync(client, "rotate@immib.org", token);

        (HttpStatusCode status, string? second, string body) = await RefreshAsync(client, first, token);

        status.ShouldBe(HttpStatusCode.OK);
        second.ShouldNotBeNullOrWhiteSpace();
        second.ShouldNotBe(first);
        body.ShouldContain("accessToken");
    }

    [Fact]
    public async Task Refresh_ShouldRejectThePreviousSecret_AfterRotation()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();
        string first = await LoginSecretAsync(client, "spent@immib.org", token);
        (_, string? second, _) = await RefreshAsync(client, first, token);

        (HttpStatusCode reused, _, _) = await RefreshAsync(client, first, token);

        // spec 0002, AC-11 / AC-12
        reused.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and the newest secret dies with the chain, because we cannot tell owner from thief.
        (HttpStatusCode afterBurn, _, _) = await RefreshAsync(client, second!, token);
        afterBurn.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ShouldNeverHandOutTwoSuccessors_WhenTheSameSecretIsPresentedInParallel()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();
        string secret = await LoginSecretAsync(client, "race@immib.org", token);

        (HttpStatusCode Status, string? Secret, string Body)[] answers = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => RefreshAsync(client, secret, token)));

        // Owner and thief racing with one secret: at most one rotation may win, and every other
        // presentation is a reuse - which burns the chain, the winner's new secret included (AC-12).
        answers.Count(answer => answer.Status == HttpStatusCode.OK).ShouldBeLessThanOrEqualTo(1);

        foreach (string successor in answers.Where(answer => answer.Secret is not null).Select(answer => answer.Secret!))
        {
            (HttpStatusCode afterRace, _, _) = await RefreshAsync(client, successor, token);
            afterRace.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Refresh_ShouldClearTheCookie_WhenRefused()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("/api/auth/refresh", UriKind.Relative));
        request.WithRefreshCookie("hic-boyle-bir-sir-yok");
        using HttpResponseMessage response = await client.SendAsync(request, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        IdentityTestFixtures.SetCookieHeaders(response)
            .ShouldContain(header => header.StartsWith("egitim_refresh=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_ShouldEndOnlyCurrentSession_WhenTwoSessionsExist()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();
        await _factory.CreateAccountAsync(
            "two-devices@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        LoginOutcomeDto phone = await client.LoginAsync(
            "two-devices@immib.org", IdentityTestFixtures.ValidPassword, token);
        LoginOutcomeDto desktop = await client.LoginAsync(
            "two-devices@immib.org", IdentityTestFixtures.ValidPassword, token);

        using HttpRequestMessage logout = new(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        logout.WithRefreshCookie(phone.RefreshSecret!);
        using HttpResponseMessage loggedOut = await client.SendAsync(logout, token);
        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // spec 0002, AC-14: the other device is untouched.
        (HttpStatusCode phoneAfter, _, _) = await RefreshAsync(client, phone.RefreshSecret!, token);
        (HttpStatusCode desktopAfter, _, _) = await RefreshAsync(client, desktop.RefreshSecret!, token);

        phoneAfter.ShouldBe(HttpStatusCode.Unauthorized);
        desktopAfter.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LogoutAll_ShouldRejectEveryRefreshToken_OfThatUser()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();
        await _factory.CreateAccountAsync(
            "all-out@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        LoginOutcomeDto phone = await client.LoginAsync(
            "all-out@immib.org", IdentityTestFixtures.ValidPassword, token);
        LoginOutcomeDto desktop = await client.LoginAsync(
            "all-out@immib.org", IdentityTestFixtures.ValidPassword, token);

        client.UseBearer(desktop.AccessToken);
        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/auth/logout-all", UriKind.Relative), content: null, token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        client.UseBearer(null);

        // spec 0002, AC-15
        (HttpStatusCode phoneAfter, _, _) = await RefreshAsync(client, phone.RefreshSecret!, token);
        (HttpStatusCode desktopAfter, _, _) = await RefreshAsync(client, desktop.RefreshSecret!, token);

        phoneAfter.ShouldBe(HttpStatusCode.Unauthorized);
        desktopAfter.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_ShouldEndTheSessionsOpenedWithTheOldPassword()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = NoCookieClient();
        await _factory.CreateAccountAsync(
            "pw-session@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        LoginOutcomeDto session = await client.LoginAsync(
            "pw-session@immib.org", IdentityTestFixtures.ValidPassword, token);
        client.UseBearer(session.AccessToken);

        using HttpResponseMessage changed = await client.PostAsJsonAsync(
            new Uri("/api/auth/change-password", UriKind.Relative),
            new { currentPassword = IdentityTestFixtures.ValidPassword, newPassword = "Yeni-Sifre-45678" },
            token);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        string? fresh = IdentityTestFixtures.RefreshSecretFrom(changed);
        client.UseBearer(null);

        // The old session is over; the one handed back by the change is the only one that renews.
        (HttpStatusCode afterChange, _, _) = await RefreshAsync(client, session.RefreshSecret!, token);
        afterChange.ShouldBe(HttpStatusCode.Unauthorized);
        fresh.ShouldNotBeNull();
        (HttpStatusCode freshRenewal, _, _) = await RefreshAsync(client, fresh, token);
        freshRenewal.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ShouldRequireLogin_WhenSevenDayWindowHasPassed()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        AdjustableClock clock = new(DateTimeOffset.UtcNow);
        await using WebApplicationFactory<Program> app = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));
        using HttpClient client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await _factory.CreateAccountAsync(
            "window@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        LoginOutcomeDto login = await client.LoginAsync("window@immib.org", IdentityTestFixtures.ValidPassword, token);

        // Rotation keeps the session alive inside the window but never moves its end (AC-13).
        clock.Advance(TimeSpan.FromDays(6));
        (HttpStatusCode insideWindow, string? rotated, _) = await RefreshAsync(client, login.RefreshSecret!, token);
        clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        (HttpStatusCode afterWindow, _, string body) = await RefreshAsync(client, rotated!, token);

        insideWindow.ShouldBe(HttpStatusCode.OK);
        afterWindow.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldContain("auth.session_not_renewable");
    }

    private HttpClient NoCookieClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<string> LoginSecretAsync(HttpClient client, string email, CancellationToken token)
    {
        await _factory.CreateAccountAsync(
            email, IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        LoginOutcomeDto login = await client.LoginAsync(email, IdentityTestFixtures.ValidPassword, token);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        return login.RefreshSecret!;
    }

    private static async Task<(HttpStatusCode Status, string? Secret, string Body)> RefreshAsync(
        HttpClient client,
        string secret,
        CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("/api/auth/refresh", UriKind.Relative));
        request.WithRefreshCookie(secret);

        using HttpResponseMessage response = await client.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);

        return (response.StatusCode, IdentityTestFixtures.RefreshSecretFrom(response), body);
    }
}
