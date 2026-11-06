using System.Data;
using Npgsql;
using Portfolio.Infrastructure;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 2: rows each view must hide, each with a value found nowhere else, so a value
/// seen anywhere a report can reach is a leak (DBS-4).
/// </summary>
public class ReportingFixture : PostgresFixture
{
    /// <summary>A soft-deleted balance, a superseded one, and a live balance on a soft-deleted account.</summary>
    public static readonly string[] HiddenBalances = ["424242", "777777", "555555"];
    public const string HiddenAccount = "Hidden deleted account";

    protected override Task SeedAsync() => ExecuteAsync("""
        INSERT INTO institution (id, name) VALUES ('40000000-0000-4000-8000-000000000001', 'Example Bank');
        INSERT INTO account (id, institution_id, display_name, currency, opened_on, closed_on, deleted_at) VALUES
          ('40000000-0000-4000-8000-00000000000a', '40000000-0000-4000-8000-000000000001', 'Everyday', 'USD', '2026-01-01', NULL, NULL),
          ('40000000-0000-4000-8000-00000000000b', '40000000-0000-4000-8000-000000000001', 'Old savings', 'USD', '2026-01-01', '2026-03-31', NULL),
          ('40000000-0000-4000-8000-00000000000d', '40000000-0000-4000-8000-000000000001', 'Hidden deleted account', 'USD', '2026-01-01', NULL, now());
        INSERT INTO account_source (account_id, source_system, source_id) VALUES
          ('40000000-0000-4000-8000-00000000000a', 'import:csv', 'EXAMPLE-REPORTING-1');
        INSERT INTO account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at, superseded_at, deleted_at) VALUES
          ('40000000-0000-4000-8000-00000000000a', '2026-02-01', 100,    'USD', 'manual', 'manual', now(), NULL,  NULL),
          ('40000000-0000-4000-8000-00000000000a', '2026-02-05', 777777, 'USD', 'manual', 'manual', now(), now(), NULL),
          ('40000000-0000-4000-8000-00000000000a', '2026-02-05', 200,    'USD', 'manual', 'manual', now(), NULL,  NULL),
          ('40000000-0000-4000-8000-00000000000a', '2026-02-10', 424242, 'USD', 'manual', 'manual', now(), NULL,  now()),
          ('40000000-0000-4000-8000-00000000000b', '2026-02-01', 50,     'USD', 'manual', 'manual', now(), NULL,  NULL),
          ('40000000-0000-4000-8000-00000000000d', '2026-02-01', 555555, 'USD', 'manual', 'manual', now(), NULL,  NULL);
        INSERT INTO ingest_run (source_system, file_name, file_sha256, started_at, finished_at, status, rows_read, created, restated, unchanged) VALUES
          ('import:csv', 'example.csv', repeat('a', 64), now(), now(), 'succeeded', 0, 0, 0, 0);
        """);

    /// <summary>
    /// One run with the runner's boundary (slice-04.md "Running a query", parts 1-3 and 6): a fresh
    /// unpooled connection on the reporting login, <see cref="ReportRunner.Begin"/>'s own prelude,
    /// the text as one command, then ROLLBACK. Kept beside the runner because it also collects the
    /// NOTICEs the leak probe raises; <c>ReportApiTests</c> sends the same refusals through the
    /// endpoint (step 3).
    /// </summary>
    public async Task<ReportingRun> RunAsync(string sql)
    {
        var notices = new List<string>();
        var builder = new NpgsqlConnectionStringBuilder(ReportingConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Notice += (_, e) => { lock (notices) notices.Add(e.Notice.MessageText); };
        await connection.OpenAsync();
        await using (var begin = new NpgsqlCommand(ReportRunner.Begin(new ReportingLimits()), connection))
            await begin.ExecuteNonQueryAsync();

        var rows = new List<object?[]>();
        try
        {
            await using var batch = new NpgsqlBatch(connection) { BatchCommands = { new NpgsqlBatchCommand(sql) } };
            await using var reader = await batch.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                await using var rollback = new NpgsqlCommand("ROLLBACK", connection);
                await rollback.ExecuteNonQueryAsync();
            }
        }
        lock (notices) return new ReportingRun(rows, [.. notices]);
    }

