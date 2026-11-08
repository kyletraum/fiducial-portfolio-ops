using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// slice-04.md step 5 and DoD 5, 6 (TST-11). The page against the whole app model for what must be
/// real: a run, a refusal by SQLSTATE, a comment changed at test time, a report saved, run and
/// deleted. The states a browser test cannot produce on demand (cut off, timed out) are fulfilled
/// from recorded bodies. axe cannot see focus or a keyboard trap, so those are asserted here.
/// </summary>
public class ReportsPageTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    const string Json = "application/json";
    const string Problem = "application/problem+json";

    static ILocator Section(IPage page) => page.GetByRole(AriaRole.Region, new() { Name = "Reports", Exact = true });
    static ILocator RunForm(IPage page) => page.GetByRole(AriaRole.Form, new() { Name = "Run a query" });
    static ILocator Sql(IPage page) => RunForm(page).GetByLabel("SQL", new() { Exact = true });

    static Task<string> ActiveAsync(IPage page, string expression) =>
        page.EvaluateAsync<string>($"() => {{ const e = document.activeElement; return {expression}; }}");

    static async Task AssertFocusIsOnAsync(IPage page, string role, string text)
    {
        await Assertions.Expect(Section(page).GetByRole(role == "status" ? AriaRole.Status : AriaRole.Alert).Filter(new() { HasText = text })).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("role => document.activeElement?.getAttribute('role') === role", role);
        Assert.Contains(text, await ActiveAsync(page, "e.textContent"));
    }

    async Task<IPage> OpenAsync(ColorScheme scheme = ColorScheme.Light)
    {
        var context = await fixture.NewSignedInContextAsync(new() { ColorScheme = scheme });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await Assertions.Expect(Section(page).GetByRole(AriaRole.Heading, new() { Name = "Saved reports" })).ToBeVisibleAsync();
        return page;
    }

    static Task FulfilRunAsync(IPage page, int status, string body) =>
        page.RouteAsync("**/api/v1/reports/run", route => route.FulfillAsync(new() { Status = status, ContentType = status == 200 ? Json : Problem, Body = body }));

    // --- The keyboard path, on the real stack (TST-11) ----------------------------------------

    [Fact]
    public async Task Tab_leaves_the_editor_Enter_runs_and_focus_lands_on_the_summary_of_a_real_table()
    {
        var page = await OpenAsync();
        await Sql(page).FillAsync("SELECT g AS n, 'x' AS label, NULL::text AS nothing FROM generate_series(1, 2) g");
        await Sql(page).FocusAsync();

        // WCAG 2.1.2: Tab moves on, and leaves the text as it was.
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("Run query", await ActiveAsync(page, "e.textContent"));
        await Assertions.Expect(Sql(page)).ToHaveValueAsync("SELECT g AS n, 'x' AS label, NULL::text AS nothing FROM generate_series(1, 2) g");

        await page.Keyboard.PressAsync("Enter");
        await AssertFocusIsOnAsync(page, "status", "2 rows.");

        var table = Section(page).GetByRole(AriaRole.Table, new() { Name = "Query result" });
        Assert.Equal(["n", "label", "nothing"], await table.Locator("th[scope=col]").AllTextContentsAsync());
        Assert.Equal(["1", "x", "null"], await table.Locator("tbody tr").First.Locator("td").AllTextContentsAsync());

        // And by Space, the button's other key.
        await RunForm(page).GetByRole(AriaRole.Button, new() { Name = "Run query" }).FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await AssertFocusIsOnAsync(page, "status", "2 rows.");
    }

    [Fact]
    public async Task A_query_the_database_refuses_shows_its_SQLSTATE_in_an_alert_that_takes_focus_and_keeps_the_text()
    {
        var page = await OpenAsync();
        const string sql = "SELECT * FROM public.account";
        await Sql(page).FillAsync(sql);
        await RunForm(page).GetByRole(AriaRole.Button, new() { Name = "Run query" }).ClickAsync();

        await AssertFocusIsOnAsync(page, "alert", "42501");
        await Assertions.Expect(Section(page).GetByRole(AriaRole.Alert)).ToContainTextAsync("The database refused the query.");
        await Assertions.Expect(Section(page).GetByRole(AriaRole.Alert)).ToContainTextAsync("permission denied");
        await Assertions.Expect(Sql(page)).ToHaveValueAsync(sql);
    }

    // --- Live documentation (FR-6.6, DoD 5) ---------------------------------------------------

    [Fact]
    public async Task A_comment_changed_in_the_database_appears_on_the_page()
    {
        var page = await OpenAsync();
        var docs = Section(page).Locator("details", new() { Has = page.GetByText("The views you can query") });
        var summary = docs.GetByText("The views you can query");
        await summary.ClickAsync();
        await Assertions.Expect(docs.GetByRole(AriaRole.Heading, new() { Name = "reporting.v_account" })).ToBeVisibleAsync();

        var original = await fixture.SuperuserScalarAsync<string>("SELECT obj_description('reporting.v_account'::regclass, 'pg_class')");
        var changed = $"Changed at test time {Guid.NewGuid():N}";
        await fixture.SuperuserExecuteAsync($"COMMENT ON VIEW reporting.v_account IS '{changed}'");
        try
        {
            await summary.ClickAsync();
            await summary.ClickAsync(); // closed and opened again: read afresh
            await Assertions.Expect(docs.GetByText(changed)).ToBeVisibleAsync();
        }
        finally
        {
            await fixture.SuperuserExecuteAsync($"COMMENT ON VIEW reporting.v_account IS '{original.Replace("'", "''")}'");
        }
    }

    // --- A saved report, whole (FR-6.4) --------------------------------------------------------

    [Fact]
    public async Task A_report_is_saved_run_with_its_parameter_and_deleted()
    {
        var page = await OpenAsync();
        var name = $"On a day {Guid.NewGuid():N}";
        await Sql(page).FillAsync("SELECT $1::date + 1 AS next_day");

        var save = Section(page).GetByRole(AriaRole.Form, new() { Name = "Save this query" });
        await save.GetByLabel("Report name").FillAsync(name);
        await save.GetByRole(AriaRole.Button, new() { Name = "Add a parameter" }).ClickAsync();
        Assert.Equal("Name of $1", await ActiveAsync(page, "e.labels[0].textContent")); // focus follows the new row
        await page.Keyboard.TypeAsync("day");
        await save.GetByLabel("Type of $1").SelectOptionAsync("date");
        await save.GetByRole(AriaRole.Button, new() { Name = "Save report" }).ClickAsync();
        await Assertions.Expect(save.GetByRole(AriaRole.Status)).ToHaveTextAsync($"Saved {name}.");

        var item = Section(page).GetByRole(AriaRole.Form, new() { Name = name });

        // A blank date is a refusal against its own input, not a quiet NULL.
        await item.GetByRole(AriaRole.Button, new() { Name = $"Run {name}" }).ClickAsync();
        await Assertions.Expect(item.GetByRole(AriaRole.Alert)).ToContainTextAsync("does not match its declared type");
        await Assertions.Expect(item.GetByText("Required.")).ToBeVisibleAsync();
        await Assertions.Expect(item.GetByLabel("day (date)")).ToHaveAttributeAsync("aria-invalid", "true");

        await item.GetByLabel("day (date)").FillAsync("2026-02-28");
        await item.GetByRole(AriaRole.Button, new() { Name = $"Run {name}" }).ClickAsync();
        await Assertions.Expect(item.GetByRole(AriaRole.Status)).ToHaveTextAsync("1 row.");
        await Assertions.Expect(item.GetByRole(AriaRole.Cell, new() { Name = "2026-03-01" })).ToBeVisibleAsync();

        // Deleting asks first, offers the safe answer first, and puts focus somewhere that still exists.
        await item.GetByRole(AriaRole.Button, new() { Name = $"Delete {name}" }).ClickAsync();
        Assert.Equal("Keep it", await ActiveAsync(page, "e.textContent"));
        await page.Keyboard.PressAsync("Enter");
        Assert.StartsWith("Delete", await ActiveAsync(page, "e.textContent"));
        await item.GetByRole(AriaRole.Button, new() { Name = $"Delete {name}" }).ClickAsync();
        await item.GetByRole(AriaRole.Button, new() { Name = "Yes, delete" }).ClickAsync();
        await Assertions.Expect(Section(page).GetByText($"Deleted {name}.")).ToBeVisibleAsync();
        await Assertions.Expect(item).ToHaveCountAsync(0);
        Assert.Equal("Saved reports", await ActiveAsync(page, "e.textContent"));
    }

    // --- axe, light and dark, in each named state (DoD 6, TST-11) -------------------------------

    const string Saved = """
        [{"id":"40000000-0000-4000-8000-000000000001","name":"Balances on a day","description":"Every balance on one day.",
          "sql":"SELECT * FROM reporting.v_balance_daily WHERE as_of_date = $1 AND balance > $2 AND source_system = $3",
          "parameters":[{"name":"day","type":"date"},{"name":"at_least","type":"numeric"},{"name":"source","type":"text"}],
          "createdAt":"2026-11-07T10:00:00Z"}]
        """;

    static string Result(string rows, string cutOffBy) => $$"""
        {"columns":[{"name":"as_of_date","type":"date"},{"name":"balance","type":"numeric"},{"name":"observed_at","type":"timestamp with time zone"},{"name":"note","type":"text"}],
         "rows":[{{rows}}],"cutOff":{{(cutOffBy == "null" ? "false" : "true")}},"cutOffBy":{{cutOffBy}}}
        """;

    const string Row = """["2026-02-01","100.0000","2026-02-01T10:00:00Z",null]""";

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Every_state_of_the_page_passes_axe_and_says_how_the_run_ended(ColorScheme scheme)
    {
        var context = await fixture.NewSignedInContextAsync(new() { ColorScheme = scheme });
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/v1/reports/saved", route => route.FulfillAsync(new() { Status = 200, ContentType = Json, Body = "[]" }));
        await page.GotoAsync("/");
        await Assertions.Expect(Section(page).GetByText("No saved reports yet.")).ToBeVisibleAsync();
        await Axe.AssertCleanAsync(page, $"{scheme}, empty");

        async Task RunAsync(string state, int status, string body, string role, string says)
        {
            await page.UnrouteAsync("**/api/v1/reports/run");
            await FulfilRunAsync(page, status, body);
            await Sql(page).FillAsync("SELECT 1");
            await RunForm(page).GetByRole(AriaRole.Button, new() { Name = "Run query" }).ClickAsync();
            await AssertFocusIsOnAsync(page, role, says);
            await Axe.AssertCleanAsync(page, $"{scheme}, {state}");
        }

        await RunAsync("results", 200, Result($"{Row},{Row}", "null"), "status", "2 rows.");
        await RunAsync("one row", 200, Result(Row, "null"), "status", "1 row.");
        await RunAsync("cut off", 200, Result(string.Join(",", Enumerable.Repeat(Row, 5)), "\"rows\""), "status", "First 5 rows shown, result cut off at the row limit.");
        await RunAsync("cut off by size", 200, Result(Row, "\"bytes\""), "status", "First 1 row shown, result cut off at the size limit.");
        await RunAsync("timed out", 200, Result("", "\"deadline\""), "status", "Timed out before any rows came back.");
        await RunAsync("refused", 422, """{"title":"The database refused the query.","status":422,"detail":"permission denied for table account","sqlState":"42501"}""", "alert", "42501");
        await RunAsync("busy", 429, """{"title":"A report is already running.","status":429,"detail":"One report runs at a time."}""", "alert", "already running");

        // The saved list, with a report's typed inputs and its SQL open.
        await page.UnrouteAsync("**/api/v1/reports/saved");
        await page.RouteAsync("**/api/v1/reports/saved", route => route.FulfillAsync(new() { Status = 200, ContentType = Json, Body = Saved }));
        await page.ReloadAsync();
        var item = Section(page).GetByRole(AriaRole.Form, new() { Name = "Balances on a day" });
        await Assertions.Expect(item.GetByLabel("day (date)")).ToHaveAttributeAsync("type", "date");
        await Assertions.Expect(item.GetByLabel("at_least (numeric)")).ToHaveAttributeAsync("inputmode", "decimal");
        await item.GetByText("Show the SQL").ClickAsync();
        await Assertions.Expect(item.GetByText("SELECT * FROM reporting.v_balance_daily")).ToBeVisibleAsync();
        await Axe.AssertCleanAsync(page, $"{scheme}, saved list");

        // The save form with a parameter row, and the documentation expanded (real, from the catalog).
        await Section(page).GetByRole(AriaRole.Button, new() { Name = "Add a parameter" }).ClickAsync();
        await Section(page).GetByText("The views you can query").ClickAsync();
        await Assertions.Expect(Section(page).GetByRole(AriaRole.Heading, new() { Name = "reporting.v_net_worth_daily" })).ToBeVisibleAsync();
        await Axe.AssertCleanAsync(page, $"{scheme}, schema and save form");
        await context.CloseAsync();
    }
}
