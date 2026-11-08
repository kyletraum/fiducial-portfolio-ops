using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Domain;
using Portfolio.Infrastructure;

namespace Portfolio.Api;

/// <summary>
/// slice-04.md step 4 (FR-6.4): saved reports. Stored through EF on the API's own login; run only on
/// the reporting login, through the same runner, slot and bounds as an ad hoc query.
/// </summary>
static partial class SavedReportEndpoints
{
    const int MaxNameLength = 200, MaxDescriptionLength = 2000, MaxParameters = 20, MaxSaved = 1000;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex ParameterName();

    public static void MapSavedReportApi(this RouteGroupBuilder reports)
    {
        var saved = reports.MapGroup("/saved");
        saved.MapGet("/", List).WithName("ListSavedReports");
        saved.MapPost("/", Create).WithName("CreateSavedReport")
            .ProducesValidationProblem(Endpoints.Unprocessable422)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        saved.MapGet("/{id:guid}", Get).WithName("GetSavedReport");
        saved.MapDelete("/{id:guid}", Delete).WithName("DeleteSavedReport");
        saved.MapPost("/{id:guid}/run", Run).WithName("RunSavedReport")
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem(Endpoints.Unprocessable422)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    static async Task<Ok<List<SavedReportResponse>>> List(PortfolioDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.SavedReports.AsNoTracking().OrderBy(r => r.Name).Take(MaxSaved).ToListAsync(ct)).Select(ToResponse).ToList());

    static async Task<Results<Ok<SavedReportResponse>, NotFound>> Get(Guid id, PortfolioDbContext db, CancellationToken ct) =>
        await db.SavedReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) is { } report
            ? TypedResults.Ok(ToResponse(report))
            : TypedResults.NotFound();

    static async Task<Results<NoContent, NotFound>> Delete(Guid id, PortfolioDbContext db, CancellationToken ct) =>
        await db.SavedReports.Where(r => r.Id == id).ExecuteDeleteAsync(ct) == 1 ? TypedResults.NoContent() : TypedResults.NotFound();

    /// <summary>
    /// <c>201</c> once PostgreSQL has parsed and described the statement on the reporting login with
    /// the declared types (<see cref="ReportRunner.DescribeAsync"/>); <c>422</c> when the request or
    /// the database refuses it; <c>409</c> for a name already used.
    /// </summary>
    static async Task<Results<Created<SavedReportResponse>, ProblemHttpResult>> Create(
        CreateSavedReportRequest request, PortfolioDbContext db, ReportRunner runner, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? "";
        var parameters = request.Parameters ?? [];
        if (name.Length is 0 or > MaxNameLength)
            errors["name"] = [$"Required, at most {MaxNameLength} characters."];
        if (request.Description?.Length > MaxDescriptionLength)
            errors["description"] = [$"At most {MaxDescriptionLength} characters."];
        if (string.IsNullOrWhiteSpace(request.Sql) || request.Sql.Length > ReportEndpoints.MaxSqlLength)
            errors["sql"] = [$"Required, at most {ReportEndpoints.MaxSqlLength} characters."];
        if (parameters.Count > MaxParameters)
            errors["parameters"] = [$"At most {MaxParameters}."];
        else if (parameters.Any(p => p?.Name is null || !ParameterName().IsMatch(p.Name) || !Enum.IsDefined(p.Type)))
            errors["parameters"] = ["Each a name of lower case letters, digits and underscores, starting with a letter, and a type: date, text or numeric."];
        else if (parameters.Select(p => p.Name).Distinct().Count() != parameters.Count)
            errors["parameters"] = ["Each name once."];
        if (errors.Count > 0)
            return Endpoints.Unprocessable(errors);

        var described = await runner.DescribeAsync(request.Sql, [.. parameters.Select(p => ReportParameters.Declare(p.Type))], ct);
        if (described is not ReportOutcome.Completed)
            return ReportEndpoints.Problem(described);

        var report = new SavedReport
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            SqlText = request.Sql,
            Parameters = JsonSerializer.Serialize(parameters, Json),
        };
        db.SavedReports.Add(report);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "A saved report already has that name.", detail: "Delete it first, or choose another name.");
        }
        await db.Entry(report).ReloadAsync(ct); // created_at is the database's
        return TypedResults.Created($"/api/v1/reports/saved/{report.Id}", ToResponse(report));
    }

    /// <summary>
    /// The saved SQL with each value bound to its declared type. A value that is not of its type, or a
    /// parameter missing or unknown, is <c>400</c> before the database (DoD 4). Then as an ad hoc run.
    /// </summary>
    static async Task<Results<Ok<ReportRunResponse>, NotFound, ProblemHttpResult>> Run(
        Guid id, RunSavedReportRequest request, PortfolioDbContext db, ReportRunner runner, CancellationToken ct)
    {
        if (await db.SavedReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) is not { } report)
            return TypedResults.NotFound();

        var declared = Declared(report);
        var values = request.Parameters ?? new Dictionary<string, string?>();
        var errors = new Dictionary<string, string[]>();
        var bound = new List<NpgsqlParameter>();
        foreach (var parameter in declared)
        {
            if (!values.TryGetValue(parameter.Name, out var value))
                errors[parameter.Name] = ["Required."];
            else if (ReportParameters.Bind(parameter.Type, value, out var problem) is { } p)
                bound.Add(p);
            else
                errors[parameter.Name] = [problem!];
        }
        foreach (var unknown in values.Keys.Except(declared.Select(p => p.Name)))
            errors[unknown] = ["Not a parameter of this report."];
        if (errors.Count > 0)
            return TypedResults.Problem(new HttpValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "A parameter value does not match its declared type.",
            });

        var outcome = await runner.RunAsync(report.SqlText, bound, ct);
        return outcome is ReportOutcome.Completed completed
            ? TypedResults.Ok(ReportEndpoints.ToResponse(completed))
            : ReportEndpoints.Problem(outcome);
    }

    static List<SavedReportParameter> Declared(SavedReport report) =>
        JsonSerializer.Deserialize<List<SavedReportParameter>>(report.Parameters, Json) ?? [];

    static SavedReportResponse ToResponse(SavedReport r) =>
        new(r.Id, r.Name, r.Description, r.SqlText, Declared(r), r.CreatedAt);
}
