// Spike F: how large a file gets through, and what refuses it?
//
// Five endpoints, each one way of taking an upload. They answer only with what they saw;
// run.sh drives them with curl, directly and through the Vite dev proxy, and records
// status codes. No file is kept: every handler reads the bytes and drops them.

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The slice's proposed limit, and the body allowance for multipart overhead (boundaries and
// part headers wrap the file, so a body limit equal to the file limit refuses a file AT it).
const long FileLimit = 1 << 20;             // 1 MiB
const long BodyLimit = FileLimit + (16 << 10); // + 16 KiB

// Counts ASP.NET's own buffer files in the temp directory WHILE the request is being handled.
// The form reader spills a file part above FormOptions.MemoryBufferThreshold (64 KiB by
// default) to a temp file; this makes that observable rather than recalled.
static int SpillFiles() =>
    Directory.EnumerateFiles(Path.GetTempPath(), "ASPNETCORE_*.tmp").Count();

// A. IFormFile, no antiforgery decision at all: the claim says this fails at request time.
app.MapPost("/api/a-iformfile", (IFormFile file) =>
    Results.Ok(new { file.Length, spill = SpillFiles() }));

// B. IFormFile with antiforgery explicitly disabled, and no size metadata: Kestrel's default.
app.MapPost("/api/b-iformfile-noaf", (IFormFile file) =>
    Results.Ok(new { file.Length, spill = SpillFiles() }))
    .DisableAntiforgery();

// C. B plus a per-endpoint body limit (IRequestSizeLimitMetadata) and a file-length check.
app.MapPost("/api/c-iformfile-limited", (IFormFile file) =>
    file.Length > FileLimit
        ? Results.Problem(statusCode: 413, title: $"file is {file.Length} bytes; the limit is {FileLimit}")
        : Results.Ok(new { file.Length, spill = SpillFiles() }))
    .DisableAntiforgery()
    .WithMetadata(new RequestSizeLimitAttribute(BodyLimit));

// D. The raw body: the file IS the request body. No form reader, so no spill and no
// multipart overhead - the body limit is the file limit. The content type is checked, which
// is also what makes a cross-site form post impossible (see RESULTS.md).
app.MapPost("/api/d-raw", async (HttpRequest req, CancellationToken ct) =>
{
    if (req.ContentType is not ("text/csv" or "application/x-ofx"))
        return Results.Problem(statusCode: 415, title: $"content type '{req.ContentType}' refused");
    using var buffer = new MemoryStream();
    try
    {
        await req.Body.CopyToAsync(buffer, ct);
    }
    catch (BadHttpRequestException e) when (e.StatusCode == 413)
    {
        return Results.Problem(statusCode: 413, title: e.Message);
    }
    return Results.Ok(new { Length = buffer.Length, spill = SpillFiles() });
})
.WithMetadata(new RequestSizeLimitAttribute(FileLimit));

// E. Kestrel's server-wide default, raw: where does the default limit actually sit?
app.MapPost("/api/e-raw-default", async (HttpRequest req, CancellationToken ct) =>
{
    long n = 0;
    var chunk = new byte[81920];
    int read;
    try
    {
        while ((read = await req.Body.ReadAsync(chunk, ct)) > 0) n += read;
    }
    catch (BadHttpRequestException e)
    {
        return Results.Problem(statusCode: e.StatusCode, title: e.Message);
    }
    var limit = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
    return Results.Ok(new { Length = n, limit });
});

app.Run();
