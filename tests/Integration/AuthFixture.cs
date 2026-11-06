using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Domain;
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
             .UseSetting("ConnectionStrings:portfolio", ApiConnectionString)
             .UseSetting("ConnectionStrings:reporting", ReportingConnectionString)
             .UseSetting("Enrolment:SetupCode", setupCode)
             .UseSetting("Auth:AllowedOrigins", Origin)
             // These tests run many ceremonies a minute; the limiter's own tests set it (TST-13).
             .UseSetting("RateLimits:SignIn:PermitLimit", "100000");
            b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter, LoopbackConnection>());
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

    /// <summary>Every audit row's id now: what <see cref="NewAuditEventAsync"/> compares against.</summary>
    public async Task<Guid[]> AuditIdsAsync()
    {
        await using var context = CreateSuperuserContext();
        return await context.AuditEvents.Select(e => e.Id).ToArrayAsync(Ct);
    }

    /// <summary>The one audit row written since <paramref name="seen"/> was taken.</summary>
    public async Task<AuditEvent> NewAuditEventAsync(Guid[] seen)
    {
        await using var context = CreateSuperuserContext();
        return Assert.Single(await context.AuditEvents.Where(e => !seen.Contains(e.Id)).ToListAsync(Ct));
    }
}

/// <summary>
/// TestServer leaves the connection's remote address null; Kestrel on loopback reports 127.0.0.1.
/// Set it as Kestrel would, ahead of the app's pipeline, so the audit row's address is the one the
/// app saw (<see cref="AuditShape"/>).
/// </summary>
sealed class LoopbackConnection : IStartupFilter
{
    public static readonly IPAddress Address = IPAddress.Loopback;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, call) =>
        {
            context.Connection.RemoteIpAddress ??= Address;
            return call(context);
        });
        next(app);
    };
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

    /// <summary>Signs out as the page will: with the signed-in user's CSRF token.</summary>
    public static async Task<HttpResponseMessage> SignOutAsync(HttpClient client)
    {
        var token = (await client.GetFromJsonAsync<Api.CsrfTokenResponse>("/api/v1/auth/csrf-token", Ct))!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out");
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request, Ct);
    }
}
