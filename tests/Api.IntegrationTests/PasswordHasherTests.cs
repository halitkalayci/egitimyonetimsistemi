using Application.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// The decoy used for unknown e-mails must cost exactly what a real verification costs. PBKDF2's
/// cost is set by the iteration count AND the derived length (every 32 bytes of SHA-256 output is
/// one more full run), so both are compared against a freshly made hash (spec 0002, AC-29).
/// </summary>
[Collection(PostgresDatabase.Name)]
public sealed class PasswordHasherTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public PasswordHasherTests(PostgresFixture postgres) => _postgres = postgres;

    public ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public void DecoyHash_ShouldCarryTheSameCostParametersAsARealHash()
    {
        IPasswordHasher hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        string[] decoy = hasher.DecoyHash.Split('$');
        string[] real = hasher.Hash(IdentityTestFixtures.ValidPassword).Split('$');

        decoy.Length.ShouldBe(4);
        decoy[0].ShouldBe(real[0]);
        decoy[1].ShouldBe(real[1]);
        Convert.FromBase64String(decoy[2]).Length.ShouldBe(Convert.FromBase64String(real[2]).Length);
        Convert.FromBase64String(decoy[3]).Length.ShouldBe(Convert.FromBase64String(real[3]).Length);
    }

    [Fact]
    public void DecoyHash_ShouldNotVerifyAnyOrdinaryPassword()
    {
        IPasswordHasher hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        hasher.Verify(IdentityTestFixtures.ValidPassword, hasher.DecoyHash).ShouldBeFalse();
        hasher.Verify(string.Empty, hasher.DecoyHash).ShouldBeFalse();
    }
}
