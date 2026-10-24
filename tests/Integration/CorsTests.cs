using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 3, C3-41: no CORS at all. The app is same-origin, so any allowed origin would be
/// an origin that can read. Every mapped endpoint is asked, with an Origin header (without one the
/// assertion passes vacuously, TST-13), and every POST path is preflighted.
/// </summary>
public class CorsTests
{
    static readonly string[] Origins = ["http://attacker.localhost", "http://localhost:5173"];

    [Fact]
    public async Task No_endpoint_grants_any_origin()
    {
        await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production")
             .UseSetting("ConnectionStrings:portfolio", "Host=localhost;Port=1;Timeout=2")
             .UseSetting("Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:DisableRetry", "true"));
        var client = api.CreateClient();
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (Method: m, Path: Concrete(e))))
            .Distinct()
            .ToList();
        Assert.Contains(endpoints, e => e.Path == "/api/v1/auth/sign-in"); // the enumeration finds the auth routes
        Assert.True(endpoints.Count > 15, $"only {endpoints.Count} endpoints enumerated");

        foreach (var (method, path) in endpoints)
        foreach (var origin in Origins)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add("Origin", origin);
            if (method != "GET") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, Ct);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"), $"{method} {path} from {origin}");
        }

        foreach (var (_, path) in endpoints.Where(e => e.Method == "POST"))
        {
            using var preflight = new HttpRequestMessage(HttpMethod.Options, path);
            preflight.Headers.Add("Origin", Origins[0]);
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            var response = await client.SendAsync(preflight, Ct);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"), $"preflight {path}");
            Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    /// <summary>The route template with its parameters filled in.</summary>
    static string Concrete(RouteEndpoint e) =>
        "/" + string.Join("/", e.RoutePattern.PathSegments.Select(s => string.Concat(s.Parts.Select(p => p switch
        {
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart l => l.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart { ParameterPolicies.Count: > 0 } q
                when q.ParameterPolicies.Any(x => x.Content == "guid") => Guid.NewGuid().ToString(),
            _ => "x",
        }))));
}
