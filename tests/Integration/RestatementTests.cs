using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;
using Portfolio.Infrastructure;

namespace Portfolio.Integration;

/// <summary>
/// slice-01 step 5, the integration test: restate a balance against real PostgreSQL, and
/// assert the old row is superseded and the partial unique index does not reject the insert.
/// </summary>
public class RestatementTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    static readonly DateOnly Day = new(2026, 3, 10);
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    async Task<Guid> NewAccountAsync()
    {
        await using var db = pg.CreateContext();
        var institution = new Institution { Id = Guid.CreateVersion7(), Name = $"Test Bank {Guid.NewGuid():N}" };
        var account = new Account
        {
            Id = Guid.CreateVersion7(), InstitutionId = institution.Id, DisplayName = "Checking", OpenedOn = new(2026, 1, 1),
        };
        db.AddRange(institution, account);
        await db.SaveChangesAsync(Ct);
        return account.Id;
    }

    [Fact]
    public async Task Restating_supersedes_the_old_row_and_the_index_accepts_the_new_one()
    {
        var accountId = await NewAccountAsync();

        Guid firstId, secondId;
        await using (var db = pg.CreateContext())
            firstId = (await BalanceRecorder.RecordAsync(db, accountId, Day, 100m, "manual", SourceStrength.Manual, Ct)).Balance.Id;

        BalanceRecorder.Result second;
        await using (var db = pg.CreateContext())
            second = await BalanceRecorder.RecordAsync(db, accountId, Day, 110m, "manual", SourceStrength.Manual, Ct);
        secondId = second.Balance.Id;

        await using var read = pg.CreateContext();
        var rows = await read.AccountBalances.Where(b => b.AccountId == accountId).ToListAsync(Ct);
        var old = rows.Single(b => b.Id == firstId);
        var @new = rows.Single(b => b.Id == secondId);

        Assert.Equal(2, rows.Count);
        Assert.NotNull(old.SupersededAt);                // the old row is retired, not edited...
        Assert.Equal(100.0000m, old.Balance);            // ...and keeps its value as history
        Assert.Null(@new.SupersededAt);
        Assert.Equal(firstId, @new.Supersedes);          // the pointer is BACKWARD
        Assert.Equal(firstId, second.Restated);
        Assert.Single(rows, b => b.SupersededAt == null && b.DeletedAt == null);
    }

    [Fact]
    public async Task The_reversed_order_is_what_the_index_refuses()
    {
        // Why BalanceRecorder's order matters: INSERT-then-UPDATE leaves two live rows for an
        // instant, and account_balance_live_uq is checked per statement (Spike D, 05).
        var accountId = await NewAccountAsync();
        await using (var db = pg.CreateContext())
            await BalanceRecorder.RecordAsync(db, accountId, Day, 100m, "manual", SourceStrength.Manual, Ct);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => pg.ExecuteAsync($"""
            INSERT INTO account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
            VALUES ('{accountId}', '{Day:yyyy-MM-dd}', 120, 'USD', 'manual', 'manual', now())
            """));

        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("account_balance_live_uq", error.ConstraintName);
    }

    [Fact]
    public async Task Money_columns_are_numeric_19_4()
    {
        // R2-M7: the precision is asserted against the database, not read off the model.
        var type = await pg.ScalarAsync<string>("""
            SELECT format_type(atttypid, atttypmod) FROM pg_attribute
             WHERE attrelid = 'account_balance'::regclass AND attname = 'balance'
            """);

        Assert.Equal("numeric(19,4)", type);
    }
}
