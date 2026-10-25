using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// The whole app model - Postgres, Migrator, Api, Vite - started by the test builder on a
/// throwaway database (no volume: AppModelTests), plus a headless Chromium.
/// </summary>
/// <remarks>
/// Since slice 03 step 4 every endpoint requires a session. The fixture enrols the owner ONCE, with
/// a setup code it sets as the AppHost parameter, and keeps the signed-in browser state; each test's
/// context starts from it (round 2 TST-25). The passkey itself is kept too, so a test can sign in
/// again with it. <see cref="NewOwnerFixture"/> skips enrolment, for the set-up view's own test.
/// </remarks>
public class AppFixture : IAsyncLifetime
{
    static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    DistributedApplication? app;
    IPlaywright? playwright;
    readonly CancellationTokenSource stopping = new();
    readonly List<string> apiLog = [];
    string storageState = "";
    JsonElement credential;
    int nextSignCount;

    public IBrowser Browser { get; private set; } = null!;
    public Uri WebUrl { get; private set; } = null!;
    /// <summary>The API's own endpoint - what an attacking page would aim at directly.</summary>
    public Uri ApiUrl { get; private set; } = null!;

    /// <summary>The setup code this fixture's app runs with: 29 letters of the generator's alphabet.</summary>
    public string SetupCode { get; } = string.Concat(Enumerable.Range(0, 29).Select(_ => "abcdefghjkmnpqrstuvwxyz"[Random.Shared.Next(23)]));

    protected virtual bool EnrolOwner => true;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);

        // Playwright's own browser, installed in-process: no PowerShell script needed (step 1's
        // note - Program.Main is public). A no-op once the browser is present.
        var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installed != 0) throw new InvalidOperationException($"playwright install chromium exited {installed}");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        builder.Configuration["Parameters:setup-code"] = SetupCode;
        builder.CreateResourceBuilder<ProjectResource>("api")
            // A copied cookie is refused at its next request, not a minute later, so the expiry
            // tests need no clock (TST-15). And the sign-in limiter is for people, not suites.
            .WithEnvironment("Auth__StampValidationInterval", "00:00:00")
            .WithEnvironment("RateLimits__SignIn__PermitLimit", "100000");
        app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("web", timeout.Token);
        WebUrl = app.GetEndpoint("web", "http");
        ApiUrl = app.GetEndpoint("api", "http");
        await WaitUntilServingAsync(WebUrl, timeout.Token);
        _ = FollowApiLogAsync(app.Services.GetRequiredService<ResourceLoggerService>(),
            app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == "api"));

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        // Every class starts its own whole stack, in parallel, so under a full run a passkey ceremony
        // or a first load can outlast Playwright's 5-second default (two such flakes in slice 03 step 7,
        // each passing alone). An assertion still fails; it waits longer before it does.
        Assertions.SetDefaultExpectTimeout(15_000);
        if (EnrolOwner) await EnrolAsync();
    }

    /// <summary>The owner's enrolment, by the API directly from the app's own page.</summary>
    async Task EnrolAsync()
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var authenticator = await VirtualAuthenticator.AttachAsync(context, page);
        await page.GotoAsync(WebUrl.ToString());
        await page.EvaluateAsync("""
            async (setupCode) => {
              const post = async (url, body) => {
                const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                if (!r.ok) throw new Error(url + ' ' + r.status + ' ' + await r.text());
                return r.json();
              };
              const options = await post('/api/v1/auth/enrolment/options', { setupCode, userName: 'owner' });
              const credential = await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) });
              await post('/api/v1/auth/enrolment', { setupCode, credential: credential.toJSON() });
            }
            """, SetupCode);
        storageState = await context.StorageStateAsync();
        credential = await authenticator.CredentialAsync();
        nextSignCount = credential.GetProperty("signCount").GetInt32();
        await context.CloseAsync();
    }

    /// <summary>
    /// The API's console, line by line, as Aspire captures it (gate G-21). DoD 4 reads from here what a
    /// browser test cannot see: which layer refused a request, and whether the session cookie arrived.
    /// </summary>
    async Task FollowApiLogAsync(ResourceLoggerService logs, IResource api)
    {
        try
        {
            // By the resource, not its name: a project's resource id can carry a replica suffix, and
            // WatchAsync("api") then follows nothing (EXECUTED: an empty log while the lines existed).
            await foreach (var batch in logs.WatchAsync(api).WithCancellation(stopping.Token))
                lock (apiLog) apiLog.AddRange(batch.Select(l => l.Content));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>How many API log lines have been seen so far: a mark to read from.</summary>
    public int ApiLogMark { get { lock (apiLog) return apiLog.Count; } }

    /// <summary>
    /// The refusal lines written since <paramref name="mark"/> that match, waiting up to ten seconds for
    /// at least <paramref name="atLeast"/>: the log reaches the test a little after the response does.
    /// </summary>
    public async Task<List<string>> RefusalsSinceAsync(int mark, Func<string, bool> match, int atLeast)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            List<string> found;
            lock (apiLog) found = apiLog.Skip(mark).Where(l => l.Contains("Refused by ", StringComparison.Ordinal) && match(l)).ToList();
            if (found.Count >= atLeast || DateTime.UtcNow > deadline) return found;
            await Task.Delay(200);
        }
    }

    /// <summary>A browser context already signed in as the owner.</summary>
    public Task<IBrowserContext> NewSignedInContextAsync(BrowserNewContextOptions? options = null)
    {
        options ??= new();
        options.BaseURL ??= WebUrl.ToString();
        options.StorageState = storageState;
        return Browser.NewContextAsync(options);
    }

    /// <summary>
    /// Gives this page's authenticator the owner's passkey. Each copy starts its counter above the last
    /// copy's, or two copies would present the same count and the second would be refused as a clone.
    /// </summary>
    public async Task<VirtualAuthenticator> OwnersPasskeyAsync(IBrowserContext context, IPage page, bool presence = true)
    {
        var authenticator = await VirtualAuthenticator.AttachAsync(context, page, presence);
        await authenticator.AddAsync(credential, Interlocked.Add(ref nextSignCount, 1000));
        return authenticator;
    }

    /// <summary>
    /// The API, called from a signed-in page with the page's own cookie and a CSRF token: a bare
    /// HttpClient has neither since step 4 (round 2 TST-6).
    /// </summary>
    public async Task<SignedInApi> ApiAsync()
    {
        var context = await NewSignedInContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        return new SignedInApi(context, page);
    }

    /// <summary>
    /// "Healthy" is not "serving" for the web resource: it has no health check, so Aspire reports it
    /// healthy once the Vite process runs, before the dev server listens. A test that navigated in
    /// that gap got net::ERR_EMPTY_RESPONSE (ImportAFileTests, 2026-10-24; it passed alone).
    /// </summary>
    static async Task WaitUntilServingAsync(Uri url, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        if (Browser is not null) await Browser.DisposeAsync();
        playwright?.Dispose();
        if (app is not null) await app.DisposeAsync();
    }
}

