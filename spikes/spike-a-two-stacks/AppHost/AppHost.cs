// Spike A: can two stacks run at once, and what selects the environment?
// STACK_ENV comes from configuration (command-line args, env var, or a launch
// profile's environmentVariables) and parameterises volume, database and container.
var builder = DistributedApplication.CreateBuilder(args);

var env = builder.Configuration["STACK_ENV"] ?? "unset";
Console.WriteLine($"[spike-a] STACK_ENV={env} DOTNET_ENVIRONMENT={builder.Environment.EnvironmentName} pid={Environment.ProcessId}");

var pg = builder.AddPostgres($"pg-{env}")
    .WithDataVolume($"spike-a-pg-{env}");
pg.AddDatabase($"portfolio-{env}");

builder.Build().Run();
