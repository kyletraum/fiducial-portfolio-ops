using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Portfolio.Infrastructure;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>slice-04.md step 3: the report routes, on a fixture's database, in Production, signed in.</summary>
static partial class ReportApi
{
    public static WebApplicationFactory<Program> Create(PostgresFixture pg, LogCapture? logs = null, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production")
             .UseSetting("ConnectionStrings:portfolio", pg.ApiConnectionString)
             .UseSetting("ConnectionStrings:reporting", pg.ReportingConnectionString)
             .WithTestAuth();
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
            if (logs is not null)
                b.ConfigureLogging(l => l.AddProvider(logs));
        });

    public static Task<HttpResponseMessage> RunAsync(HttpClient client, string sql, CancellationToken? ct = null) =>
        client.PostAsJsonAsync("/api/v1/reports/run", new { sql }, ct ?? Ct);

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    /// <summary>The run's rows as raw JSON text, one string per row, for exact comparison.</summary>
    public static string[] Rows(JsonElement result) =>
        [.. result.GetProperty("rows").EnumerateArray().Select(r => r.GetRawText())];

    /// <summary>A superuser query on a fresh, unpooled connection: it survives a crash-restart between calls.</summary>
    public static async Task<T> SuperuserAsync<T>(PostgresFixture pg, string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(pg.SuperuserConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>Reporting backends running a statement now, by pid.</summary>
    public static async Task<int[]> ActivePidsAsync(PostgresFixture pg) =>
        await SuperuserAsync<int[]>(pg, "SELECT coalesce(array_agg(pid), '{}') FROM pg_stat_activity WHERE usename = 'portfolio_reporting' AND state = 'active'");

    public static async Task<bool> BackendExistsAsync(PostgresFixture pg, int pid) =>
        await SuperuserAsync<bool>(pg, $"SELECT EXISTS (SELECT FROM pg_stat_activity WHERE pid = {pid})");

    /// <summary>The one reporting backend running a statement, once it is.</summary>
    public static async Task<int> WaitForActiveAsync(PostgresFixture pg)
    {
        var pid = 0;
        await WaitUntilAsync(async () => (await ActivePidsAsync(pg)) is [var one] && (pid = one) > 0, TimeSpan.FromSeconds(15), "a reporting backend to be active");
        return pid;
    }

    public static Task WaitUntilGoneAsync(PostgresFixture pg, int pid) =>
        WaitUntilAsync(async () => !await BackendExistsAsync(pg, pid), TimeSpan.FromSeconds(5), $"backend {pid} to end");

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            if (clock.Elapsed > timeout)
                Assert.Fail($"timed out after {timeout.TotalSeconds} s waiting for {what}");
            await Task.Delay(25, Ct);
        }
    }

    /// <summary>The backend the runner logged as started, read from its own line (part 6: from the connection).</summary>
    public static int? StartedPid(LogCapture logs) =>
        logs.Entries.Where(e => e.Category == typeof(ReportRunner).FullName && e.Text.Contains("started on backend", StringComparison.Ordinal))
            .Select(e => int.Parse(PidPattern().Match(e.Text).Groups[1].Value)).Cast<int?>().LastOrDefault();

    [GeneratedRegex(@"Pid=(\d+)")]
    private static partial Regex PidPattern();
}

/// <summary>
/// slice-04.md step 3: running a query through the endpoint. The refusals of step 2, now on the real
/// runner; the typing rule; the caps at their boundaries; no session state surviving a run; one run
/// at a time; the schema documentation read live.
/// </summary>
public class ReportApiTests(ReportingFixture pg) : IClassFixture<ReportingFixture>, IAsyncLifetime
{
    WebApplicationFactory<Program> api = null!;
    HttpClient client = null!;

    public ValueTask InitializeAsync()
    {
        api = ReportApi.Create(pg);
        client = api.CreateSignedInClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await api.DisposeAsync();
    }

    // --- The boundary, through the endpoint (DoD 2, TST-5) --------------------------------------

    [Theory]
    [MemberData(nameof(ReportingSchemaTests.Refusals), MemberType = typeof(ReportingSchemaTests))]
    public async Task Each_refusal_answers_422_with_its_SQLSTATE(string sql, string sqlState)
    {
        var response = await ReportApi.RunAsync(client, sql);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{sql}\n{(int)response.StatusCode} {body}");
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal("The database refused the query.", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("sqlState").GetString() == sqlState, $"{sql}\nexpected {sqlState}, got {body}");
    }

