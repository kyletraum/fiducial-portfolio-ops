// One-shot: apply migrations, then exit. The Api does not start until this exits 0
// (WaitForCompletion; Spike C). A non-zero exit keeps the Api down, which is the point.
// Step 2 adds the migrations - and a connect retry, because in compose this process
// starts on `service_started`, not `service_healthy`.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

using var host = builder.Build();
await host.StartAsync();

var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrator");
log.LogInformation("No migrations yet; exiting 0.");

await host.StopAsync();
return 0;
