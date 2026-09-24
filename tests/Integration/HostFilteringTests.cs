using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Portfolio.Integration;

/// <summary>
/// SEC-7: the Api answers only to loopback Host headers. AllowedHosts is one configuration
/// line; this is the test step 3 asks for. It needs no database - the health endpoint does
/// not touch one.
/// </summary>
public class HostFilteringTests(HostFilteringTests.ApiFactory factory) : IClassFixture<HostFilteringTests.ApiFactory>
{
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            // Production, so nothing Development-only (the OpenAPI endpoint, the dev health
            // endpoints) is in play: this is the host a published container runs.
            builder.UseEnvironment("Production")
                   .UseSetting("ConnectionStrings:portfolio", "Host=never-contacted");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:8080")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task Loopback_hosts_are_served(string host)
    {
        var response = await Get(host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("192.168.1.20")]
    [InlineData("localhost.evil.example")]
    public async Task Any_other_host_is_refused(string host)
    {
        var response = await Get(host);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    async Task<HttpResponseMessage> Get(string host)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
        request.Headers.Host = host;
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