    [Fact]
    public async Task After_every_probe_through_the_endpoint_nothing_has_changed()
    {
        var before = await pg.StateAsync();
        foreach (var sql in ReportingSchemaTests.Refusals.Select(r => (string)r.Data.Item1))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReportApi.RunAsync(client, sql)).StatusCode);
        Assert.Equal(before, await pg.StateAsync());
    }

    // --- Results of unknown shape (C4-12, SCO-5, SRC-9) ---------------------------------------

    [Fact]
    public async Task Values_come_back_by_their_PostgreSQL_type_and_money_keeps_every_digit()
    {
        var response = await ReportApi.RunAsync(client, """
            SELECT 1e40::numeric AS big, 123.4500::numeric AS scaled, -0.001::numeric AS small, 'NaN'::numeric AS nan,
                   true AS yes, 42 AS answer, 3e9::bigint AS wide, 1.5::float8 AS half, 'Infinity'::float8 AS inf,
                   DATE '2026-02-05' AS day, TIMESTAMPTZ '2026-02-05 10:00:00.25+02' AS moment, TIMESTAMP '2026-02-05 10:00:00' AS wall,
                   INTERVAL '1 year 2 mons 3 days 04:05:06.5' AS span, 'abc'::bytea AS raw, '{"a": 1}'::jsonb AS doc,
                   ARRAY['csv', 'ofx'] AS formats, NULL::text AS nothing, 'é€😀' AS words
            """);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReportApi.JsonAsync(response);

        Assert.Equal(
            ["big numeric", "scaled numeric", "small numeric", "nan numeric", "yes boolean", "answer integer", "wide bigint",
             "half double precision", "inf double precision", "day date", "moment timestamp with time zone",
             "wall timestamp without time zone", "span interval", "raw bytea", "doc jsonb", "formats text[]", "nothing text", "words text"],
            result.GetProperty("columns").EnumerateArray().Select(c => $"{c.GetProperty("name").GetString()} {c.GetProperty("type").GetString()}"));
        var row = result.GetProperty("rows")[0].EnumerateArray().ToArray();
        Assert.Equal("1" + new string('0', 40), row[0].GetString());
        Assert.Equal("123.4500", row[1].GetString());
        Assert.Equal("-0.001", row[2].GetString());
        Assert.Equal("NaN", row[3].GetString());
        Assert.True(row[4].GetBoolean());
        Assert.Equal(42, row[5].GetInt32());
        Assert.Equal("3" + new string('0', 9), row[6].GetString());
        Assert.Equal(1.5, row[7].GetDouble());
        Assert.Equal("Infinity", row[8].GetString());
        Assert.Equal("2026-02-05", row[9].GetString());
        Assert.Equal("2026-02-05T08:00:00.25Z", row[10].GetString());
        Assert.Equal("2026-02-05T10:00:00", row[11].GetString());
        Assert.Equal("P1Y2M3DT4H5M6.5S", row[12].GetString());
        Assert.Equal("\\x616263", row[13].GetString());
        Assert.Equal("{\"a\": 1}", row[14].GetString());
        Assert.Equal("[\"csv\",\"ofx\"]", row[15].GetRawText());
        Assert.Equal(JsonValueKind.Null, row[16].ValueKind);
        Assert.Equal("é€😀", row[17].GetString());
        Assert.False(result.GetProperty("cutOff").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("cutOffBy").ValueKind);
    }

    [Fact]
    public async Task The_views_read_through_the_runner_as_the_chart_and_the_page_need_them()
    {
        var balances = await ReportApi.JsonAsync(await ReportApi.RunAsync(client,
            "SELECT as_of_date, balance, source_strength FROM reporting.v_balance_daily ORDER BY as_of_date, balance"));
        Assert.Equal(["[\"2026-02-01\",\"50.0000\",\"manual\"]", "[\"2026-02-01\",\"100.0000\",\"manual\"]", "[\"2026-02-05\",\"200.0000\",\"manual\"]"],
            ReportApi.Rows(balances));

        var formats = await ReportApi.JsonAsync(await ReportApi.RunAsync(client,
            "SELECT account_name, linked_formats FROM reporting.v_account ORDER BY account_name"));
        Assert.Equal(["[\"Everyday\",[\"import:csv\"]]", "[\"Old savings\",[]]"], ReportApi.Rows(formats));
    }

    [Fact]
    public async Task A_column_of_a_type_with_no_reader_is_refused_by_name()
    {
        var response = await ReportApi.RunAsync(client, "SELECT 'pg_class'::regclass AS rel");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await ReportApi.JsonAsync(response);
        Assert.Equal("A column has a type reports cannot return.", problem.GetProperty("title").GetString());
        Assert.Contains("\"rel\" is regclass", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_query_is_refused_before_the_database(string sql)
    {
        var response = await ReportApi.RunAsync(client, sql);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await ReportApi.JsonAsync(response)).GetProperty("errors").TryGetProperty("sql", out _));
    }

    // --- The caps at their boundaries (TST-8, R2-TST-6) ---------------------------------------

    [Theory]
    [InlineData(4, 4, false)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, true)]
    public async Task The_row_cap_cuts_one_past_it(int produced, int returned, bool cutOff)
    {
        await using var capped = ReportApi.Create(pg, null, ("Reporting:MaxRows", "5"));
        var result = await ReportApi.JsonAsync(await ReportApi.RunAsync(capped.CreateSignedInClient(), $"SELECT g FROM generate_series(1, {produced}) g"));
        Assert.Equal(returned, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(cutOff, result.GetProperty("cutOff").GetBoolean());
        Assert.Equal(cutOff ? "rows" : null, result.GetProperty("cutOffBy").GetString());
    }

    /// <summary>A byte is a UTF-8 byte of a returned cell; the row whose cell crosses the cap is dropped.</summary>
    [Theory]
    [InlineData("SELECT repeat('x', 999)", 1, false)]
    [InlineData("SELECT repeat('x', 1000)", 1, false)]
    [InlineData("SELECT repeat('x', 1001)", 0, true)]
    [InlineData("SELECT repeat('é', 500)", 1, false)]
    [InlineData("SELECT repeat('é', 501)", 0, true)]
    [InlineData("SELECT repeat('x', 400) FROM generate_series(1, 3)", 2, true)]
    public async Task The_byte_cap_cuts_at_the_cell_that_crosses_it(string sql, int returned, bool cutOff)
    {
        await using var capped = ReportApi.Create(pg, null, ("Reporting:MaxBytes", "1000"));
        var result = await ReportApi.JsonAsync(await ReportApi.RunAsync(capped.CreateSignedInClient(), sql));
        Assert.Equal(returned, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(cutOff, result.GetProperty("cutOff").GetBoolean());
        Assert.Equal(cutOff ? "bytes" : null, result.GetProperty("cutOffBy").GetString());
    }

    // --- No session state survives a run (R2-TST-8) ------------------------------------------

    [Fact]
    public async Task Two_runs_have_two_backends()
    {
        var first = ReportApi.Rows(await ReportApi.JsonAsync(await ReportApi.RunAsync(client, "SELECT pg_backend_pid()")));
        var second = ReportApi.Rows(await ReportApi.JsonAsync(await ReportApi.RunAsync(client, "SELECT pg_backend_pid()")));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task An_advisory_lock_taken_in_a_run_is_held_by_no_backend_once_it_returns()
    {
        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(client, "SELECT pg_advisory_lock(4242)")).StatusCode);
        await ReportApi.WaitUntilAsync(async () => await ReportApi.SuperuserAsync<long>(pg,
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND objid = 4242") == 0, TimeSpan.FromSeconds(5), "the advisory lock to be released");
    }

    [Fact]
    public async Task A_statement_prepared_in_one_run_is_unknown_in_the_next()
    {
        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(client, "PREPARE probe_p AS SELECT 1")).StatusCode);
        var next = await ReportApi.RunAsync(client, "EXECUTE probe_p");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, next.StatusCode);
        Assert.Equal("26000", (await ReportApi.JsonAsync(next)).GetProperty("sqlState").GetString());
    }

    /// <summary>
    /// Part 6, shown. <c>NOTIFY</c> and <c>pg_notify</c> are allowed in a read-only transaction, and
    /// a notification is delivered only when its transaction commits. So the runner's ROLLBACK, not
    /// the read-only mode, is what keeps a report from signalling another session. The control is
    /// committed after the runs, and notifications arrive in commit order: any a run delivered would
    /// come first.
    /// </summary>
    [Fact]
    public async Task A_notification_sent_in_a_run_is_never_delivered()
    {
        var received = new List<string>();
        var builder = new NpgsqlConnectionStringBuilder(pg.SuperuserConnectionString) { Pooling = false };
        await using var listener = new NpgsqlConnection(builder.ConnectionString);
        listener.Notification += (_, e) => { lock (received) received.Add(e.Payload); };
        await listener.OpenAsync(Ct);
        await using (var listen = new NpgsqlCommand("LISTEN report_probe", listener))
            await listen.ExecuteNonQueryAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(client, "SELECT pg_notify('report_probe', 'from pg_notify')")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(client, "NOTIFY report_probe, 'from NOTIFY'")).StatusCode);
        await pg.ExecuteAsync("NOTIFY report_probe, 'control'");

        var clock = Stopwatch.StartNew();
        while (!received.Contains("control") && clock.Elapsed < TimeSpan.FromSeconds(10))
            await listener.WaitAsync(TimeSpan.FromSeconds(1), Ct);
        lock (received) Assert.Equal(["control"], received);
    }

    // --- One run at a time (S4-6, S4-10, R2-TST-10): by order, not by time ----------------------

    [Theory]
    [InlineData("aborted")]
    [InlineData("deadline")]
    [InlineData("refused")]
    public async Task A_second_run_gets_429_while_one_runs_and_the_slot_comes_back_however_it_ends(string ending)
    {
        await using var gated = ReportApi.Create(pg, null, ("Reporting:Deadline", "00:00:03"));
        var browser = gated.CreateSignedInClient();
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var a = ReportApi.RunAsync(browser, ending == "refused" ? "SELECT 1 / (count(*) - count(*)) FROM (SELECT pg_sleep(1.5)) s" : "SELECT pg_sleep(600)", abort.Token);
        var pid = await ReportApi.WaitForActiveAsync(pg);

        var b = await ReportApi.RunAsync(browser, "SELECT 1");
        Assert.Equal(HttpStatusCode.TooManyRequests, b.StatusCode);
        Assert.Equal("A report is already running.", (await ReportApi.JsonAsync(b)).GetProperty("title").GetString());
        Assert.Equal([pid], await ReportApi.ActivePidsAsync(pg));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/reports/schema", Ct)).StatusCode);

        switch (ending)
        {
            case "aborted":
                await abort.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
                break;
            case "deadline":
                Assert.Equal("deadline", (await ReportApi.JsonAsync(await a)).GetProperty("cutOffBy").GetString());
                break;
            default:
                Assert.Equal("22012", (await ReportApi.JsonAsync(await a)).GetProperty("sqlState").GetString());
                break;
        }
        await ReportApi.WaitUntilGoneAsync(pg, pid);

        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(browser, "SELECT 1")).StatusCode);
    }

    // --- The schema documentation (FR-6.6, TST-10) --------------------------------------------

    [Fact]
    public async Task The_schema_route_reads_every_comment_live_from_the_catalog()
    {
        var schema = await ReportApi.JsonAsync(await client.GetAsync("/api/v1/reports/schema", Ct));
        var views = schema.GetProperty("views").EnumerateArray().ToList();
        Assert.Equal(["v_account", "v_balance_daily", "v_ingest_run", "v_net_worth_daily"], views.Select(v => v.GetProperty("name").GetString()));
        Assert.All(views, v => Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("description").GetString())));
        Assert.All(views.SelectMany(v => v.GetProperty("columns").EnumerateArray()),
            c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("description").GetString()), c.GetProperty("name").GetString()));

        var original = await ReportApi.SuperuserAsync<string>(pg, "SELECT col_description('reporting.v_account'::regclass, 2)");
        var changed = $"Changed at test time {Guid.NewGuid():N}";
        await pg.ExecuteAsync($"COMMENT ON COLUMN reporting.v_account.account_name IS '{changed}'");
        try
        {
            var again = await ReportApi.JsonAsync(await client.GetAsync("/api/v1/reports/schema", Ct));
            var column = again.GetProperty("views")[0].GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "account_name");
            Assert.Equal(changed, column.GetProperty("description").GetString());
        }
        finally
        {
            await pg.ExecuteAsync($"COMMENT ON COLUMN reporting.v_account.account_name IS '{original.Replace("'", "''")}'");
        }
    }

    /// <summary>
    /// The reporting string is not required to start (SRC-8 asks that of the Api's own): without it
    /// the Api serves, and only the report routes answer 503, after the CSRF filter has had its say.
    /// </summary>
    [Fact]
    public async Task Without_a_reporting_connection_string_the_Api_serves_and_the_report_routes_answer_503()
    {
        await using var bare = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", pg.ApiConnectionString).WithTestAuth());
        var browser = bare.CreateSignedInClient();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/accounts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ReportApi.RunAsync(browser, "SELECT 1")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await browser.GetAsync("/api/v1/reports/schema", Ct)).StatusCode);
    }

    // --- The values (S4-2) ---------------------------------------------------------------------

    [Fact]
    public void The_Production_values_are_10_s_10000_rows_8_MB_and_256_MB()
    {
        var limits = api.Services.GetRequiredService<IOptions<ReportingLimits>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(10), limits.Deadline);
        Assert.Equal(10_000, limits.MaxRows);
        Assert.Equal(8 * 1024 * 1024, limits.MaxBytes);
        Assert.Equal(256, limits.TempFileLimitMegabytes);
    }
}

