using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Portfolio.Domain;
using Portfolio.Infrastructure;
using Portfolio.Ingestion;

namespace Portfolio.Api;

/// <summary>slice-02.md step 3: bind a file's identifier to an account; upload a file; list runs.</summary>
static class ImportEndpoints
{
    const int DefaultRunLimit = 50, MaxRunLimit = 500;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapImportApi(this RouteGroupBuilder v1, RouteGroupBuilder accounts)
    {
        accounts.MapPost("/{id:guid}/sources", BindSource).WithName("BindSource")
            .ProducesValidationProblem(Endpoints.Unprocessable422).ProducesProblem(StatusCodes.Status409Conflict);
        accounts.MapGet("/{id:guid}/sources", ListSources).WithName("ListSources");

        var imports = v1.MapGroup("/imports").WithTags("Imports");
        // Spike F: the file IS the request body - no multipart form, so no form reader spilling it
        // to a temp file and no antiforgery to disable. Kestrel refuses a body over the limit
        // before the handler reads it; the handler also reads with its own bound, so the limit
        // holds on any server, including the in-memory one the tests use.
        imports.MapPost("/", Import).WithName("ImportFile")
            // Describes the body to OpenAPI, AND routing enforces it: a Content-Type other than
            // these is 415 before the handler runs (shown 2026-10-18 by disabling the handler's
            // own check - only a request with NO Content-Type then got through). The handler's
            // check is what refuses that one.
            .Accepts<Stream>(ContentTypes.Csv, ContentTypes.Ofx)
            .WithMetadata(new RequestSizeLimitAttribute(ImportPipeline.MaxFileBytes))
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesValidationProblem(Endpoints.Unprocessable422);
        imports.MapGet("/", ListRuns).WithName("ListImports").ProducesValidationProblem(Endpoints.Unprocessable422);
        imports.MapGet("/{id:guid}", GetRun).WithName("GetImport");
    }

    public static class ContentTypes
    {
        public const string Csv = "text/csv";
        public const string Ofx = "application/x-ofx";
    }

    // ---- Bindings ----

