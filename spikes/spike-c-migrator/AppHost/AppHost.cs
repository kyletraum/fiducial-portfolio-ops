// Spike C: does the API wait for a one-shot migrator, and does the wait survive publish?
var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

var pg = builder.AddPostgres("pg");
var db = pg.AddDatabase("portfolio");

var migrator = builder.AddProject<Projects.Migrator>("migrator")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("MIGRATOR_EXIT", builder.Configuration["MIGRATOR_EXIT"] ?? "0");

builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WaitForCompletion(migrator);

builder.Build().Run();

