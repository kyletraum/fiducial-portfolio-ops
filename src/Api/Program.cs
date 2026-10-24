using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Api;
using Portfolio.Infrastructure;

// WebApplication.CreateBuilder, deliberately: it is what registers host filtering from
// AllowedHosts (SEC-7). CreateSlimBuilder/CreateEmptyBuilder do not.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Slice 03 step 5 (DoD 7): the request-start and -finish lines carry the full query string, and
// a secret pasted into a URL would be written down. Off below Warning whatever configuration
// says: a filter added here comes after configuration's and wins at the same category. Each
// request is still a span, whose url.query OpenTelemetry already redacts.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddProblemDetails();

// One clock, injected, so tests can pin it (slice-02.md "Dates and time"). The data layer's
// "now" is UTC: TimeProvider.GetUtcNow() is offset zero, which Npgsql requires for timestamptz.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Portfolio API";
    doc.Info.Description = "Manual balance entry and file import (CSV, OFX) to a net-worth chart. Money is a decimal string at scale 4.";
    return Task.CompletedTask;
}));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddDbContext<PortfolioDbContext>(options =>
    PortfolioDbContext.Configure(options, builder.Configuration.GetConnectionString("portfolio")));
builder.EnrichNpgsqlDbContext<PortfolioDbContext>();
// Slice 03: Identity with passkeys, whose store is the same context (step 1).
builder.AddPortfolioAuthentication();
builder.Services.AddCsrfTokens();
SignInRateLimit.AddSignInRateLimit(builder.Services, builder.Configuration);

var app = builder.Build();

// The order is slice-03.md step 3's, and each place is load-bearing (round 2 SRC-1, TST-5, TST-9):
// - The headers first, so every later response carries them, refusals and errors included.
// - The cross-site refusal before anything reads the request.
// - Static files BEFORE authentication: a fallback policy (step 4) also covers a file served after
//   it, and the page would answer 401.
// - Authentication and authorization called explicitly. Otherwise WebApplication inserts them ahead
//   of every middleware here, and a 401 would leave without the headers.
// Host filtering wraps all of this: its 400 refuses before any of it runs, and carries no headers.
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCrossSiteWriteRefusal();
app.UseFileServer();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Every route the browser calls lives under /api: the Vite dev proxy forwards that prefix
// only, and at publish this process also serves the web app (Spike B).
app.MapPortfolioApi();

app.MapDefaultEndpoints();

app.Run();

// For WebApplicationFactory in the tests.
public partial class Program;
