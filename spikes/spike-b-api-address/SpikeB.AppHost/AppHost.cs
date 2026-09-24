// Spike B: how does the browser learn the API's address?
// Template wiring (aspire-ts-cs-starter 13.5.4), plus a compose environment for publish.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

var server = builder.AddProject<Projects.SpikeB_Server>("server")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
