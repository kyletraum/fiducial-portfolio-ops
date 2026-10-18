using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Portfolio.Integration;

/// <summary>
/// slice-02.md step 2: migration 4 is "applied to a database that already has rows, as migration
/// 2 was" - which no other fixture does: they all migrate an empty database. This one stops at
/// migration 3, writes slice-01-shaped rows, then goes up to 4, down to 3 and up again.
/// </summary>
public sealed class Migration3Fixture : PostgresFixture
{
    public override async ValueTask InitializeAsync()
    {
        await StartAsync();
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20260924151919_SpineRunsToToday");
    }
}

public class MigrationOnRowsTests(Migration3Fixture pg) : IClassFixture<Migration3Fixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_4_applies_to_existing_rows_and_reverses_cleanly()
    {
        await pg.ExecuteAsync("""
            INSERT INTO institution (id, name) VALUES ('30000000-0000-4000-8000-000000000001', 'Migration Test Bank');
            INSERT INTO account (id, institution_id, display_name, currency, opened_on) VALUES
              ('30000000-0000-4000-8000-00000000000a', '30000000-0000-4000-8000-000000000001', 'Before', 'USD', '2026-01-01');
            INSERT INTO account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at) VALUES
              ('30000000-0000-4000-8000-00000000000a', '2026-02-01', 100, 'USD', 'manual', 'manual', now()),
              ('30000000-0000-4000-8000-00000000000a', '2026-03-01', 150, 'USD', 'manual', 'manual', now());
            """);

        await using (var db = pg.CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(null, Ct); // up to the latest: 4

        // The rows survive with the new columns empty: they were manual, and carried no stamp.
        Assert.Equal(2L, await pg.ScalarAsync<long>("SELECT count(*) FROM account_balance WHERE ingest_run_id IS NULL AND source_as_of IS NULL"));
        Assert.Equal(250m, await pg.ScalarAsync<decimal>("SELECT sum(balance) FROM account_balance"));
        Assert.Equal(150m, await pg.ScalarAsync<decimal>("SELECT net_worth FROM v_net_worth_daily WHERE as_of_date = '2026-03-01'"));

        await using (var db = pg.CreateContext())
            await db.GetService<IMigrator>().MigrateAsync("20260924151919_SpineRunsToToday", Ct); // Down
        Assert.Null(await pg.ScalarAsync<string>("SELECT to_regclass('ingest_run')::text"));
        Assert.Equal(0L, await pg.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns WHERE table_name = 'account_balance' AND column_name IN ('ingest_run_id', 'source_as_of')
            """));
        Assert.Equal(150m, await pg.ScalarAsync<decimal>("SELECT net_worth FROM v_net_worth_daily WHERE as_of_date = '2026-03-01'"));

        await using (var db = pg.CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(null, Ct); // and up again
        Assert.Equal(2L, await pg.ScalarAsync<long>("SELECT count(*) FROM account_balance"));
    }
}