/// <summary>The producer table's fixture: a <c>temp_file_limit</c> of 1 MB, so a sort that spills is refused at once.</summary>
public sealed class ProducerFixture : PostgresFixture
{
    protected override ReportingLimits Limits => new() { TempFileLimitMegabytes = 1 };
}

/// <summary>
/// slice-04.md step 3, the producer table (R2-TST-7, TST-7): each query ends as stated, with the
/// deadline at 2 s, and its backend is gone after. A time-out's backend is seen active first.
/// </summary>
public class ReportProducerTests(ProducerFixture pg) : IClassFixture<ProducerFixture>
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public static TheoryData<string, string> Producers => new()
    {
        // Lifts its own statement_timeout: the client's deadline is out of its reach.
        { "SELECT set_config('statement_timeout', '0', true), pg_sleep(600)", "deadline" },
        // The series in the target list streams, so nothing spills and only the deadline ends it.
        { "SELECT count(*) FROM (SELECT generate_series(1, 1e11::bigint)) s", "deadline" },
        // In FROM it is materialised first (a function scan's tuplestore): that spills, and
        // temp_file_limit, which the login cannot lift, refuses it. The review's count(*) row was this
        // form, and under a small limit it is refused, not timed out.
        { "SELECT count(*) FROM generate_series(1, 1e11::bigint)", "53400" },
        // A sort that spills: refused the same way.
        { "SELECT g FROM generate_series(1, 1e11::bigint) g ORDER BY g DESC", "53400" },
        // Results that stream without end: cut at the row cap, then cancelled before the reader is disposed.
        { "SELECT generate_series(1, 1e11::bigint)", "rows" },
        { "WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r) SELECT n FROM r", "rows" },
    };

    [Theory]
    [MemberData(nameof(Producers))]
    public async Task Each_producer_ends_as_stated_and_its_backend_with_it(string sql, string expected)
    {
        var logs = new LogCapture();
        await using var api = ReportApi.Create(pg, logs, ("Reporting:Deadline", "00:00:02"));
        var clock = Stopwatch.StartNew();
        var run = ReportApi.RunAsync(api.CreateSignedInClient(), sql);

        var seenActive = false;
        while (!run.IsCompleted && !seenActive)
        {
            if (ReportApi.StartedPid(logs) is { } started)
                seenActive = (await ReportApi.ActivePidsAsync(pg)).Contains(started);
            await Task.Delay(20, Ct);
        }
        var response = await run;
        var elapsed = clock.Elapsed;
        var pid = ReportApi.StartedPid(logs) ?? throw new InvalidOperationException("the runner logged no backend");
        var body = await ReportApi.JsonAsync(response);

        switch (expected)
        {
            case "deadline":
                Assert.True(seenActive, "the backend was never seen running");
                Assert.Equal("deadline", body.GetProperty("cutOffBy").GetString());
                // The client's deadline, not the server's statement_timeout a second later. Elapsed time
                // cannot tell them apart reliably (a CPU-bound query's cancel took 0.8 s once), so the
                // runner's own line says which ended it.
                Assert.InRange(elapsed, Deadline, Deadline + TimeSpan.FromSeconds(3));
                Assert.Contains(logs.Entries, e => e.Text.Contains($"backend {pid} ended: cut off by the client's deadline", StringComparison.Ordinal));
                break;
            case "rows":
                Assert.Equal("rows", body.GetProperty("cutOffBy").GetString());
                Assert.Equal(10_000, body.GetProperty("rows").GetArrayLength());
                // Cancel before dispose (SRC-1, G4-3): disposing alone would drain until the server's own timeout.
                Assert.True(elapsed < Deadline, $"took {elapsed.TotalMilliseconds} ms");
                break;
            default:
                Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
                Assert.Equal(expected, body.GetProperty("sqlState").GetString());
                break;
        }
        await ReportApi.WaitUntilGoneAsync(pg, pid);
    }
}

