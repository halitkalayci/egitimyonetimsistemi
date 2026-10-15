using Microsoft.AspNetCore.Http;

namespace Api.Auth;

/// <summary>
/// The refresh secret travels in a cookie the browser's JavaScript cannot read (spec 0002, AC-28):
/// it survives a page reload, which is what makes the seven day window real, while an XSS payload
/// still cannot steal it. It is never written into a response body.
/// </summary>
internal static class RefreshCookie
{
    internal const string Name = "egitim_refresh";

    internal static void Write(HttpContext context, string secret, DateTimeOffset expiresAtUtc)
    {
        context.Response.Cookies.Append(Name, secret, new CookieOptions
        {
            HttpOnly = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,

            // Secure is required over HTTPS and impossible over plain HTTP in development, so it
            // follows the request rather than being hard-coded either way.
            Secure = context.Request.IsHttps,
            Expires = expiresAtUtc,
            Path = "/api/auth",
            IsEssential = true
        });
    }

    internal static string? Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(Name, out string? secret) ? secret : null;

    internal static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/api/auth"
        });

}
