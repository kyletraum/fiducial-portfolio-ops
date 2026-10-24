using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api;
using static Portfolio.Integration.AuthFixture;
using static Portfolio.Integration.Ceremony;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// slice-03.md step 7: the recovery commands. Called through the app's own services, as
/// <c>docker compose run --rm api &lt;mode&gt;</c> calls them, and the process itself run once (TST-22).
/// Audit counts are per-action deltas: the table is append-only and shared by the class.
/// </summary>
public class OwnerCommandsTests(AuthFixture db) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await db.ResetOwnerAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A copied cookie is checked at every request: the effect of a command is immediate.</summary>
    WebApplicationFactory<Program> Api(string code) =>
        db.Api(code).WithWebHostBuilder(b => b.UseSetting("Auth:StampValidationInterval", "00:00:00"));

    Task<long> AuditCountAsync(string kind) => db.ScalarAsync<long>($"SELECT count(*) FROM audit_event WHERE kind = '{kind}'");

    async Task RunAsync(WebApplicationFactory<Program> api, string mode)
    {
        await using var scope = api.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<OwnerCommands>().RunAsync(mode, TextWriter.Null, Ct));
    }

    [Fact]
    public async Task Revoke_sessions_refuses_every_existing_cookie_and_is_audited()
    {
        var code = NewSetupCode();
        await using var api = Api(code);
        var authenticator = new SoftAuthenticator(Origin);
        var enrolled = await EnrolAsync(Client(api), authenticator, code);
        var copied = enrolled.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-portfolio=", StringComparison.Ordinal)).Split(';')[0];
        var audited = await AuditCountAsync("sessions_revoked");

        await RunAsync(api, OwnerCommands.RevokeSessions);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        request.Headers.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(api, cookies: false).SendAsync(request, Ct)).StatusCode);
        Assert.Equal(audited + 1, await AuditCountAsync("sessions_revoked"));
        // The owner and the passkey are untouched: signing in again works.
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(Client(api), authenticator)).StatusCode);
    }

    [Fact]
    public async Task Reset_owner_reopens_enrolment_for_a_fresh_code_only()
    {
        // Round 2 TST-20, now with the command itself: enrol with A, reset, A is refused with nothing
        // created, B enrols. Each step audited.
        var a = NewSetupCode();
        await using (var api = Api(a))
        {
            Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(api), new SoftAuthenticator(Origin), a)).StatusCode);
            var audited = await AuditCountAsync("owner_reset");

            await RunAsync(api, OwnerCommands.ResetOwner);

            Assert.Equal(audited + 1, await AuditCountAsync("owner_reset"));
            Assert.Equal((0L, 0L), (await db.ScalarAsync<long>("SELECT count(*) FROM identity_user"), await db.ScalarAsync<long>("SELECT count(*) FROM identity_user_passkey")));
            Assert.False((await Client(api).GetFromJsonAsync<AuthStateResponse>("/api/v1/auth/state", Ct))!.Enrolled);
            Assert.Equal(HttpStatusCode.Forbidden, (await EnrolAsync(Client(api), new SoftAuthenticator(Origin), a)).StatusCode);
            Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM identity_user"));
        }

        var b = NewSetupCode();
        await using var fresh = Api(b);
        Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(fresh), new SoftAuthenticator(Origin), b)).StatusCode);
    }

    [Fact]
    public async Task With_no_owner_the_commands_do_nothing_and_say_so()
    {
        await using var api = Api(NewSetupCode());
        var before = (await AuditCountAsync("sessions_revoked"), await AuditCountAsync("owner_reset"));
        var output = new StringWriter();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var commands = scope.ServiceProvider.GetRequiredService<OwnerCommands>();
            Assert.Equal(0, await commands.RunAsync(OwnerCommands.RevokeSessions, output, Ct));
            Assert.Equal(0, await commands.RunAsync(OwnerCommands.ResetOwner, output, Ct));
        }

        Assert.Contains("No owner is enrolled", output.ToString());
        Assert.Equal(before, (await AuditCountAsync("sessions_revoked"), await AuditCountAsync("owner_reset")));
    }

    [Fact]
    public async Task The_built_Api_runs_a_command_and_exits_without_serving()
    {
        // The process path: Program.cs dispatches on its one argument and returns before app.Run().
        var code = NewSetupCode();
        await using (var api = Api(code))
            Assert.Equal(HttpStatusCode.OK, (await EnrolAsync(Client(api), new SoftAuthenticator(Origin), code)).StatusCode);
        var audited = await AuditCountAsync("sessions_revoked");

        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Api.dll"));
        start.ArgumentList.Add(OwnerCommands.RevokeSessions);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__portfolio"] = db.ConnectionString;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(1)), "the command did not exit: it must not start the web server");

        Assert.True(process.ExitCode == 0, await stderr);
        Assert.Contains("Every session is revoked", await stdout);
        Assert.Equal(audited + 1, await AuditCountAsync("sessions_revoked"));
    }
}