    static async Task<Results<Created<SourceResponse>, NotFound, ProblemHttpResult>> BindSource(
        Guid id, BindSourceRequest request, PortfolioDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!SourceIds.ImportSystems.Contains(request.SourceSystem))
            errors["sourceSystem"] = [$"One of: {string.Join(", ", SourceIds.ImportSystems)}."];
        if (SourceIds.Problem(request.SourceId ?? "") is { } problem)
            errors["sourceId"] = [$"The source id {problem}."];
        if (errors.Count > 0) return Endpoints.Unprocessable(errors);
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.DeletedAt == null, ct))
            return TypedResults.NotFound();

        var binding = new AccountSource { Id = Guid.CreateVersion7(), AccountId = id, SourceSystem = request.SourceSystem, SourceId = request.SourceId! };
        db.AccountSources.Add(binding);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // account_source_identity is unique across ALL rows: an id once seen is never
            // re-bindable, to this account or another.
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "That source id is already bound.",
                detail: $"'{request.SourceId}' under {request.SourceSystem} is bound to an account already, and a binding is never moved.");
        }
        await db.Entry(binding).ReloadAsync(ct); // created_at is the database's
        return TypedResults.Created($"/api/v1/accounts/{id}/sources", ToResponse(binding));
    }

    static async Task<Results<Ok<List<SourceResponse>>, NotFound>> ListSources(Guid id, PortfolioDbContext db, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.DeletedAt == null, ct))
            return TypedResults.NotFound();
        var rows = await db.AccountSources.Where(s => s.AccountId == id)
            .OrderBy(s => s.SourceSystem).ThenBy(s => s.SourceId).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToResponse).ToList());
    }

    static SourceResponse ToResponse(AccountSource s) => new(s.AccountId, s.SourceSystem, s.SourceId, s.CreatedAt);

    // ---- Imports ----

    /// <summary>
    /// <c>201</c> with the run whether it succeeded or failed - the run is the resource, and a
    /// failed run is a recorded fact. <c>413</c>, <c>415</c> and <c>422</c> only for what is
    /// refused before a run begins.
    /// </summary>
    static async Task<Results<Created<ImportRunResponse>, ProblemHttpResult>> Import(
        HttpRequest request, PortfolioDbContext db, TimeProvider clock, CancellationToken ct, string? fileName = null)
    {
        IBalanceSource? source = MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            ? type.MediaType.Value?.ToLowerInvariant() switch
            {
                ContentTypes.Csv => new CsvBalanceSource(),
                ContentTypes.Ofx => new OfxBalanceSource(),
                _ => null,
            }
            : null;
        // Also the cross-site defence (slice-02.md "Cross-site writes"): a plain HTML form can
        // send none of these types, and a cross-origin fetch of them needs a preflight this API
        // never answers.
        if (source is null)
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "The file must be sent as text/csv or application/x-ofx.",
                detail: $"Content-Type '{request.ContentType}' is not accepted; the file is the request body, not a form.");

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > ImportPipeline.MaxFileNameLength || fileName.Any(char.IsControl))
            return Endpoints.Unprocessable(new() { ["fileName"] = [$"Required, at most {ImportPipeline.MaxFileNameLength} characters, no control characters."] });

        if (request.ContentLength > ImportPipeline.MaxFileBytes)
            return TooLarge();
        byte[] content;
        try
        {
            content = await ReadBoundedAsync(request.Body, ImportPipeline.MaxFileBytes, ct);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(); // Kestrel's own limit, from the endpoint's RequestSizeLimit
        }
        catch (InvalidDataException)
        {
            return TooLarge(); // ours: a body with no Content-Length that runs past the bound
        }

        var outcome = await new ImportPipeline(db, clock).RunAsync(source, fileName, content, ct);
        return TypedResults.Created($"/api/v1/imports/{outcome.Run.Id}", ToResponse(outcome.Run, outcome.Errors));
    }

    static ProblemHttpResult TooLarge() =>
        TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
            title: $"The file is larger than {ImportPipeline.MaxFileBytes / 1024 / 1024} MiB.",
            detail: $"At most {ImportPipeline.MaxFileBytes} bytes are accepted.");

    /// <summary>Reads at most <paramref name="limit"/> bytes into memory - never a temp file.</summary>
    static async Task<byte[]> ReadBoundedAsync(Stream body, int limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException("over the limit");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    static async Task<Results<Ok<List<ImportRunResponse>>, ProblemHttpResult>> ListRuns(
        PortfolioDbContext db, CancellationToken ct, int? limit = null)
    {
        // SE-23: the collection is bounded, always.
        var take = limit ?? DefaultRunLimit;
        if (take is < 1 or > MaxRunLimit)
            return Endpoints.Unprocessable(new() { ["limit"] = [$"Between 1 and {MaxRunLimit}."] });
        var runs = await db.IngestRuns.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Take(take).ToListAsync(ct);
        return TypedResults.Ok(runs.Select(r => ToResponse(r, StoredErrors(r))).ToList());
    }

    static async Task<Results<Ok<ImportRunResponse>, NotFound>> GetRun(Guid id, PortfolioDbContext db, CancellationToken ct) =>
        await db.IngestRuns.SingleOrDefaultAsync(r => r.Id == id, ct) is { } run
            ? TypedResults.Ok(ToResponse(run, StoredErrors(run)))
            : TypedResults.NotFound();

    static IReadOnlyList<ParseError> StoredErrors(IngestRun run) =>
        run.ErrorDetail is null ? [] : JsonSerializer.Deserialize<List<ParseError>>(run.ErrorDetail, Json) ?? [];

    static ImportRunResponse ToResponse(IngestRun r, IReadOnlyList<ParseError> errors) => new(
        r.Id, r.SourceSystem, r.FileName, r.FileSha256, r.StartedAt, r.FinishedAt,
        r.Status == IngestRunStatus.Succeeded ? ImportStatus.Succeeded : ImportStatus.Failed,
        r.RowsRead, r.Created, r.Restated, r.Unchanged,
        errors.Select(e => new ImportErrorResponse(e.Location, e.Reason)).ToList());
}
