// Spike H driver: Chromium with a CDP virtual authenticator, against SpikeH.Web.
//
//   dotnet run -- http://localhost:5299
//
// A virtual authenticator is the browser's own WebAuthn test hook: the page's
// navigator.credentials calls are answered by a software authenticator instead of a device.
// It is what the slice-03 E2E will use, so this also proves that route.

using Microsoft.Playwright;

var origin = args.Length > 0 ? args[0] : "http://localhost:5299";
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

async Task<string[]> RunAsync(string at)
{
    var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var cdp = await context.NewCDPSessionAsync(page);
    await cdp.SendAsync("WebAuthn.enable");
    await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
    {
        ["options"] = new Dictionary<string, object>
        {
            ["protocol"] = "ctap2", ["transport"] = "internal",
            ["hasResidentKey"] = true, ["hasUserVerification"] = true, ["isUserVerified"] = true,
        },
    });
    await page.GotoAsync(at);
    var steps = new List<string> { "secure context: " + await page.EvaluateAsync<bool>("() => window.isSecureContext") };
    foreach (var call in new[] { "register('spike-user')", "me()", "login()", "me()" })
    {
        try { steps.Add(await page.EvaluateAsync<string>($"() => {call}")); }
        catch (PlaywrightException e) { steps.Add($"{call} THREW {e.Message.Split('\n')[0]}"); }
    }
    await context.CloseAsync();
    return [.. steps];
}

foreach (var at in new[] { origin, origin.Replace("localhost", "127.0.0.1") })
{
    Console.WriteLine($"=== {at}");
    foreach (var line in await RunAsync(at)) Console.WriteLine("  " + (line.Length > 220 ? line[..220] + "..." : line));
}
