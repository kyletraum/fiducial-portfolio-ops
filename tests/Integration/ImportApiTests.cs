using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>A WebApplicationFactory on the fixture's database, in Production, as a container runs.</summary>
public abstract class ApiTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    protected PostgresFixture Pg { get; } = pg;
    WebApplicationFactory<Program> factory = null!;
    protected HttpClient Client { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", Pg.ConnectionString));
        Client = factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    protected static JsonElement Json(HttpResponseMessage r) =>
        JsonDocument.Parse(r.Content.ReadAsStringAsync(Ct).GetAwaiter().GetResult()).RootElement;

    protected Task<HttpResponseMessage> UploadAsync(byte[] body, string? contentType, string? fileName = "balances.csv")
    {
        var content = new ByteArrayContent(body);
        if (contentType is not null) content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var url = fileName is null ? "/api/v1/imports" : $"/api/v1/imports?fileName={Uri.EscapeDataString(fileName)}";
        return Client.PostAsync(url, content, Ct);
    }

    protected async Task<HttpResponseMessage> BindAsync(Guid account, string system, string id) =>
        await Client.PostAsJsonAsync($"/api/v1/accounts/{account}/sources", new { sourceSystem = system, sourceId = id }, Ct);
}

/// <summary>slice-02.md step 3: binding an identifier, uploading a file, and the run history.</summary>
public class ImportApiTests(PostgresFixture pg) : ApiTests(pg)
{
    [Fact]
    public async Task Binds_an_identifier_and_lists_it()
    {
        var account = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));

        var bound = await BindAsync(account, "import:csv", "EXAMPLE-CHK-1");
        var list = await Client.GetAsync($"/api/v1/accounts/{account}/sources", Ct);

        Assert.Equal(HttpStatusCode.Created, bound.StatusCode);
        var s = Assert.Single(Json(list).EnumerateArray());
        Assert.Equal(("import:csv", "EXAMPLE-CHK-1"), (s.GetProperty("sourceSystem").GetString(), s.GetProperty("sourceId").GetString()));
    }

    [Theory]
    [InlineData("manual", "EXAMPLE-1", "sourceSystem")]
    [InlineData("import:qfx", "EXAMPLE-1", "sourceSystem")]
    [InlineData("import:csv", " EXAMPLE-1", "sourceId")]
    [InlineData("import:csv", "", "sourceId")]
    public async Task Refuses_a_binding_it_could_never_import_against(string system, string id, string field)
    {
        var account = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));

        var response = await BindAsync(account, system, id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True(Json(response).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task A_bound_identifier_is_never_rebound_to_this_account_or_another()
    {
        var first = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));
        var second = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));
        var id = $"EXAMPLE-{Guid.NewGuid():N}"[..20];
        await BindAsync(first, "import:ofx", id);

        Assert.Equal(HttpStatusCode.Conflict, (await BindAsync(first, "import:ofx", id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await BindAsync(second, "import:ofx", id)).StatusCode);
    }

    [Fact]
    public async Task Binding_to_an_unknown_account_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await BindAsync(Guid.CreateVersion7(), "import:csv", "EXAMPLE-1")).StatusCode);

    [Fact]
    public async Task Uploads_a_file_and_the_run_is_the_resource()
    {
        var account = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));
        var id = $"EXAMPLE-{Guid.NewGuid():N}"[..20];
        await BindAsync(account, "import:csv", id);

        var response = await UploadAsync(Csv((id, "2026-10-15", "1234.56")), "text/csv; charset=utf-8", "my balances.csv");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = Json(response);
        Assert.Equal(("succeeded", 1, 1), (run.GetProperty("status").GetString(), run.GetProperty("rowsRead").GetInt32(), run.GetProperty("created").GetInt32()));
        Assert.Equal("my balances.csv", run.GetProperty("fileName").GetString());
        Assert.Empty(run.GetProperty("errors").EnumerateArray());
        Assert.Equal($"/api/v1/imports/{run.GetProperty("id").GetString()}", response.Headers.Location!.OriginalString);

        var fetched = Json(await Client.GetAsync(response.Headers.Location, Ct));
        Assert.Equal(run.GetProperty("fileSha256").GetString(), fetched.GetProperty("fileSha256").GetString());
    }

    [Fact]
    public async Task A_refused_file_is_still_201_with_a_failed_run_and_its_reasons()
    {
        var response = await UploadAsync(Csv(("EXAMPLE-NEVER-BOUND", "2026-10-15", "1")), "text/csv");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = Json(response);
        Assert.Equal("failed", run.GetProperty("status").GetString());
        var error = Assert.Single(run.GetProperty("errors").EnumerateArray());
        Assert.Equal("line 2", error.GetProperty("location").GetString());

        // And the history returns the stored reasons, not only the response.
        var stored = Json(await Client.GetAsync($"/api/v1/imports/{run.GetProperty("id").GetString()}", Ct));
        Assert.Equal("line 2", Assert.Single(stored.GetProperty("errors").EnumerateArray()).GetProperty("location").GetString());
    }

    [Fact]
    public async Task An_OFX_import_shows_its_run_and_its_stamp_on_the_balance()
    {
        var account = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));
        var id = $"EXAMPLE-{Guid.NewGuid():N}"[..20];
        await BindAsync(account, "import:ofx", id);

        var run = Json(await UploadAsync(Ofx(id, "321.00", "2026-10-15", "230000", "[-5:CDT]"), "application/x-ofx", "statement.ofx"));
        var balance = Assert.Single(Json(await Client.GetAsync($"/api/v1/accounts/{account}/balances", Ct)).EnumerateArray());

        Assert.Equal(run.GetProperty("id").GetString(), balance.GetProperty("ingestRunId").GetString());
        Assert.Equal("2026-10-15", balance.GetProperty("asOfDate").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-10-16T04:00:00Z"), balance.GetProperty("sourceAsOf").GetDateTimeOffset());
        Assert.Equal("export", balance.GetProperty("sourceStrength").GetString());
    }

    [Fact]
    public async Task The_history_is_newest_first_and_bounded()
    {
        await UploadAsync("x"u8.ToArray(), "text/csv", "older.csv");
        await UploadAsync("x"u8.ToArray(), "text/csv", "newer.csv");

        var runs = Json(await Client.GetAsync("/api/v1/imports?limit=2", Ct)).EnumerateArray().ToList();

        Assert.Equal(["newer.csv", "older.csv"], runs.Select(r => r.GetProperty("fileName").GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.GetAsync("/api/v1/imports?limit=0", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.GetAsync("/api/v1/imports?limit=501", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("application/vnd.ms-excel")] // what Windows reports for a .csv: the web sends the type itself
    public async Task Any_other_content_type_is_415_before_a_run_begins(string? type)
    {
        var before = await RunCountAsync();

        var response = await UploadAsync(Csv(("EXAMPLE-1", "2026-10-15", "1")), type);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(before, await RunCountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad\nname.csv")]
    public async Task A_missing_or_unsafe_file_name_is_422(string? name)
    {
        var response = await UploadAsync(Csv(("EXAMPLE-1", "2026-10-15", "1")), "text/csv", name);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task One_byte_over_the_limit_is_413_with_or_without_a_Content_Length()
    {
        var before = await RunCountAsync();
        var over = new byte[(1 << 20) + 1];

        var withLength = await UploadAsync(over, "text/csv");
        var chunked = await Client.PostAsync("/api/v1/imports?fileName=x.csv",
            new StreamContent(new UnknownLengthStream(over)) { Headers = { ContentType = new("text/csv") } }, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, withLength.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        Assert.Equal(before, await RunCountAsync()); // refused before a run begins
    }

    [Fact]
    public async Task Exactly_the_limit_is_read_and_becomes_a_run()
    {
        var response = await UploadAsync(new byte[1 << 20], "text/csv");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    async Task<int> RunCountAsync()
    {
        await using var db = Pg.CreateContext();
        return await db.IngestRuns.CountAsync(Ct);
    }

    /// <summary>A stream that will not say how long it is, so HttpClient sends it chunked.</summary>
    sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
