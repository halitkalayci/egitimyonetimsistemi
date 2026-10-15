using System.Text;
using System.Threading.RateLimiting;

using Api.Auth;
using Api.Endpoints;
using Api.Errors;

using Application.Common;
using Application.Identity;
using Application.System;
using Application.Users;

using Infrastructure;
using Infrastructure.Security;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Database is not configured. See .env.example; secrets never live in appsettings.");

string signingKey = builder.Configuration["Jwt:SigningKey"] ?? string.Empty;

// A missing signing key stops startup here rather than letting the application run insecurely
// (spec 0002, AC-27).
builder.Services.AddInfrastructure(new InfrastructureOptions(
    connectionString,
    signingKey,
    TimeSpan.FromMinutes(15),
    SeedAdminFrom(builder.Configuration)));

builder.Services.AddScoped<GetSystemInfoQueryHandler>();
builder.Services.AddScoped<LoginCommandHandler>();
builder.Services.AddScoped<RefreshSessionCommandHandler>();
builder.Services.AddScoped<LogoutCommandHandler>();
builder.Services.AddScoped<LogoutAllCommandHandler>();
builder.Services.AddScoped<ChangePasswordCommandHandler>();
builder.Services.AddScoped<CreateUserCommandHandler>();
builder.Services.AddScoped<UserAdministrationHandlers>();
builder.Services.AddProblemDetails();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = JwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

// Default-deny: every endpoint requires an authenticated caller unless it says otherwise
// (docs/security.md, AuthN / AuthZ).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// Login attempts are limited per network address, independently of the per-account lock, so one
// account cannot be locked out on purpose by someone else (spec 0002, AC-30).
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
        await Result<bool>
            .Failure(Error.TooManyRequests(
                "auth.too_many_attempts",
                "Çok fazla giriş denemesi yapıldı. Lütfen bir dakika sonra tekrar deneyin."))
            .ToResponse(context.HttpContext)
            .ExecuteAsync(context.HttpContext)
            .ConfigureAwait(false);
});

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseTemporaryPasswordGate();

app.MapSystemInfoEndpoints();
app.MapPingEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();

if (args.Contains(InitializeDatabaseArgument, StringComparer.Ordinal))
{
    // The deployment step (README, "Üretime kurulum"): apply migrations, seed the first
    // administrator from SeedAdmin__* if no account exists yet, then exit without serving.
    await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);
    return;
}

if (app.Environment.IsDevelopment())
{
    // Development convenience only; elsewhere the same work runs as the deployment step above.
    await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);
}

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Entry point marker so WebApplicationFactory can boot this application in integration tests.
/// </summary>
public partial class Program
{
    private const string InitializeDatabaseArgument = "--initialize-database";

    private static SeedAdminOptions? SeedAdminFrom(ConfigurationManager configuration)
    {
        string? email = configuration["SeedAdmin:Email"];
        string? password = configuration["SeedAdmin:Password"];

        return string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)
            ? null
            : new SeedAdminOptions(email, password);
    }
}
