using Application.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Errors;

/// <summary>
/// The single place where an expected failure becomes an HTTP response (docs/conventions.md,
/// Error handling). The mapping is: Validation 400, Unauthorized 401, Forbidden 403,
/// NotFound 404, BusinessRule 409, Unavailable 503. Nothing internal is ever written into the
/// payload - no connection string, SQL, type name or stack trace.
/// </summary>
public static class ResultToProblemDetails
{
    public static IResult ToResponse<T>(this Result<T> result, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Problem(result.Error!, httpContext);
    }

    private static ProblemHttpResult Problem(Error error, HttpContext httpContext)
    {
        int status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        ProblemDetails problem = new()
        {
            Type = $"https://egitimyonetimsistemi/errors/{error.Code}",
            Title = TitleFor(error.Type),
            Detail = error.Message,
            Status = status
        };

        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return TypedResults.Problem(problem);
    }

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Geçersiz istek",
        ErrorType.Unauthorized => "Kimlik doğrulanamadı",
        ErrorType.Forbidden => "Yetkiniz yok",
        ErrorType.NotFound => "Kayıt bulunamadı",
        ErrorType.BusinessRule => "İşlem kurallara aykırı",
        ErrorType.TooManyRequests => "Çok fazla deneme yapıldı",
        ErrorType.Unavailable => "Servis şu anda kullanılamıyor",
        _ => "Beklenmeyen bir hata oluştu"
    };
}
