using Api.Auth;
using Api.Errors;

using Application.Common;
using Application.Identity;
using Application.Users;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>What the client sees. The refresh secret is NOT here: it travels in a cookie (AC-28).</summary>
public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string Role,
    bool MustChangePassword);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public static class AuthEndpoints
{
    /// <summary>Endpoints a caller may reach while still carrying a temporary password.</summary>
    internal const string AllowWithTemporaryPassword = nameof(AllowWithTemporaryPassword);

    internal const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/api/auth/login", async (
            LoginRequest request,
            LoginCommandHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            Result<AuthenticationResultDto> result = await handler
                .HandleAsync(
                    new LoginCommand(request?.Email ?? string.Empty, request?.Password ?? string.Empty),
                    cancellationToken)
                .ConfigureAwait(false);

            return ToSessionResponse(result, context);
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimitPolicy)
        .WithName("Login");

        endpoints.MapPost("/api/auth/refresh", async (
            RefreshSessionCommandHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            Result<AuthenticationResultDto> result = await handler
                .HandleAsync(new RefreshSessionCommand(RefreshCookie.Read(context) ?? string.Empty), cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                // A refusal means this secret is worthless; stop sending it back.
                RefreshCookie.Clear(context);
            }

            return ToSessionResponse(result, context);
        })
        .AllowAnonymous()
        .WithName("RefreshSession");

        endpoints.MapPost("/api/auth/logout", async (
            LogoutCommandHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            Result<bool> result = await handler
                .HandleAsync(new LogoutCommand(RefreshCookie.Read(context) ?? string.Empty), cancellationToken)
                .ConfigureAwait(false);

            RefreshCookie.Clear(context);

            return result.IsSuccess ? Results.NoContent() : result.ToResponse(context);
        })
        .AllowAnonymous()
        .WithName("Logout");

        endpoints.MapPost("/api/auth/logout-all", async (
            LogoutAllCommandHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<bool> result = await handler
                .HandleAsync(new LogoutAllCommand(caller.ParticipantId), cancellationToken)
                .ConfigureAwait(false);

            RefreshCookie.Clear(context);

            return result.IsSuccess ? Results.NoContent() : result.ToResponse(context);
        })
        .WithName("LogoutAllSessions");

        endpoints.MapPost("/api/auth/change-password", async (
            ChangePasswordRequest request,
            ChangePasswordCommandHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<AuthenticationResultDto> result = await handler
                .HandleAsync(
                    new ChangePasswordCommand(
                        caller.ParticipantId,
                        request?.CurrentPassword ?? string.Empty,
                        request?.NewPassword ?? string.Empty),
                    cancellationToken)
                .ConfigureAwait(false);

            // Every older session is gone; this device continues on the fresh one returned here.
            return ToSessionResponse(result, context);
        })
        .WithMetadata(new TemporaryPasswordAllowedAttribute())
        .WithName("ChangePassword");

        return endpoints;
    }

    private static IResult ToSessionResponse(Result<AuthenticationResultDto> result, HttpContext context)
    {
        if (!result.IsSuccess)
        {
            return result.ToResponse(context);
        }

        AuthenticationResultDto session = result.Value;
        RefreshCookie.Write(context, session.RefreshToken, session.RefreshTokenExpiresAtUtc);

        return Results.Ok(new SessionResponse(
            session.AccessToken,
            session.AccessTokenExpiresAtUtc,
            session.Role,
            session.MustChangePassword));
    }

    private static IResult Unauthenticated(HttpContext context) =>
        Result<bool>.Failure(IdentityErrors.SessionNotRenewable).ToResponse(context);
}

/// <summary>Marks an endpoint as reachable while the caller still has a temporary password.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class TemporaryPasswordAllowedAttribute : Attribute;
