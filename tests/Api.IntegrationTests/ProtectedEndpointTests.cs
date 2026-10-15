using System.Net;
using System.Security.Claims;
using System.Text;

using Infrastructure.Security;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

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

    [Theory]
    [InlineData(JwtOptions.Issuer, JwtOptions.Audience, HttpStatusCode.OK)]
    [InlineData("another-system", JwtOptions.Audience, HttpStatusCode.Unauthorized)]
    [InlineData(JwtOptions.Issuer, "another-client", HttpStatusCode.Unauthorized)]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    public async Task Ping_ShouldAcceptOnlyTokensMintedByThisApiForThisClient(
        string? issuer, string? audience, HttpStatusCode expected)
    {
        // Correctly signed in every case: only the issuer and audience differ.
        DateTime now = DateTime.UtcNow;
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
                new Claim(ClaimTypes.Role, "Participant")
            ]),
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SkeletonApiFactory.TestSigningKey)),
                SecurityAlgorithms.HmacSha256)
        });
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
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