    /// <summary>
    /// Writes judged by their effect (TST-5): the login's privileges and settings, the large objects,
    /// a view's comment and every table's row count, as one text to compare before and after.
    /// </summary>
    public async Task<string> StateAsync()
    {
        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        var snapshot = await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Reporting);
        var rest = await ScalarAsync<string>("""
            SELECT concat_ws(E'\n',
                'large objects ' || (SELECT count(*) FROM pg_largeobject_metadata),
                'role settings ' || (SELECT coalesce(string_agg(array_to_string(setconfig, ','), ';'), '') FROM pg_db_role_setting WHERE setrole = 'portfolio_reporting'::regrole),
                'comment ' || obj_description('reporting.v_account'::regclass, 'pg_class'),
                (SELECT string_agg(format('%s %s', c.relname,
                        (xpath('/row/n/text()', query_to_xml(format('SELECT count(*) AS n FROM public.%I', c.relname), false, true, '')))[1]::text),
                        E'\n' ORDER BY c.relname)
                   FROM pg_class c WHERE c.relnamespace = 'public'::regnamespace AND c.relkind = 'r'))
            """);
        return string.Join("\n", snapshot) + "\n" + rest;
    }
}

public sealed record ReportingRun(List<object?[]> Rows, List<string> Notices);

public class ReportingSchemaTests(ReportingFixture pg) : IClassFixture<ReportingFixture>
{
    static readonly string[] Views = ["v_account", "v_balance_daily", "v_ingest_run", "v_net_worth_daily"];

    async Task<PostgresException> RefusedAsync(string sql) =>
        await Assert.ThrowsAsync<PostgresException>(() => pg.RunAsync(sql));

    // --- The schema itself -------------------------------------------------------------------

