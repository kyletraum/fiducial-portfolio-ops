using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-02.md DoD 10, the server half - Kyle's rule, 2026-10-18: the data layer's "today" is the
/// UTC date. With the API's clock pinned at 23:30 in Chicago on either side of the US change, the
/// default end of the net-worth series is the UTC date - a day ahead of Chicago - and a recorded
/// balance's observed_at is the pinned moment, in UTC. The browser always passes its own "to";
/// TimeZoneTests in the E2E holds that half.
/// </summary>
public class ClockTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    (WebApplicationFactory<Program> Factory, HttpClient Client) ApiAt(DateTimeOffset now)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:portfolio", pg.ConnectionString)
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(now))));
        return (factory, factory.CreateClient());
    }

    [Theory]
    [InlineData("2026-11-01T04:30:00Z", "2026-11-01")] // 23:30 CDT on 31 Oct
    [InlineData("2026-11-02T05:30:00Z", "2026-11-02")] // 23:30 CST on 1 Nov, after the fall-back
    public async Task The_default_end_of_the_series_is_the_UTC_date(string pinned, string utcToday)
    {
        var (factory, client) = ApiAt(DateTimeOffset.Parse(pinned));
        await using (factory)
        using (client)
        {
            var series = await client.GetFromJsonAsync<JsonElement>("/api/v1/net-worth", Ct);

            Assert.Equal(utcToday, series.GetProperty("to").GetString());
        }
    }

    [Fact]
    public async Task A_recorded_balance_is_observed_at_the_pinned_moment_in_UTC()
    {
        var pinned = DateTimeOffset.Parse("2026-11-02T05:30:00Z");
        var account = await NewAccountAsync(pg, new DateOnly(2026, 1, 1));
        var (factory, client) = ApiAt(pinned);
        await using (factory)
        using (client)
        {
            // The calendar date the viewer chose (their 1 Nov) is stored as written, not as the UTC date.
            var response = await client.PostAsJsonAsync($"/api/v1/accounts/{account}/balances", new { asOfDate = "2026-11-01", balance = "5" }, Ct);
            var balance = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

            Assert.Equal("2026-11-01", balance.GetProperty("asOfDate").GetString());
            Assert.Equal(pinned, balance.GetProperty("observedAt").GetDateTimeOffset());
            Assert.Equal(TimeSpan.Zero, balance.GetProperty("observedAt").GetDateTimeOffset().Offset);
        }
    }
}
