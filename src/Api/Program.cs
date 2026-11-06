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

// Slice 04 step 1b (SRC-8): the Api connects only as its own login, so with no connection string it
// refuses to start, rather than meeting the first request with a missing database. The build-time
// OpenAPI tool runs this entry point with no database at all, and is the one exception.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("portfolio"))
    && System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
    throw new InvalidOperationException("ConnectionStrings:portfolio is not set. The Api connects as its own database login and will not start without one.");

builder.Services.AddDbContext<PortfolioDbContext>(options =>
    PortfolioDbContext.Configure(options, builder.Configuration.GetConnectionString("portfolio")));
builder.EnrichNpgsqlDbContext<PortfolioDbContext>();
// Slice 03: Identity with passkeys, whose store is the same context (step 1).
builder.AddPortfolioAuthentication();
builder.Services.AddCsrfTokens();
SignInRateLimit.AddSignInRateLimit(builder.Services, builder.Configuration);
builder.Services.AddScoped<OwnerCommands>();

// Slice 04 step 3: the report runner, on the reporting login. Its data source is KEYED, so it can
// never become EF's connection (SRC-8), and built on first use: the Api starts without a reporting
// string, and only the report routes answer 503 (and log why) if it is missing.
builder.Services.Configure<ReportingLimits>(builder.Configuration.GetSection(ReportingLimits.Section));
builder.Services.AddKeyedSingleton(ReportRunner.DataSourceKey, (_, _) => ReportRunner.CreateDataSource(
    builder.Configuration.GetConnectionString("reporting") is { Length: > 0 } reporting ? reporting
        : throw new InvalidOperationException("ConnectionStrings:reporting is not set. Reports run on the reporting login, and there is none.")));
builder.Services.AddSingleton<ReportRunner>();

var app = builder.Build();

// `docker compose run --rm api revoke-sessions` (or reset-owner): run the command and exit, with no
// web server started (slice-03.md step 7).
if (OwnerCommands.ModeFrom(args) is { } mode)
{
    await using var scope = app.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<OwnerCommands>().RunAsync(mode, Console.Out);
}

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
return 0;

// For WebApplicationFactory in the tests.
public partial class Program;
