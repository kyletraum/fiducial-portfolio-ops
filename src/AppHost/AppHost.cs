// The app model. Postgres -> Migrator (one-shot) -> Api -> web.
// Spike C: the Migrator exits, and the Api waits for it to exit 0.
// Spike B: the browser calls a relative /api; Vite proxies it under `aspire run`,
// and at publish the built web app is served by the Api from wwwroot.
using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

// Step 7: publish target is Docker Compose. No dashboard in the published stack: it would be
// emitted with no host IP (every interface) and restart: always, listed in no document.
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(false);

var postgres = builder.AddPostgres("postgres");

// S-25b: the named volume is OPT-IN in run mode. DistributedApplicationTestingBuilder randomises
// ports and nothing else, so an unconditional WithDataVolume would mount the developer's real
// data into every test run. `aspire run` sets Postgres:DataVolume from the launch profile (which
// it always applies - Spike A); the test builder leaves it unset, and tests/E2E asserts that.
// PUBLISH always gets one: a deployed database must outlive its container.
// Named explicitly; `_dev`, not a bare `portfolio`: a PRD name must never be a substring of the
// DEV one (environments.md banner).
var volume = builder.Configuration["Postgres:DataVolume"] is { Length: > 0 } configured ? configured
    : builder.ExecutionContext.IsPublishMode ? "portfolio-dev-pgdata"
    : null;
if (volume is not null)
    postgres.WithDataVolume(volume);

postgres.PublishAsDockerComposeService((_, service) =>
{
    // The publisher writes no healthcheck, and degrades every WaitFor to service_started
    // (its service_healthy branch is commented out). Without this the Migrator can connect
    // before Postgres accepts connections.
    service.Healthcheck = new()
    {
        Test = ["CMD-SHELL", "pg_isready -U postgres"],
        Interval = "5s",
        Timeout = "3s",
        Retries = 10,
        StartPeriod = "10s",
    };
    service.Restart = "unless-stopped";
});

var db = postgres.AddDatabase("portfolio", databaseName: "portfolio_dev");

var migrator = builder.AddProject<Projects.Migrator>("migrator")
    .WithReference(db)
    .WaitFor(db)
    .PublishAsDockerComposeService((_, service) =>
        // Overwrite the service_started entry the publisher wrote. No restart policy: this is
        // a one-shot, and the Api waits for it to EXIT (service_completed_successfully).
        service.DependsOn["postgres"] = new() { Condition = "service_healthy" });

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .PublishAsDockerComposeService((_, service) =>
    {
        // DoD 3: the Api on LOOPBACK. The publisher writes a bare "${API_PORT}" (no host IP,
        // so Docker binds every interface), and a compose override cannot remove it - `ports`
        // appends. Kestrel still binds 0.0.0.0 inside the container (ASPNETCORE_HTTP_PORTS);
        // loopback is purely a property of this publish string.
        service.Ports = service.Ports.Select(p => $"127.0.0.1:{p}:{p}").ToList();
        service.Restart = "unless-stopped";
    });

var web = builder.AddViteApp("web", "../../web")
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