[CollectionDefinition(nameof(ReportMemory), DisableParallelization = true)]
public sealed class ReportMemory;

/// <summary>
/// slice-04.md step 3 (R2-TST-6, G4-4): the byte cap holds the Api's memory, not only the response.
/// Allocation is counted process-wide, so this runs with no other class alongside it.
/// </summary>
[Collection(nameof(ReportMemory))]
public class ReportMemoryTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_cell_of_1e8_bytes_under_a_64_KB_cap_allocates_under_16_MB()
    {
        await using var api = ReportApi.Create(pg, null, ("Reporting:MaxBytes", "65536"));
        var client = api.CreateSignedInClient();
        // Warm: the token fetch, the routes, the runner and the text reader, so what is counted is the cell.
        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(client, "SELECT repeat('x', 100)")).StatusCode);

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var response = await ReportApi.RunAsync(client, "SELECT repeat('x', 1e8::int) AS wide");
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        var result = await ReportApi.JsonAsync(response);
        Assert.Equal("bytes", result.GetProperty("cutOffBy").GetString());
        Assert.Equal(0, result.GetProperty("rows").GetArrayLength());
        Assert.True(allocated < 16 * 1024 * 1024, $"{allocated / 1024 / 1024} MB allocated across the request");
    }
}

/// <summary>The crash test's own container: it is killed and restarted, and no other class shares it.</summary>
public sealed class CrashFixture : PostgresFixture;

