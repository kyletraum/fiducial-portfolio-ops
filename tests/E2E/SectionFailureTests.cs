using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// A render error in one section must not blank the page. Found by slice 02 step 4's E2E: a
/// toLocaleString option clash threw inside the import history, and with no error boundary React
/// unmounted EVERYTHING - net worth, accounts, the forms. The cause is fixed; this holds the
/// containment, so the next such bug costs one section, not the page.
/// </summary>
/// <remarks>
/// The error is made real, not simulated in code: the import-history response is replaced with an
/// object where the page expects an array, so ImportHistory throws while rendering.
/// </remarks>
public class SectionFailureTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task A_section_that_throws_is_replaced_by_a_notice_and_the_rest_of_the_page_still_works()
    {
        var context = await fixture.NewSignedInContextAsync();
        var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, e) => pageErrors.Add(e);
        await page.RouteAsync("**/api/v1/imports?limit=50", route =>
            route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));

        await page.GotoAsync("/");

        var imports = page.GetByRole(AriaRole.Region, new() { Name = "Imports" });
        await Assertions.Expect(imports.GetByRole(AriaRole.Alert)).ToContainTextAsync("This part of the page could not be shown");
        await Assertions.Expect(imports.GetByRole(AriaRole.Heading, new() { Name = "Imports" })).ToBeVisibleAsync();

        // Everything else still renders and still works.
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Net worth" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("API: reachable")).ToBeVisibleAsync();
        var add = page.GetByRole(AriaRole.Form, new() { Name = "Add an account" });
        await add.GetByLabel("Institution").FillAsync("Example Bank");
        await add.GetByLabel("Account name").FillAsync("Still works");
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await Assertions.Expect(add.GetByRole(AriaRole.Status)).ToHaveTextAsync("Added Still works.");

        var axe = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
        });
        Assert.True(axe.Violations.Length == 0,
            string.Join("\n", axe.Violations.Select(v => $"{v.Id} ({v.Impact}): {v.Nodes.Length} node(s), e.g. {v.Nodes[0].Target}")));

        // Once the response is good again, reloading brings the section back.
        await page.UnrouteAllAsync();
        await imports.GetByRole(AriaRole.Button, new() { Name = "Reload the page" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Form, new() { Name = "Import a balance file" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Imports" }).GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }
}
