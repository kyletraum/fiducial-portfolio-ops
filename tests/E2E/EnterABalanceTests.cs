using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// slice-01 step 5, the E2E: enter a balance, assert the chart moves. Plus what the middle cut
/// kept with it - DoD 7's table and keyboard assertions and the one AxeBuilder call, the only
/// mechanical check on the accessibility floor.
/// </summary>
public class EnterABalanceTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task A_balance_entered_in_the_browser_appears_in_the_chart()
    {
        var context = await fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString() });
        var page = await context.NewPageAsync();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var opened = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");

        await page.GotoAsync("/");
        await Assertions.Expect(page.GetByText("API: reachable")).ToBeVisibleAsync();

        // A fresh database: no chart yet, and the page says why rather than drawing a zero.
        await Assertions.Expect(page.GetByText("No verified balance in this range yet.")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("figure.chart")).ToHaveCountAsync(0);

        // R2-B1: the account is created through the UI, not seeded.
        var add = page.GetByRole(AriaRole.Form, new() { Name = "Add an account" });
        await add.GetByLabel("Institution").FillAsync("E2E Bank");
        await add.GetByLabel("Account name").FillAsync("Checking");
        await add.GetByLabel("Opened on").FillAsync(opened);
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await Assertions.Expect(add.GetByRole(AriaRole.Status)).ToHaveTextAsync("Added Checking.");

        // Enter a balance.
        var record = page.GetByRole(AriaRole.Form, new() { Name = "Record a balance" });
        await record.GetByLabel("As of").FillAsync(today);
        await record.GetByLabel("Balance (USD)").FillAsync("1234.56");
        await record.GetByRole(AriaRole.Button, new() { Name = "Record balance" }).ClickAsync();
        await Assertions.Expect(record.GetByRole(AriaRole.Status)).ToContainTextAsync("Recorded 1,234.56 USD");

        // The chart moved: it now exists, and draws the value (a lone point is a dot - step 4).
        var chart = page.Locator("figure.chart");
        await Assertions.Expect(chart).ToBeVisibleAsync();
        await Assertions.Expect(chart.Locator("svg circle")).Not.ToHaveCountAsync(0);

        // DoD 7: a tabular equivalent, with real header cells, holding the exact figure.
        await page.GetByText("Show as a table").ClickAsync();
        var table = page.GetByRole(AriaRole.Table, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Net worth by day") });
        await Assertions.Expect(table.Locator("thead th[scope=col]")).ToHaveCountAsync(6);
        var newest = table.Locator("tbody tr").First;
        await Assertions.Expect(newest.Locator("th[scope=row]")).ToHaveTextAsync(today);
        await Assertions.Expect(newest.Locator("td").First).ToHaveTextAsync("1,234.56");

        // The one AxeBuilder call: WCAG 2.2 AA, with the table expanded.
        var axe = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
        });
        Assert.True(axe.Violations.Length == 0,
            string.Join("\n", axe.Violations.Select(v => $"{v.Id} ({v.Impact}): {v.Nodes.Length} node(s), e.g. {v.Nodes[0].Target}")));

        // DoD 7: the chart is keyboard-reachable - Tab from the top of the page lands on it.
        // A fresh load, so sequential focus starts at the document's top (focusing <body> does
        // not move the starting point: it is not focusable).
        await page.ReloadAsync();
        await Assertions.Expect(chart).ToBeVisibleAsync();
        var reached = false;
        for (var i = 0; i < 15 && !reached; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached = await page.EvaluateAsync<bool>("() => !!document.activeElement?.closest('figure.chart')");
        }
        Assert.True(reached, "Tabbing from the top of the page never focused the chart.");

        await context.CloseAsync();
    }
}
