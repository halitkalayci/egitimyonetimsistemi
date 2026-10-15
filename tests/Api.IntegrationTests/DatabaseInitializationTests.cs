using System.Diagnostics;
using System.Net;

using Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// The deployment step behind `--initialize-database` (README, "Üretime kurulum"): it opens the
/// first administrator on an empty installation and never touches an existing one.
/// </summary>
[Collection(PostgresDatabase.Name)]
public sealed class DatabaseInitializationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private SkeletonApiFactory _factory = null!;

    public DatabaseInitializationTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new SkeletonApiFactory(_postgres.ConnectionString);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Initialize_ShouldOpenTheFirstAdminWithATemporaryPassword_AndNeverReplaceIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        await using (WebApplicationFactory<Program> first = Seeding("ilk@immib.org", "Ilk-Kurulum-Sifresi-1"))
        {
            await first.Services.InitializeDatabaseAsync(token);
        }

        await using (WebApplicationFactory<Program> again = Seeding("ikinci@immib.org", "Ikinci-Sifre-12345"))
        {
            await again.Services.InitializeDatabaseAsync(token);
        }

        using HttpClient client = _factory.CreateClient();
        LoginOutcomeDto seeded = await client.LoginAsync("ilk@immib.org", "Ilk-Kurulum-Sifresi-1", token);
        LoginOutcomeDto second = await client.LoginAsync("ikinci@immib.org", "Ikinci-Sifre-12345", token);

        seeded.StatusCode.ShouldBe(HttpStatusCode.OK);
        seeded.Role.ShouldBe("Admin");
        seeded.MustChangePassword.ShouldBeTrue();

        // An installation that already has an account is left alone: a seeded password would be a
        // known credential.
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Application_ShouldInitializeTheDatabaseAndExitWithZero_WhenStartedWithTheArgument()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        // The real entry point, as the deployment step runs it - not the method behind it - so the
        // argument check and the early exit before serving are what is under test.
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Api.dll"));
        start.ArgumentList.Add("--initialize-database");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Staging";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        start.Environment["ConnectionStrings__Database"] = _postgres.ConnectionString;
        start.Environment["Jwt__SigningKey"] = SkeletonApiFactory.TestSigningKey;
        start.Environment["SeedAdmin__Email"] = "kurulum@immib.org";
        start.Environment["SeedAdmin__Password"] = "Kurulum-Sifresi-123";

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        Task<string> errors = process.StandardError.ReadToEndAsync(token);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            // A process that went on to serve requests would never exit on its own.
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        process.ExitCode.ShouldBe(0, $"{await output}{await errors}");

        using HttpClient client = _factory.CreateClient();
        LoginOutcomeDto seeded = await client.LoginAsync("kurulum@immib.org", "Kurulum-Sifresi-123", token);
        seeded.StatusCode.ShouldBe(HttpStatusCode.OK);
        seeded.Role.ShouldBe("Admin");
    }

    private WebApplicationFactory<Program> Seeding(string email, string password) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SeedAdmin:Email", email);
            builder.UseSetting("SeedAdmin:Password", password);
        });
}
