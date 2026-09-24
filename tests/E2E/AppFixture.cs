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

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        playwright?.Dispose();
        if (app is not null) await app.DisposeAsync();
    }
}
