using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Portfolio.Infrastructure;
using static Portfolio.Integration.AuthFixture;
using static Portfolio.Integration.Ceremony;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 5 and DoD 7: no secret reaches a log, a trace or an error response. The real
/// secrets of a session (the setup code, a passkey assertion, the session cookie, the CSRF token and
/// its cookie) are driven through enrolment, sign-in, a failed and a malformed sign-in, sign-out and a
/// 500, and a canary is pasted into every field of every anonymous endpoint and into a query string.
/// Everything is captured at Trace, in Production configuration, and scanned in every encoding a
/// secret is likely to take.
/// </summary>
/// <remarks>
/// The redaction filter is written only if this goes red (the middle cut). Secrets travel only in
/// POST bodies and cookies, so what this proves is that nothing on the way writes them down.
/// </remarks>
public class SecretsStayOutTests(AuthFixture db) : IClassFixture<AuthFixture>
{
    (WebApplicationFactory<Program> Api, LogCapture Logs, Spans Spans) Api(string setupCode, string? connectionString = null)
    {
        var logs = new LogCapture();
        var spans = new Spans();
        var api = db.Api(setupCode).WithWebHostBuilder(b =>
        {
            b.UseSetting("Logging:LogLevel:Default", "Trace")
             .UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace")
             .UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Trace")
             // The category the Api pins off in code: configuration asking for it must not win.
             .UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics", "Trace")
             .ConfigureLogging(l => l.AddProvider(logs))
             .ConfigureTestServices(s => s.ConfigureOpenTelemetryTracerProvider(t => t.AddInMemoryExporter(spans)));
            if (connectionString is not null)
                b.UseSetting("ConnectionStrings:portfolio", connectionString)
                 .UseSetting("Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:DisableRetry", "true")
                 .WithTestAuth();
        });
        return (api, logs, spans);
    }

    static HttpClient Recording(WebApplicationFactory<Program> api, List<string> bodies) =>
        api.CreateDefaultClient(Https, new ErrorBodies(bodies), new CookieContainerHandler());

