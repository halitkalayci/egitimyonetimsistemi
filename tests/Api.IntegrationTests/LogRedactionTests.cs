using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using Domain.People;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Shouldly;

namespace Api.IntegrationTests;

/// <summary>
/// spec 0002, AC-26: no password, temporary password, token or personal datum may appear in any log
/// line. The whole identity flow is exercised while every log entry is captured, and the assertion
/// is made against the collected text rather than against a sample of it.
/// </summary>
[Collection(PostgresDatabase.Name)]
public sealed class LogRedactionTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly CapturingLoggerProvider _logs = new();
    private CapturingApiFactory _factory = null!;

    public LogRedactionTests(PostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _factory = new CapturingApiFactory(_postgres.ConnectionString, _logs);
        await _factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Logs_ShouldContainNoSecretOrPersonalData_DuringAuthFlows()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        const string password = "Gizli-Sifre-9876";
        const string email = "log.test@immib.org";

        await _factory.CreateAccountAsync("admin@immib.org", password, Role.Admin, cancellationToken: token);
        using HttpClient admin = _factory.CreateClient();
        LoginOutcomeDto adminLogin = await admin.LoginAsync("admin@immib.org", password, token);
        admin.UseBearer(adminLogin.AccessToken);

        // Create a user (temporary password in the response), log in as them, rotate, reset.
        using HttpResponseMessage created = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new { fullName = "Log Testi", email, role = "Participant", memberFirmId = (Guid?)null },
            token);
        string createdBody = await created.Content.ReadAsStringAsync(token);
        JsonElement payload = JsonSerializer.Deserialize<JsonElement>(createdBody);
        string temporaryPassword = payload.GetProperty("temporaryPassword").GetString()!;
        Guid userId = payload.GetProperty("user").GetProperty("id").GetGuid();

        using HttpClient user = _factory.CreateClient();
        LoginOutcomeDto userLogin = await user.LoginAsync(email, temporaryPassword, token);
        await user.LoginAsync(email, "yanlis-sifre-123", token);
        using HttpResponseMessage refreshed = await user.PostAsync(
            new Uri("/api/auth/refresh", UriKind.Relative), content: null, token);

        using HttpResponseMessage reset = await admin.PostAsync(
            new Uri($"/api/users/{userId}/reset-password", UriKind.Relative), content: null, token);
        string resetBody = await reset.Content.ReadAsStringAsync(token);
        string newTemporary = JsonSerializer.Deserialize<JsonElement>(resetBody)
            .GetProperty("temporaryPassword").GetString()!;

        string logged = _logs.AllText();
        logged.ShouldNotBeNullOrWhiteSpace("No log output was captured at all - the assertion would be vacuous.");

        foreach (string secret in new[]
                 {
                     password, temporaryPassword, newTemporary, email, "Log Testi",
                     userLogin.AccessToken!, userLogin.RefreshSecret!, adminLogin.RefreshSecret!
                 })
        {
            logged.ShouldNotContain(secret, Case.Insensitive);
        }
    }

    private sealed class CapturingApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly CapturingLoggerProvider _logs;

        internal CapturingApiFactory(string connectionString, CapturingLoggerProvider logs)
        {
            _connectionString = connectionString;
            _logs = logs;
        }

        internal async Task ResetDatabaseAsync(CancellationToken cancellationToken)
        {
            using SkeletonApiFactory reset = new(_connectionString);
            await reset.ResetDatabaseAsync(cancellationToken);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Staging);
            builder.UseSetting("ConnectionStrings:Database", _connectionString);
            builder.UseSetting("Jwt:SigningKey", SkeletonApiFactory.TestSigningKey);
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(_logs);
            });
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public ILogger CreateLogger(string categoryName) => new QueueLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        internal string AllText() => string.Join('\n', _entries);

        private sealed class QueueLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<string> _entries;

            internal QueueLogger(string category, ConcurrentQueue<string> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                _entries.Enqueue($"{_category} scope: {state}");

                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                _entries.Enqueue($"{_category} [{logLevel}] {formatter(state, exception)} {exception}");
            }
        }
    }
}
