namespace Portfolio.Api;

/// <summary>
/// One line per refused request, naming the layer that refused it (slice-03.md DoD 4). A browser test
/// cannot read the answer to a request its attacking page cannot read, nor see the cookie Chromium
/// sent on it (Spike J, EXECUTED); this line is where both are read instead.
/// </summary>
/// <remarks>
/// No secret: the path without its query string, the Sec-Fetch-Site value (cut short: a non-browser
/// client sets it to anything), and whether the session cookie ARRIVED, never its value.
/// </remarks>
public static class Refusals
{
    public const string Category = "Portfolio.Api.Refusals";

    public const string CrossSite = "cross-site";
    public const string CsrfToken = "csrf-token";
    public const string NoSession = "no-session";

    public static void Log(HttpContext http, string layer)
    {
        var site = http.Request.Headers[CrossSiteWrites.Header].ToString();
        if (site.Length > 32) site = site[..32];
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category).LogInformation(
            "Refused by {Layer}: {Method} {Path} Sec-Fetch-Site={Site} session-cookie={SessionCookie}",
            layer, http.Request.Method, http.Request.Path.Value, site.Length > 0 ? site : "-",
            http.Request.Cookies.ContainsKey(AuthServices.SessionCookie) ? "arrived" : "absent");
    }
}
