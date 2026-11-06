using Microsoft.AspNetCore.Http.HttpResults;
using Portfolio.Infrastructure;

namespace Portfolio.Api;

/// <summary>
/// slice-04.md step 3: running a query, and the schema documentation. Saved reports are step 4's.
/// </summary>
/// <remarks>
/// Problem details pass on PostgreSQL's SQLSTATE and message text only, never its Detail or Where,
/// and never a connection string: a failure to connect answers a fixed title (TST-14).
/// </remarks>
static class ReportEndpoints
{
    /// <summary>A bound on the request, not on what a query may do: the database bounds that.</summary>
    public const int MaxSqlLength = 100_000;

    public static void MapReportApi(this RouteGroupBuilder v1)
    {
        var reports = v1.MapGroup("/reports").WithTags("Reports");
        reports.MapPost("/run", Run).WithName("RunReport")
            .ProducesValidationProblem(Endpoints.Unprocessable422)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        reports.MapGet("/schema", Schema).WithName("GetReportSchema")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// <c>200</c> with the result, cut off or not; <c>422</c> when PostgreSQL refuses the statement
    /// (<c>sqlState</c> says why) or a column's type cannot be returned; <c>429</c> while another
    /// run holds the slot; <c>503</c> when the database cannot be reached.
    /// </summary>
    static async Task<Results<Ok<ReportRunResponse>, ProblemHttpResult>> Run(RunReportRequest request, ReportRunner runner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Sql) || request.Sql.Length > MaxSqlLength)
            return Endpoints.Unprocessable(new() { ["sql"] = [$"Required, at most {MaxSqlLength} characters."] });

        return await runner.RunAsync(request.Sql, ct) switch
        {
            ReportOutcome.Completed r => TypedResults.Ok(new ReportRunResponse(
                r.Columns.Select(c => new ReportColumnResponse(c.Name, c.Type)).ToList(),
                r.Rows, r.CutOff is not null, r.CutOff)),
            ReportOutcome.Refused r => TypedResults.Problem(statusCode: Endpoints.Unprocessable422,
                title: "The database refused the query.", detail: r.Message,
                extensions: new Dictionary<string, object?> { ["sqlState"] = r.SqlState }),
            ReportOutcome.UnreadableColumn r => TypedResults.Problem(statusCode: Endpoints.Unprocessable422,
                title: "A column has a type reports cannot return.",
                detail: $"Column \"{r.Column}\" is {r.Type}. Cast it to text, for example \"{r.Column}\"::text."),
            ReportOutcome.Busy => TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: "A report is already running.",
                detail: "One report runs at a time. Run this one when the other has finished."),
            ReportOutcome.Unavailable => Unavailable(),
            _ => throw new InvalidOperationException("An outcome with no response."),
        };
    }

    static async Task<Results<Ok<ReportSchemaResponse>, ProblemHttpResult>> Schema(ReportRunner runner, CancellationToken ct) =>
        await runner.SchemaAsync(ct) is { } views
            ? TypedResults.Ok(new ReportSchemaResponse(views.Select(v => new ReportViewResponse(v.Name, v.Description,
                v.Columns.Select(c => new ReportViewColumnResponse(c.Name, c.Type, c.Description)).ToList())).ToList()))
            : Unavailable();

    static ProblemHttpResult Unavailable() =>
        TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The reporting database is not available.",
            detail: "The query did not complete. Try again in a moment.");
}
