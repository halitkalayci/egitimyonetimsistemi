using System.Text;

using Api.Endpoints;

using Application.System;

using Infrastructure;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Database is not configured. See .env.example; secrets never live in appsettings.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddScoped<GetSystemInfoQueryHandler>();
builder.Services.AddProblemDetails();

// Authentication is only wired far enough to REJECT: no login, no token issuing, no refresh
// rotation - those belong to ADR-0011's own spec (spec 0001, out of scope).
string signingKey = builder.Configuration["Jwt:SigningKey"] ?? string.Empty;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = signingKey.Length > 0,
            IssuerSigningKey = signingKey.Length > 0
                ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
                : null
        };
    });

// Default-deny: every endpoint requires an authenticated caller unless it says otherwise
// (docs/security.md, AuthN / AuthZ).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapSystemInfoEndpoints();
app.MapPingEndpoints();

if (app.Environment.IsDevelopment())
{
    // Development convenience only; production applies migrations as a deployment step.
    await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);
}

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Entry point marker so WebApplicationFactory can boot this application in integration tests.
/// </summary>
public partial class Program;