/// <summary>
/// slice-04.md step 3 (S4-1, R2-TST-9, G4-6): one query can crash-restart PostgreSQL, which is
/// accepted; the Api must serve the next request after. The crash is a SIGKILL of the run's backend,
/// the path an out-of-memory kill takes, made deterministic.
/// </summary>
public class ReportCrashTests(CrashFixture pg) : IClassFixture<CrashFixture>
{
    [Fact]
    public async Task After_a_crash_restart_the_run_answers_503_and_the_first_request_succeeds()
    {
        await using var api = ReportApi.Create(pg, null, ("Reporting:Deadline", "00:01:00"));
        var client = api.CreateSignedInClient();

        // Warm the Api's pool: concurrent requests hold several connections, which stay pooled.
        foreach (var warm in await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.GetAsync("/api/v1/accounts", Ct))))
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        Assert.True(await ReportApi.SuperuserAsync<long>(pg, "SELECT count(*) FROM pg_stat_activity WHERE usename = 'portfolio_api'") > 0, "the pool is cold");

        // Testcontainers runs PostgreSQL with synchronous_commit=off (and fsync=off), so a commit can
        // return before its WAL is written, and crash recovery loses it. On CI the kill came within
        // 200 ms of the fixture's routine, and the API login's password was lost (28P01). Production
        // runs the defaults, where a commit is durable once it returns; CHECKPOINT gives this test that
        // starting point.
        await pg.ExecuteAsync("CHECKPOINT");

        var run = ReportApi.RunAsync(client, "SELECT pg_sleep(600)");
        var pid = await ReportApi.WaitForActiveAsync(pg);
        var kill = await pg.ExecInContainerAsync("sh", "-c", $"kill -9 {pid}");
        Assert.Equal(0, kill.ExitCode);

        var response = await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("The reporting database is not available.", (await ReportApi.JsonAsync(response)).GetProperty("title").GetString());

        await ReportApi.WaitUntilAsync(async () =>
        {
            try { return await ReportApi.SuperuserAsync<int>(pg, "SELECT 1") == 1; }
            catch (NpgsqlException) { return false; }
        }, TimeSpan.FromSeconds(30), "PostgreSQL to answer again");
        var (stdout, stderr) = await pg.ServerLogAsync();
        Assert.Contains("all server processes terminated; reinitializing", stdout + stderr);
        Assert.Equal(0, await ReportApi.SuperuserAsync<long>(pg, "SELECT count(*) FROM pg_stat_activity WHERE usename = 'portfolio_api'"));

        // The first request after: its pooled connections are dead, and EF's retrying strategy recovers (G4-6).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/accounts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReportApi.RunAsync(client, "SELECT 1")).StatusCode);
    }
}
