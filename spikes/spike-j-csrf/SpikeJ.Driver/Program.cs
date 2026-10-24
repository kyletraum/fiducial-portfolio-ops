// Spike J driver: every case from the app's own page in Chromium, then a same-site attacker
// page on another localhost port.
//
//   dotnet run -- http://localhost:5293 http://localhost:5294
//
// Each case prints the status, the ProblemDetails title, and what it wrote: the API's write
// counters are read before and after, so a refusal is shown to have written nothing.

using System.Text.Json;
using Microsoft.Playwright;

var app = args.Length > 0 ? args[0] : "http://localhost:5293";
var attacker = args.Length > 1 ? args[1] : "http://localhost:5294";

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();
await page.GotoAsync(app);

async Task<string> WritesAsync() => await page.EvaluateAsync<string>("async () => JSON.stringify(await (await fetch('/writes')).json())");

async Task CaseAsync(string label, string js)
{
    var before = await WritesAsync();
    var r = await page.EvaluateAsync<JsonElement>(js);
    var after = await WritesAsync();
    var status = r.TryGetProperty("status", out var s) ? s.GetInt32().ToString() : "?";
    var title = r.TryGetProperty("title", out var t) ? t.GetString() : "";
    var text = r.TryGetProperty("text", out var x) ? x.GetString() : r.ToString();
    var wrote = before == after ? "nothing written" : "WROTE";
    Console.WriteLine($"  {label,-62} {status}  {wrote,-15} {(string.IsNullOrEmpty(title) ? text : "title: " + title)}");
}

const string json = "JSON.stringify({ name: 'x' })";
string Call(string method, string url, string send, string body = json, string type = "application/json") =>
    $"() => call('{method}', '{url}', {{ body: {(body == "" ? "undefined" : body)}, type: '{type}', send: '{send}' }})";

Console.WriteLine("=== anonymous");
await CaseAsync("GET token endpoint, no session", "() => fetchToken()");
await CaseAsync("/api POST json, no token", Call("POST", "/api/json", "none"));
await CaseAsync("CONTROL /fw POST json, no token (framework only)", Call("POST", "/fw/json", "none"));
await CaseAsync("CONTROL /fw DELETE, no token (framework only)", Call("DELETE", "/fw/thing/1", "none", ""));
await CaseAsync("CONTROL /fw POST raw text/csv, no token (framework only)", Call("POST", "/fw/upload", "none", "'a,b\\n1,2'", "text/csv"));

Console.WriteLine("=== sign in as alice (the exempt route, no token: R1)");
await CaseAsync("POST /api/auth/sign-in, no token", Call("POST", "/api/auth/sign-in", "none", "JSON.stringify({ user: 'alice' })"));
await CaseAsync("GET token endpoint, signed in", "() => fetchToken()");

Console.WriteLine("=== signed in as alice: the slice's filter");
await CaseAsync("/api POST json, no token", Call("POST", "/api/json", "none"));
await CaseAsync("/api POST json, wrong token", Call("POST", "/api/json", "wrong"));
await CaseAsync("/api POST json, token", Call("POST", "/api/json", "token"));
await CaseAsync("/api PUT json, no token", Call("PUT", "/api/json", "none"));
await CaseAsync("/api PUT json, token", Call("PUT", "/api/json", "token"));
await CaseAsync("/api DELETE, no token", Call("DELETE", "/api/thing/7", "none", ""));
await CaseAsync("/api DELETE, token", Call("DELETE", "/api/thing/7", "token", ""));
await CaseAsync("/api POST raw text/csv, no token", Call("POST", "/api/upload", "none", "'a,b\\n1,2'", "text/csv"));
await CaseAsync("/api POST raw text/csv, token", Call("POST", "/api/upload", "token", "'a,b\\n1,2'", "text/csv"));
await CaseAsync("/api POST form-urlencoded, token in header", Call("POST", "/api/upload", "token", "'a=1&b=2'", "application/x-www-form-urlencoded"));
var tokenJs = "'__RequestVerificationToken=' + encodeURIComponent(currentToken())";
await CaseAsync("/api POST form-urlencoded, token only in the form body", Call("POST", "/api/upload", "none", tokenJs, "application/x-www-form-urlencoded"));
await CaseAsync("/api GET (safe method), no token", Call("GET", "/api/read", "none", ""));
await CaseAsync("CONTROL /fw POST json, wrong token, signed in", Call("POST", "/fw/json", "wrong"));

Console.WriteLine("=== the token binds to the user (G-20 and C3-22)");
var aliceToken = await page.EvaluateAsync<string>("() => currentToken()");
await CaseAsync("sign in as bob (exempt)", Call("POST", "/api/auth/sign-in", "none", "JSON.stringify({ user: 'bob' })"));
await CaseAsync("/api POST json as bob, with alice's token", Call("POST", "/api/json", aliceToken));
await CaseAsync("GET token endpoint as bob", "() => fetchToken()");
await CaseAsync("/api POST json as bob, bob's token", Call("POST", "/api/json", "token"));
await CaseAsync("POST /api/auth/sign-out, no token", Call("POST", "/api/auth/sign-out", "none", ""));
await CaseAsync("POST /api/auth/sign-out, token", Call("POST", "/api/auth/sign-out", "token", ""));
await CaseAsync("/api POST json signed out, bob's old token", Call("POST", "/api/json", "token"));

Console.WriteLine("=== G-19: a same-site page on another port, alice signed in");
await CaseAsync("sign in as alice again (exempt)", Call("POST", "/api/auth/sign-in", "none", "JSON.stringify({ user: 'alice' })"));
var attackPage = await context.NewPageAsync();
await attackPage.GotoAsync(attacker);
foreach (var target in new[] { $"{app}/api/upload", $"{app}/fw/upload" })
{
    var before = await WritesAsync();
    var responseTask = attackPage.WaitForResponseAsync(r => r.Url == target);
    var seenByPage = await attackPage.EvaluateAsync<string>($"() => attack('{target}')");
    var response = await responseTask;
    var cookie = (await response.Request.AllHeadersAsync()).GetValueOrDefault("cookie", "");
    string body;
    try { body = await response.TextAsync(); } catch (PlaywrightException e) { body = "TextAsync THREW " + e.Message.Split('\n')[0]; }
    var after = await WritesAsync();
    Console.WriteLine($"  attack -> {target}");
    Console.WriteLine($"    the page saw:        {seenByPage}");
    Console.WriteLine($"    session cookie sent: {cookie.Contains("__Host-portfolio=")}");
    Console.WriteLine($"    Playwright read:     {response.Status} {(body.Length > 140 ? body[..140] : body)}");
    Console.WriteLine($"    response headers:    X-Refused-By={(await response.AllHeadersAsync()).GetValueOrDefault("x-refused-by", "(absent)")}");
    Console.WriteLine($"    request headers Playwright reports: {string.Join(", ", (await response.Request.AllHeadersAsync()).Keys.Order())}");
    Console.WriteLine($"    written:             {(before == after ? "nothing" : "YES " + after)}");
}

await context.CloseAsync();
