using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// slice-02.md DoD 11, the browser half: a page on ANOTHER site tries to write to this API in a
/// real browser, and nothing is written. Two attacker origins - attacker.localhost (cross-site to
/// the API on localhost) and localhost on another port (same-site: a site ignores ports) - against
/// two targets: the API's own port, and the Vite dev proxy.
/// </summary>
/// <remarks>
/// DoD 11 requires recording whether each request REACHED the API, so a browser's own blocking of
/// requests to loopback cannot pass this for the wrong reason. The attacks that need no preflight
/// (a plain form post; a no-cors fetch) must arrive and be answered 403 by the API - which also
/// proves the browser is not blanket-blocking loopback. The two that need a preflight must never
/// be SENT: the browser asks first, the API grants no CORS, and the write does not happen.
/// The attacker page is served by a real loopback listener, not by Playwright routing, so the
/// browser treats it as a real local origin.
/// </remarks>
public class CrossSiteBrowserTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    const string Csv = "account,as_of_date,balance\nEX-ATTACK-1,2026-10-15,1\n";

    static string AttackPage(string target, Guid account) => $$"""
        <!doctype html><html lang="en"><head><title>Not this app</title></head><body>
        <iframe name="sink" title="sink"></iframe>
        <form id="form" method="POST" enctype="multipart/form-data" action="{{target}}/api/v1/imports?fileName=form.csv" target="sink">
          <input type="hidden" name="file" value="{{Csv.Replace("\n", "&#10;")}}">
        </form>
        <script>
          window.attack = async () => {
            const out = {};
            const run = async (name, request) => {
              try { const r = await request(); out[name] = 'sent: ' + r.type + ' ' + r.status; }
              catch (e) { out[name] = 'blocked: ' + e.name; }
            };
            // A READ: no preflight, so it reaches the API and is answered - but the browser must
            // hide the answer from this page unless CORS was granted. Nothing grants it.
            await run('read', async () => { const r = await fetch('{{target}}/api/v1/accounts'); await r.json(); return r; });
            await run('csv', () => fetch('{{target}}/api/v1/imports?fileName=cors.csv',
              { method: 'POST', headers: { 'Content-Type': 'text/csv' }, body: {{JsonSerializer.Serialize(Csv)}} }));
            await run('json', () => fetch('{{target}}/api/v1/accounts/{{account}}/balances',
              { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{"asOfDate":"2026-10-15","balance":"1"}' }));
            // no-cors cannot carry a non-safelisted Content-Type: the browser drops it and sends
            // text/plain - no preflight, so this one ARRIVES, and the API must refuse it.
            await run('nocors', () => fetch('{{target}}/api/v1/imports?fileName=nocors.csv',
              { method: 'POST', mode: 'no-cors', headers: { 'Content-Type': 'text/csv' }, body: {{JsonSerializer.Serialize(Csv)}} }));
            document.getElementById('form').submit();
            return out;
          };
        </script></body></html>
        """;

    sealed record Seen(string Method, string Url, int? Status, string? Failure);

    public static TheoryData<string, string> Cases() => new()
    {
        { "attacker.localhost", "api" },
        { "attacker.localhost", "web" },
        { "localhost", "api" },
        { "localhost", "web" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_page_on_another_site_cannot_write(string attackerHost, string targetName)
    {
        var target = (targetName == "api" ? fixture.ApiUrl : fixture.WebUrl).GetLeftPart(UriPartial.Authority);
        using var api = new HttpClient { BaseAddress = fixture.ApiUrl };
        var account = await NewAccountAsync(api);
        var before = await WrittenAsync(api, account);

        await using var site = AttackerSite.Start(AttackPage(target, account));
        var context = await fixture.Browser.NewContextAsync();
        var seen = new List<Seen>();
        context.Response += (_, r) => { if (r.Url.StartsWith(target)) lock (seen) seen.Add(new(r.Request.Method, r.Url, r.Status, null)); };
        context.RequestFailed += (_, r) => { if (r.Url.StartsWith(target)) lock (seen) seen.Add(new(r.Method, r.Url, null, r.Failure)); };
        context.Request += (_, r) => { if (r.Url.StartsWith(target)) lock (seen) seen.Add(new(r.Method, r.Url, null, "requested")); };

        var page = await context.NewPageAsync();
        await page.GotoAsync($"http://{attackerHost}:{site.Port}/");
        // A string, parsed here: binding a JS object straight to a Dictionary came back empty.
        var outcome = JsonSerializer.Deserialize<Dictionary<string, string>>(
            await page.EvaluateAsync<string>("() => attack().then(o => JSON.stringify(o))"))!;

        // The form post navigates the iframe; wait until its answer has been seen.
        await WaitForAsync(() => seen.Any(s => s.Url.Contains("fileName=form.csv") && s.Status is not null));
        string Report() { lock (seen) return string.Join("\n", seen.Select(s => $"{s.Method} {s.Url} -> {s.Status?.ToString() ?? s.Failure}")) + "\n" + JsonSerializer.Serialize(outcome); }

        // A cross-site page cannot READ the API's answers: the same-origin policy holds only while
        // nothing grants CORS - and Vite's dev server did, by default, until web/vite.config.ts
        // turned it off (found by this test, 2026-10-18).
        Assert.True(outcome["read"].StartsWith("blocked"), "read: " + outcome["read"] + "\n" + Report());

        // Preflighted writes: the browser asked, was refused CORS, and never sent the POST.
        Assert.StartsWith("blocked", outcome["csv"]);
        Assert.StartsWith("blocked", outcome["json"]);
        lock (seen)
        {
            Assert.DoesNotContain(seen, s => s.Method == "POST" && s.Url.Contains("fileName=cors.csv") && s.Status is not null);
            Assert.DoesNotContain(seen, s => s.Method == "POST" && s.Url.Contains("/balances") && s.Status is not null);

            // Unpreflighted writes: they REACHED the API, and the API refused them (layer 2).
            Assert.True(seen.Any(s => s.Url.Contains("fileName=form.csv") && s.Status == 403), Report());
            Assert.True(seen.Any(s => s.Url.Contains("fileName=nocors.csv") && s.Status == 403), Report());
        }

        // And nothing was written, whatever the browser did.
        Assert.Equal(before, await WrittenAsync(api, account));
        await context.CloseAsync();
    }

    static async Task<Guid> NewAccountAsync(HttpClient api)
    {
        var created = await api.PostAsJsonAsync("/api/v1/accounts",
            new { institutionName = "Example Bank", displayName = "Target", openedOn = "2026-01-01" }, Ct);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        (await api.PostAsJsonAsync($"/api/v1/accounts/{id}/sources", new { sourceSystem = "import:csv", sourceId = $"EX-{Guid.NewGuid():N}" }, Ct))
            .EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>Imports recorded, and balances on the target account.</summary>
    static async Task<(int Runs, int Balances)> WrittenAsync(HttpClient api, Guid account)
    {
        var runs = await api.GetFromJsonAsync<JsonElement>("/api/v1/imports?limit=500", Ct);
        var balances = await api.GetFromJsonAsync<JsonElement>($"/api/v1/accounts/{account}/balances", Ct);
        return (runs.GetArrayLength(), balances.GetArrayLength());
    }

    static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(100, Ct);
    }

    /// <summary>
    /// The attacker's web server: one page, on loopback, answering whatever Host it is asked as -
    /// so the same port is attacker.localhost (cross-site) and localhost (same-site).
    /// </summary>
    sealed class AttackerSite : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly byte[] response;
        Task? loop;

        AttackerSite(string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n")
                .Concat(body).ToArray();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public static AttackerSite Start(string html)
        {
            var site = new AttackerSite(html);
            site.listener.Start();
            site.loop = site.ServeAsync();
            return site;
        }

        async Task ServeAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = client.GetStream();
                    var buffer = new byte[8192];
                    var head = new StringBuilder();
                    while (!head.ToString().Contains("\r\n\r\n"))
                    {
                        var n = await stream.ReadAsync(buffer, stop.Token);
                        if (n == 0) break;
                        head.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    }
                    await stream.WriteAsync(response, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            if (loop is not null) await loop;
        }
    }
}