    [Fact]
    public async Task No_secret_reaches_a_log_a_trace_or_an_error_body()
    {
        await db.ResetOwnerAsync();
        var setupCode = NewSetupCode();
        var pasted = "canary" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var (api, logs, spans) = Api(setupCode);
        await using var _ = api;
        var bodies = new List<string>();
        var browser = Recording(api, bodies);
        var owner = new SoftAuthenticator(Origin);

        // Enrolment, then a fresh sign-in whose assertion is kept to look for.
        Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(browser, owner, setupCode)).StatusCode);
        var signedIn = Recording(api, bodies);
        var assertion = JsonNode.Parse(owner.Assert(await SignInOptionsAsync(signedIn)))!;
        var signIn = await signedIn.PostAsync("/api/v1/auth/sign-in", Json(assertion.ToJsonString()), Ct);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var session = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-portfolio=", StringComparison.Ordinal)).Split(';')[0]["__Host-portfolio=".Length..];
        var token = (await signedIn.GetFromJsonAsync<Api.CsrfTokenResponse>("/api/v1/auth/csrf-token", Ct))!.Token;

        // A failed and a malformed sign-in, and the health route once (the trace guard's span).
        await SignInAsync(Recording(api, bodies), new SoftAuthenticator(Origin, registeredElsewhere: SoftAuthenticator.ForeignUserHandle()));
        await SignInAsync(Recording(api, bodies), owner, new() { MalformedCbor = true });
        await browser.GetAsync("/api/v1/system/health", Ct);

        // The canary in every field of every anonymous endpoint, and in a query string. Each must reach
        // its handler, so a 429 would mean this proved nothing (TST-13; the fixture raises the limiter).
        var anonymous = Recording(api, bodies);
        // "@@" stands for the canary: the JSON's closing braces defeat an interpolated raw string.
        var injected = new (string Path, string Body)[]
        {
            ("/api/v1/auth/enrolment/options?q=@@", """{"setupCode":"@@","userName":"@@"}"""),
            ("/api/v1/auth/enrolment?q=@@", """{"setupCode":"@@","credential":{"id":"@@","rawId":"@@","type":"@@","clientExtensionResults":{},"response":{"clientDataJSON":"@@","attestationObject":"@@"}}}"""),
            ("/api/v1/auth/sign-in/options?q=@@", """{"userName":"@@"}"""),
            ("/api/v1/auth/sign-in?q=@@", """{"id":"@@","rawId":"@@","type":"@@","clientExtensionResults":{},"response":{"clientDataJSON":"@@","authenticatorData":"@@","signature":"@@","userHandle":"@@"}}"""),
        };
        foreach (var (path, body) in injected)
            Assert.NotEqual(HttpStatusCode.TooManyRequests,
                (await anonymous.PostAsync(path.Replace("@@", pasted), Json(body.Replace("@@", pasted)), Ct)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await anonymous.GetAsync($"/api/v1/auth/state?q={pasted}", Ct)).StatusCode);

        // A 500, with the session cookie on the request.
        var (broken, brokenLogs, brokenSpans) = Api(setupCode, "Host=localhost;Port=1;Timeout=2");
        await using (broken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/accounts?q={pasted}");
            request.Headers.Add(TestAuth.Header, "owner");
            request.Headers.Add("Cookie", $"__Host-portfolio={session}");
            Assert.Equal(HttpStatusCode.InternalServerError, (await Recording(broken, bodies).SendAsync(request, Ct)).StatusCode);
        }

        // Sign-out, with the token.
        using (var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out"))
        {
            signOut.Headers.Add("X-CSRF-TOKEN", token);
            Assert.Equal(HttpStatusCode.NoContent, (await signedIn.SendAsync(signOut, Ct)).StatusCode);
        }

        // The non-empty guards: the capture saw the host's own lines and a span, so a silent provider
        // or a missing exporter cannot pass (G-22).
        var allLogs = logs.Entries.Concat(brokenLogs.Entries).ToList();
        Assert.Contains(allLogs, e => e.Category.StartsWith("Microsoft.AspNetCore.Routing", StringComparison.Ordinal));
        Assert.Contains(allLogs, e => e.Level == LogLevel.Trace);
        // The request lines that carried the query string stay off (step 5's one finding).
        Assert.DoesNotContain(allLogs, e => e.Category == "Microsoft.AspNetCore.Hosting.Diagnostics" && e.Level < LogLevel.Warning);
        Assert.Contains(spans.Snapshot(), s => s.TagObjects.Any(t => t.Key == "url.path" && Equals(t.Value, "/api/v1/system/health")));
        // G-24: EF writes parameter values only with sensitive-data logging on.
        var options = api.Services.CreateScope().ServiceProvider.GetRequiredService<DbContextOptions<PortfolioDbContext>>();
        Assert.False(options.FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled ?? false);

        var secrets = new (string, string)[]
        {
            ("the setup code", setupCode),
            ("the passkey signature", assertion["response"]!["signature"]!.GetValue<string>()),
            ("the passkey client data", assertion["response"]!["clientDataJSON"]!.GetValue<string>()),
            ("the session cookie", session),
            ("the CSRF token", token),
            ("the pasted canary", pasted),
            // Slice 04 (TST-14): the two logins' passwords, which this Api holds in its connection strings.
            ("the API login's password", db.ApiPassword),
            ("the reporting login's password", db.ReportingPassword),
        };
        var auditRows = await db.ScalarAsync<string>("SELECT coalesce(string_agg(row_to_json(a)::text, ' '), '') FROM audit_event a");
        var haystack = allLogs.Select(e => $"[{e.Category} {e.Level}] {e.Text}")
            .Concat(spans.Snapshot().Concat(brokenSpans.Snapshot()).Select(SecretScan.Describe))
            .Concat(bodies)
            .Append(auditRows ?? "");

        var hits = SecretScan.Hits(secrets, haystack).ToList();
        Assert.True(hits.Count == 0, "found:\n" + string.Join("\n", hits.Take(20)));
    }

    /// <summary>
    /// slice-04.md step 3 (TST-14, R2-TST-11): the report routes, through every way a run ends. Each
    /// kind of PostgreSQL refusal; and the two failures that are not a <see cref="Npgsql.PostgresException"/>
    /// from the statement, where Npgsql's messages carry host, port and user: a wrong password
    /// (28P01) and a closed port, the latter holding the REAL password in its connection string. A
    /// pasted canary rides in the SQL text, which the app never logs or traces (S4-3).
    /// </summary>
    [Fact]
    public async Task No_database_password_or_report_text_reaches_a_log_a_trace_or_an_error_body()
    {
        var pasted = "canary" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var wrongSecret = PostgresFixture.NewPassword();
        var wrong = new Npgsql.NpgsqlConnectionStringBuilder(db.ReportingConnectionString);
        wrong["Password"] = wrongSecret;
        var closed = new Npgsql.NpgsqlConnectionStringBuilder(db.ReportingConnectionString) { Port = 1, Timeout = 2 };
        var bodies = new List<string>();
        var allLogs = new List<(string Category, LogLevel Level, string Text)>();
        var allSpans = new List<Activity>();

        async Task DriveAsync(string reporting, HttpStatusCode schema, params (string Sql, HttpStatusCode Status)[] runs)
        {
            var (traced, logs, spans) = Api(NewSetupCode());
            await using var api = traced.WithWebHostBuilder(b => b
                .UseSetting("ConnectionStrings:reporting", reporting)
                .UseSetting("Reporting:Deadline", "00:00:01")
                .WithTestAuth());
            var client = api.CreateSignedInClient(outermost: new ErrorBodies(bodies));
            foreach (var (sql, status) in runs)
                Assert.Equal(status, (await client.PostAsJsonAsync("/api/v1/reports/run", new { sql }, Ct)).StatusCode);
            Assert.Equal(schema, (await client.GetAsync("/api/v1/reports/schema", Ct)).StatusCode);
            allLogs.AddRange(logs.Entries);
            allSpans.AddRange(spans.Snapshot());
            await traced.DisposeAsync();
        }

        await DriveAsync(db.ReportingConnectionString, HttpStatusCode.OK,
            ($"SELECT '{pasted}' AS pasted", HttpStatusCode.OK),
            ($"SELECT '{pasted}' FROM public.account", HttpStatusCode.UnprocessableEntity),   // 42501
            ($"SELECT '{pasted}' AS pasted; SELECT 1", HttpStatusCode.UnprocessableEntity),   // 42601
            ($"SELECT '{pasted}', 1 / 0", HttpStatusCode.UnprocessableEntity),                // 22012
            ($"SELECT '{pasted}', pg_sleep(600)", HttpStatusCode.OK));                        // 57014: cut off by the deadline
        await DriveAsync(wrong.ConnectionString, HttpStatusCode.ServiceUnavailable, ("SELECT 1", HttpStatusCode.ServiceUnavailable));
        await DriveAsync(closed.ConnectionString, HttpStatusCode.ServiceUnavailable, ("SELECT 1", HttpStatusCode.ServiceUnavailable));

        // Not vacuous: the runner ran and was captured, both connection failures were met, spans were exported.
        Assert.Contains(allLogs, e => e.Text.Contains("Report run started on backend", StringComparison.Ordinal));
        Assert.Contains(allLogs, e => e.Text.Contains("could not connect", StringComparison.Ordinal) && e.Text.Contains("28P01", StringComparison.Ordinal));
        Assert.Contains(allLogs, e => e.Text.Contains("could not connect", StringComparison.Ordinal) && !e.Text.Contains("28P01", StringComparison.Ordinal));
        Assert.NotEmpty(allSpans);
        Assert.Equal(7, bodies.Count); // three refusals, two run failures and two schema failures

        // A problem passes MessageText and SqlState only: no connection string, host or user in any body.
        foreach (var fragment in new[] { "Host=", "Username=", "portfolio_reporting", "127.0.0.1", "localhost" })
            Assert.DoesNotContain(bodies, b => b.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        var secrets = new (string, string)[]
        {
            ("the API login's password", db.ApiPassword),
            ("the reporting login's password", db.ReportingPassword),
            ("the wrong reporting password", wrongSecret),
            ("the canary in the SQL", pasted),
        };
        var haystack = allLogs.Select(e => $"[{e.Category} {e.Level}] {e.Text}").Concat(allSpans.Select(SecretScan.Describe)).Concat(bodies);
        var hits = SecretScan.Hits(secrets, haystack).ToList();
        Assert.True(hits.Count == 0, "found:\n" + string.Join("\n", hits.Take(20)));
    }

    [Fact]
    public async Task The_scan_finds_a_secret_logged_at_Debug_in_any_encoding()
    {
        // Seen failing, and kept: the capture, the levels and the scanner together, in the same
        // Production configuration, find a secret the moment anything logs one.
        var secret = "canary" + Guid.NewGuid().ToString("N");
        var (api, logs, _) = Api(NewSetupCode());
        await using var _ = api;
        await api.CreateClient().GetAsync("/api/v1/system/health", Ct);
        var log = api.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Portfolio.Leak");

        log.LogDebug("plain {Value}", secret);
        log.LogDebug("inside a base64 blob {Value}", Convert.ToBase64String([0x01, .. System.Text.Encoding.UTF8.GetBytes(secret), 0x02]));
        using (log.BeginScope(new Dictionary<string, object> { ["Escaped"] = Uri.EscapeDataString(secret + "/x") }))
            log.LogDebug("in a scope");

        var found = SecretScan.Hits([("secret", secret)], logs.Entries.Select(e => e.Text)).ToList();
        Assert.Contains(found, h => h.Contains("plain"));
        Assert.Contains(found, h => h.Contains("inside a base64 blob"));
        Assert.Contains(found, h => h.Contains("in a scope"));
    }
}
