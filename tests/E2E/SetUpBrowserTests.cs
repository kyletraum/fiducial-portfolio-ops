using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Portfolio.E2E;

/// <summary>
/// slice-03.md step 4 and C3-53: first-run set-up through the page, on a stack with no owner yet.
/// Setup code, then a user name, then a passkey; a wrong code is caught at the passkey step and the
/// person can go back. Each step is a view whose heading takes focus.
/// </summary>
public class SetUpBrowserTests(NewOwnerFixture fixture) : IClassFixture<NewOwnerFixture>
{
    [Fact]
    public async Task The_owner_sets_up_with_the_setup_code_and_a_passkey()
    {
        var context = await fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString() });
        var page = await context.NewPageAsync();
        await VirtualAuthenticator.AttachAsync(context, page);
        await page.GotoAsync("/");

        await Expect(page).ToHaveTitleAsync("Set up — Portfolio");

        // DoD 6: the set-up view in both colour schemes, while there is still no owner to make it go away.
        await Axe.AssertCleanAsync(page, "set-up (light)");
        var dark = await fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString(), ColorScheme = ColorScheme.Dark });
        var darkPage = await dark.NewPageAsync();
        await darkPage.GotoAsync("/");
        await Expect(darkPage.GetByRole(AriaRole.Heading, new() { Name = "Step 1 of 3: Enter the setup code" })).ToBeVisibleAsync();
        await Axe.AssertCleanAsync(darkPage, "set-up (dark)");
        await dark.CloseAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Step 1 of 3: Enter the setup code" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Environment:")).ToContainTextAsync("Development");
        await Expect(page.GetByLabel("Setup code")).ToHaveAttributeAsync("autocomplete", "one-time-code");

        // A wrong code first: refused at the passkey step, before any passkey is made.
        await page.GetByLabel("Setup code").FillAsync("not-the-code");
        await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Step 2 of 3: Choose a user name" })).ToBeFocusedAsync();
        await Expect(page.GetByLabel("User name")).ToHaveAttributeAsync("autocomplete", "username");
        await page.GetByLabel("User name").FillAsync("owner");
        await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Step 3 of 3: Add a passkey" })).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Setup code not accepted");

        // Back to step 1, with the code as a person might paste it: grouped, in capitals.
        await page.GetByRole(AriaRole.Button, new() { Name = "Change the setup code or user name" }).ClickAsync();
        var grouped = string.Join("-", fixture.SetupCode.Chunk(5).Select(c => new string(c))).ToUpperInvariant();
        await page.GetByLabel("Setup code").FillAsync(grouped);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(page.GetByLabel("User name")).ToHaveValueAsync("owner");
        await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Set-up complete" })).ToBeFocusedAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "You are signed in as owner." })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Open the portfolio" }).ClickAsync();
        await Expect(page.GetByText("Signed in as owner")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Level = 1 })).ToBeFocusedAsync();

        // Once an owner exists, a fresh browser is offered sign-in, not set-up.
        var fresh = await fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString() });
        var second = await fresh.NewPageAsync();
        await second.GotoAsync("/");
        await Expect(second.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Level = 1 })).ToBeVisibleAsync();
        await fresh.CloseAsync();
        await context.CloseAsync();
    }
}
