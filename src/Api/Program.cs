using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.Api;
using Portfolio.Infrastructure;

// WebApplication.CreateBuilder, deliberately: it is what registers host filtering from
// AllowedHosts (SEC-7). CreateSlimBuilder/CreateEmptyBuilder do not.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Portfolio API";
    doc.Info.Description = "Slice 01: manual balance entry to a net-worth chart. Money is a decimal string at scale 4.";
    return Task.CompletedTask;
}));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddDbContext<PortfolioDbContext>(options =>
    PortfolioDbContext.Configure(options, builder.Configuration.GetConnectionString("portfolio")));
builder.EnrichNpgsqlDbContext<PortfolioDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Every route the browser calls lives under /api: the Vite dev proxy forwards that prefix
// only, and at publish this process also serves the web app (Spike B).
app.MapPortfolioApi();

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();

// For WebApplicationFactory in the tests.
public partial class Program;
