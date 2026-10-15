using Api.Auth;
using Api.Errors;

using Application.Common;
using Application.Users;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public sealed record CreateUserRequest(string? FullName, string? Email, string? Role, Guid? MemberFirmId);

public sealed record AssignRoleRequest(string? Role);

public sealed record SetAccountStatusRequest(bool? Disabled);

/// <summary>
/// Administrative endpoints. Role and firm checks are NOT done here: every handler re-derives the
/// caller from the verified token and asks the Application layer, so the rule holds even if an
/// endpoint is ever wired without the right metadata (docs/security.md).
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/users", async (
            UserAdministrationHandlers handlers,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<IReadOnlyList<UserDto>> result = await handlers
                .ListAsync(caller, new ListUsersQuery(), cancellationToken)
                .ConfigureAwait(false);

            return result.ToResponse(context);
        })
        .WithName("ListUsers");

        endpoints.MapPost("/api/users", async (
            CreateUserRequest request,
            CreateUserCommandHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<UserWithTemporaryPasswordDto> result = await handler
                .HandleAsync(
                    caller,
                    new CreateUserCommand(
                        request?.FullName ?? string.Empty,
                        request?.Email ?? string.Empty,
                        request?.Role ?? string.Empty,
                        request?.MemberFirmId),
                    cancellationToken)
                .ConfigureAwait(false);

            return result.ToResponse(context);
        })
        .WithName("CreateUser");

        endpoints.MapPut("/api/users/{id:guid}/role", async (
            Guid id,
            AssignRoleRequest request,
            UserAdministrationHandlers handlers,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<UserDto> result = await handlers
                .AssignRoleAsync(caller, new AssignRoleCommand(id, request?.Role ?? string.Empty), cancellationToken)
                .ConfigureAwait(false);

            return result.ToResponse(context);
        })
        .WithName("AssignUserRole");

        endpoints.MapPut("/api/users/{id:guid}/status", async (
            Guid id,
            SetAccountStatusRequest request,
            UserAdministrationHandlers handlers,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<UserDto> result = await handlers
                .SetAccountStatusAsync(
                    caller,
                    new SetAccountStatusCommand(id, request?.Disabled),
                    cancellationToken)
                .ConfigureAwait(false);

            return result.ToResponse(context);
        })
        .WithName("SetUserAccountStatus");

        endpoints.MapPost("/api/users/{id:guid}/reset-password", async (
            Guid id,
            UserAdministrationHandlers handlers,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerContextFactory.From(context) is not CallerContext caller)
            {
                return Unauthenticated(context);
            }

            Result<UserWithTemporaryPasswordDto> result = await handlers
                .ResetPasswordAsync(caller, new ResetPasswordCommand(id), cancellationToken)
                .ConfigureAwait(false);

            return result.ToResponse(context);
        })
        .WithName("ResetUserPassword");

        return endpoints;
    }

    private static IResult Unauthenticated(HttpContext context) =>
        Result<bool>.Failure(IdentityErrors.SessionNotRenewable).ToResponse(context);
}
