// Spike I remainder (G-5) driver: Chromium with a CDP virtual authenticator, through the Vite
// dev proxy.
//
//   dotnet run -- <allowed Vite origin> <second Vite origin> <API origin>
//
// One page and one authenticator, navigated across three origins, so the credential registered
// at the first is the one presented at the others:
//   A, the allowed Vite origin:      register, /me, sign in, /me          -> expected to work
//   B, a second Vite port, same API: sign in (cookies cleared)            -> expected REJECTED
//   the API's own port:              sign in (cookies cleared)            -> expected REJECTED
//   B again:                         register, then a diagnosed sign-in   -> REJECTED, and the
//                                    orphan the refused registration leaves behind
// B and the API's port are not on Spike:AllowedOrigins: they are the positive control that the
// origin check is live, not merely permissive.

using Microsoft.Playwright;

if (args.Length < 3) { Console.Error.WriteLine("usage: <viteA> <viteB> <api>"); return 2; }
var (viteA, viteB, api) = (args[0], args[1], args[2]);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
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

async Task StepsAsync(string label, string at, params string[] calls)
{
    await page.GotoAsync(at);
    Console.WriteLine($"=== {label} ({at}), secure context: {await page.EvaluateAsync<bool>("() => window.isSecureContext")}");
    foreach (var call in calls)
    {
        string line;
        try { line = await page.EvaluateAsync<string>($"() => {call}"); }
        catch (PlaywrightException e) { line = $"{call} THREW {e.Message.Split('\n')[0]}"; }
        Console.WriteLine("  " + (line.Length > 200 ? line[..200] + "..." : line));
    }
}

await StepsAsync("A: allowed Vite origin", viteA, "register('spike-a')", "me()", "login()", "me()");

Console.WriteLine("  cookies the browser holds for A (name, secure, httpOnly, sameSite, expires):");
foreach (var c in await context.CookiesAsync([viteA]))
{
    var expires = c.Expires <= 0 ? "session" : DateTimeOffset.FromUnixTimeSeconds((long)c.Expires).ToString("u");
    Console.WriteLine($"    {c.Name}  secure={c.Secure} httpOnly={c.HttpOnly} sameSite={c.SameSite} expires={expires}");
}

await context.ClearCookiesAsync();
await StepsAsync("B: second Vite port, not allowed", viteB, "me()", "login()", "me()");

await context.ClearCookiesAsync();
await StepsAsync("API's own port, not allowed", api, "login()", "me()");

// Last, because a refused registration still leaves a credential in the authenticator (and,
// with this spike's options endpoint, a user row): every later sign-in would present it.
await context.ClearCookiesAsync();
await StepsAsync("B: registration, not allowed", viteB, "register('spike-b')", "loginDiagnose()");

await context.CloseAsync();
return 0;
