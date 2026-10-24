using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Portfolio.E2E;

/// <summary>
/// slice-03.md step 7 and DoD 3, 6: the PUBLISHED stack, the built bundle served by the Api itself
/// under the strict CSP (D16), as `aspire deploy` leaves it. Run by hand against a fresh deploy, and
/// recorded in the assessment with date and commit (D13); skipped unless pointed at one:
///
///     PORTFOLIO_PUBLISHED_URL=http://localhost:8080
///     PORTFOLIO_SETUP_CODE=&lt;SETUP_CODE from aspire-output/.env.&lt;Environment&gt;&gt;
///     PORTFOLIO_STATE_OUT=&lt;optional: a file to save the signed-in browser state to&gt;
///
/// It enrols the owner, so the stack must have none: on a re-run, `reset-owner` and a fresh code.
/// </summary>
public class PublishedStackTests : IAsyncLifetime
{
    const string Csp = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; "
        + "frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    /// <summary>Records every CSP violation the page reports, from before its first script runs.</summary>
    const string ViolationRecorder = """
        window.__csp = [];
        document.addEventListener('securitypolicyviolation', e => window.__csp.push(e.violatedDirective + ' ' + e.blockedURI));
        """;

    IPlaywright? playwright;
    IBrowser browser = null!;

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("PORTFOLIO_PUBLISHED_URL") is null) return;
        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (playwright is null) return;
        await browser.DisposeAsync();
        playwright.Dispose();
    }

    [Fact]
    public async Task The_published_stack_runs_the_tier_under_its_CSP()
    {
        var url = Environment.GetEnvironmentVariable("PORTFOLIO_PUBLISHED_URL");
        var setupCode = Environment.GetEnvironmentVariable("PORTFOLIO_SETUP_CODE");
        if (url is null || setupCode is null)
            Assert.Skip("Run by hand against a deployed stack: set PORTFOLIO_PUBLISHED_URL and PORTFOLIO_SETUP_CODE.");

        // No BypassCSP: this context is held to the page's policy (TST-11).
        var context = await browser.NewContextAsync(new() { BaseURL = url });
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(ViolationRecorder);
        var refused = new List<string>();
        page.Console += (_, m) => { if (m.Text.Contains("Refused to", StringComparison.Ordinal)) lock (refused) refused.Add(m.Text); };
        var authenticator = await VirtualAuthenticator.AttachAsync(context, page);

        // The document's headers: the exact policy, the rest of the set, and no HSTS.
        var document = await page.GotoAsync("/");
        var headers = await document!.AllHeadersAsync();
        Assert.Equal(Csp, headers["content-security-policy"]);
        Assert.Equal("nosniff", headers["x-content-type-options"]);
        Assert.Equal("DENY", headers["x-frame-options"]);
        Assert.Equal("no-referrer", headers["referrer-policy"]);
        Assert.Contains("publickey-credentials-get=(self)", headers["permissions-policy"]);
        Assert.False(headers.ContainsKey("strict-transport-security"));

        // The positive control: an inline script is refused, and the recorder sees it, before anything
        // else is asserted against the recorder.
        try { await page.AddScriptTagAsync(new() { Content = "window.__x = 1;" }); } catch (PlaywrightException) { }
        Assert.Null(await page.EvaluateAsync<int?>("() => window.__x ?? null"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__csp.length"));

        // Set-up through the page, with the deploy's code.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Step 1 of 3: Enter the setup code" })).ToBeVisibleAsync();
        await page.GetByLabel("Setup code").FillAsync(setupCode);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await page.GetByLabel("User name").FillAsync("owner");
        await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Set-up complete" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Open the portfolio" }).ClickAsync();
        await Expect(page.GetByText("Signed in as owner")).ToBeVisibleAsync();

        // Slice 01 and 02, through the built bundle: an account, a balance, an import, the chart.
        var name = $"Published {DateTime.UtcNow:HHmmss}";
        var add = page.GetByRole(AriaRole.Form, new() { Name = "Add an account" });
        await add.GetByLabel("Institution").FillAsync("Example Bank");
        await add.GetByLabel("Account name").FillAsync(name);
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await Expect(add.GetByRole(AriaRole.Status)).ToContainTextAsync("Added");
        var record = page.GetByRole(AriaRole.Form, new() { Name = "Record a balance" });
        await record.GetByLabel("Account").SelectOptionAsync(new SelectOptionValue { Label = $"{name} (Example Bank)" });
        await record.GetByLabel("Balance (USD)").FillAsync("1234.56");
        await record.GetByRole(AriaRole.Button, new() { Name = "Record balance" }).ClickAsync();
        await Expect(record.GetByRole(AriaRole.Status)).ToContainTextAsync("Recorded");
        await Expect(page.Locator("figure.chart")).ToHaveCountAsync(1);

        // Nothing else the app did was refused by its own policy.
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__csp.length"));
        lock (refused) Assert.True(refused.Count <= 1, string.Join("\n", refused)); // the control's own message

        // axe in both schemes, in contexts held to the policy too (gate G-29: no BypassCSP needed?).
        var state = await context.StorageStateAsync();
        await Axe.AssertCleanAsync(page, "published, light");
        var dark = await browser.NewContextAsync(new() { BaseURL = url, ColorScheme = ColorScheme.Dark, StorageState = state });
        var darkPage = await dark.NewPageAsync();
        await darkPage.GotoAsync("/");
        await Expect(darkPage.GetByText("Signed in as owner")).ToBeVisibleAsync();
        await Axe.AssertCleanAsync(darkPage, "published, dark");
        await dark.CloseAsync();

        // Sign out, and sign in again with the passkey on the published port (gate G-12).
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Level = 1 })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
        await Expect(page.GetByText("Signed in as owner")).ToBeVisibleAsync();

        if (Environment.GetEnvironmentVariable("PORTFOLIO_STATE_OUT") is { Length: > 0 } stateOut)
            await File.WriteAllTextAsync(stateOut, await context.StorageStateAsync());
        _ = authenticator; // held for the context's lifetime: it is the passkey
        await context.CloseAsync();
    }
}
