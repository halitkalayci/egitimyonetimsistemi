using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// spec 0002, AC-27: a missing or too-short signing key must stop the application instead of
/// letting it serve requests with a guessable secret.
/// </summary>
public sealed class StartupConfigurationTests
{
    [Fact]
    public void Startup_ShouldFail_WhenSigningKeyIsMissing()
    {
        InvalidOperationException failure = Should.Throw<InvalidOperationException>(() =>
        {
            using NoSigningKeyFactory factory = new(signingKey: null);
            using HttpClient client = factory.CreateClient();
        });

        failure.Message.ShouldContain("Jwt:SigningKey");
    }

    [Fact]
    public void Startup_ShouldFail_WhenSigningKeyIsTooShort()
    {
        InvalidOperationException failure = Should.Throw<InvalidOperationException>(() =>
        {
            using NoSigningKeyFactory factory = new("kisa-anahtar");
            using HttpClient client = factory.CreateClient();
        });

        failure.Message.ShouldContain("32");
    }

    private sealed class NoSigningKeyFactory : WebApplicationFactory<Program>
    {
        private readonly string? _signingKey;

        internal NoSigningKeyFactory(string? signingKey) => _signingKey = signingKey;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Staging);
            builder.UseSetting(
                "ConnectionStrings:Database",
                "Host=127.0.0.1;Port=1;Database=nope;Username=nobody;Password=nothing;Timeout=1");
            builder.UseSetting("Jwt:SigningKey", _signingKey ?? string.Empty);
        }
    }
}
