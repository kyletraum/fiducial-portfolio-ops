using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;

namespace Portfolio.Infrastructure;

/// <summary>
/// One balance through <see cref="BalanceWrites"/>: restates any live row for the same
/// (account, date, source), and since slice 02 writes NOTHING when that row already holds the
/// same amount (the D-030 Amendment 1 deferral). The key is the content - a retried request
/// repeats it on its own, so no Idempotency-Key header is needed.
/// </summary>
/// <remarks>
/// The context runs under a retrying execution strategy (Aspire's client integration), which
/// refuses a user-initiated transaction unless the whole unit runs inside the strategy, so a
/// retry replays the transaction rather than half of it.
/// </remarks>
public static class BalanceRecorder
{
    /// <param name="Balance">The live row afterwards: the new one, or the existing one if unchanged.</param>
    /// <param name="Restated">The row this one superseded, if any.</param>
    /// <param name="Unchanged">True when the same amount was already live and nothing was written.</param>
    public sealed record Result(AccountBalance Balance, Guid? Restated, bool Unchanged);

    public static Task<Result> RecordAsync(
        PortfolioDbContext db, Guid accountId, DateOnly asOfDate, decimal amount,
        string sourceSystem, SourceStrength strength, CancellationToken ct = default) =>
        RecordAsync(db, TimeProvider.System, accountId, asOfDate, amount, sourceSystem, strength, ct);

    public static Task<Result> RecordAsync(
        PortfolioDbContext db, TimeProvider clock, Guid accountId, DateOnly asOfDate, decimal amount,
        string sourceSystem, SourceStrength strength, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var plan = await BalanceWrites.PlanAsync(db,
                [new(accountId, asOfDate, amount, sourceSystem, strength, SourceAsOf: null)], ct);
            var written = await BalanceWrites.ApplyAsync(db, plan, ingestRunId: null, clock.GetUtcNow(), ct);

            await tx.CommitAsync(ct);
            var p = plan[0];
            return new Result(written[0], p.Outcome == BalanceWrites.Outcome.Restated ? p.Live!.Id : null,
                p.Outcome == BalanceWrites.Outcome.Unchanged);
        });
    }
}
