var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Every route the browser calls lives under /api: the Vite dev proxy forwards that
// prefix only, and at publish this process also serves the web app (Spike B).
var api = app.MapGroup("/api");
api.MapGet("health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
