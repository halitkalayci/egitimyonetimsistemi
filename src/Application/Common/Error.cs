namespace Application.Common;

/// <summary>
/// The HTTP boundary maps these to status codes (docs/conventions.md, Error handling):
/// Validation -> 400, Unauthorized -> 401, Forbidden -> 403, NotFound -> 404,
/// BusinessRule -> 409, TooManyRequests -> 429, Unavailable -> 503. Anything unexpected stays an
/// exception -> 500.
/// </summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    BusinessRule,
    TooManyRequests,
    Unavailable
}

/// <summary>
/// An expected failure. <paramref name="Code"/> is a stable machine-readable key (never a
/// sentence); <paramref name="Message"/> is user-facing and must leak no internals -
/// no connection string, SQL, stack trace or third-party payload.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>The caller is not authenticated, or the credential presented is no longer valid.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>The caller is authenticated but may not do this.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>A business rule (BR-n) refused the operation.</summary>
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    /// <summary>The caller is sending more requests than the limit allows.</summary>
    public static Error TooManyRequests(string code, string message) => new(code, message, ErrorType.TooManyRequests);

    /// <summary>A dependency the request needs is not reachable right now.</summary>
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}
