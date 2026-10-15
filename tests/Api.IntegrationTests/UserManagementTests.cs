using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Application.Abstractions;

using Domain.People;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Api.IntegrationTests;

[Collection(PostgresDatabase.Name)]
public sealed class UserManagementTests : IAsyncLifetime
{
    private static readonly Guid FirmA = Guid.CreateVersion7();
    private static readonly Guid FirmB = Guid.CreateVersion7();

    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public UserManagementTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CreateUser_ShouldReturnTemporaryPasswordOnce_AndNeverExposeItAgain()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);

        using HttpResponseMessage created = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new { fullName = "Yeni Kullanıcı", email = "yeni@immib.org", role = "Participant", memberFirmId = FirmA },
            token);
        string createdBody = await created.Content.ReadAsStringAsync(token);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = JsonSerializer.Deserialize<JsonElement>(createdBody);
        string temporaryPassword = payload.GetProperty("temporaryPassword").GetString()!;
        temporaryPassword.Length.ShouldBeGreaterThanOrEqualTo(10);

        // spec 0002, AC-18: the listing must not carry it, under any name.
        using HttpResponseMessage listed = await admin.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        string listedBody = await listed.Content.ReadAsStringAsync(token);

        listedBody.ShouldNotContain(temporaryPassword);
        listedBody.ShouldNotContain("temporaryPassword", Case.Insensitive);
        listedBody.ShouldNotContain("passwordHash", Case.Insensitive);
    }

    [Fact]
    public async Task CreateUser_ShouldReturn409_WhenEmailAlreadyExists()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        await _factory.CreateAccountAsync(
            "taken@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, FirmA, cancellationToken: token);

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new { fullName = "Aynı E-posta", email = "Taken@IMMIB.org", role = "Participant", memberFirmId = FirmA },
            token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-31 - including the e-mail being matched case-insensitively.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("users.email_already_used");
    }

    [Fact]
    public async Task FirmRepresentative_ShouldBeRejected_WhenCreatingUserForAnotherFirm()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient representative = await FirmRepresentativeClientAsync(token);

        using HttpResponseMessage response = await representative.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new { fullName = "Başka Firma", email = "baska@firma.com", role = "Participant", memberFirmId = FirmB },
            token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-19: refused, and nothing was created in either firm.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("users.other_firm");

        using HttpClient admin = await AdminClientAsync(token);
        using HttpResponseMessage list = await admin.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        (await list.Content.ReadAsStringAsync(token)).ShouldNotContain("baska@firma.com");
    }

    [Fact]
    public async Task FirmRepresentative_ShouldSeeOnlyOwnFirmUsers_InList()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient representative = await FirmRepresentativeClientAsync(token);
        await _factory.CreateAccountAsync(
            "mine@firma.com", IdentityTestFixtures.ValidPassword, Role.Participant, FirmA, cancellationToken: token);
        await _factory.CreateAccountAsync(
            "theirs@firma.com", IdentityTestFixtures.ValidPassword, Role.Participant, FirmB, cancellationToken: token);

        using HttpResponseMessage response = await representative.GetAsync(
            new Uri("/api/users", UriKind.Relative), token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-20
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("mine@firma.com");
        body.ShouldNotContain("theirs@firma.com");

        // The admin who seeded the representative has no firm at all and must not show up either.
        body.ShouldNotContain("admin@immib.org");
    }

    [Fact]
    public async Task FirmRepresentative_ShouldBeRejected_WhenRequestingUserOfAnotherFirmById()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient representative = await FirmRepresentativeClientAsync(token);
        Guid otherFirmUser = await _factory.CreateAccountAsync(
            "other-id@firma.com",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            FirmB,
            cancellationToken: token);

        using HttpResponseMessage response = await representative.PutAsJsonAsync(
            new Uri($"/api/users/{otherFirmUser}/role", UriKind.Relative),
            new { role = "Participant" },
            token);
        string body = await response.Content.ReadAsStringAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("users.other_firm");
    }

    [Theory]
    [InlineData("reset-password")]
    [InlineData("status")]
    [InlineData("role")]
    public async Task FirmRepresentative_ShouldBeRejected_WhenTargetingAnotherRepresentativeOfTheSameFirm(string action)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient representative = await FirmRepresentativeClientAsync(token);
        Guid colleague = await _factory.CreateAccountAsync(
            "colleague@firma.com",
            IdentityTestFixtures.ValidPassword,
            Role.FirmRepresentative,
            FirmA,
            cancellationToken: token);
        Uri uri = new($"/api/users/{colleague}/{action}", UriKind.Relative);

        using HttpResponseMessage response = action switch
        {
            "reset-password" => await representative.PostAsync(uri, content: null, token),
            "status" => await representative.PutAsJsonAsync(uri, new { disabled = true }, token),
            _ => await representative.PutAsJsonAsync(uri, new { role = "Participant" }, token)
        };
        string body = await response.Content.ReadAsStringAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("users.not_allowed");
        body.ShouldNotContain("temporaryPassword");

        // The colleague's own password still works and the role is untouched: nothing was handed over.
        using HttpClient colleagueClient = _factory.CreateClient();
        LoginOutcomeDto login = await colleagueClient.LoginAsync(
            "colleague@firma.com", IdentityTestFixtures.ValidPassword, token);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.Role.ShouldBe("FirmRepresentative");
    }

    [Theory]
    [InlineData("FirmRepresentative")]
    [InlineData("Instructor")]
    [InlineData("Admin")]
    public async Task FirmRepresentative_ShouldBeRejected_WhenAssigningNonParticipantRole(string role)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient representative = await FirmRepresentativeClientAsync(token);
        Guid ownFirmUser = await _factory.CreateAccountAsync(
            $"own-{role}@firma.com",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            FirmA,
            cancellationToken: token);

        using HttpResponseMessage response = await representative.PutAsJsonAsync(
            new Uri($"/api/users/{ownFirmUser}/role", UriKind.Relative),
            new { role },
            token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-32
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("users.role_not_assignable");
    }

    [Theory]
    [InlineData(Role.Instructor)]
    [InlineData(Role.Participant)]
    public async Task UserEndpoints_ShouldReturn403_ForRolesThatMayNotManageUsers(Role role)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();
        await _factory.CreateAccountAsync(
            $"{role}@immib.org", IdentityTestFixtures.ValidPassword, role, cancellationToken: token);
        LoginOutcomeDto login = await client.LoginAsync(
            $"{role}@immib.org", IdentityTestFixtures.ValidPassword, token);
        client.UseBearer(login.AccessToken);

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        string body = await response.Content.ReadAsStringAsync(token);

        // spec 0002, AC-21
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        JsonElement problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();

        // The general "not allowed", not the role-assignment message that once answered here.
        body.ShouldContain("users.not_allowed");
    }

    [Fact]
    public async Task RoleChange_ShouldRejectRefreshImmediately_AndNewRoleAppliesAfterRelogin()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        using HttpClient user = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        Guid targetId = await _factory.CreateAccountAsync(
            "promoted@immib.org",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            cancellationToken: token);
        LoginOutcomeDto before = await user.LoginAsync(
            "promoted@immib.org", IdentityTestFixtures.ValidPassword, token);

        using HttpResponseMessage promoted = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{targetId}/role", UriKind.Relative),
            new { role = "Admin" },
            token);
        promoted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // spec 0002, AC-16: renewal stops at once...
        using HttpRequestMessage refresh = new(HttpMethod.Post, new Uri("/api/auth/refresh", UriKind.Relative));
        refresh.WithRefreshCookie(before.RefreshSecret!);
        using HttpResponseMessage refreshed = await user.SendAsync(refresh, token);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and the new role is in force after logging in again.
        LoginOutcomeDto after = await user.LoginAsync(
            "promoted@immib.org", IdentityTestFixtures.ValidPassword, token);
        after.Role.ShouldBe("Admin");

        user.UseBearer(after.AccessToken);
        using HttpResponseMessage asAdmin = await user.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        asAdmin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RoleDemotion_ShouldRejectRefreshImmediately_AndTheOldRoleIsRefusedAfterRelogin()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        using HttpClient user = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        Guid targetId = await _factory.CreateAccountAsync(
            "demoted@firma.com",
            IdentityTestFixtures.ValidPassword,
            Role.FirmRepresentative,
            FirmA,
            cancellationToken: token);
        LoginOutcomeDto before = await user.LoginAsync("demoted@firma.com", IdentityTestFixtures.ValidPassword, token);
        user.UseBearer(before.AccessToken);
        using (HttpResponseMessage asRepresentative = await user.GetAsync(new Uri("/api/users", UriKind.Relative), token))
        {
            asRepresentative.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using HttpResponseMessage demoted = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{targetId}/role", UriKind.Relative), new { role = "Participant" }, token);
        demoted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // spec 0002, AC-16: renewal stops at once...
        user.UseBearer(null);
        using HttpRequestMessage refresh = new(HttpMethod.Post, new Uri("/api/auth/refresh", UriKind.Relative));
        refresh.WithRefreshCookie(before.RefreshSecret!);
        using HttpResponseMessage refreshed = await user.SendAsync(refresh, token);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and after logging in again the endpoint the old role could reach is refused.
        LoginOutcomeDto after = await user.LoginAsync("demoted@firma.com", IdentityTestFixtures.ValidPassword, token);
        after.Role.ShouldBe("Participant");
        user.UseBearer(after.AccessToken);
        using HttpResponseMessage asParticipant = await user.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        asParticipant.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Disable_ShouldRejectRefreshImmediately_AndBlockLogin()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        using HttpClient user = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        Guid targetId = await _factory.CreateAccountAsync(
            "to-disable@immib.org",
            IdentityTestFixtures.ValidPassword,
            Role.Participant,
            cancellationToken: token);
        LoginOutcomeDto session = await user.LoginAsync(
            "to-disable@immib.org", IdentityTestFixtures.ValidPassword, token);

        using HttpResponseMessage disabled = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{targetId}/status", UriKind.Relative),
            new { disabled = true },
            token);
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK);

        // spec 0002, AC-17
        using HttpRequestMessage refresh = new(HttpMethod.Post, new Uri("/api/auth/refresh", UriKind.Relative));
        refresh.WithRefreshCookie(session.RefreshSecret!);
        using HttpResponseMessage refreshed = await user.SendAsync(refresh, token);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        LoginOutcomeDto blocked = await user.LoginAsync(
            "to-disable@immib.org", IdentityTestFixtures.ValidPassword, token);
        blocked.Body.ShouldContain("auth.account_disabled");
    }

    [Fact]
    public async Task ResetPassword_ShouldIssueNewTemporaryPassword_AndForceAChangeOnNextLogin()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        using HttpClient user = _factory.CreateClient();

        Guid targetId = await _factory.CreateAccountAsync(
            "forgot@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, cancellationToken: token);

        using HttpResponseMessage reset = await admin.PostAsync(
            new Uri($"/api/users/{targetId}/reset-password", UriKind.Relative), content: null, token);
        string body = await reset.Content.ReadAsStringAsync(token);
        string temporary = JsonSerializer.Deserialize<JsonElement>(body)
            .GetProperty("temporaryPassword").GetString()!;

        LoginOutcomeDto withOld = await user.LoginAsync(
            "forgot@immib.org", IdentityTestFixtures.ValidPassword, token);
        LoginOutcomeDto withTemporary = await user.LoginAsync("forgot@immib.org", temporary, token);

        withOld.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withTemporary.StatusCode.ShouldBe(HttpStatusCode.OK);
        withTemporary.MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task SetStatus_ShouldReturn400AndKeepTheAccountDisabled_WhenTheBodyOmitsTheStatus()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        Guid target = await _factory.CreateAccountAsync(
            "kapali@immib.org", IdentityTestFixtures.ValidPassword, Role.Participant, disabled: true, cancellationToken: token);

        using HttpResponseMessage response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{target}/status", UriKind.Relative), new { }, token);
        string body = await response.Content.ReadAsStringAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("users.status_required");

        // An empty body must not have silently re-opened the account.
        using HttpClient user = _factory.CreateClient();
        LoginOutcomeDto login = await user.LoginAsync("kapali@immib.org", IdentityTestFixtures.ValidPassword, token);
        login.Body.ShouldContain("auth.account_disabled");
    }

    [Fact]
    public async Task CreateUser_ShouldReturn400NotAnOutage_WhenTheNameIsTooLong()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new { fullName = new string('a', 300), email = "uzun@immib.org", role = "Participant" },
            token);
        string body = await response.Content.ReadAsStringAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("users.full_name_too_long");
    }

    [Fact]
    public async Task Admin_ShouldNotBeAbleToDisableTheirOwnAccount()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using HttpClient admin = await AdminClientAsync(token);
        using HttpResponseMessage list = await admin.GetAsync(new Uri("/api/users", UriKind.Relative), token);
        Guid self = (await list.Content.ReadFromJsonAsync<JsonElement>(token))
            .EnumerateArray()
            .Single(user => user.GetProperty("email").GetString() == "admin@immib.org")
            .GetProperty("id").GetGuid();

        using HttpResponseMessage response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{self}/status", UriKind.Relative), new { disabled = true }, token);
        string body = await response.Content.ReadAsStringAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("users.own_access");
    }

    [Fact]
    public async Task TwoAdmins_ShouldNeverLeaveTheSystemWithoutAnActiveAdmin_WhenTheyDisableEachOtherAtOnce()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        // A few rounds, because one round may not happen to interleave; each round starts over with
        // exactly two active Admins (login limit: 10 per minute, two per round).
        for (int round = 1; round <= 4; round++)
        {
            await _factory.ResetDatabaseAsync(token);
            (Guid firstId, HttpClient first) = await SignedInAdminAsync($"first-{round}@immib.org", token);
            (Guid secondId, HttpClient second) = await SignedInAdminAsync($"second-{round}@immib.org", token);

            using (first)
            using (second)
            {
                HttpResponseMessage[] responses = await Task.WhenAll(
                    first.PutAsJsonAsync(new Uri($"/api/users/{secondId}/status", UriKind.Relative), new { disabled = true }, token),
                    second.PutAsJsonAsync(new Uri($"/api/users/{firstId}/status", UriKind.Relative), new { disabled = true }, token));

                // spec 0002 / review L-2: each saw "two Admins" before; only one may act on it.
                responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
                (await CountActiveAdminsAsync(token)).ShouldBe(1);

                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }
    }

    private async Task<(Guid Id, HttpClient Client)> SignedInAdminAsync(string email, CancellationToken token)
    {
        Guid id = await _factory.CreateAccountAsync(
            email, IdentityTestFixtures.ValidPassword, Role.Admin, cancellationToken: token);
        HttpClient client = _factory.CreateClient();

        LoginOutcomeDto login = await client.LoginAsync(email, IdentityTestFixtures.ValidPassword, token);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        client.UseBearer(login.AccessToken);

        return (id, client);
    }

    private async Task<int> CountActiveAdminsAsync(CancellationToken token)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IParticipantRepository>()
            .CountActiveAdminsAsync(token)).Value;
    }

    private async Task<HttpClient> AdminClientAsync(CancellationToken token)
    {
        HttpClient client = _factory.CreateClient();
        await _factory.CreateAccountAsync(
            "admin@immib.org", IdentityTestFixtures.ValidPassword, Role.Admin, cancellationToken: token);

        LoginOutcomeDto login = await client.LoginAsync(
            "admin@immib.org", IdentityTestFixtures.ValidPassword, token);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        client.UseBearer(login.AccessToken);

        return client;
    }

    private async Task<HttpClient> FirmRepresentativeClientAsync(CancellationToken token)
    {
        HttpClient client = _factory.CreateClient();
        await _factory.CreateAccountAsync(
            "rep@firma.com",
            IdentityTestFixtures.ValidPassword,
            Role.FirmRepresentative,
            FirmA,
            cancellationToken: token);

        LoginOutcomeDto login = await client.LoginAsync("rep@firma.com", IdentityTestFixtures.ValidPassword, token);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        client.UseBearer(login.AccessToken);

        return client;
    }
}