    [Fact]
    public async Task Reporting_holds_exactly_the_four_views_each_a_security_barrier()
    {
        await using var connection = new NpgsqlConnection(pg.SuperuserConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.relname, c.relkind::text, coalesce('security_barrier=true' = ANY(c.reloptions), false)
              FROM pg_class c WHERE c.relnamespace = 'reporting'::regnamespace AND c.relkind NOT IN ('i', 'S', 't', 'c')
             ORDER BY c.relname
            """, connection);
        var found = new List<(string Name, string Kind, bool Barrier)>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                found.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));

        Assert.Equal(Views, found.Select(f => f.Name));
        Assert.All(found, f => Assert.Equal("v", f.Kind));
        Assert.All(found, f => Assert.True(f.Barrier, $"reporting.{f.Name} is not a security_barrier view (DBS-4)"));
    }

    [Fact]
    public async Task Every_view_and_every_column_in_reporting_carries_a_comment()
    {
        var missing = await pg.ScalarAsync<string>("""
            SELECT string_agg(what, ', ' ORDER BY what) FROM (
                SELECT 'reporting' AS what WHERE obj_description('reporting'::regnamespace, 'pg_namespace') IS NULL
                UNION ALL
                SELECT c.relname FROM pg_class c
                 WHERE c.relnamespace = 'reporting'::regnamespace AND c.relkind = 'v' AND obj_description(c.oid, 'pg_class') IS NULL
                UNION ALL
                SELECT c.relname || '.' || a.attname FROM pg_class c
                  JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                 WHERE c.relnamespace = 'reporting'::regnamespace AND c.relkind = 'v' AND col_description(c.oid, a.attnum) IS NULL
            ) m
            """);
        Assert.Null(missing);
    }

    // --- The view rules ----------------------------------------------------------------------

    [Fact]
    public async Task v_account_shows_open_and_closed_accounts_and_not_a_deleted_one()
    {
        var run = await pg.RunAsync("SELECT account_name, institution_name, is_closed, closed_on, linked_formats FROM reporting.v_account ORDER BY account_name");
        Assert.Equal(["Everyday", "Old savings"], run.Rows.Select(r => (string)r[0]!));
        Assert.All(run.Rows, r => Assert.Equal("Example Bank", r[1]));
        Assert.Equal([false, true], run.Rows.Select(r => (bool)r[2]!));
        Assert.Equal(new DateOnly(2026, 3, 31), run.Rows[1][3]);
        Assert.Equal(["import:csv"], (string[])run.Rows[0][4]!);
        Assert.Empty((string[])run.Rows[1][4]!);
    }

    [Fact]
    public async Task v_balance_daily_shows_live_balances_only_and_fills_no_gap()
    {
        var run = await pg.RunAsync("SELECT account_id::text, as_of_date, balance, source_strength FROM reporting.v_balance_daily ORDER BY account_id, as_of_date");
        Assert.Equal(
            [("40000000-0000-4000-8000-00000000000a", new DateOnly(2026, 2, 1), 100m),
             ("40000000-0000-4000-8000-00000000000a", new DateOnly(2026, 2, 5), 200m),
             ("40000000-0000-4000-8000-00000000000b", new DateOnly(2026, 2, 1), 50m)],
            run.Rows.Select(r => ((string)r[0]!, (DateOnly)r[1]!, (decimal)r[2]!)));
        Assert.All(run.Rows, r => Assert.Equal("manual", r[3]));
    }

    [Fact]
    public async Task v_ingest_run_shows_every_import()
    {
        var run = await pg.RunAsync("SELECT file_name, status, problems FROM reporting.v_ingest_run");
        var row = Assert.Single(run.Rows);
        Assert.Equal(["example.csv", "succeeded", null], row);
    }

    [Fact]
    public async Task v_net_worth_daily_is_the_charts_view_row_for_row()
    {
        // Compared as the owner, in both directions; then the reporting login sees the same rows.
        Assert.Equal(0L, await pg.ScalarAsync<long>("""
            SELECT count(*) FROM (
                (SELECT * FROM reporting.v_net_worth_daily EXCEPT ALL SELECT * FROM public.v_net_worth_daily)
                UNION ALL
                (SELECT * FROM public.v_net_worth_daily EXCEPT ALL SELECT * FROM reporting.v_net_worth_daily)) d
            """));
        var days = await pg.ScalarAsync<long>("SELECT count(*) FROM public.v_net_worth_daily");
        Assert.True(days > 30, $"the chart's view has {days} days; the comparison needs rows to mean anything");

        var run = await pg.RunAsync("SELECT count(*), sum(net_worth) FILTER (WHERE as_of_date = '2026-02-05') FROM reporting.v_net_worth_daily");
        Assert.Equal(days, run.Rows[0][0]);
        Assert.Equal(250m, run.Rows[0][1]); // Everyday's 200 and Old savings' 50, carried: no hidden value
    }

    // --- The leaky function (DBS-4) ----------------------------------------------------------

    /// <summary>
    /// The attack a barrier stops: a function cheaper than the view's filter, in the user's WHERE,
    /// that reports every value it is shown. The reporting login cannot create one (no TEMPORARY, no
    /// plpgsql, a read-only transaction), so the owner makes it and grants it, as DBS-4 directs.
    /// </summary>
    async Task<List<string>> LeakedAsync(string relation, string column, string? alsoCreate = null)
    {
        await pg.ExecuteAsync($"""
            CREATE SCHEMA probe;
            CREATE FUNCTION probe.leak(v text) RETURNS boolean LANGUAGE plpgsql COST 0.0000001 AS $$
                BEGIN RAISE NOTICE 'seen %', v; RETURN true; END $$;
            {alsoCreate}
            GRANT USAGE ON SCHEMA probe TO {DatabaseLogins.Reporting};
            GRANT SELECT ON ALL TABLES IN SCHEMA probe TO {DatabaseLogins.Reporting};
            GRANT EXECUTE ON FUNCTION probe.leak(text) TO {DatabaseLogins.Reporting};
            """);
        try
        {
            var run = await pg.RunAsync($"SELECT count(*) FROM {relation} WHERE probe.leak({column}::text)");
            var hidden = ReportingFixture.HiddenBalances.Append(ReportingFixture.HiddenAccount);
            return run.Notices.Where(n => hidden.Any(h => n.Contains(h, StringComparison.Ordinal))).ToList();
        }
        finally
        {
            await pg.ExecuteAsync("DROP SCHEMA probe CASCADE");
        }
    }

    [Theory]
    [InlineData("v_balance_daily", "balance")]
    [InlineData("v_account", "account_name")]
    public async Task A_cheap_function_in_the_WHERE_sees_no_row_the_view_hides(string view, string column) =>
        Assert.Empty(await LeakedAsync("reporting." + view, column));

    /// <summary>
    /// Seen failing, committed: the same probe over a copy of v_balance_daily's own definition,
    /// made without the barrier, sees a hidden row, so the test above cannot pass merely because the
    /// probe sees nothing. (A copy, not the view altered and put back: a test that repaired the view
    /// in its finally hid a mutation that dropped the barrier from the migration.) The leak is
    /// through the join: the probe is pushed into the balance scan, below the join that drops a
    /// deleted account's balances, and sees 555555. The superseded and soft-deleted balances stay
    /// hidden even then: their <c>IS NULL</c> filters sit on the same scan and cost less than any
    /// function, as DBS-4 found. For the same reason v_account does not leak without its barrier
    /// today; the barrier is what keeps that true after the next edit to a view.
    /// </summary>
    [Fact]
    public async Task Without_the_barrier_the_same_probe_sees_a_deleted_accounts_balance() =>
        Assert.Equal(["seen 555555.0000"], await LeakedAsync("probe.plain", "balance", """
            DO $$ BEGIN
                EXECUTE 'CREATE VIEW probe.plain AS ' || pg_get_viewdef('reporting.v_balance_daily'::regclass);
            END $$;
            """));

    // --- The boundary: each refusal, by its SQLSTATE (TST-5) ----------------------------------

    /// <summary>
    /// slice-04.md "The boundary, stated". Every write route tested, and every read outside the
    /// views, with the SQLSTATE that says WHICH layer refused it: 42501 a privilege, 25006 the
    /// read-only transaction, 25001 a mode change after the first query, 42601 a second statement.
    /// A misspelt name (42P01) would pass a test that only expected an error; this one fails on it.
    /// </summary>
    public static TheoryData<string, string> Refusals => new()
    {
        // Writes to its own role (DBS-1): no privilege governs them; the read-only transaction does.
        { "ALTER ROLE portfolio_reporting PASSWORD 'changed-by-a-report'", "25006" },
        { "ALTER ROLE portfolio_reporting SET work_mem = '2000MB'", "25006" },
        { "ALTER ROLE portfolio_reporting SET statement_timeout = 0", "25006" },
        // Large objects and WAL messages: revoked by name (DBS-1, R2-DBS-1).
        { "SELECT lo_from_bytea(0, 'written by a report'::bytea)", "42501" },
        { "SELECT lo_create(0)", "42501" },
        { "SELECT lo_creat(-1)", "42501" },
        { "SELECT lo_import('/etc/hostname')", "42501" },
        { "SELECT pg_logical_emit_message(false, 'probe', 'x')", "42501" },
        { "SELECT pg_logical_emit_message(true, 'probe', 'x'::bytea)", "42501" },
        // Temporary objects, DO, functions.
        { "CREATE TEMP TABLE probe_t (x int)", "25006" },
        { "CREATE FUNCTION pg_temp.probe_f() RETURNS int LANGUAGE sql AS 'SELECT 1'", "25006" },
        { "DO $$ BEGIN NULL; END $$", "42501" },
        // Sequences.
        { "SELECT nextval('public.identity_user_claim_id_seq')", "42501" },
        // Locks that block writers, and row locks: anything above ACCESS SHARE needs a write
        // privilege, so the grant refuses it before the read-only check is reached.
        { "LOCK TABLE reporting.v_account IN SHARE MODE", "42501" },
        { "LOCK TABLE public.account IN ACCESS SHARE MODE", "42501" },
        { "SELECT * FROM reporting.v_account FOR UPDATE", "42501" },
        // The documentation is the database's: a report cannot rewrite it.
        { "COMMENT ON VIEW reporting.v_account IS 'rewritten by a report'", "25006" },
        // Undoing the read-only transaction from inside a query: refused (DBS-1). SET TRANSACTION
        // READ WRITE as the whole statement succeeded in step 2, before the runner took its snapshot
        // ahead of the user's text (ReportRunner.Begin); now it is refused too.
        { "SELECT set_config('transaction_read_only', 'off', true)", "25001" },
        { "SET TRANSACTION READ WRITE", "25001" },
        // The role default that holds (DBS-2): only a superuser may change temp_file_limit.
        { "SET temp_file_limit = '100GB'", "42501" },
        { "SELECT set_config('temp_file_limit', '-1', true)", "42501" },
        // ... and a second statement is refused by the server, not by parsing (D5).
        { "SET TRANSACTION READ WRITE; ALTER ROLE portfolio_reporting SET work_mem = '1GB'", "42601" },
        { "COMMIT; ALTER ROLE portfolio_reporting SET work_mem = '1GB'", "42601" },
        // Leaving the database.
        { "COPY (SELECT 1) TO PROGRAM 'true'", "42501" },
        { "SELECT pg_read_file('/etc/hostname')", "42501" },
        { "SET ROLE postgres", "42501" },
        { "SET SESSION AUTHORIZATION postgres", "42501" },
        // Reads outside the views: the tables beneath them, Identity, the audit log, the chart's view.
        { "SELECT * FROM public.account", "42501" },
        { "SELECT * FROM public.account_balance", "42501" },
        { "SELECT * FROM public.account_source", "42501" },
        { "SELECT * FROM public.institution", "42501" },
        { "SELECT * FROM public.ingest_run", "42501" },
        { "SELECT * FROM public.app_setting", "42501" },
        { "SELECT * FROM public.v_net_worth_daily", "42501" },
        { "SELECT * FROM public.v_date_spine", "42501" },
        { "SELECT * FROM public.audit_event", "42501" },
        { "SELECT * FROM public.used_setup_code", "42501" },
        { "SELECT * FROM public.identity_user", "42501" },
        { "SELECT * FROM public.identity_user_passkey", "42501" },
        { "SELECT * FROM public.identity_user_token", "42501" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task The_reporting_login_is_refused(string sql, string sqlState)
    {
        var refused = await RefusedAsync(sql);
        Assert.True(refused.SqlState == sqlState, $"{sql}\nexpected {sqlState}, got {refused.SqlState}: {refused.MessageText}");
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template1")]
    public async Task The_reporting_login_cannot_connect_to_another_database(string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(pg.ReportingConnectionString) { Database = database, Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

    /// <summary>
    /// Writes judged by their effect, not by the errors collected (TST-5): after every probe, the
    /// login's privileges, its settings, the large objects and every table's rows are as before.
    /// </summary>
    [Fact]
    public async Task After_every_probe_nothing_has_changed()
    {
        var before = await pg.StateAsync();
        foreach (var sql in Refusals.Select(r => (string)r.Data.Item1))
            await Assert.ThrowsAsync<PostgresException>(() => pg.RunAsync(sql));
        Assert.Equal(before, await pg.StateAsync());
    }

    // --- The two revokes, seen failing (R2-DBS-1, TST-5) --------------------------------------

    /// <summary>
    /// With PUBLIC's EXECUTE given back to the login, the non-transactional WAL message is written
    /// and returns its position from inside the read-only transaction, which ROLLBACK does not undo.
    /// The revoke is what refuses it.
    /// </summary>
    [Fact]
    public async Task With_its_EXECUTE_granted_pg_logical_emit_message_writes_from_the_read_only_transaction()
    {
        const string sql = "SELECT pg_logical_emit_message(false, 'probe', 'x')::text";
        await pg.ExecuteAsync($"GRANT EXECUTE ON FUNCTION pg_logical_emit_message(boolean, text, text, boolean) TO {DatabaseLogins.Reporting}");
        try
        {
            var run = await pg.RunAsync(sql);
            Assert.Matches("^[0-9A-F]+/[0-9A-F]+$", (string)run.Rows[0][0]!);
        }
        finally
        {
            await pg.ExecuteAsync($"REVOKE EXECUTE ON FUNCTION pg_logical_emit_message(boolean, text, text, boolean) FROM {DatabaseLogins.Reporting}");
        }
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await RefusedAsync(sql)).SqlState);
    }

    /// <summary>
    /// With its EXECUTE granted, lo_from_bytea is still refused here, by the read-only transaction
    /// (25006): the revoke is the second wall, for the run whose transaction guard fails (DBS-1).
    /// Outside a read-only transaction the same call writes a large object the login then owns.
    /// </summary>
    [Fact]
    public async Task With_its_EXECUTE_granted_lo_from_bytea_writes_unless_the_transaction_is_read_only()
    {
        const string sql = "SELECT lo_from_bytea(0, 'written by a report'::bytea)";
        await pg.ExecuteAsync($"GRANT EXECUTE ON FUNCTION lo_from_bytea(oid, bytea) TO {DatabaseLogins.Reporting}");
        try
        {
            Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, (await RefusedAsync(sql)).SqlState);

            await using var connection = new NpgsqlConnection(pg.ReportingConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            var oid = (uint)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
            Assert.Equal(1L, await pg.ScalarAsync<long>($"SELECT count(*) FROM pg_largeobject_metadata WHERE oid = {oid} AND lomowner = 'portfolio_reporting'::regrole"));
            await pg.ExecuteAsync($"SELECT lo_unlink({oid})");
        }
        finally
        {
            await pg.ExecuteAsync($"REVOKE EXECUTE ON FUNCTION lo_from_bytea(oid, bytea) FROM {DatabaseLogins.Reporting}");
        }
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await RefusedAsync(sql)).SqlState);
    }
}
