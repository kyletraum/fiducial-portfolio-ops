using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 3 and DoD 6: every status the Api produces carries the security headers, and
/// step 3's middleware order serves the page before authentication. No database: the 500 comes from
/// a connection string that points at a closed port.
/// </summary>
public class SecurityHeadersTests
{
    const string Csp = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; "
        + "frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    static WebApplicationFactory<Program> Api(string? webRoot = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production")
             // Nothing listens on port 1. Aspire's retrying strategy is off so the 500 is immediate.
             .UseSetting("ConnectionStrings:portfolio", "Host=localhost;Port=1;Timeout=2")
             .UseSetting("Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:DisableRetry", "true")
             // Signed in where a row asks: since step 4 an anonymous request is 401 before anything
             // else runs, so the other statuses need a session to be reached.
             .WithTestAuth();
            if (webRoot is not null)
                b.UseSetting(WebHostDefaults.WebRootKey, webRoot);
        });

    /// <summary>Each status the Api produces, and a request that produces it (round 2 TST-10).</summary>
    public static TheoryData<HttpStatusCode, string, string, string?, string?, bool> Producers => new()
    {
        { HttpStatusCode.OK, "GET", "/api/v1/system/health", null, null, false },
        { HttpStatusCode.BadRequest, "POST", "/api/v1/accounts", "application/json", "{", true }, // binding fails first
        { HttpStatusCode.Unauthorized, "GET", "/api/v1/accounts", null, null, false },
        { HttpStatusCode.Forbidden, "POST", "/api/v1/accounts", "application/json", "same-site", true }, // layer 2
        { HttpStatusCode.Forbidden, "POST", "/api/v1/accounts", "application/json", "{}", true }, // no CSRF token
        { HttpStatusCode.NotFound, "GET", "/api/v1/no-such-route", null, null, true },
        { HttpStatusCode.UnsupportedMediaType, "POST", "/api/v1/imports", "text/plain", "a,b", true },
        { HttpStatusCode.InternalServerError, "GET", "/api/v1/accounts", null, null, true },
    };

    [Theory]
    [MemberData(nameof(Producers))]
    public async Task Every_status_carries_the_security_headers(HttpStatusCode status, string method, string path, string? type, string? body, bool signedIn)
    {
        await using var api = Api();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (signedIn)
            request.Headers.Add(TestAuth.Header, "owner");
        if (body == "same-site")
        {
            request.Headers.Add("Sec-Fetch-Site", "same-site");
            request.Content = new StringContent("{}", Encoding.UTF8, type!);
        }
        else if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, type!);

        var response = await api.CreateClient().SendAsync(request, Ct);

        Assert.Equal(status, response.StatusCode);
        AssertHeaders(response);
    }

    [Fact]
    public async Task The_page_and_its_assets_are_served_before_authentication_under_the_policy()
    {
        // Round 2 TST-5 / SRC-1: a static file served AFTER authorization falls under the fallback
        // policy (step 4) and would answer 401 or a redirect, so the published page could not load.
        var root = Directory.CreateTempSubdirectory("portfolio-wwwroot-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "index.html"), "<!doctype html><title>Portfolio</title>");
            Directory.CreateDirectory(Path.Combine(root.FullName, "assets"));
            File.WriteAllText(Path.Combine(root.FullName, "assets", "app.js"), "console.log(1);");
            await using var api = Api(root.FullName);
            var client = api.CreateClient();

            var page = await client.GetAsync("/", Ct);
            var asset = await client.GetAsync("/assets/app.js", Ct);
            var anonymousApi = await client.GetAsync("/api/v1/auth/session", Ct);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Unauthorized), (page.StatusCode, asset.StatusCode, anonymousApi.StatusCode));
            Assert.Equal(Csp, Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
            AssertHeaders(page);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_401_names_no_login_page()
    {
        // SRC-15: the cookie handler's redirect event answers 401, and nothing points at a login path.
        await using var api = Api();
        var response = await api.CreateClient().GetAsync("/api/v1/auth/session", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    static void AssertHeaders(HttpResponseMessage response)
    {
        string Header(string name) => Assert.Single(response.Headers.GetValues(name));
        Assert.Equal(Csp, Header("Content-Security-Policy"));
        Assert.Equal("nosniff", Header("X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header("Referrer-Policy"));
        Assert.Equal("DENY", Header("X-Frame-Options"));
        Assert.Contains("publickey-credentials-get=(self)", Header("Permissions-Policy"));
        Assert.Contains("publickey-credentials-create=(self)", Header("Permissions-Policy"));
        Assert.Contains("camera=()", Header("Permissions-Policy"));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"), "no HSTS on this stack (D1)");
    }
}
