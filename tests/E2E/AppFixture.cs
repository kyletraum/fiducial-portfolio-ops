using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// The whole app model - Postgres, Migrator, Api, Vite - started by the test builder on a
/// throwaway database (no volume: AppModelTests), plus a headless Chromium.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    DistributedApplication? app;
    IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;
    public Uri WebUrl { get; private set; } = null!;
    /// <summary>The API's own endpoint - what an attacking page would aim at directly.</summary>
    public Uri ApiUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);

        // Playwright's own browser, installed in-process: no PowerShell script needed (step 1's
        // note - Program.Main is public). A no-op once the browser is present.
        var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installed != 0) throw new InvalidOperationException($"playwright install chromium exited {installed}");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("web", timeout.Token);
        WebUrl = app.GetEndpoint("web", "http");
        ApiUrl = app.GetEndpoint("api", "http");
        await WaitUntilServingAsync(WebUrl, timeout.Token);

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
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
        if (Browser is not null) await Browser.DisposeAsync();
        playwright?.Dispose();
        if (app is not null) await app.DisposeAsync();
    }
}
