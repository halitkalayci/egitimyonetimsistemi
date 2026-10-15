using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Application.Abstractions;

using Domain.People;

using Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests;

/// <summary>
/// Helpers shared by the identity tests. Accounts are created through the real password hasher, so
/// what the tests log in against is what production stores.
/// </summary>
internal static class IdentityTestFixtures
{
    internal const string ValidPassword = "Gecerli-Sifre-123";

    internal static async Task<Guid> CreateAccountAsync(
        this WebApplicationFactory<Program> factory,
        string email,
        string password,
        Role role,
        Guid? memberFirmId = null,
        bool mustChangePassword = false,
        bool disabled = false,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        Participant person = Participant.CreateWithAccount(
            $"Test {role}", memberFirmId, email, hasher.Hash(password), role);

        if (!mustChangePassword)
        {
            person.Account!.ChangePassword(hasher.Hash(password));
        }

        if (disabled)
        {
            person.Account!.Disable();
        }

        context.Participants.Add(person);
        await context.SaveChangesAsync(cancellationToken);

        return person.Id;
    }

    internal static async Task<LoginOutcomeDto> LoginAsync(
        this HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { email, password },
            cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? accessToken = null;
        string? role = null;
        bool mustChange = false;

        if (response.IsSuccessStatusCode)
        {
            JsonElement payload = JsonSerializer.Deserialize<JsonElement>(body);
            accessToken = payload.GetProperty("accessToken").GetString();
            role = payload.GetProperty("role").GetString();
            mustChange = payload.GetProperty("mustChangePassword").GetBoolean();
        }

        return new LoginOutcomeDto(
            response.StatusCode,
            accessToken,
            role,
            mustChange,
            RefreshSecretFrom(response),
            body);
    }

    /// <summary>Reads the refresh secret out of the Set-Cookie header, the only place it appears.</summary>
    internal static string? RefreshSecretFrom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            return null;
        }

        foreach (string cookie in cookies)
        {
            if (!cookie.StartsWith("egitim_refresh=", StringComparison.Ordinal))
            {
                continue;
            }

            string value = cookie["egitim_refresh=".Length..].Split(';')[0];

            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    internal static IEnumerable<string> SetCookieHeaders(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
            ? cookies
            : [];

    internal static void UseBearer(this HttpClient client, string? accessToken) =>
        client.DefaultRequestHeaders.Authorization = accessToken is null
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);

    internal static HttpRequestMessage WithRefreshCookie(this HttpRequestMessage request, string secret)
    {
        request.Headers.Add("Cookie", $"egitim_refresh={secret}");

        return request;
    }
}

internal sealed record LoginOutcomeDto(
    System.Net.HttpStatusCode StatusCode,
    string? AccessToken,
    string? Role,
    bool MustChangePassword,
    string? RefreshSecret,
    string Body);
