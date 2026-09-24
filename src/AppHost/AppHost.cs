// The app model. Postgres -> Migrator (one-shot) -> Api -> web.
// Spike C: the Migrator exits, and the Api waits for it to exit 0.
// Spike B: the browser calls a relative /api; Vite proxies it under `aspire run`,
// and at publish the built web app is served by the Api from wwwroot.
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");

// S-25b: the named volume is OPT-IN. DistributedApplicationTestingBuilder randomises ports and
// nothing else, so an unconditional WithDataVolume would mount the developer's real data into
// every test run. `aspire run` sets Postgres:DataVolume from the launch profile (which it
// always applies - Spike A); the test builder leaves it unset, and tests/E2E asserts that.
// Named explicitly so the data survives the container; `_dev`, not a bare `portfolio`: a PRD
// name must never be a substring of the DEV one (environments.md banner).
if (builder.Configuration["Postgres:DataVolume"] is { Length: > 0 } volume)
    postgres.WithDataVolume(volume);

var db = postgres.AddDatabase("portfolio", databaseName: "portfolio_dev");

var migrator = builder.AddProject<Projects.Migrator>("migrator")
    .WithReference(db)
    .WaitFor(db);

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var web = builder.AddViteApp("web", "../../web")
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
