using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 4 (FR-6.4, DoD 4): saved reports, and their parameters bound, never written into
/// the SQL. The binding is probed with values the grant ALLOWS, so only the binding can explain the
/// result (TST-9): an interpolating runner would echo a different value and match more rows.
/// </summary>
public class SavedReportTests(ReportingFixture pg) : IClassFixture<ReportingFixture>, IAsyncLifetime
{
    /// <summary>Quotes, a second statement, a comment, both placeholder styles and a backslash.</summary>
    const string Hostile = """x'); SELECT 1; -- @y $1 \' " :z""";

    readonly LogCapture logs = new();
    WebApplicationFactory<Program> api = null!;
    HttpClient client = null!;

    public ValueTask InitializeAsync()
    {
        api = ReportApi.Create(pg, logs);
        client = api.CreateSignedInClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await api.DisposeAsync();
    }

    async Task<HttpResponseMessage> SaveAsync(string sql, params (string Name, string Type)[] parameters) =>
        await client.PostAsJsonAsync("/api/v1/reports/saved", new
        {
            name = $"Report {Guid.NewGuid():N}",
            sql,
            parameters = parameters.Select(p => new { name = p.Name, type = p.Type }),
        }, Ct);

    async Task<Guid> SavedAsync(string sql, params (string Name, string Type)[] parameters)
    {
        var response = await SaveAsync(sql, parameters);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await ReportApi.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    Task<HttpResponseMessage> RunAsync(Guid id, object parameters) =>
        client.PostAsJsonAsync($"/api/v1/reports/saved/{id}/run", new { parameters }, Ct);

    async Task<JsonElement> RowAsync(Guid id, object parameters)
    {
        var response = await RunAsync(id, parameters);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return Assert.Single((await ReportApi.JsonAsync(response)).GetProperty("rows").EnumerateArray());
    }

    // --- Bound, not interpolated (TST-9, SRC-4) ------------------------------------------------

    [Fact]
    public async Task The_echo_report_returns_a_hostile_value_byte_for_byte()
    {
        var echo = await SavedAsync("SELECT $1 AS v", ("v", "text"));
        Assert.Equal(Hostile, (await RowAsync(echo, new { v = Hostile }))[0].GetString());
        Assert.Equal(JsonValueKind.Null, (await RowAsync(echo, new Dictionary<string, string?> { ["v"] = null }))[0].ValueKind);
    }

    [Fact]
    public async Task The_effect_report_matches_nothing_for_an_injected_OR()
    {
        var effect = await SavedAsync("SELECT count(*) AS n FROM reporting.v_account WHERE account_name = $1", ("name", "text"));
        // Two accounts are visible, which is what an interpolated OR would count.
        var all = await ReportApi.JsonAsync(await ReportApi.RunAsync(client, "SELECT count(*) FROM reporting.v_account"));
        Assert.Equal(["[\"2\"]"], ReportApi.Rows(all));
        Assert.Equal("1", (await RowAsync(effect, new { name = "Everyday" }))[0].GetString());
        Assert.Equal("0", (await RowAsync(effect, new { name = "nobody' OR 'a'='a" }))[0].GetString());
    }

    [Fact]
    public async Task An_at_sign_in_a_literal_or_a_comment_is_left_alone()
    {
        var literal = await SavedAsync("SELECT '@name' AS lit, ':name' AS colon, $1 AS v -- @name :name", ("name", "text"));
        var row = await RowAsync(literal, new { name = "bound" });
        Assert.Equal(["@name", ":name", "bound"], row.EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Dates_and_numbers_are_bound_as_their_types()
    {
        var typed = await SavedAsync("SELECT $1 + 1 AS next_day, $2 * 2 AS doubled", ("day", "date"), ("amount", "numeric"));
        var row = await RowAsync(typed, new { day = "2026-02-28", amount = "-1.25" });
        Assert.Equal(["2026-03-01", "-2.50"], row.EnumerateArray().Select(c => c.GetString()));
    }

    // --- A wrong value is 400, before the database (DoD 4) --------------------------------------

    [Theory]
    [InlineData("""{"amount":"1; DROP TABLE account"}""", "amount")]
    [InlineData("""{"amount":"1e3"}""", "amount")]
    [InlineData("""{"amount":"1,000"}""", "amount")]
    [InlineData("""{"amount":"1","day":"2026-13-01"}""", "day")]
    [InlineData("""{"amount":"1","day":"01/02/2026"}""", "day")]
    [InlineData("""{"day":"2026-01-01"}""", "amount")]
    [InlineData("""{"amount":"1","day":"2026-01-01","extra":"x"}""", "extra")]
    public async Task A_value_not_of_its_type_answers_400_and_never_reaches_the_database(string parameters, string named)
    {
        var typed = await SavedAsync("SELECT $1 AS amount, $2 AS day", ("amount", "numeric"), ("day", "date"));
        var started = logs.Entries.Count(e => e.Text.Contains("Report run started", StringComparison.Ordinal));

        var response = await client.PostAsync($"/api/v1/reports/saved/{typed}/run",
            new StringContent($$"""{"parameters":{{parameters}}}""", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReportApi.JsonAsync(response);
        Assert.Equal("A parameter value does not match its declared type.", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(named, out _), problem.GetRawText());
        Assert.Equal(started, logs.Entries.Count(e => e.Text.Contains("Report run started", StringComparison.Ordinal)));
    }

    // --- Saving: PostgreSQL describes it on the reporting login, without running it -----------

    [Theory]
    [InlineData("SELEC 1", "42601")]
    [InlineData("SELECT 1; SELECT 2", "42601")]
    [InlineData("SELECT * FROM reporting.v_nothing", "42P01")]
    public async Task A_statement_PostgreSQL_cannot_describe_is_not_saved(string sql, string sqlState)
    {
        var response = await SaveAsync(sql, ("only", "text"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(sqlState, (await ReportApi.JsonAsync(response)).GetProperty("sqlState").GetString());
    }

    /// <summary>
    /// SRC-4: a <c>$n</c> beyond the declared parameters is refused at save. Described with the
    /// declared types alone, PostgreSQL inferred a type for an undeclared <c>$2</c> and the report was
    /// saved, and its run would fail to bind (found by running this test); the server's own count, by
    /// preparing it, refuses it now. A declared parameter left unused is saved: it is bound and ignored.
    /// </summary>
    [Theory]
    [InlineData("SELECT $1, $2", 1, "mismatch")]
    // A gap PostgreSQL cannot type ($2) is refused by its own describe: 42P18.
    [InlineData("SELECT $3", 1, "42P18")]
    [InlineData("SELECT $1", 1, "saved")]
    [InlineData("SELECT 1", 1, "saved")]
    public async Task A_parameter_beyond_those_declared_is_not_saved(string sql, int declared, string expected)
    {
        var response = await SaveAsync(sql, [.. Enumerable.Range(1, declared).Select(i => ($"p{i}", "text"))]);
        if (expected == "saved")
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await ReportApi.JsonAsync(response);
        if (expected == "mismatch")
            Assert.Equal("The statement's parameters do not match those declared.", problem.GetProperty("title").GetString());
        else
            Assert.Equal(expected, problem.GetProperty("sqlState").GetString());
    }

    [Fact]
    public async Task A_column_with_no_reader_is_not_saved()
    {
        var response = await SaveAsync("SELECT 'pg_class'::regclass AS rel");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("A column has a type reports cannot return.", (await ReportApi.JsonAsync(response)).GetProperty("title").GetString());
    }

    /// <summary>
    /// Describing is not running: a save of a five-second sleep returns at once. And privileges are
    /// checked at execution, not description, so a report over a table the login cannot read IS
    /// saved; it is refused when it runs.
    /// </summary>
    [Fact]
    public async Task Saving_runs_nothing_and_a_refused_table_is_refused_at_run_time()
    {
        var clock = Stopwatch.StartNew();
        await SavedAsync("SELECT pg_sleep(5)");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"took {clock.Elapsed.TotalMilliseconds} ms");

        var beneath = await SavedAsync("SELECT count(*) FROM public.account");
        var run = await RunAsync(beneath, new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, run.StatusCode);
        Assert.Equal("42501", (await ReportApi.JsonAsync(run)).GetProperty("sqlState").GetString());
    }

    [Theory]
    [InlineData("""{"name":"","sql":"SELECT 1"}""", "name")]
    [InlineData("""{"name":"x","sql":" "}""", "sql")]
    [InlineData("""{"name":"x","sql":"SELECT $1","parameters":[{"name":"Bad Name","type":"text"}]}""", "parameters")]
    [InlineData("""{"name":"x","sql":"SELECT $1","parameters":[{"name":"a","type":"text"},{"name":"a","type":"date"}]}""", "parameters")]
    public async Task A_malformed_save_is_refused_before_the_database(string body, string field)
    {
        var response = await client.PostAsync("/api/v1/reports/saved", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await ReportApi.JsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    // --- Create, list, get, delete; no edit (S4-7) ------------------------------------------

    [Fact]
    public async Task A_report_is_saved_listed_read_and_deleted()
    {
        var name = $"Balances on a day {Guid.NewGuid():N}";
        var created = await client.PostAsJsonAsync("/api/v1/reports/saved", new
        {
            name,
            description = "Every live balance on one day.",
            sql = "SELECT account_id, balance FROM reporting.v_balance_daily WHERE as_of_date = $1",
            parameters = new[] { new { name = "day", type = "date" } },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = await ReportApi.JsonAsync(created);
        var id = report.GetProperty("id").GetGuid();
        Assert.Equal("""[{"name":"day","type":"date"}]""", report.GetProperty("parameters").GetRawText());

        var listed = await ReportApi.JsonAsync(await client.GetAsync("/api/v1/reports/saved", Ct));
        Assert.Contains(listed.EnumerateArray(), r => r.GetProperty("name").GetString() == name);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/reports/saved/{id}", Ct)).StatusCode);

        var again = await client.PostAsJsonAsync("/api/v1/reports/saved", new { name, sql = "SELECT 1" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/reports/saved/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/reports/saved/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/reports/saved/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(id, new { })).StatusCode);
    }

    // --- The same slot as an ad hoc run (S4-6, R2-TST-10) ---------------------------------------

    [Fact]
    public async Task A_saved_run_gets_429_while_another_run_holds_the_slot()
    {
        var saved = await SavedAsync("SELECT 1");
        await using var gated = ReportApi.Create(pg, null, ("Reporting:Deadline", "00:00:03"));
        var browser = gated.CreateSignedInClient();
        var a = ReportApi.RunAsync(browser, "SELECT pg_sleep(600)");
        var pid = await ReportApi.WaitForActiveAsync(pg);

        var b = await browser.PostAsJsonAsync($"/api/v1/reports/saved/{saved}/run", new { parameters = new { } }, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, b.StatusCode);
        Assert.Equal([pid], await ReportApi.ActivePidsAsync(pg));

        Assert.Equal("deadline", (await ReportApi.JsonAsync(await a)).GetProperty("cutOffBy").GetString());
        await ReportApi.WaitUntilGoneAsync(pg, pid);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsJsonAsync($"/api/v1/reports/saved/{saved}/run", new { parameters = new { } }, Ct)).StatusCode);
    }
}
