using Api.Endpoints;
using Api.Errors;

using Application.Common;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Api.Auth;

/// <summary>
/// While an account still carries its temporary password, the only thing it may do is change it
/// (spec 0002, AC-6). The refusal carries a machine-readable code so the client can route to the
/// password screen instead of guessing from a status code - and it cannot be skipped by calling an
/// endpoint directly, which is what makes AC-23 hold on the server side too.
/// </summary>
internal static class TemporaryPasswordGate
{
    internal static IApplicationBuilder UseTemporaryPasswordGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            bool allowed = context.GetEndpoint()?.Metadata
                .GetMetadata<TemporaryPasswordAllowedAttribute>() is not null;

            if (!allowed && CallerContextFactory.MustChangePassword(context))
            {
                IResult refusal = Result<bool>
                    .Failure(IdentityErrors.PasswordChangeRequired)
                    .ToResponse(context);

                await refusal.ExecuteAsync(context).ConfigureAwait(false);

                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }
}