/// <summary>The same app model with no owner yet: for the set-up view.</summary>
public sealed class NewOwnerFixture : AppFixture
{
    protected override bool EnrolOwner => false;
}

/// <summary>fetch, from a signed-in page of the app. Every unsafe call fetches a CSRF token first.</summary>
public sealed class SignedInApi(IBrowserContext context, IPage page) : IAsyncDisposable
{
    public async Task<(int Status, JsonElement Body)> SendAsync(string method, string path, string? contentType = null, string? body = null)
    {
        var json = await page.EvaluateAsync<string>("""
            async ({ method, path, contentType, body }) => {
              const headers = {};
              if (contentType) headers['Content-Type'] = contentType;
              if (method !== 'GET') headers['X-CSRF-TOKEN'] = (await (await fetch('/api/v1/auth/csrf-token')).json()).token;
              const r = await fetch(path, { method, headers, body: body ?? undefined });
              const text = await r.text();
              return JSON.stringify({ status: r.status, body: text ? JSON.parse(text) : null });
            }
            """, new { method, path, contentType, body });
        var result = JsonDocument.Parse(json).RootElement;
        return (result.GetProperty("status").GetInt32(), result.GetProperty("body").Clone());
    }

    public async Task<JsonElement> PostJsonAsync(string path, object body) => Ok(await SendAsync("POST", path, "application/json", JsonSerializer.Serialize(body)), path);
    public async Task<JsonElement> PostAsync(string path, string contentType, string body) => Ok(await SendAsync("POST", path, contentType, body), path);
    public async Task<JsonElement> GetJsonAsync(string path) => Ok(await SendAsync("GET", path), path);

    static JsonElement Ok((int Status, JsonElement Body) result, string path) =>
        result.Status is >= 200 and < 300 ? result.Body : throw new InvalidOperationException($"{path}: HTTP {result.Status} {result.Body}");

    public async ValueTask DisposeAsync() => await context.CloseAsync();
}
