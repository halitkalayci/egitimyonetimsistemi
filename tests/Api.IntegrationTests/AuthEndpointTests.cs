using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Application.Abstractions;

using Domain.People;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Api.IntegrationTests;

[Collection(PostgresDatabase.Name)]
public sealed class AuthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public AuthEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Login_ShouldIssueSessionWithRole_WhenCredentialsAreValid()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "instructor@immib.org", IdentityTestFixtures.ValidPassword, Role.Instructor, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        LoginOutcomeDto login = await client.LoginAsync(
            "Instructor@IMMIB.org", IdentityTestFixtures.ValidPassword, token);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        login.Role.ShouldBe("Instructor");
        login.MustChangePassword.ShouldBeFalse();

        // The token really opens a protected door (AC-1).
        client.UseBearer(login.AccessToken);
        using HttpResponseMessage ping = await client.GetAsync(new Uri("/api/ping", UriKind.Relative), token);
        ping.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_ShouldSetRefreshTokenAsHttpOnlyStrictCookie_AndNotInBody()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "cookie@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { email = "cookie@immib.org", password = IdentityTestFixtures.ValidPassword },
            token);

        string body = await response.Content.ReadAsStringAsync(token);
        string cookie = IdentityTestFixtures.SetCookieHeaders(response)
            .Single(header => header.StartsWith("egitim_refresh=", StringComparison.Ordinal));

        // spec 0002, AC-28
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/auth", Case.Insensitive);
        body.ShouldNotContain("refreshToken", Case.Insensitive);
        body.ShouldNotContain(IdentityTestFixtures.RefreshSecretFrom(response)!);
    }

    [Fact]
    public async Task Login_ShouldReturnIdenticalResponse_WhenPasswordIsWrongAndWhenEmailIsUnknown()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "known@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        LoginOutcomeDto wrongPassword = await client.LoginAsync("known@immib.org", "yanlis-sifre-123", token);
        LoginOutcomeDto unknownEmail = await client.LoginAsync("unknown@immib.org", "yanlis-sifre-123", token);

        // spec 0002, AC-2: an attacker must not be able to tell the two apart.
        unknownEmail.StatusCode.ShouldBe(wrongPassword.StatusCode);
        Normalise(unknownEmail.Body).ShouldBe(Normalise(wrongPassword.Body));
    }

    [Fact]
    public async Task Login_ShouldAnswerAsForAnUnknownEmail_WhenAccountIsDisabledAndPasswordIsWrong()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "disabled@immib.org",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            disabled: true,
            cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        LoginOutcomeDto disabledWrongPassword = await client.LoginAsync("disabled@immib.org", "yanlis-sifre-123", token);
        LoginOutcomeDto unknownEmail = await client.LoginAsync("unknown@immib.org", "yanlis-sifre-123", token);
        LoginOutcomeDto disabledRightPassword = await client.LoginAsync(
            "disabled@immib.org", IdentityTestFixtures.ValidPassword, token);

        // Without the password the disabled account is indistinguishable from no account at all;
        // with it, the user is told why they cannot get in (spec 0002 UX, "devre dışı").
        disabledWrongPassword.StatusCode.ShouldBe(unknownEmail.StatusCode);
        Normalise(disabledWrongPassword.Body).ShouldBe(Normalise(unknownEmail.Body));
        disabledRightPassword.Body.ShouldContain("auth.account_disabled");
    }

    [Fact]
    public async Task Login_ShouldLockAccountAfterFiveFailures_AndRejectTheCorrectPasswordWhileLocked()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "locked@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            await client.LoginAsync("locked@immib.org", "yanlis-sifre-123", token);
        }

        LoginOutcomeDto locked = await client.LoginAsync(
            "locked@immib.org", IdentityTestFixtures.ValidPassword, token);

        // spec 0002, AC-3
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        locked.Body.ShouldContain("auth.account_locked");
    }

    [Fact]
    public async Task Login_ShouldAnswerAsForAnUnknownEmail_AndCountEveryAttempt_WhenWrongPasswordsArriveInParallel()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid id = await _factory.CreateAccountAsync(
            "parallel@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();
        const int parallel = UserAccount.MaxFailedAttempts - 1;

        // All requests read the row before any of them writes it (hashing takes far longer than
        // the read), so they race on the counter.
        LoginOutcomeDto[] wrongPasswords = await Task.WhenAll(Enumerable.Range(0, parallel)
            .Select(_ => client.LoginAsync("parallel@immib.org", "yanlis-sifre-123", token)));
        LoginOutcomeDto unknownEmail = await client.LoginAsync("unknown@immib.org", "yanlis-sifre-123", token);

        // spec 0002, AC-2: a race must not give the existing account away (no 409 conflict) ...
        foreach (LoginOutcomeDto wrongPassword in wrongPasswords)
        {
            wrongPassword.StatusCode.ShouldBe(unknownEmail.StatusCode);
            Normalise(wrongPassword.Body).ShouldBe(Normalise(unknownEmail.Body));
        }

        // ... and AC-3: none of the parallel attempts is lost, so one more wrong password locks.
        (await _factory.ReadFailedAttemptsAsync(id, token)).ShouldBe(parallel);
        await client.LoginAsync("parallel@immib.org", "yanlis-sifre-123", token);
        LoginOutcomeDto locked = await client.LoginAsync("parallel@immib.org", IdentityTestFixtures.ValidPassword, token);
        locked.Body.ShouldContain("auth.account_locked");
    }

    [Fact]
    public async Task Login_ShouldKeepTheLockForFifteenMinutesWithoutExtending_AndLetTheCorrectPasswordInAfterwards()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        AdjustableClock clock = new(DateTimeOffset.UtcNow);
        await using WebApplicationFactory<Program> app = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));
        using HttpClient client = app.CreateClient();
        Guid id = await _factory.CreateAccountAsync(
            "window-lock@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        for (int attempt = 1; attempt <= UserAccount.MaxFailedAttempts; attempt++)
        {
            await client.LoginAsync("window-lock@immib.org", "yanlis-sifre-123", token);
        }

        // spec 0002, AC-3: the lock written by the database lasts 15 minutes, and an attempt made
        // while locked does not move its end.
        clock.Advance(TimeSpan.FromMinutes(14));
        LoginOutcomeDto stillLocked = await client.LoginAsync(
            "window-lock@immib.org", IdentityTestFixtures.ValidPassword, token);
        LoginOutcomeDto wrongWhileLocked = await client.LoginAsync("window-lock@immib.org", "yanlis-sifre-123", token);

        // AC-4: once the 15 minutes from the first lock are over, the correct password gets in.
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        LoginOutcomeDto afterWindow = await client.LoginAsync(
            "window-lock@immib.org", IdentityTestFixtures.ValidPassword, token);

        stillLocked.Body.ShouldContain("auth.account_locked");
        wrongWhileLocked.Body.ShouldContain("auth.account_locked");
        afterWindow.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _factory.ReadFailedAttemptsAsync(id, token)).ShouldBe(0);
    }

    [Fact]
    public async Task Login_ShouldResetTheFailedAttemptCounter_WhenTheCorrectPasswordFollowsFailures()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid id = await _factory.CreateAccountAsync(
            "reset-counter@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        for (int attempt = 1; attempt < UserAccount.MaxFailedAttempts; attempt++)
        {
            await client.LoginAsync("reset-counter@immib.org", "yanlis-sifre-123", token);
        }

        LoginOutcomeDto success = await client.LoginAsync(
            "reset-counter@immib.org", IdentityTestFixtures.ValidPassword, token);
        LoginOutcomeDto oneMoreWrong = await client.LoginAsync("reset-counter@immib.org", "yanlis-sifre-123", token);

        // spec 0002, AC-4: the earlier failures no longer count - one more wrong password is the
        // first of a new series, not the fifth that would lock the account.
        success.StatusCode.ShouldBe(HttpStatusCode.OK);
        oneMoreWrong.Body.ShouldContain("auth.invalid_credentials");
        (await _factory.ReadFailedAttemptsAsync(id, token)).ShouldBe(1);
    }

    [Fact]
    public async Task Login_ShouldReject_WhenAccountIsDisabled()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "disabled@immib.org",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            disabled: true,
            cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        LoginOutcomeDto login = await client.LoginAsync(
            "disabled@immib.org", IdentityTestFixtures.ValidPassword, token);

        login.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        login.Body.ShouldContain("auth.account_disabled");
    }

    [Fact]
    public async Task ProtectedEndpoint_ShouldRejectWithPasswordChangeRequired_WhenAccountHasTemporaryPassword()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "temp@immib.org",
            IdentityTestFixtures.ValidPassword,
            Role.Admin,
            mustChangePassword: true,
            cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        LoginOutcomeDto login = await client.LoginAsync(
            "temp@immib.org", IdentityTestFixtures.ValidPassword, token);
        login.MustChangePassword.ShouldBeTrue();

        client.UseBearer(login.AccessToken);
        using HttpResponseMessage ping = await client.GetAsync(new Uri("/api/ping", UriKind.Relative), token);
        string body = await ping.Content.ReadAsStringAsync(token);

        // spec 0002, AC-6 / AC-23: the gate cannot be walked around by calling an endpoint directly.
        ping.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("auth.password_change_required");
    }

    [Fact]
    public async Task ChangePassword_ShouldRejectOldPasswordAfterwards_AndAcceptTheNewOne()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "change@immib.org",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            mustChangePassword: true,
            cancellationToken: token);
        using HttpClient client = _factory.CreateClient();

        LoginOutcomeDto login = await client.LoginAsync(
            "change@immib.org", IdentityTestFixtures.ValidPassword, token);
        client.UseBearer(login.AccessToken);

        using HttpResponseMessage changed = await client.PostAsJsonAsync(
            new Uri("/api/auth/change-password", UriKind.Relative),
            new { currentPassword = IdentityTestFixtures.ValidPassword, newPassword = "Yeni-Sifre-45678" },
            token);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The device that changed the password continues on a fresh, unrestricted session.
        JsonElement session = await changed.Content.ReadFromJsonAsync<JsonElement>(token);
        session.GetProperty("mustChangePassword").GetBoolean().ShouldBeFalse();
        client.UseBearer(session.GetProperty("accessToken").GetString());
        using HttpResponseMessage ping = await client.GetAsync(new Uri("/api/ping", UriKind.Relative), token);
        ping.StatusCode.ShouldBe(HttpStatusCode.OK);

        client.UseBearer(null);
        LoginOutcomeDto withOld = await client.LoginAsync(
            "change@immib.org", IdentityTestFixtures.ValidPassword, token);
        LoginOutcomeDto withNew = await client.LoginAsync("change@immib.org", "Yeni-Sifre-45678", token);

        // spec 0002, AC-7
        withOld.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withNew.StatusCode.ShouldBe(HttpStatusCode.OK);
        withNew.MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangePassword_ShouldLockTheAccount_WhenTheCurrentPasswordIsGuessedRepeatedly()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "guess@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();
        LoginOutcomeDto login = await client.LoginAsync("guess@immib.org", IdentityTestFixtures.ValidPassword, token);
        client.UseBearer(login.AccessToken);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage guess = await client.PostAsJsonAsync(
                new Uri("/api/auth/change-password", UriKind.Relative),
                new { currentPassword = $"tahmin-{attempt}-abcdef", newPassword = "Yeni-Sifre-45678" },
                token);
            guess.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // A held access token is not a way around the lock: the right password is refused now too.
        client.UseBearer(null);
        LoginOutcomeDto afterGuessing = await client.LoginAsync(
            "guess@immib.org", IdentityTestFixtures.ValidPassword, token);
        afterGuessing.Body.ShouldContain("auth.account_locked");
    }

    [Fact]
    public async Task ChangePassword_ShouldRejectTooShortPassword_AndNameTheRule()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "short@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        using HttpClient client = _factory.CreateClient();
        LoginOutcomeDto login = await client.LoginAsync(
            "short@immib.org", IdentityTestFixtures.ValidPassword, token);
        client.UseBearer(login.AccessToken);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/auth/change-password", UriKind.Relative),
            new { currentPassword = IdentityTestFixtures.ValidPassword, newPassword = "kisa" },
            token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-8
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("auth.password_too_short");
        body.ShouldContain("10");
    }

    [Fact]
    public async Task StoredPassword_ShouldBeSaltedHash_NotPlainText()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await _factory.CreateAccountAsync(
            "hash1@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);
        await _factory.CreateAccountAsync(
            "hash2@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        IReadOnlyList<string> hashes = await _factory.ReadPasswordHashesAsync(token);

        // spec 0002, AC-9: no plaintext anywhere, and the same password hashes differently twice.
        hashes.Count.ShouldBe(2);
        hashes.ShouldAllBe(hash => !hash.Contains(IdentityTestFixtures.ValidPassword, StringComparison.Ordinal));
        hashes.ShouldAllBe(hash => hash.StartsWith("v1$", StringComparison.Ordinal));
        hashes[0].ShouldNotBe(hashes[1]);
    }

    [Fact]
    public async Task Unauthenticated_ShouldReturnProblemDetailsWithTraceId_OnProtectedEndpoint()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-21 (the 401 half of AC-3 from spec 0001, now parsed rather than searched)
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        JsonElement problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>Strips the per-request trace identifier so two problem bodies can be compared.</summary>
    private static string Normalise(string body)
    {
        JsonElement problem = JsonSerializer.Deserialize<JsonElement>(body);
        Dictionary<string, JsonElement> fields = [];

        foreach (JsonProperty property in problem.EnumerateObject())
        {
            if (property.NameEquals("traceId"))
            {
                continue;
            }

            fields[property.Name] = property.Value;
        }

        return JsonSerializer.Serialize(fields);
    }
}
