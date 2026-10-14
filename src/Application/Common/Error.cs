namespace Application.Common;

/// <summary>
/// The HTTP boundary maps these to status codes (docs/conventions.md, Error handling):
/// Validation -> 400, Unauthorized -> 401, Forbidden -> 403, NotFound -> 404,
/// BusinessRule -> 409, Unavailable -> 503. Anything unexpected stays an exception -> 500.
/// </summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    BusinessRule,
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

    /// <summary>A business rule (BR-n) refused the operation.</summary>
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    /// <summary>A dependency the request needs is not reachable right now.</summary>
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}
