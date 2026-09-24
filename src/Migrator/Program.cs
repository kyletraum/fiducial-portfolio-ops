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

// Waits for the SERVER, not the database. Spike C: in compose this process may start before
// Postgres accepts connections (step 7 now adds a healthcheck, but the wait stays - it is the
// Migrator's own guarantee). And in compose NOTHING creates the database: Aspire's AddDatabase
// creates it only under `aspire run`, and the image creates only POSTGRES_DB. So "database does
// not exist" (3D000) means the server is up; MigrateAsync then creates the database.
// CanConnectAsync cannot be used here: it returns false for both cases, and the first version
// of this loop retried a missing database for 60s and then reported it as "not reachable".
static async Task WaitForDatabaseAsync(PortfolioDbContext db, TimeSpan timeout, ILogger log)
{
    var deadline = DateTime.UtcNow + timeout;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
            log.LogInformation("Database reachable after {Attempts} attempt(s).", attempt);
            return;
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == Npgsql.PostgresErrorCodes.InvalidCatalogName)
        {
            log.LogInformation("Server reachable after {Attempts} attempt(s); the database does not exist yet, and migrating will create it.", attempt);
            return;
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or System.Net.Sockets.SocketException or TimeoutException
                                  && DateTime.UtcNow < deadline)
        {
            log.LogWarning("Server not reachable yet (attempt {Attempt}: {Reason}); retrying.", attempt, e.GetBaseException().Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
