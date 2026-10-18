using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-02.md DoD 11, the integration half: cross-site writes are refused by two layers, and each
/// layer is shown on its OWN - layer 1 (content types) with no Sec-Fetch-Site header at all, layer 2
/// (Sec-Fetch-Site) with a content type layer 1 accepts. The browser half, both layers together
/// from a real second origin, is step 5's E2E.
/// </summary>
public class CrossSiteWriteTests(PostgresFixture pg) : ApiTests(pg)
{
    public static TheoryData<string> WriteEndpoints() => new("accounts", "balances", "sources", "imports");

    /// <summary>The three content types a plain HTML form can send without a CORS preflight.</summary>
    static readonly string[] FormTypes = ["application/x-www-form-urlencoded", "multipart/form-data; boundary=x", "text/plain"];

    const string NewAccountJson = """{"institutionName":"Example Bank","displayName":"Checking","openedOn":"2026-01-01"}""";
    const string CsvBody = "account,as_of_date,balance\nEX-1,2026-10-15,1\n";

    /// <summary>A URL and the body and content type that endpoint accepts. Creates the account it needs.</summary>
    async Task<(string Url, string Body, string AcceptedType)> TargetAsync(string endpoint)
    {
        var account = await NewAccountAsync(Pg, new DateOnly(2026, 1, 1));
        return endpoint switch
        {
            "accounts" => ("/api/v1/accounts", NewAccountJson, "application/json"),
            "balances" => ($"/api/v1/accounts/{account}/balances", """{"asOfDate":"2026-10-15","balance":"1"}""", "application/json"),
            "sources" => ($"/api/v1/accounts/{account}/sources", $$"""{"sourceSystem":"import:csv","sourceId":"EX-{{Guid.NewGuid():N}}"}""", "application/json"),
            _ => ("/api/v1/imports?fileName=x.csv", CsvBody, "text/csv"),
        };
    }

    /// <summary>Every table a write could touch.</summary>
    async Task<int> WrittenAsync()
    {
        await using var db = Pg.CreateContext();
        return await db.Accounts.CountAsync(Ct) + await db.AccountBalances.CountAsync(Ct)
             + await db.AccountSources.CountAsync(Ct) + await db.IngestRuns.CountAsync(Ct);
    }

    static HttpRequestMessage Post(string url, string body, string contentType, string? secFetchSite = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        if (secFetchSite is not null) request.Headers.Add("Sec-Fetch-Site", secFetchSite);
        return request;
    }

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Layer_1_every_write_endpoint_refuses_every_form_content_type(string endpoint)
    {
        foreach (var formType in FormTypes)
        {
            var (url, body, _) = await TargetAsync(endpoint);
            var before = await WrittenAsync();

            var response = await Client.SendAsync(Post(url, body, formType), Ct); // no Sec-Fetch-Site: layer 2 is not in play

            Assert.True(response.StatusCode == HttpStatusCode.UnsupportedMediaType, $"{endpoint} as {formType}: {(int)response.StatusCode}");
            Assert.Equal(before, await WrittenAsync());
        }
    }

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Layer_2_a_write_marked_cross_site_or_same_site_is_403_with_an_accepted_content_type(string endpoint)
    {
        foreach (var site in new[] { "cross-site", "same-site", "CROSS-SITE" })
        {
            var (url, body, accepted) = await TargetAsync(endpoint);
            var before = await WrittenAsync();

            var response = await Client.SendAsync(Post(url, body, accepted, site), Ct); // layer 1 would accept this

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{endpoint} from {site}: {(int)response.StatusCode}");
            Assert.Equal(before, await WrittenAsync());
        }
    }

    [Theory]
    [InlineData("same-origin")]
    [InlineData("none")]
    [InlineData(null)] // a non-browser client sends no header, and is not the threat
    public async Task A_write_from_the_app_a_typed_request_or_a_non_browser_client_is_accepted(string? site)
    {
        var response = await Client.SendAsync(Post("/api/v1/accounts", NewAccountJson, "application/json", site), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Reads_are_not_refused_whatever_the_site()
    {
        // The same-origin policy keeps a cross-site page from reading the response; refusing the
        // GET would only break legitimate navigation.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/imports");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(request, Ct)).StatusCode);
    }
}
