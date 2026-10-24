// Spike H: is ASP.NET Core 10 Identity's in-box passkey support enough to build on?
//
//   dotnet run --urls http://localhost:5299
//
// Registers a passkey and signs in with it, using only SignInManager/UserManager's passkey
// API, on Identity's EF store in a throwaway PostgreSQL (ConnectionStrings:spike). The EF
// in-memory provider was tried first and is NOT usable: it does not read back the passkey's
// JSON-mapped data, so a stored passkey loads as null (see RESULTS.md). The page calls navigator.credentials.create()/get();
// SpikeH.Driver runs it in Chromium with a CDP virtual authenticator. Nothing is persisted.

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<SpikeDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("spike")));
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services
    .AddIdentityCore<IdentityUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3) // passkeys need v3
    .AddEntityFrameworkStores<SpikeDb>()
    .AddSignInManager();

// An API answers 401, not a redirect to a login page that does not exist (the first run's /me
// was 404 for that reason).
builder.Services.ConfigureApplicationCookie(o => o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; });

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<SpikeDb>().Database.EnsureCreated(); // a spike: no migrations
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Content(Page.Html, "text/html"));

app.MapPost("/passkey/creation-options", async (Who who, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    var user = await users.FindByNameAsync(who.UserName);
    if (user is null)
    {
        user = new IdentityUser(who.UserName);
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) return Results.Problem(string.Join("; ", created.Errors.Select(e => e.Description)));
    }
    var options = await signIn.MakePasskeyCreationOptionsAsync(new()
    {
        Id = await users.GetUserIdAsync(user),
        Name = who.UserName,
        DisplayName = who.UserName,
    });
    return Results.Content(options, "application/json");
});

app.MapPost("/passkey/register", async (HttpRequest request, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    var credential = await new StreamReader(request.Body).ReadToEndAsync();
    var attested = await signIn.PerformPasskeyAttestationAsync(credential);
    if (!attested.Succeeded) return Results.Problem($"attestation failed: {attested.Failure.Message}", statusCode: 400);
    var user = await users.FindByIdAsync(attested.UserEntity.Id);
    if (user is null) return Results.Problem("no such user", statusCode: 400);
    var stored = await users.AddOrUpdatePasskeyAsync(user, attested.Passkey);
    return stored.Succeeded
        ? Results.Ok(new { registered = true, credentialIdBytes = attested.Passkey.CredentialId.Length })
        : Results.Problem(string.Join("; ", stored.Errors.Select(e => e.Description)));
});

app.MapPost("/passkey/request-options", async (SignInManager<IdentityUser> signIn) =>
    Results.Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json"));

app.MapPost("/passkey/login", async (HttpRequest request, SignInManager<IdentityUser> signIn) =>
{
    var credential = await new StreamReader(request.Body).ReadToEndAsync();
    var result = await signIn.PasskeySignInAsync(credential);
    return result.Succeeded ? Results.Ok(new { signedIn = true }) : Results.Problem($"sign-in refused: {result}", statusCode: 401);
});

app.MapGet("/me", (HttpContext http) => Results.Ok(new { user = http.User.Identity?.Name }))
    .RequireAuthorization();

app.Run();

sealed record Who(string UserName);

sealed class SpikeDb(DbContextOptions<SpikeDb> options) : IdentityDbContext<IdentityUser>(options);

static class Page
{
    public const string Html = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Spike H</title></head><body>
        <script>
          async function post(url, body) {
            const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body });
            return { status: r.status, text: await r.text() };
          }
          window.register = async (userName) => {
            const opts = await post('/passkey/creation-options', JSON.stringify({ userName }));
            if (opts.status !== 200) return 'creation-options ' + opts.status + ' ' + opts.text;
            const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(opts.text));
            const cred = await navigator.credentials.create({ publicKey });
            const r = await post('/passkey/register', JSON.stringify(cred.toJSON()));
            return 'register ' + r.status + ' ' + r.text;
          };
          window.login = async () => {
            const opts = await post('/passkey/request-options', '');
            const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(opts.text));
            const cred = await navigator.credentials.get({ publicKey });
            const r = await post('/passkey/login', JSON.stringify(cred.toJSON()));
            return 'login ' + r.status + ' ' + r.text;
          };
          window.me = async () => { const r = await fetch('/me'); return 'me ' + r.status + ' ' + await r.text(); };
        </script></body></html>
        """;
}
