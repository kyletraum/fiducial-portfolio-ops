using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 2's harness for the authentication classes: the real Api in Production
/// configuration over this fixture's PostgreSQL, a setup code per factory, and clients on
/// https://localhost. .NET's cookie container stores a Secure cookie from http://localhost and never
/// sends it back (round 2 TST-6, EXECUTED), and every Identity cookie is Secure. So IsHttps is true
/// here and false in production; the E2E covers plain HTTP.
/// </summary>
public class AuthFixture : PostgresFixture
{
    /// <summary>The page's origin, as the authenticator reports it and the Api is told to accept.</summary>
    public const string Origin = "http://localhost:5173";

    public static readonly Uri Https = new("https://localhost");

    /// <summary>An Api with this setup code, and optionally a pinned clock.</summary>
    public WebApplicationFactory<Program> Api(string setupCode, TimeProvider? clock = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production")
             .UseSetting("ConnectionStrings:portfolio", ConnectionString)
             .UseSetting("Enrolment:SetupCode", setupCode)
             .UseSetting("Auth:AllowedOrigins", Origin);
            if (clock is not null)
                b.ConfigureTestServices(s => s.AddSingleton(clock));
        });

    public static HttpClient Client(WebApplicationFactory<Program> api, bool cookies = true) =>
        api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = Https, HandleCookies = cookies });

    /// <summary>A setup code of the configured shape, fresh each call.</summary>
    public static string NewSetupCode() =>
        string.Concat(Enumerable.Range(0, Domain.SetupCode.Length).Select(_ => "abcdefghjkmnpqrstuvwxyz"[Random.Shared.Next(23)]));

    /// <summary>What reset-owner (step 7) will do to the user; used_setup_code and audit_event stay.</summary>
    public Task ResetOwnerAsync() => ExecuteAsync("DELETE FROM identity_user");
}

/// <summary>The two ceremonies, as the page will run them.</summary>
static class Ceremony
{
    public static async Task<HttpResponseMessage> EnrolAsync(HttpClient client, SoftAuthenticator authenticator, string setupCode,
        string userName = "owner", SoftAuthenticator.Variant? variant = null)
    {
        var options = await client.PostAsJsonAsync("/api/v1/auth/enrolment/options", new { setupCode, userName }, Ct);
        if (!options.IsSuccessStatusCode)
            return options;
        var credential = authenticator.Register(await options.Content.ReadAsStringAsync(Ct), variant);
        return await client.PostAsync("/api/v1/auth/enrolment", Json($$"""{"setupCode":"{{setupCode}}","credential":{{credential}}}"""), Ct);
    }

    public static async Task<string> SignInOptionsAsync(HttpClient client) =>
        await (await client.PostAsync("/api/v1/auth/sign-in/options", null, Ct)).EnsureSuccessStatusCode().Content.ReadAsStringAsync(Ct);

    public static async Task<HttpResponseMessage> SignInAsync(HttpClient client, SoftAuthenticator authenticator, SoftAuthenticator.Variant? variant = null) =>
        await client.PostAsync("/api/v1/auth/sign-in", Json(authenticator.Assert(await SignInOptionsAsync(client), variant)), Ct);

    public static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
