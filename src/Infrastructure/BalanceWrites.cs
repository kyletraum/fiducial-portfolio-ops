using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;

namespace Portfolio.Infrastructure;

/// <summary>
/// The ONE restatement rule, for a manual entry and an import alike (slice-02.md step 2): for
/// each (account, date, source), <b>unchanged</b> if a live row holds the same amount - nothing
/// is written, <b>restated</b> if it holds a different one, <b>created</b> if there is none.
/// </summary>
/// <remarks>
/// Runs inside the caller's transaction. M-9: the partial unique index
/// <c>account_balance_live_uq</c> is checked per statement, so EVERY retirement is written
/// before ANY insert - an UPDATE of the old rows' <c>superseded_at</c>, then the new rows
/// pointing back at them. Reversed, two rows are live for an instant and the insert is refused
/// (Spike D, 05).
/// </remarks>
static class BalanceWrites
{
    public sealed record Request(
        Guid AccountId, DateOnly AsOfDate, decimal Amount, string SourceSystem, SourceStrength Strength, DateTimeOffset? SourceAsOf);

    public enum Outcome { Created, Restated, Unchanged }

    /// <param name="Live">The live row this request found, if any. For Unchanged, it IS the result.</param>
    public sealed record Planned(Request Request, Outcome Outcome, AccountBalance? Live);

    public static async Task<List<Planned>> PlanAsync(PortfolioDbContext db, IReadOnlyList<Request> requests, CancellationToken ct)
    {
        var accounts = requests.Select(r => r.AccountId).Distinct().ToList();
        var dates = requests.Select(r => r.AsOfDate).Distinct().ToList();
        var sources = requests.Select(r => r.SourceSystem).Distinct().ToList();

        // A superset, narrowed in memory: three IN lists rather than one tuple per request.
        var live = (await db.AccountBalances
                .Where(b => accounts.Contains(b.AccountId) && dates.Contains(b.AsOfDate) && sources.Contains(b.SourceSystem)
                         && b.DeletedAt == null && b.SupersededAt == null)
                .ToListAsync(ct))
            .ToDictionary(b => (b.AccountId, b.AsOfDate, b.SourceSystem));

        return requests.Select(r =>
        {
            var found = live.GetValueOrDefault((r.AccountId, r.AsOfDate, r.SourceSystem));
            var outcome = found is null ? Outcome.Created
                : found.Balance == Normalise(r.Amount) ? Outcome.Unchanged
                : Outcome.Restated;
            return new Planned(r, outcome, found);
        }).ToList();
    }

    /// <summary>Writes the plan. Returns, per request in order, the row that is live afterwards.</summary>
    public static async Task<List<AccountBalance>> ApplyAsync(
        PortfolioDbContext db, IReadOnlyList<Planned> plan, Guid? ingestRunId, DateTimeOffset now, CancellationToken ct)
    {
        // 1. Retire every row being restated - one statement, before any insert.
        var retiring = plan.Where(p => p.Outcome == Outcome.Restated).Select(p => p.Live!.Id).ToList();
        if (retiring.Count > 0)
            await db.AccountBalances.Where(b => retiring.Contains(b.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.SupersededAt, now), ct);

        // 2. Then the new rows, each restatement pointing BACK at the row it replaces.
        var result = new List<AccountBalance>(plan.Count);
        foreach (var p in plan)
        {
            if (p.Outcome == Outcome.Unchanged)
            {
                result.Add(p.Live!);
                continue;
            }
            var balance = new AccountBalance
            {
                AccountId = p.Request.AccountId,
                AsOfDate = p.Request.AsOfDate,
                Balance = Normalise(p.Request.Amount),
                Currency = ReportingCurrency.Code,
                SourceSystem = p.Request.SourceSystem,
                SourceStrength = p.Request.Strength,
                SourceAsOf = p.Request.SourceAsOf?.ToUniversalTime(),
                ObservedAt = now,
                IngestRunId = ingestRunId,
                Supersedes = p.Live?.Id,
            };
            db.AccountBalances.Add(balance);
            result.Add(balance);
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>numeric(19,4), rounded the way PostgreSQL rounds (S-24), so equality is the database's.</summary>
    static decimal Normalise(decimal amount) => new Money(amount, ReportingCurrency.Code).Amount;
}
