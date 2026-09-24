using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Domain;
using Portfolio.Infrastructure;

namespace Portfolio.Api;

static class Endpoints
{
    /// <summary>The source system every balance entered through this API is recorded under.</summary>
    const string ManualSource = "manual";

    const int DefaultBalanceLimit = 100, MaxBalanceLimit = 1000;
    const int DefaultSeriesDays = 365, MaxSeriesDays = 3660;
    const int MaxNameLength = 200;
    const int Unprocessable422 = StatusCodes.Status422UnprocessableEntity;

    public static void MapPortfolioApi(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");

        v1.MapGet("/system/health", () => TypedResults.Ok(new HealthResponse("ok")))
            .WithName("GetHealth");

        var accounts = v1.MapGroup("/accounts").WithTags("Accounts");
        accounts.MapGet("/", ListAccounts).WithName("ListAccounts");
        accounts.MapPost("/", CreateAccount).WithName("CreateAccount").ProducesValidationProblem(Unprocessable422);
        accounts.MapGet("/{id:guid}/balances", ListBalances).WithName("ListBalances").ProducesValidationProblem(Unprocessable422);
        accounts.MapPost("/{id:guid}/balances", RecordBalance).WithName("RecordBalance").ProducesValidationProblem(Unprocessable422);

        v1.MapGet("/net-worth", GetNetWorth).WithName("GetNetWorth").WithTags("Net worth").ProducesValidationProblem(Unprocessable422);
    }

    static async Task<Ok<List<AccountResponse>>> ListAccounts(PortfolioDbContext db, CancellationToken ct)
    {
        var rows = await (
            from a in db.Accounts
            join i in db.Institutions on a.InstitutionId equals i.Id
            where a.DeletedAt == null
            let latest = db.AccountBalances
                .Where(b => b.AccountId == a.Id && b.DeletedAt == null && b.SupersededAt == null)
                .OrderByDescending(b => b.AsOfDate)
                .FirstOrDefault()
            orderby i.Name, a.DisplayName
            select new { a, InstitutionName = i.Name, LatestBalance = (decimal?)latest!.Balance, LatestAsOfDate = (DateOnly?)latest.AsOfDate }
        ).ToListAsync(ct);

        return TypedResults.Ok(rows.Select(r => new AccountResponse(
            r.a.Id, r.InstitutionName, r.a.DisplayName, r.a.Currency, r.a.OpenedOn, r.a.ClosedOn,
            r.LatestBalance is { } b ? MoneyText.Format(b) : null, r.LatestAsOfDate)).ToList());
    }

    static async Task<Results<Created<AccountResponse>, ProblemHttpResult>> CreateAccount(
        CreateAccountRequest request, PortfolioDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var institutionName = request.InstitutionName?.Trim() ?? "";
        var displayName = request.DisplayName?.Trim() ?? "";
        if (institutionName.Length is 0 or > MaxNameLength)
            errors["institutionName"] = [$"Required, at most {MaxNameLength} characters."];
        if (displayName.Length is 0 or > MaxNameLength)
            errors["displayName"] = [$"Required, at most {MaxNameLength} characters."];
        if (errors.Count > 0) return Unprocessable(errors);

        var institution = await db.Institutions
            .FirstOrDefaultAsync(i => i.Name == institutionName && i.DeletedAt == null, ct);
        if (institution is null)
        {
            // Keys are generated here so the account can reference the institution in the same
            // SaveChanges. The column default (gen_random_uuid) serves raw-SQL inserts.
            institution = new Institution { Id = Guid.CreateVersion7(), Name = institutionName };
            db.Institutions.Add(institution);
        }

        var account = new Account
        {
            Id = Guid.CreateVersion7(),
            InstitutionId = institution.Id,
            DisplayName = displayName,
            OpenedOn = request.OpenedOn,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        var response = new AccountResponse(account.Id, institution.Name, account.DisplayName,
            account.Currency, account.OpenedOn, account.ClosedOn, null, null);
        return TypedResults.Created($"/api/v1/accounts/{account.Id}", response);
    }

    static async Task<Results<Ok<List<BalanceResponse>>, NotFound, ProblemHttpResult>> ListBalances(
        Guid id, PortfolioDbContext db, CancellationToken ct, int? limit = null)
    {
        // SE-23: the collection is bounded, always.
        var take = limit ?? DefaultBalanceLimit;
        if (take is < 1 or > MaxBalanceLimit)
            return Unprocessable(new() { ["limit"] = [$"Between 1 and {MaxBalanceLimit}."] });
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.DeletedAt == null, ct))
            return TypedResults.NotFound();

        var rows = await db.AccountBalances
            .Where(b => b.AccountId == id && b.DeletedAt == null && b.SupersededAt == null)
            .OrderByDescending(b => b.AsOfDate)
            .Take(take)
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(ToResponse).ToList());
    }

    static async Task<Results<Created<BalanceResponse>, NotFound, ProblemHttpResult>> RecordBalance(
        Guid id, RecordBalanceRequest request, PortfolioDbContext db, CancellationToken ct)
    {
        if (!MoneyText.TryParse(request.Balance, out var amount, out var error))
            return Unprocessable(new() { ["balance"] = [error!] });
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.DeletedAt == null, ct))
            return TypedResults.NotFound();

        try
        {
            // Provenance is set HERE, never taken from the request (S-27).
            var result = await BalanceRecorder.RecordAsync(
                db, id, request.AsOfDate, amount, ManualSource, SourceStrength.Manual, ct);
            return TypedResults.Created($"/api/v1/accounts/{id}/balances", ToResponse(result.Balance));
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.CheckViolation } pg)
        {
            // The window guards (S-17): a balance before opened_on or after closed_on.
            return Unprocessable(new() { ["asOfDate"] = [pg.MessageText] });
        }
    }

    static async Task<Results<Ok<NetWorthResponse>, ProblemHttpResult>> GetNetWorth(
        PortfolioDbContext db, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = to ?? today;
        var start = from ?? end.AddDays(-DefaultSeriesDays);
        if (start > end)
            return Unprocessable(new() { ["from"] = ["Must not be after 'to'."] });
        if (end.DayNumber - start.DayNumber > MaxSeriesDays)
            return Unprocessable(new() { ["from"] = [$"At most {MaxSeriesDays} days per request."] });

        var staleness = await db.AppSettings.Select(s => s.BalanceStalenessDays).SingleAsync(ct);
        var days = await NetWorthDay.Query(db, start, end).OrderBy(d => d.AsOfDate).ToListAsync(ct);

        var points = days.Select(d => new NetWorthPoint(
            d.AsOfDate,
            d.NetWorth is { } n ? MoneyText.Format(n) : null,
            d.AccountsVerified == 0 ? NetWorthStatus.Unverified
                : d.AccountsUnverified > 0 ? NetWorthStatus.Partial
                : NetWorthStatus.Verified,
            d.AccountsInWindow, d.AccountsVerified, d.AccountsUnverified, d.AccountsCarried,
            d.MaxStalenessDays)).ToList();

        return TypedResults.Ok(new NetWorthResponse(start, end, staleness, points));
    }

    static BalanceResponse ToResponse(AccountBalance b) => new(
        b.Id, b.AccountId, b.AsOfDate, MoneyText.Format(b.Balance), b.Currency,
        b.SourceSystem, b.SourceStrength, b.ObservedAt, b.Supersedes);

    /// <summary>R2-M8: a well-formed request whose content is refused is 422, as RFC 9457.</summary>
    static ProblemHttpResult Unprocessable(Dictionary<string, string[]> errors) =>
        TypedResults.Problem(new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "The request was understood but refused.",
        });
}
