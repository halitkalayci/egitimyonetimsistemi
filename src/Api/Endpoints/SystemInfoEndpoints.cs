using Api.Errors;

using Application.Common;
using Application.System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SystemInfoEndpoints
{
    /// <summary>
    /// The one deliberately anonymous endpoint of the skeleton: it is the end-to-end proof, so it
    /// is marked AllowAnonymous explicitly rather than by omission (docs/security.md, default-deny).
    /// </summary>
    public static IEndpointRouteBuilder MapSystemInfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/system-info", async (
            GetSystemInfoQueryHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            Result<SystemInfoDto> result = await handler
                .HandleAsync(new GetSystemInfoQuery(), cancellationToken)
                .ConfigureAwait(false);

            return result.ToResponse(httpContext);
        })
        .AllowAnonymous()
        .WithName("GetSystemInfo");

        return endpoints;
    }
}
