using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;

namespace Portfolio.Infrastructure;

/// <summary>
/// Writes a balance, restating any live row for the same (account, date, source).
/// </summary>
/// <remarks>
/// M-9: the partial unique index <c>account_balance_live_uq</c> is checked per statement,
/// so the order is fixed - UPDATE the old row's <c>superseded_at</c>, THEN insert the new
/// row pointing back at it. Reversed, both rows are live for an instant and the insert is
/// refused (Spike D, 05). So this is an explicit transaction with <c>ExecuteUpdate</c> then
/// <c>Add</c>, not one <c>SaveChanges</c> whose statement order EF chooses.
/// <para>
/// The context runs under a retrying execution strategy (Aspire's client integration), which
/// refuses a user-initiated transaction unless the whole unit runs inside the strategy, so a
/// retry replays the transaction rather than half of it.
/// </para>
/// </remarks>
public static class BalanceRecorder
{
    public sealed record Result(AccountBalance Balance, Guid? Restated);

    public static Task<Result> RecordAsync(
        PortfolioDbContext db, Guid accountId, DateOnly asOfDate, decimal amount,
        string sourceSystem, SourceStrength strength, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;

            var live = await db.AccountBalances
                .Where(b => b.AccountId == accountId && b.AsOfDate == asOfDate
                         && b.SourceSystem == sourceSystem
                         && b.DeletedAt == null && b.SupersededAt == null)
                .Select(b => (Guid?)b.Id)
                .SingleOrDefaultAsync(ct);

            // 1. Retire the old row first - no forward reference, and it leaves the index.
            if (live is { } oldId)
                await db.AccountBalances.Where(b => b.Id == oldId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.SupersededAt, now), ct);

            // 2. Then the new row, pointing BACK at the one it replaces.
            var balance = new AccountBalance
            {
                AccountId = accountId,
                AsOfDate = asOfDate,
                Balance = new Money(amount, ReportingCurrency.Code).Amount,
                Currency = ReportingCurrency.Code,
                SourceSystem = sourceSystem,
                SourceStrength = strength,
                ObservedAt = now,
                Supersedes = live,
            };
            db.AccountBalances.Add(balance);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return new Result(balance, live);
        });
    }
}
