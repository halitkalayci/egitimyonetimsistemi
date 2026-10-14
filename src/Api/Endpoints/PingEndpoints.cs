using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class PingEndpoints
{
    /// <summary>
    /// Exists purely so the default-deny policy is observable: it carries no AllowAnonymous, so an
    /// unauthenticated request must be rejected with 401 (spec 0001, AC-4).
    /// </summary>
    public static IEndpointRouteBuilder MapPingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }))
            .WithName("Ping");

        return endpoints;
    }
}
