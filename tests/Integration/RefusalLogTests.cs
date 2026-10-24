using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Portfolio.Api;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md DoD 4: each refusing layer writes one line naming itself, and whether the session
/// cookie arrived, so the browser test can read from the API what the browser hides from it. The line
/// carries the path and never the query string.
/// </summary>
public class RefusalLogTests
{
    static (WebApplicationFactory<Program> Api, LogCapture Logs) Api()
    {
        var logs = new LogCapture();
        var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:portfolio", "Host=never-contacted")
            .ConfigureLogging(l => l.AddProvider(logs))
            .WithTestAuth());
        return (api, logs);
    }

    static async Task<string> RefusalAfterAsync(Action<HttpRequestMessage> shape, string path = "/api/v1/accounts?secret=QUERY")
    {
        var (api, logs) = Api();
        await using var _ = api;
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        shape(request);
        var response = await api.CreateClient().SendAsync(request, Ct);
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"{response.StatusCode}");
        return Assert.Single(logs.Entries, e => e.Category == Refusals.Category).Text;
    }

    [Fact]
    public async Task Layer_2_names_itself_and_says_the_cookie_arrived()
    {
        var line = await RefusalAfterAsync(r =>
        {
            r.Headers.Add("Sec-Fetch-Site", "same-site");
            r.Headers.Add("Cookie", "__Host-portfolio=anything");
        });

        Assert.StartsWith("Refused by cross-site: POST /api/v1/accounts Sec-Fetch-Site=same-site session-cookie=arrived", line);
        Assert.DoesNotContain("QUERY", line);
        Assert.DoesNotContain("anything", line); // that the cookie arrived, never its value
    }

    [Fact]
    public async Task The_CSRF_filter_names_itself()
    {
        var line = await RefusalAfterAsync(r => r.Headers.Add(TestAuth.Header, "owner"));

        Assert.StartsWith("Refused by csrf-token: POST /api/v1/accounts Sec-Fetch-Site=- session-cookie=absent", line);
    }

    [Fact]
    public async Task No_session_names_itself()
    {
        var line = await RefusalAfterAsync(_ => { });

        Assert.StartsWith("Refused by no-session: POST /api/v1/accounts", line);
    }
}
