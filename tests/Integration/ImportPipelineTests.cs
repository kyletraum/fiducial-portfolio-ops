using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Domain;
using Portfolio.Infrastructure;
using Portfolio.Ingestion;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-02.md step 2, against real PostgreSQL migrated by the real migrations: the pipeline,
/// and the guarantees migration 4 puts in the database itself.
/// </summary>
public class ImportPipelineTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    static readonly DateOnly Opened = new(2026, 1, 1);
    static readonly CsvBalanceSource CsvSource = new();

    async Task<List<AccountBalance>> RowsAsync(Guid accountId)
    {
        await using var db = pg.CreateContext();
        return await db.AccountBalances.Where(b => b.AccountId == accountId).OrderBy(b => b.AsOfDate).ThenBy(b => b.CreatedAt).ToListAsync(Ct);
    }

    [Fact]
    public async Task Imports_a_file_with_provenance_and_records_the_run()
    {
        var account = await NewAccountAsync(pg, Opened);
        var id = await BindAsync(pg, account, CsvBalanceSource.System);

        var outcome = await ImportAsync(pg, CsvSource, Csv(( id, "2026-10-01", "100.00"), (id, "2026-10-15", "150.25")));

        var run = outcome.Run;
        Assert.Empty(outcome.Errors);
        Assert.Equal((IngestRunStatus.Succeeded, 2, 2, 0, 0), (run.Status, run.RowsRead, run.Created, run.Restated, run.Unchanged));
        Assert.Matches("^[0-9a-f]{64}$", run.FileSha256);
        Assert.Equal("balances.csv", run.FileName);
        Assert.Null(run.ErrorDetail);

        var rows = await RowsAsync(account);
        Assert.Equal([100.00m, 150.25m], rows.Select(r => r.Balance));
        Assert.All(rows, r =>
        {
            Assert.Equal(("import:csv", SourceStrength.Export, run.Id), (r.SourceSystem, r.SourceStrength, r.IngestRunId));
            Assert.Null(r.SourceAsOf); // a CSV carries no time
            Assert.Null(r.SupersededAt);
        });
    }

    [Fact]
    public async Task The_same_file_twice_is_a_second_run_that_changes_nothing()
    {
        // FR-4.2: no duplicates, no altered counts. And the hash matching is NOT a shortcut -
        // the second run's outcome comes from the data.
        var account = await NewAccountAsync(pg, Opened);
        var id = await BindAsync(pg, account, CsvBalanceSource.System);
        var file = Csv((id, "2026-10-01", "100"), (id, "2026-10-15", "150"));

        var first = await ImportAsync(pg, CsvSource, file);
        var before = await RowsAsync(account);
        var second = await ImportAsync(pg, CsvSource, file);
        var after = await RowsAsync(account);

        Assert.Equal((2, 0, 0, 2), (second.Run.RowsRead, second.Run.Created, second.Run.Restated, second.Run.Unchanged));
        Assert.Equal(first.Run.FileSha256, second.Run.FileSha256);
        Assert.NotEqual(first.Run.Id, second.Run.Id);
        Assert.Equal(before.Select(r => (r.Id, r.Balance, r.IngestRunId)), after.Select(r => (r.Id, r.Balance, r.IngestRunId)));
    }

    [Fact]
    public async Task A_changed_value_restates_and_the_rest_are_unchanged()
    {
        var account = await NewAccountAsync(pg, Opened);
        var id = await BindAsync(pg, account, CsvBalanceSource.System);
        var first = await ImportAsync(pg, CsvSource, Csv((id, "2026-10-01", "100"), (id, "2026-10-15", "150")));

        var second = await ImportAsync(pg, CsvSource, Csv((id, "2026-10-01", "100"), (id, "2026-10-15", "175")));

        Assert.Equal((1, 1, 0), (second.Run.Unchanged, second.Run.Restated, second.Run.Created));
        var rows = await RowsAsync(account);
        var old = Assert.Single(rows, r => r.AsOfDate == new DateOnly(2026, 10, 15) && r.SupersededAt is not null);
        var now = Assert.Single(rows, r => r.AsOfDate == new DateOnly(2026, 10, 15) && r.SupersededAt is null);
        Assert.Equal((150m, first.Run.Id), (old.Balance, old.IngestRunId));       // history kept, provenance intact
        Assert.Equal((175m, second.Run.Id, old.Id), (now.Balance, now.IngestRunId, now.Supersedes!.Value));
    }

    [Fact]
    public async Task An_unbound_identifier_fails_the_whole_file_and_writes_nothing()
    {
        // Identity is never guessed: the importer does not create an account or a binding.
        var account = await NewAccountAsync(pg, Opened);
        var id = await BindAsync(pg, account, CsvBalanceSource.System);

        var outcome = await ImportAsync(pg, CsvSource, Csv((id, "2026-10-01", "100"), ("EXAMPLE-NOT-BOUND", "2026-10-01", "5")));

        Assert.Equal((IngestRunStatus.Failed, 2, 0), (outcome.Run.Status, outcome.Run.RowsRead, outcome.Run.Created));
        var error = Assert.Single(outcome.Errors);
        Assert.Equal("line 3", error.Location);
        Assert.Contains("is not bound", error.Reason);
        Assert.Empty(await RowsAsync(account)); // the bound row on line 2 was not imported either
    }

    [Fact]
    public async Task A_binding_under_another_source_system_does_not_count()
    {
        var account = await NewAccountAsync(pg, Opened);
        var id = await BindAsync(pg, account, OfxBalanceSource.System); // bound for OFX, not CSV

        var outcome = await ImportAsync(pg, CsvSource, Csv((id, "2026-10-01", "100")));

        Assert.Equal(IngestRunStatus.Failed, outcome.Run.Status);
        Assert.Contains("under import:csv", Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task A_parse_error_fails_the_file_and_the_run_records_why()
    {
        var outcome = await ImportAsync(pg, CsvSource, "account,as_of_date,balance\nEXAMPLE-1,03/04/2026,1\n"u8.ToArray());

        Assert.Equal((IngestRunStatus.Failed, 0), (outcome.Run.Status, outcome.Run.RowsRead));
        await using var db = pg.CreateContext();
        var stored = await db.IngestRuns.SingleAsync(r => r.Id == outcome.Run.Id, Ct);
        using var detail = JsonDocument.Parse(stored.ErrorDetail!);
        var e = Assert.Single(detail.RootElement.EnumerateArray());
        Assert.Equal("line 2", e.GetProperty("location").GetString());
        Assert.Contains("ISO date", e.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("2025-12-31", "precedes the account's opened_on")]
    [InlineData("2026-07-01", "follows the account's closed_on")]
    public async Task A_row_outside_the_account_window_fails_the_file(string date, string expected)
    {
        var account = await NewAccountAsync(pg, Opened, closed: new DateOnly(2026, 6, 30));
        var id = await BindAsync(pg, account, CsvBalanceSource.System);

        var outcome = await ImportAsync(pg, CsvSource, Csv((id, "2026-03-01", "1"), (id, date, "2")));

        Assert.Equal(IngestRunStatus.Failed, outcome.Run.Status);
        Assert.Contains(expected, Assert.Single(outcome.Errors).Reason);
        Assert.Empty(await RowsAsync(account));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_and_recorded()
    {
        var outcome = await ImportAsync(pg, CsvSource, new byte[ImportPipeline.MaxFileBytes + 1]);

        Assert.Equal(IngestRunStatus.Failed, outcome.Run.Status);
        Assert.Contains("at most 1048576", Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task An_OFX_stamp_is_dated_as_written_and_kept_as_a_UTC_moment()
    {
        // slice-02.md "Dates and time", Kyle's example: 23:00 CDT on the 15th is the 15th, and
        // 04:00 UTC on the 16th as a moment.
        var account = await NewAccountAsync(pg, Opened);
        var id = await BindAsync(pg, account, OfxBalanceSource.System);

        var outcome = await ImportAsync(pg, new OfxBalanceSource(), Ofx(id, "321.00", "2026-10-15", "230000", "[-5:CDT]"), fileName: "statement.ofx");

        Assert.Equal(1, outcome.Run.Created);
        var row = Assert.Single(await RowsAsync(account));
        Assert.Equal(new DateOnly(2026, 10, 15), row.AsOfDate);
        Assert.Equal(DateTimeOffset.Parse("2026-10-16T04:00:00Z"), row.SourceAsOf);
        Assert.Equal(("import:ofx", SourceStrength.Export), (row.SourceSystem, row.SourceStrength));
    }

    [Fact]
    public async Task One_account_imports_from_two_sources_under_two_bindings()
    {
        // Identity across sources: account_source, built in slice 01 and never exercised until now.
        var account = await NewAccountAsync(pg, Opened);
        var csvId = await BindAsync(pg, account, CsvBalanceSource.System);
        var ofxId = await BindAsync(pg, account, OfxBalanceSource.System);

        await ImportAsync(pg, CsvSource, Csv((csvId, "2026-10-15", "300")));
        await ImportAsync(pg, new OfxBalanceSource(), Ofx(ofxId, "300", "2026-10-15"));

        var rows = await RowsAsync(account);
        Assert.Equal(["import:csv", "import:ofx"], rows.Select(r => r.SourceSystem).Order());
        Assert.All(rows, r => Assert.Null(r.SupersededAt)); // two sources, two live rows, one account
    }

    // ---- What migration 4 puts in the database itself ----

    [Theory]
    [InlineData("UPDATE ingest_run SET file_name = 'rewritten'")]
    [InlineData("DELETE FROM ingest_run")]
    [InlineData("TRUNCATE ingest_run CASCADE")]
    public async Task An_ingest_run_cannot_be_changed_or_removed(string sql)
    {
        await ImportAsync(pg, CsvSource, "not,a,file\n"u8.ToArray()); // at least one run exists

        var error = await Assert.ThrowsAsync<PostgresException>(() => pg.ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Contains("ingest_run is immutable", error.MessageText);
    }

    [Theory]
    // A failed run that claims to have written something; a succeeded run whose counts do not add up.
    [InlineData("'failed', 1, 1, 0, 0, '[]'")]
    [InlineData("'succeeded', 3, 1, 1, 0, NULL")]
    [InlineData("'succeeded', 1, 1, 0, 0, '[]'")]
    public async Task The_run_row_holds_the_all_or_nothing_rule(string values)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => pg.ExecuteAsync($"""
            INSERT INTO ingest_run (source_system, file_name, file_sha256, started_at, finished_at, status, rows_read, created, restated, unchanged, error_detail)
            VALUES ('import:csv', 'x.csv', repeat('a', 64), now(), now(), {values})
            """));

        Assert.Equal((PostgresErrorCodes.CheckViolation, "ingest_run_all_or_nothing"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task The_database_time_zone_is_declared_UTC_not_inherited()
    {
        // The postgres image defaults to UTC anyway, so SHOW timezone alone would pass without
        // the migration. The database-level setting is what the migration wrote.
        var setting = await pg.ScalarAsync<string[]>("""
            SELECT s.setconfig FROM pg_db_role_setting s
              JOIN pg_database d ON d.oid = s.setdatabase
             WHERE d.datname = current_database() AND s.setrole = 0
            """);

        Assert.Contains("TimeZone=UTC", setting ?? []);
        Assert.Equal("UTC", await pg.ScalarAsync<string>("SHOW timezone"));
    }
}
