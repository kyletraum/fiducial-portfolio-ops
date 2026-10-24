using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 3, C3-1: the application's CSRF filter, on every kind of write the platform has
/// and one it does not yet (DELETE). A minimal app, so the filter is shown on its own: the Api's
/// existing endpoints get it in step 4, when the session requirement goes on (D11). Spike J ran the
/// same cases in a browser.
/// </summary>
public class CsrfFilterTests : IAsyncLifetime
{
    WebApplication app = null!;
    readonly Dictionary<string, int> writes = [];

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuth.TestScheme).AddTestScheme();
        builder.Services.AddAuthorization();
        builder.Services.AddCsrfTokens();
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // A lambda, not the method group: RouteHandlerAnalyzer 10.0.13 throws (AD0001) on a method
        // group from another assembly here, and a crashed analyzer checks nothing else in the file.
        app.MapGet("/csrf-token", (HttpContext h, Microsoft.AspNetCore.Antiforgery.IAntiforgery a) => CsrfTokens.Issue(h, a)).RequireAuthorization();
        var guarded = app.MapGroup("/w").RequireCsrfToken();
        guarded.MapPost("/json", (Thing t) => Wrote("post json"));
        guarded.MapPut("/json", (Thing t) => Wrote("put json"));
        guarded.MapDelete("/thing/{id:int}", (int id) => Wrote("delete"));
        guarded.MapPost("/raw", async (HttpRequest r) => { var body = await new StreamReader(r.Body).ReadToEndAsync(); return Wrote("raw " + body.Length); });
        guarded.MapPost("/exempt", () => Wrote("exempt")).WithMetadata(new CsrfTokens.Exempt());
        guarded.MapGet("/read", () => Results.Ok());
        await app.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    IResult Wrote(string what) { lock (writes) writes[what] = writes.GetValueOrDefault(what) + 1; return Results.Ok(); }

    HttpClient Client(string? user)
    {
        var client = new HttpClient(new CookieContainerHandler { InnerHandler = app.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://localhost") };
        if (user is not null) client.DefaultRequestHeaders.Add(TestAuth.Header, user);
        return client;
    }

    static async Task<string> TokenAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CsrfTokenResponse>("/csrf-token", Ct))!.Token;

    static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, HttpContent? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };
        if (token is not null) request.Headers.Add(CsrfTokens.HeaderName, token);
        return await client.SendAsync(request, Ct);
    }

    static StringContent JsonBody() => new("""{"name":"x"}""", Encoding.UTF8, "application/json");

    public static TheoryData<string, string, string?> Writes => new()
    {
        { "POST", "/w/json", "application/json" },
        { "PUT", "/w/json", "application/json" },
        { "DELETE", "/w/thing/7", null },
        { "POST", "/w/raw", "text/csv" },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task A_write_without_a_valid_token_is_refused_and_writes_nothing(string method, string path, string? type)
    {
        var alice = Client("alice");
        var token = await TokenAsync(alice);
        HttpContent? Body() => type switch { "application/json" => JsonBody(), "text/csv" => new StringContent("a,b\n1,2", Encoding.UTF8, "text/csv"), _ => null };

        foreach (var sent in new[] { null, "CfDJ8-not-a-token" })
            await AssertRefusedAsync(await SendAsync(alice, new HttpMethod(method), path, sent, Body()));
        Assert.Empty(writes);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, new HttpMethod(method), path, token, Body())).StatusCode);
        Assert.Single(writes);
    }

    [Fact]
    public async Task A_token_in_the_form_body_alone_is_not_read()
    {
        var alice = Client("alice");
        var token = await TokenAsync(alice);
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

        await AssertRefusedAsync(await SendAsync(alice, HttpMethod.Post, "/w/raw", null, form));
        Assert.Empty(writes);
    }

    [Fact]
    public async Task A_token_binds_to_its_user()
    {
        // C3-22: the page must fetch a token after every sign-in. Spike J showed it in a browser.
        var alice = Client("alice");
        var aliceToken = await TokenAsync(alice);
        var bob = new HttpClient(new CookieContainerHandler { InnerHandler = app.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://localhost") };
        bob.DefaultRequestHeaders.Add(TestAuth.Header, "bob");
        await TokenAsync(bob); // bob has his own antiforgery cookie

        await AssertRefusedAsync(await SendAsync(bob, HttpMethod.Post, "/w/json", aliceToken, JsonBody()));
        var anonymous = Client(null);
        await AssertRefusedAsync(await SendAsync(anonymous, HttpMethod.Post, "/w/json", aliceToken, JsonBody()));
        Assert.Empty(writes);
    }

    [Fact]
    public async Task An_exempt_route_and_a_safe_method_need_no_token()
    {
        var anonymous = Client(null);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(anonymous, HttpMethod.Post, "/w/exempt", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(anonymous, HttpMethod.Get, "/w/read", null)).StatusCode);
    }

    static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemTitles.CsrfTokenRefused, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString());
    }

    sealed record Thing(string Name);
}
