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
        await using var db = CreateSuperuserContext();
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

        await using (var db = pg.CreateSuperuserContext())
            await db.GetService<IMigrator>().MigrateAsync(null, Ct); // up to the latest: 4

        // The rows survive with the new columns empty: they were manual, and carried no stamp.
        Assert.Equal(2L, await pg.ScalarAsync<long>("SELECT count(*) FROM account_balance WHERE ingest_run_id IS NULL AND source_as_of IS NULL"));
        Assert.Equal(250m, await pg.ScalarAsync<decimal>("SELECT sum(balance) FROM account_balance"));
        Assert.Equal(150m, await pg.ScalarAsync<decimal>("SELECT net_worth FROM v_net_worth_daily WHERE as_of_date = '2026-03-01'"));

        await using (var db = pg.CreateSuperuserContext())
            await db.GetService<IMigrator>().MigrateAsync("20260924151919_SpineRunsToToday", Ct); // Down
        Assert.Null(await pg.ScalarAsync<string>("SELECT to_regclass('ingest_run')::text"));
        Assert.Equal(0L, await pg.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns WHERE table_name = 'account_balance' AND column_name IN ('ingest_run_id', 'source_as_of')
            """));
        Assert.Equal(150m, await pg.ScalarAsync<decimal>("SELECT net_worth FROM v_net_worth_daily WHERE as_of_date = '2026-03-01'"));

        await using (var db = pg.CreateSuperuserContext())
            await db.GetService<IMigrator>().MigrateAsync(null, Ct); // and up again
        Assert.Equal(2L, await pg.ScalarAsync<long>("SELECT count(*) FROM account_balance"));
    }
}

/// <summary>Stops at migration 5, the last before <c>reporting</c> (slice-04.md step 2).</summary>
public sealed class Migration5Fixture : PostgresFixture
{
    public const string Migration5 = "20261024160637_AuthAndAudit";

    public override async ValueTask InitializeAsync()
    {
        await StartAsync();
        await using var db = CreateSuperuserContext();
        await db.GetService<IMigrator>().MigrateAsync(Migration5);
    }

    /// <summary>The Migrator's order: migrate, then the logins routine (R2-TST-12).</summary>
    public async Task UpAsync()
    {
        await using (var db = CreateSuperuserContext())
            await db.GetService<IMigrator>().MigrateAsync(null);
        await EnsureLoginsAsync();
    }
}

public class ReportingMigrationTests(Migration5Fixture pg) : IClassFixture<Migration5Fixture>
{
    async Task AssertReportingSnapshotAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(pg.SuperuserConnectionString);
        await connection.OpenAsync();
        var actual = await CatalogSnapshot.TakeAsync(connection, Portfolio.Infrastructure.DatabaseLogins.Reporting);
        var expected = CatalogSnapshot.Expected(Portfolio.Infrastructure.DatabaseLogins.Reporting);
        Assert.True(expected.SequenceEqual(actual), CatalogSnapshot.Diff(expected, actual));
    }

    [Fact]
    public async Task Migration_6_goes_up_down_and_up_with_the_logins_routine_after_each_up()
    {
        await pg.UpAsync();
        Assert.Equal(4L, await pg.ScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace = 'reporting'::regnamespace"));
        await AssertReportingSnapshotAsync();

        await using (var db = pg.CreateSuperuserContext())
            await db.GetService<IMigrator>().MigrateAsync(Migration5Fixture.Migration5); // Down
        Assert.Null(await pg.ScalarAsync<string>("SELECT to_regnamespace('reporting')::text"));
        // The chart's view, which reporting.v_net_worth_daily passes through, is untouched by Down.
        Assert.NotNull(await pg.ScalarAsync<string>("SELECT to_regclass('public.v_net_worth_daily')::text"));

        await pg.UpAsync(); // and up again: the views back, and granted again in the same run
        await AssertReportingSnapshotAsync();
    }
}
