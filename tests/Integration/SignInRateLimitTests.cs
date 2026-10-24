using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Portfolio.Api;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 3 and DoD 5. The trip test owns its window, an hour long, so no boundary falls
/// inside it (round 2 TST-12); the production numbers are pinned separately. The reset is never
/// asserted: that would be the suite's one timing-dependent test.
/// </summary>
public class SignInRateLimitTests(AuthFixture db) : IClassFixture<AuthFixture>
{
    [Fact]
    public async Task The_sign_in_endpoints_share_one_window_and_refuse_with_429()
    {
        await using var api = db.Api(AuthFixture.NewSetupCode()).WithWebHostBuilder(b => b
            .UseSetting("RateLimits:SignIn:PermitLimit", "3")
            .UseSetting("RateLimits:SignIn:Window", "01:00:00"));
        var client = AuthFixture.Client(api);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/sign-in/options", null, Ct)).StatusCode);
        var refused = await client.PostAsync("/api/v1/auth/sign-in/options", null, Ct);
        var enrolmentToo = await client.PostAsJsonAsync("/api/v1/auth/enrolment/options", new { setupCode = "x", userName = "x" }, Ct);

        foreach (var response in new[] { refused, enrolmentToo })
        {
            // Only OnRejected writes this title: the 429 is the limiter's, so no handler ran.
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(ProblemTitles.TooManySignInAttempts, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString());
            Assert.Equal("3600", Assert.Single(response.Headers.GetValues("Retry-After")));
        }
        // Not every anonymous route: the state check is not a sign-in attempt.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/state", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_production_numbers_are_ten_a_minute()
    {
        await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", "Host=never-contacted"));
        var limits = api.Services.GetRequiredService<IOptions<SignInRateLimit>>().Value;

        Assert.Equal((10, TimeSpan.FromMinutes(1)), (limits.PermitLimit, limits.Window));
    }
}
