using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Portfolio.E2E;

/// <summary>
/// slice-03.md step 4 and DoD 3: sign-in, its outcomes, expiry and sign-out, in Chromium with a CDP
/// virtual authenticator holding the owner's passkey. Every test signs in through the page itself:
/// sign-out rotates the security stamp, which ends every stored session on this stack.
/// </summary>
public class AuthBrowserTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    async Task<(IBrowserContext Context, IPage Page)> SignInPageAsync(string? initScript = null, bool passkey = true)
    {
        var context = await fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString() });
        var page = await context.NewPageAsync();
        if (initScript is not null) await page.AddInitScriptAsync(initScript);
        if (passkey) await fixture.OwnersPasskeyAsync(context, page);
        await page.GotoAsync("/");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Level = 1 })).ToBeVisibleAsync();
        return (context, page);
    }

    static ILocator PasskeyButton(IPage page) => page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" });

    static async Task SignInAsync(IPage page)
    {
        await PasskeyButton(page).ClickAsync();
        await Expect(page.GetByText("Signed in as")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_owner_signs_in_with_a_passkey()
    {
        var (context, page) = await SignInPageAsync();

        // Before the ceremony: the view names itself, the environment, the lifetime and the time allowed.
        await Expect(page).ToHaveTitleAsync("Sign in — Portfolio");
        await Expect(page.GetByText("Environment:")).ToContainTextAsync("Development");
        await Expect(page.GetByText("up to 10 minutes")).ToBeVisibleAsync();
        await Expect(page.GetByText("7 days after you last use the app")).ToBeVisibleAsync();

        await SignInAsync(page);

        await Expect(page).ToHaveTitleAsync("Portfolio");
        await Expect(page.GetByText("Signed in as owner")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Level = 1 })).ToBeFocusedAsync();
        await Expect(page.GetByText("API: reachable")).ToBeVisibleAsync();
        await context.CloseAsync();
    }

    [Fact]
    public async Task While_the_browser_has_control_the_page_says_it_is_waiting()
    {
        // TST-15's producer for "pending": the sign-in POST is held until the assertions are done.
        var (context, page) = await SignInPageAsync();
        var release = new TaskCompletionSource();
        await page.RouteAsync("**/api/v1/auth/sign-in", async route => { await release.Task; await route.ContinueAsync(); });

        await PasskeyButton(page).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Waiting for your passkey…" })).ToBeVisibleAsync();
        await Expect(PasskeyButton(page)).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(PasskeyButton(page)).ToBeFocusedAsync(); // still enabled, so focus was not dropped
        release.SetResult();
        await Expect(page.GetByText("Signed in as owner")).ToBeVisibleAsync();
        await context.CloseAsync();
    }

    public static TheoryData<string, string, string> Failures => new()
    {
        { "NotAllowedError", "cancelled or timed out. Nothing has changed.", "navigator.credentials.get = () => Promise.reject(new DOMException('x', 'NotAllowedError'));" },
        { "SecurityError", "Passkeys do not work at this address", "navigator.credentials.get = () => Promise.reject(new DOMException('x', 'SecurityError'));" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failed_ceremony_says_why_and_returns_focus_to_the_button(string _, string text, string script)
    {
        // TST-15's producers: an init script makes the browser's answer the one under test.
        var (context, page) = await SignInPageAsync(script, passkey: false);

        await PasskeyButton(page).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(text);
        await Expect(PasskeyButton(page)).ToBeFocusedAsync();
        await context.CloseAsync();
    }

    [Fact]
    public async Task A_browser_without_passkeys_is_told_it_cannot_sign_in_here()
    {
        var (context, page) = await SignInPageAsync("delete window.PublicKeyCredential;", passkey: false);

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("This browser cannot use passkeys");
        await Expect(PasskeyButton(page)).ToHaveCountAsync(0);
        await context.CloseAsync();
    }

    [Fact]
    public async Task A_passkey_this_app_does_not_know_is_named_as_such()
    {
        // Spike I §3: a refused enrolment, or another app, can leave such a passkey on the device.
        var (context, page) = await SignInPageAsync(passkey: false);
        await (await VirtualAuthenticator.AttachAsync(context, page)).AddStrangerAsync();

        await PasskeyButton(page).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("This passkey isn't registered with this app");
        await context.CloseAsync();
    }

    [Fact]
    public async Task Signing_out_returns_to_sign_in_and_says_so()
    {
        var (context, page) = await SignInPageAsync();
        await SignInAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Level = 1 })).ToBeFocusedAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "You are signed out." })).ToBeVisibleAsync();
        await context.CloseAsync();
    }

    [Theory]
    [InlineData("ticket expired")] // the cookie is still sent; the session behind it has ended
    [InlineData("no cookie sent")]
    public async Task An_ended_session_opens_sign_in_over_the_page_and_loses_nothing(string how)
    {
        // C3-17 / C3-47. B holds a copy of A's session; A signs out, which rotates the stamp, so B's
        // cookie is refused at its next request (the E2E's validation interval is zero).
        var (a, aPage) = await SignInPageAsync();
        await SignInAsync(aPage);
        var b = await fixture.Browser.NewContextAsync(new() { BaseURL = fixture.WebUrl.ToString(), StorageState = await a.StorageStateAsync() });
        var page = await b.NewPageAsync();
        await fixture.OwnersPasskeyAsync(b, page);
        await page.GotoAsync("/");
        await Expect(page.GetByText("Signed in as owner")).ToBeVisibleAsync();

        // Idle first: with a zero validation interval every response re-issues the cookie, and one
        // still in flight would put it back after the jar is cleared (EXECUTED: it did).
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        if (how == "ticket expired")
            await aPage.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        else
            await b.ClearCookiesAsync();
        await Expect(aPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
        Assert.Equal(how == "ticket expired", (await b.CookiesAsync()).Any(c => c.Name == "__Host-portfolio"));

        // A write after the session ended: refused, not replayed, and the form keeps what was typed.
        var add = page.GetByRole(AriaRole.Form, new() { Name = "Add an account" });
        await add.GetByLabel("Institution").FillAsync("Example Bank");
        await add.GetByLabel("Account name").FillAsync("Typed before expiry");
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Signed out" });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Could not load" })).ToHaveCountAsync(0); // no alert storm
        await Expect(add.GetByLabel("Account name")).ToHaveValueAsync("Typed before expiry");
        await Expect(add.GetByRole(AriaRole.Alert)).ToContainTextAsync("You were signed out before this was saved");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Signed in again." })).ToBeAttachedAsync();
        await Expect(add.GetByLabel("Account name")).ToHaveValueAsync("Typed before expiry");
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await Expect(add.GetByRole(AriaRole.Status)).ToContainTextAsync("Added");
        await a.CloseAsync();
        await b.CloseAsync();
    }
}
