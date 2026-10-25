using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md DoD 2: no endpoint answers without a session except an explicit allow-list, and every
/// unsafe endpoint off that list carries the CSRF filter. Asserted by REQUEST over EndpointDataSource,
/// on the unmodified app in both environments (no test scheme: round 2 TST-2), so a new endpoint
/// cannot be added anonymous, or tokenless, by accident.
/// </summary>
public class AllowListTests
{
    /// <summary>By endpoint name, or by route pattern for an endpoint that has none.</summary>
    static readonly string[] Production =
        ["GetHealth", "GetAuthState", "BeginEnrolment", "CompleteEnrolment", "BeginSignIn", "CompleteSignIn"];
    static readonly string[] Development = [.. Production, "/health", "/alive"]; // /openapi stays behind the session (R4)

    static WebApplicationFactory<Program> Api(string environment, bool testAuth = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment)
             .UseSetting("ConnectionStrings:portfolio", "Host=localhost;Port=1;Timeout=2")
             .UseSetting("Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:DisableRetry", "true");
            if (testAuth) b.WithTestAuth();
        });

    static string Key(RouteEndpoint e) => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? e.RoutePattern.RawText!;

    static List<(RouteEndpoint Endpoint, string Method)> Enumerate(WebApplicationFactory<Program> api) =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (e, m)))
            .ToList();

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Only_the_allow_list_answers_without_a_session(string environment)
    {
        var allowed = environment == "Production" ? Production : Development;
        await using var api = Api(environment);
        var client = api.CreateClient();
        var endpoints = Enumerate(api);
        // Guard: the enumeration finds what it guards, and every name on the list exists.
        Assert.All(allowed, name => Assert.Contains(endpoints, e => Key(e.Endpoint) == name));
        Assert.Contains(endpoints, e => Key(e.Endpoint) == "ListAccounts");

        foreach (var (endpoint, method) in endpoints)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Concrete(endpoint));
            if (method != "GET") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, Ct);

            if (allowed.Contains(Key(endpoint)))
                Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            else
            {
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {Key(endpoint)} answered {(int)response.StatusCode} without a session");
                Assert.Null(response.Headers.Location); // no redirect to a login page (SRC-15)
            }
        }
    }

    /// <summary>
    /// A request that binds, for each unsafe endpoint: without a valid body the binder answers first
    /// and the filter would never be seen (round 2 TST-17). An unsafe endpoint missing here fails.
    /// </summary>
    static readonly Dictionary<string, (string ContentType, string Body)> Bodies = new()
    {
        ["CreateAccount"] = ("application/json", """{"institutionName":"x","displayName":"x","openedOn":"2026-01-01"}"""),
        ["RecordBalance"] = ("application/json", """{"asOfDate":"2026-01-01","balance":"1"}"""),
        ["BindSource"] = ("application/json", """{"sourceSystem":"import:csv","sourceId":"x"}"""),
        ["ImportFile"] = ("text/csv", "account,as_of_date,balance\n"),
        ["SignOut"] = ("application/json", "{}"),
        ["BeginEnrolment"] = ("application/json", """{"setupCode":"x","userName":"x"}"""),
        ["CompleteEnrolment"] = ("application/json", """{"setupCode":"x","credential":{}}"""),
        ["BeginSignIn"] = ("application/json", "{}"),
        ["CompleteSignIn"] = ("application/json", "{}"),
    };

    [Fact]
    public async Task Every_unsafe_endpoint_off_the_list_requires_a_CSRF_token_and_none_on_it_does()
    {
        await using var api = Api("Production", testAuth: true);
        var client = api.CreateClient();
        var unsafeEndpoints = Enumerate(api).Where(e => e.Method is not ("GET" or "HEAD" or "OPTIONS" or "TRACE")).ToList();
        Assert.Contains(unsafeEndpoints, e => Key(e.Endpoint) == "ImportFile");

        foreach (var (endpoint, method) in unsafeEndpoints)
        {
            var name = Key(endpoint);
            Assert.True(Bodies.TryGetValue(name, out var body), $"no request body for unsafe endpoint {name}: add one here");
            using var request = new HttpRequestMessage(new HttpMethod(method), Concrete(endpoint) + (name == "ImportFile" ? "?fileName=x.csv" : ""))
            {
                Content = new StringContent(body.Body, Encoding.UTF8, body.ContentType),
            };
            request.Headers.Add(TestAuth.Header, "owner"); // signed in, no token
            var response = await client.SendAsync(request, Ct);
            var refusedForToken = response.StatusCode == HttpStatusCode.Forbidden && await TitleAsync(response) == ProblemTitles.CsrfTokenRefused;

            Assert.True(refusedForToken != Production.Contains(name),
                $"{method} {name}: {(int)response.StatusCode}, {(refusedForToken ? "refused for its token" : "not refused for its token")}");
        }
    }

    static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        try { return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString(); }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    /// <summary>The route template with its parameters filled in.</summary>
    static string Concrete(RouteEndpoint e) =>
        "/" + string.Join("/", e.RoutePattern.PathSegments.Select(s => string.Concat(s.Parts.Select(p => p switch
        {
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart l => l.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart q when q.ParameterPolicies.Any(x => x.Content == "guid") => Guid.NewGuid().ToString(),
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart { Name: "documentName" } => "v1",
            _ => "x",
        }))));
}
