// One-shot: wait for the database, apply migrations, exit. The Api does not start until
// this exits 0 (WaitForCompletion; Spike C), so a non-zero exit keeps the Api down - which
// is the point: an Api running against a half-migrated schema is worse than no Api.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portfolio.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddDbContext<PortfolioDbContext>(options =>
    PortfolioDbContext.Configure(options, builder.Configuration.GetConnectionString("portfolio")));
// Aspire's client integration adds health checks, tracing and a retrying execution strategy
// to the context registered above, without owning its provider configuration.
builder.EnrichNpgsqlDbContext<PortfolioDbContext>();

using var host = builder.Build();
await host.StartAsync();

var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrator");
var connectTimeout = builder.Configuration.GetValue("Migrator:ConnectTimeoutSeconds", 60);
var exitCode = 0;

try
{
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

    await WaitForDatabaseAsync(db, TimeSpan.FromSeconds(connectTimeout), log);

    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    log.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, pending);
    await db.Database.MigrateAsync();

    var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
    log.LogInformation("Database at {Latest} ({Count} applied).", applied.LastOrDefault(), applied.Count);
}
catch (Exception ex)
{
    log.LogCritical(ex, "Migration failed; the Api will not start.");
    exitCode = 1;
}

await host.StopAsync();
return exitCode;

// Spike C: in the published compose file this process starts on `service_started`, not
// `service_healthy` - Postgres may not accept connections yet. Under `aspire run` it is
// already healthy, and the first attempt succeeds.
static async Task WaitForDatabaseAsync(PortfolioDbContext db, TimeSpan timeout, ILogger log)
{
    var deadline = DateTime.UtcNow + timeout;
    for (var attempt = 1; ; attempt++)
    {
        if (await db.Database.CanConnectAsync())
        {
            log.LogInformation("Database reachable after {Attempts} attempt(s).", attempt);
            return;
        }
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException($"Database not reachable within {timeout.TotalSeconds:0}s.");

        log.LogWarning("Database not reachable yet (attempt {Attempt}); retrying.", attempt);
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
