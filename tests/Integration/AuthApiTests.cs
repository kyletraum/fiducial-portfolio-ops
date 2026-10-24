using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Portfolio.Api;
using Portfolio.Domain;
using static Portfolio.Integration.AuthFixture;
using static Portfolio.Integration.Ceremony;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 2: enrolment by setup code, passkey sign-in and sign-out, through the real Api and
/// Identity, driven by <see cref="SoftAuthenticator"/>. Tests in a class run in turn, and each starts
/// from no owner, with a setup code of its own.
/// </summary>
public class AuthApiTests(AuthFixture db) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await db.ResetOwnerAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ---- Enrolment ----

    [Fact]
    public async Task Before_enrolment_the_state_says_so_and_names_the_environment()
    {
        await using var api = db.Api(NewSetupCode());
        var state = await Client(api).GetFromJsonAsync<AuthStateResponse>("/api/v1/auth/state", Ct);

        Assert.Equal(new AuthStateResponse(false, "Production"), state);
    }

    [Fact]
    public async Task Enrolment_creates_the_owner_with_a_passkey_spends_the_code_and_signs_in()
    {
        // Gate GT-3: Identity accepts the software authenticator's "none" attestation.
        var code = NewSetupCode();
        await using var api = db.Api(code);
        var client = Client(api);

        var audited = await AuditCountAsync("enrolment", "owner");
        var enrolled = await EnrolAsync(client, new SoftAuthenticator(Origin), code, "owner");

        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        Assert.Equal("owner", (await enrolled.Content.ReadFromJsonAsync<SessionResponse>(Ct))!.UserName);
        Assert.Equal("owner", (await client.GetFromJsonAsync<SessionResponse>("/api/v1/auth/session", Ct))!.UserName);
        Assert.True((await client.GetFromJsonAsync<AuthStateResponse>("/api/v1/auth/state", Ct))!.Enrolled);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM identity_user_passkey"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM used_setup_code WHERE sha256 = '{SetupCode.Sha256Hex(code)}'"));
        Assert.Equal(audited + 1, await AuditCountAsync("enrolment", "owner"));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    public async Task A_wrong_setup_code_is_refused_and_nothing_is_created(string presented)
    {
        await using var api = db.Api(NewSetupCode());
        var response = await Client(api).PostAsJsonAsync("/api/v1/auth/enrolment/options", new { setupCode = presented, userName = "owner" }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, ProblemTitles.SetupCodeRefused);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM identity_user"));
    }

    [Fact]
    public async Task No_setup_code_configured_accepts_none()
    {
        await using var api = db.Api("");
        var response = await Client(api).PostAsJsonAsync("/api/v1/auth/enrolment/options", new { setupCode = "", userName = "owner" }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, ProblemTitles.SetupCodeRefused);
    }

    [Fact]
    public async Task Once_an_owner_exists_enrolment_answers_409()
    {
        var code = NewSetupCode();
        await using var api = db.Api(code);
        Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(api), new SoftAuthenticator(Origin), code)).StatusCode);

        var again = await EnrolAsync(Client(api), new SoftAuthenticator(Origin), code, "second");

        await AssertProblemAsync(again, HttpStatusCode.Conflict, ProblemTitles.AlreadyEnrolled);
    }

    [Fact]
    public async Task A_spent_code_is_refused_after_the_owner_is_reset_and_a_fresh_one_enrols()
    {
        // Round 2 SCO-4 / TST-20. reset-owner is step 7's command; here, what it does to the rows.
        var spent = NewSetupCode();
        await using (var first = db.Api(spent))
            Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(first), new SoftAuthenticator(Origin), spent)).StatusCode);
        await db.ResetOwnerAsync();

        await using (var reused = db.Api(spent))
            await AssertProblemAsync(await EnrolAsync(Client(reused), new SoftAuthenticator(Origin), spent), HttpStatusCode.Forbidden, ProblemTitles.SetupCodeRefused);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM identity_user"));

        var fresh = NewSetupCode();
        await using var api = db.Api(fresh);
        Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(api), new SoftAuthenticator(Origin), fresh)).StatusCode);
    }

    public static TheoryData<string, SoftAuthenticator.Variant> HostileRegistrations => new()
    {
        { "a foreign origin", new() { Origin = "http://localhost:6666" } },
        { "crossOrigin", new() { CrossOrigin = true } },
        { "user not verified", new() { UserVerified = false } },
        { "malformed CBOR", new() { MalformedCbor = true } },
    };

    [Theory]
    [MemberData(nameof(HostileRegistrations))]
    public async Task A_refused_registration_creates_nothing_and_leaves_the_code_unspent(string _, SoftAuthenticator.Variant variant)
    {
        // C3-3: nothing exists before the ceremony succeeds (Spike I §3 saw the alternative).
        var code = NewSetupCode();
        await using var api = db.Api(code);

        var response = await EnrolAsync(Client(api), new SoftAuthenticator(Origin), code, variant: variant);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, ProblemTitles.PasskeyRefused);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM identity_user"));
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM used_setup_code WHERE sha256 = '{SetupCode.Sha256Hex(code)}'"));
        Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(api), new SoftAuthenticator(Origin), code)).StatusCode);
    }

    [Fact]
    public async Task The_rp_id_is_localhost_whatever_the_Host_header_says()
    {
        // C3-10: without ServerDomain the RP ID is the Host header, and 127.0.0.1 is admitted.
        var code = NewSetupCode();
        await using var api = db.Api(code);
        foreach (var host in new[] { "localhost", "127.0.0.1", "[::1]" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/enrolment/options")
            {
                Content = JsonContent.Create(new { setupCode = code, userName = "owner" }),
            };
            request.Headers.Host = host;
            var options = JsonNode.Parse(await (await Client(api).SendAsync(request, Ct)).EnsureSuccessStatusCode().Content.ReadAsStringAsync(Ct))!;

            Assert.Equal("localhost", options["rp"]!["id"]!.GetValue<string>());
            Assert.Equal("required", options["authenticatorSelection"]!["userVerification"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Finishing_with_no_ceremony_underway_is_a_400_not_a_500()
    {
        // SRC-3: an expired, missing or spent challenge cookie makes Identity throw.
        var code = NewSetupCode();
        await using var api = db.Api(code);
        var withoutOptions = Json("""{"setupCode":"CODE","credential":{"id":"AA","rawId":"AA","type":"public-key","clientExtensionResults":{},"response":{"clientDataJSON":"AA","attestationObject":"AA"}}}""".Replace("CODE", code));

        await AssertProblemAsync(await Client(api).PostAsync("/api/v1/auth/enrolment", withoutOptions, Ct), HttpStatusCode.BadRequest, ProblemTitles.NoCeremony);
        await AssertProblemAsync(await Client(api).PostAsync("/api/v1/auth/sign-in", Json("""{"id":"AA","rawId":"AA","type":"public-key","clientExtensionResults":{},"response":{"clientDataJSON":"AA","authenticatorData":"AA","signature":"AA"}}"""), Ct),
            HttpStatusCode.BadRequest, ProblemTitles.NoCeremony);
    }

    // ---- Sign-in ----

    [Fact]
    public async Task The_owners_passkey_signs_in_from_a_fresh_browser()
    {
        var (api, authenticator) = await EnrolledAsync();
        await using var _ = api;
        var browser = Client(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/session", Ct)).StatusCode);
        var audited = await AuditCountAsync("sign_in_succeeded", "owner");

        var signedIn = await SignInAsync(browser, authenticator);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal("owner", (await browser.GetFromJsonAsync<SessionResponse>("/api/v1/auth/session", Ct))!.UserName);
        Assert.Equal(audited + 1, await AuditCountAsync("sign_in_succeeded", "owner"));
    }

    [Fact]
    public async Task A_passkey_this_app_does_not_know_is_told_so()
    {
        var (api, _) = await EnrolledAsync();
        await using var __ = api;
        var audited = await AuditCountAsync("sign_in_failed", AuditEvent.UnknownActor);

        var stranger = await SignInAsync(Client(api), new SoftAuthenticator(Origin, registeredElsewhere: Guid.NewGuid().ToByteArray()));

        await AssertProblemAsync(stranger, HttpStatusCode.Unauthorized, ProblemTitles.NotRegisteredHere);
        Assert.Equal(audited + 1, await AuditCountAsync("sign_in_failed", AuditEvent.UnknownActor));
    }

    public static TheoryData<string, SoftAuthenticator.Variant> HostileAssertions => new()
    {
        { "a foreign origin", new() { Origin = "http://localhost:6666" } },
        { "crossOrigin", new() { CrossOrigin = true } },
        { "user not verified", new() { UserVerified = false } },
        { "a counter that did not advance", new() { SignCount = 0 } },
        { "truncated authenticator data", new() { MalformedCbor = true } },
    };

    [Theory]
    [MemberData(nameof(HostileAssertions))]
    public async Task A_hostile_assertion_is_refused(string _, SoftAuthenticator.Variant variant)
    {
        var (api, authenticator) = await EnrolledAsync();
        await using var __ = api;
        var browser = Client(api);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(browser, authenticator)).StatusCode); // the counter is now 1

        var hostile = await SignInAsync(Client(api), authenticator, variant);

        Assert.Equal(HttpStatusCode.Unauthorized, hostile.StatusCode);
    }

    [Fact]
    public async Task A_replayed_sign_in_is_refused()
    {
        var (api, authenticator) = await EnrolledAsync();
        await using var _ = api;
        var browser = Client(api, cookies: false);
        var options = await browser.PostAsync("/api/v1/auth/sign-in/options", null, Ct);
        var challengeCookie = SetCookie(options, "__Host-portfolio-challenge");
        var assertion = authenticator.Assert(await options.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, (await PostWithCookie(browser, "/api/v1/auth/sign-in", assertion, challengeCookie)).StatusCode);
        // The same exchange again, challenge cookie and all: caught by the sign counter (C3-39).
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWithCookie(browser, "/api/v1/auth/sign-in", assertion, challengeCookie)).StatusCode);
    }

    [Fact]
    public async Task A_lockout_on_the_user_does_not_stop_the_passkey()
    {
        // C3-8, restated by SRC-11: the twin. Lockout is off for new users; even set on this one, the
        // passkey still signs in, so the deferred password path could never lock it out.
        var (api, authenticator) = await EnrolledAsync();
        await using var _ = api;
        await db.ExecuteAsync("UPDATE identity_user SET lockout_enabled = true, lockout_end = now() + interval '1 day'");

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(Client(api), authenticator)).StatusCode);
    }

    // ---- Cookies and sessions ----

    [Fact]
    public async Task Every_cookie_the_ceremonies_set_is_host_prefixed_secure_strict_and_http_only()
    {
        // C3-6. The antiforgery cookie, the one exception, arrives in step 3.
        var code = NewSetupCode();
        await using var api = db.Api(code);
        var client = Client(api, cookies: false);
        var options = await client.PostAsJsonAsync("/api/v1/auth/enrolment/options", new { setupCode = code, userName = "owner" }, Ct);
        var challenge = SetCookie(options, "__Host-portfolio-challenge");
        var enrolled = await PostWithCookie(client, "/api/v1/auth/enrolment",
            $$"""{"setupCode":"{{code}}","credential":{{new SoftAuthenticator(Origin).Register(await options.Content.ReadAsStringAsync(Ct))}}}""", challenge);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);

        var cookies = options.Headers.GetValues("Set-Cookie").Concat(enrolled.Headers.GetValues("Set-Cookie")).ToList();
        Assert.Contains(cookies, c => c.StartsWith("__Host-portfolio=", StringComparison.Ordinal));
        Assert.All(cookies, c =>
        {
            var attributes = c.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToList();
            Assert.StartsWith("__Host-", c);
            Assert.Contains("secure", attributes);
            Assert.Contains("httponly", attributes);
            Assert.Contains("samesite=strict", attributes);
            Assert.Contains("path=/", attributes);
            Assert.DoesNotContain(attributes, a => a.StartsWith("domain=", StringComparison.Ordinal));
        });

        // D10 / SRC-2: the session is persistent, for about 7 days.
        var session = cookies.Single(c => c.StartsWith("__Host-portfolio=", StringComparison.Ordinal));
        var expires = DateTimeOffset.Parse(session.Split(';', StringSplitOptions.TrimEntries).Single(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))[8..]);
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5), TimeSpan.FromDays(7) + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_cookie_copied_before_sign_out_is_refused_within_the_validation_interval()
    {
        // D4 / SRC-16 / TST-18: sign-out rotates the stamp; a copy keeps working until its next check.
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var code = NewSetupCode();
        await using var api = db.Api(code, clock);
        var browser = Client(api);
        var enrolled = await EnrolAsync(browser, new SoftAuthenticator(Origin), code);
        var copied = SetCookie(enrolled, "__Host-portfolio");
        var thief = Client(api, cookies: false);
        var audited = await AuditCountAsync("sign_out", "owner");

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/sign-out", null, Ct)).StatusCode);
        clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(HttpStatusCode.OK, (await GetWithCookie(thief, "/api/v1/auth/session", copied)).StatusCode); // the lag D4 accepted
        clock.Now += TimeSpan.FromSeconds(31);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithCookie(thief, "/api/v1/auth/session", copied)).StatusCode);
        Assert.Equal(audited + 1, await AuditCountAsync("sign_out", "owner"));
    }

    // ---- Support ----

    async Task<(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Api, SoftAuthenticator Authenticator)> EnrolledAsync()
    {
        var code = NewSetupCode();
        var api = db.Api(code);
        var authenticator = new SoftAuthenticator(Origin);
        Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(api), authenticator, code)).StatusCode);
        return (api, authenticator);
    }

    /// <summary>audit_event is append-only and shared by the class, so tests count deltas (TST-22).</summary>
    Task<long> AuditCountAsync(string kind, string actor) =>
        db.ScalarAsync<long>($"SELECT count(*) FROM audit_event WHERE kind = '{kind}' AND actor = '{actor}'");

    static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(title, problem.GetProperty("title").GetString());
    }

    /// <summary>The name=value part of a Set-Cookie, to replay by hand.</summary>
    static string SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(name + "=", StringComparison.Ordinal)).Split(';')[0];

    static Task<HttpResponseMessage> PostWithCookie(HttpClient client, string path, string json, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = Json(json) };
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request, Ct);
    }

    static Task<HttpResponseMessage> GetWithCookie(HttpClient client, string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request, Ct);
    }
}
