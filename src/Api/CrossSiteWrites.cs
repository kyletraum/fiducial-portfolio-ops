namespace Portfolio.Api;

/// <summary>
/// slice-02.md "Cross-site writes", layer 2. The API is on loopback with no authentication, so
/// between any website the viewer visits and a write there is only the browser. A browser marks
/// every request with <c>Sec-Fetch-Site</c>, which a page's script cannot set or remove (it is a
/// forbidden request header).
/// </summary>
/// <remarks>
/// Refused: any unsafe method marked <c>cross-site</c>, or <c>same-site</c> - a site ignores ports,
/// so another server on localhost is the same site. Allowed: <c>same-origin</c> (this app's own
/// page, also through the Vite dev proxy, which forwards the header unchanged), <c>none</c> (a
/// request the user typed), and no header at all - a non-browser client such as curl or the
/// tests, which is not the threat this exists for. Layer 1 is the content-type allowlist on every
/// write endpoint; each layer is tested on its own (DoD 11).
/// </remarks>
static class CrossSiteWrites
{
    public const string Header = "Sec-Fetch-Site";

    public static IApplicationBuilder UseCrossSiteWriteRefusal(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            var unsafeMethod = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));
            var site = context.Request.Headers[Header].ToString();
            if (unsafeMethod && (site.Equals("cross-site", StringComparison.OrdinalIgnoreCase) || site.Equals("same-site", StringComparison.OrdinalIgnoreCase)))
            {
                await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Cross-site writes are refused.",
                    detail: $"This request came from another site ({Header}: {site}). Only this app's own page may write.")
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        });
}
