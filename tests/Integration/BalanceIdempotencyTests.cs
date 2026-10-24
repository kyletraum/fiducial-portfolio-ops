using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// D-030 Amendment 1's deferral, discharged in slice 02: POST /balances is idempotent by content.
/// Through the real API and the real database - the endpoint's status code is the contract.
/// </summary>
public class BalanceIdempotencyTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    WebApplicationFactory<Program> factory = null!;

    public ValueTask InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", pg.ConnectionString).WithTestAuth());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    async Task<(HttpStatusCode Status, Guid Id)> PostAsync(HttpClient client, Guid account, string balance)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/accounts/{account}/balances", new { asOfDate = "2026-10-15", balance }, Ct);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_same_amount_again_is_200_with_the_existing_row_and_writes_nothing()
    {
        var account = await NewAccountAsync(pg, new DateOnly(2026, 1, 1));
        using var client = factory.CreateSignedInClient();

        var first = await PostAsync(client, account, "100.00");
        var again = await PostAsync(client, account, "100.0000"); // the same amount, written differently

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(first.Id, again.Id);
        await using var db = pg.CreateContext();
        Assert.Equal(1, await db.AccountBalances.CountAsync(b => b.AccountId == account, Ct));
    }

    [Fact]
    public async Task A_different_amount_still_restates()
    {
        var account = await NewAccountAsync(pg, new DateOnly(2026, 1, 1));
        using var client = factory.CreateSignedInClient();

        var first = await PostAsync(client, account, "100");
        var changed = await PostAsync(client, account, "110");

        Assert.Equal(HttpStatusCode.Created, changed.Status);
        Assert.NotEqual(first.Id, changed.Id);
        await using var db = pg.CreateContext();
        Assert.Equal(first.Id, (await db.AccountBalances.SingleAsync(b => b.Id == changed.Id, Ct)).Supersedes);
    }
}
