// The app model. Postgres -> Migrator (one-shot) -> Api -> web.
// Spike C: the Migrator exits, and the Api waits for it to exit 0.
// Spike B: the browser calls a relative /api; Vite proxies it under `aspire run`,
// and at publish the built web app is served by the Api from wwwroot.
var builder = DistributedApplication.CreateBuilder(args);

// Named explicitly so the data survives the container (SOURCE@microsoft/aspire@b477bdd:
// WithDataVolume(name) takes the name). `_dev`, not a bare `portfolio`: a PRD name must
// never be a substring of the DEV one (environments.md banner).
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("portfolio-dev-pgdata");
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
