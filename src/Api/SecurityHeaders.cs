namespace Portfolio.Api;

/// <summary>
/// security.md "API" and "Frontend", slice-03.md step 3: the headers on every response the Api
/// produces, refusals and errors included.
/// </summary>
/// <remarks>
/// Written in <c>Response.OnStarting</c>, not before <c>next()</c>: the exception handler clears the
/// response's headers before writing a 500 (round 2 SRC-8, EXECUTED there). And registered first, so
/// every later middleware's response carries them. The one response without them is host filtering's
/// 400, which wraps the whole pipeline and refuses before any application code runs.
/// </remarks>
static class SecurityHeaders
{
    /// <summary>
    /// The published page's policy, EXECUTED with the built bundle in Chromium 153 and Firefox 155
    /// with zero violations (slice 03 committee, BRW F15). On every response: a JSON body ignores it,
    /// and frame-ancestors works only as a header, never in a meta tag (BRW F16).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; "
        + "frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    /// <summary>
    /// Everything off but WebAuthn for this origin: a deny-all policy would switch passkeys off for
    /// the app itself (C3-38).
    /// </summary>
    public const string PermissionsPolicy =
        "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), fullscreen=(), "
        + "geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), "
        + "picture-in-picture=(), screen-wake-lock=(), usb=(), xr-spatial-tracking=(), "
        + "publickey-credentials-get=(self), publickey-credentials-create=(self)";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Frame-Options"] = "DENY";
                headers["Permissions-Policy"] = PermissionsPolicy;
                // No Strict-Transport-Security, ever, on this stack (D1): one accepted header on
                // localhost would pin every localhost port to HTTPS for its max-age.
                return Task.CompletedTask;
            });
            await next(context);
        });
}
