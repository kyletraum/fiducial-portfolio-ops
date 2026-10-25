using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Portfolio.Api;

/// <summary>
/// slice-03.md step 3, C3-1: CSRF tokens, refused by the application. ASP.NET Core's own antiforgery
/// middleware validates and never refuses a JSON or raw-body minimal API, and never checks DELETE
/// (Spike J, EXECUTED: every such write ran). So the refusal is this endpoint filter.
/// </summary>
/// <remarks>
/// The token travels only in <see cref="HeaderName"/>, never a form body. It is bound to the signed-in
/// user, so the page fetches one after every sign-in (C3-22). It is defence in depth behind layer 2
/// (D12): what it refuses on its own is a client that sends no Sec-Fetch-Site, or a write after a
/// later change removes layer 2.
/// </remarks>
public static class CsrfTokens
{
    public const string HeaderName = "X-CSRF-TOKEN";

    /// <summary>Not __Host-: antiforgery throws on a non-HTTPS request when its cookie is Secure (C3-21).</summary>
    public const string CookieName = "portfolio-csrf";

    public static void AddCsrfTokens(this IServiceCollection services) =>
        services.AddAntiforgery(o =>
        {
            o.HeaderName = HeaderName;
            o.SuppressReadingTokenFromFormBody = true;
            o.SuppressXFrameOptionsHeader = true; // SecurityHeaders owns X-Frame-Options
            o.Cookie.Name = CookieName;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.HttpOnly = true;
        });

    /// <summary>Marks an anonymous route the filter lets through: no token can exist before sign-in (R1).</summary>
    public sealed class Exempt;

    /// <summary>Refuses an unsafe request whose token is missing, wrong, or another user's.</summary>
    public static TBuilder RequireCsrfToken<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var method = http.Request.Method;
            var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
            if (safe || http.GetEndpoint()?.Metadata.GetMetadata<Exempt>() is not null)
                return await next(context);
            try
            {
                await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                Refusals.Log(http, Refusals.CsrfToken);
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: ProblemTitles.CsrfTokenRefused);
            }
            return await next(context);
        });

    /// <summary>The page's token, for the signed-in user only.</summary>
    public static Ok<CsrfTokenResponse> Issue(HttpContext http, IAntiforgery antiforgery) =>
        TypedResults.Ok(new CsrfTokenResponse(antiforgery.GetAndStoreTokens(http).RequestToken!));
}
