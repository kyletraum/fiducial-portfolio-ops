using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// slice-02.md DoD 10, the browser half - Kyle's rule, 2026-10-18: the data layer is UTC; the page
/// shows the VIEWER's zone; a calendar date is never converted. Every other test runs the browser
/// in the machine's zone, so none of this was exercised before.
/// </summary>
public class TimeZoneTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    const string Chicago = "America/Chicago";

    Task<IBrowserContext> ContextAsync(string zone) =>
        fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString(), TimezoneId = zone, Locale = "en-US" });

    [Theory]
    // 23:30 local on the evening BEFORE the US change (CDT, UTC-5) - already 1 Nov in UTC -
    // and 23:30 on the day of it, after 02:00 fell back to 01:00 (CST, UTC-6) - already 2 Nov.
    [InlineData("2026-11-01T04:30:00Z", "2026-10-31")]
    [InlineData("2026-11-02T05:30:00Z", "2026-11-01")]
    public async Task At_23_30_in_Chicago_today_is_Chicagos_date_not_UTCs(string pinnedUtc, string chicagoToday)
    {
        var context = await ContextAsync(Chicago);
        var page = await context.NewPageAsync();
        // Date.now() and new Date() are pinned; timers keep running, so the app works normally.
        await page.Clock.SetFixedTimeAsync(DateTime.Parse(pinnedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));
        await page.GotoAsync("/");
        await Assertions.Expect(page.GetByText("API: reachable")).ToBeVisibleAsync();

        // Every default "today" on the page is the viewer's calendar date.
        var add = page.GetByRole(AriaRole.Form, new() { Name = "Add an account" });
        await Assertions.Expect(add.GetByLabel("Opened on")).ToHaveValueAsync(chicagoToday);
        await add.GetByLabel("Institution").FillAsync("Example Bank");
        await add.GetByLabel("Account name").FillAsync($"Chicago {chicagoToday}");
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await Assertions.Expect(add.GetByRole(AriaRole.Status)).ToContainTextAsync("Added");

        var record = page.GetByRole(AriaRole.Form, new() { Name = "Record a balance" });
        await record.GetByLabel("Account").SelectOptionAsync(new SelectOptionValue { Label = $"Chicago {chicagoToday} (Example Bank)" });
        await Assertions.Expect(record.GetByLabel("As of")).ToHaveValueAsync(chicagoToday);
        await record.GetByLabel("Balance (USD)").FillAsync("100");
        await record.GetByRole(AriaRole.Button, new() { Name = "Record balance" }).ClickAsync();
        await Assertions.Expect(record.GetByRole(AriaRole.Status)).ToContainTextAsync($"for {chicagoToday}");

        // The chart's series ends at the viewer's today, not the UTC date a day later.
        await page.GetByText("Show as a table").ClickAsync();
        var table = page.GetByRole(AriaRole.Table, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Net worth by day") });
        await Assertions.Expect(table.Locator("tbody tr").First.Locator("th[scope=row]")).ToHaveTextAsync(chicagoToday);
        await context.CloseAsync();
    }

    [Fact]
    public async Task An_import_time_is_shown_in_the_viewers_zone_with_the_zone_named()
    {
        using var api = new HttpClient { BaseAddress = fixture.ApiUrl };
        var run = await (await api.PostAsync("/api/v1/imports?fileName=tz.csv",
            new StringContent("x", null, "text/csv"), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var started = run.GetProperty("startedAt").GetDateTimeOffset();
        // .NET on Windows resolves the IANA id through ICU, as the browser does.
        var local = TimeZoneInfo.ConvertTime(started, TimeZoneInfo.FindSystemTimeZoneById(Chicago));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Chicago).IsDaylightSavingTime(local) ? "CDT" : "CST";

        var context = await ContextAsync(Chicago);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        var history = page.GetByRole(AriaRole.Table, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Every import") });
        var cell = history.Locator("tbody tr").Filter(new() { HasText = "tz.csv" }).Locator("th[scope=row]");

        await Assertions.Expect(cell).ToContainTextAsync(local.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
        await Assertions.Expect(cell).ToContainTextAsync(local.ToString("h:mm", CultureInfo.InvariantCulture));
        await Assertions.Expect(cell).ToContainTextAsync(zone);
        await context.CloseAsync();
    }

    [Theory]
    [InlineData(Chicago)]
    [InlineData("Pacific/Kiritimati")] // UTC+14: a naive new Date("2026-10-15") reads correctly here...
    [InlineData("Pacific/Pago_Pago")]  // UTC-11: ...and as the 14th here. Neither may happen.
    public async Task A_calendar_date_is_shown_as_written_in_every_zone(string zone)
    {
        using var api = new HttpClient { BaseAddress = fixture.ApiUrl };
        var name = $"Dated {zone}";
        var created = await (await api.PostAsJsonAsync("/api/v1/accounts",
            new { institutionName = "Example Bank", displayName = name, openedOn = "2026-01-01" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await api.PostAsJsonAsync($"/api/v1/accounts/{created.GetProperty("id").GetGuid()}/balances",
            new { asOfDate = "2026-10-15", balance = "1" }, Ct)).EnsureSuccessStatusCode();

        var context = await ContextAsync(zone);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        var row = page.GetByRole(AriaRole.Table, new() { Name = "Each account and its newest balance" })
            .Locator("tbody tr").Filter(new() { HasText = name });

        await Assertions.Expect(row.Locator("td").Nth(1)).ToHaveTextAsync("2026-01-01"); // opened on
        await Assertions.Expect(row.Locator("td").Nth(3)).ToHaveTextAsync("2026-10-15"); // as of
        await context.CloseAsync();
    }
}
