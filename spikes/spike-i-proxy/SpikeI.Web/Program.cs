// Spike I remainder (gate G-5): does a passkey ceremony pass Identity's origin check end to end
// through the Vite dev proxy, configured as slice-03.md step 2 specifies?
//
//   dotnet run --urls http://localhost:5297      (run.sh starts it, and two Vite servers)
//
// Spike H's API, moved under /api (the Vite proxy forwards only that prefix) and configured as
// the spec says: ServerDomain pinned to "localhost"; ValidateOrigin comparing against origins
// from configuration (Spike:AllowedOrigins), rejecting crossOrigin; every Identity cookie
// Strict, Secure-always, HttpOnly and __Host-; the session cookie persistent; the challenge
// cookie and AuthenticatorTimeout both 10 minutes. Each request logs the Origin and Host the
// API saw; each response logs its Set-Cookie names and attributes, never values.

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<SpikeDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("spike")));
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services
    .AddIdentityCore<IdentityUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
    .AddEntityFrameworkStores<SpikeDb>()
    .AddSignInManager();

var allowed = (builder.Configuration["Spike:AllowedOrigins"] ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.Configure<IdentityPasskeyOptions>(o =>
{
    o.ServerDomain = "localhost";
    o.AuthenticatorTimeout = TimeSpan.FromMinutes(10);
    // Replaces Identity's default check (which accepts any origin equal to the request's own
    // Origin header), so crossOrigin must be refused here too.
    o.ValidateOrigin = ctx =>
    {
        var ok = !ctx.CrossOrigin && allowed.Contains(ctx.Origin, StringComparer.Ordinal);
        Console.WriteLine($"  [api] ValidateOrigin origin={ctx.Origin} crossOrigin={ctx.CrossOrigin} topOrigin={ctx.TopOrigin ?? "-"} -> {(ok ? "accept" : "REJECT")}");
        return ValueTask.FromResult(ok);
    };
});

void Harden(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions o, string name)
{
    o.Cookie.Name = name;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.HttpOnly = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
}
builder.Services.ConfigureApplicationCookie(o =>
{
    Harden(o, "__Host-portfolio");
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    // Passkey sign-in hard-codes isPersistent: false; the spec makes the session persistent.
    o.Events.OnSigningIn = ctx => { ctx.Properties.IsPersistent = true; return Task.CompletedTask; };
});
builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    IdentityConstants.TwoFactorUserIdScheme, o => { Harden(o, "__Host-portfolio-challenge"); o.ExpireTimeSpan = TimeSpan.FromMinutes(10); });

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<SpikeDb>().Database.EnsureCreated(); // a spike: no migrations

app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        foreach (var c in ctx.Response.Headers.SetCookie)
        {
            var parts = (c ?? "").Split(';', StringSplitOptions.TrimEntries);
            var name = parts[0].Split('=')[0];
            Console.WriteLine($"  [api] Set-Cookie {name}; {string.Join("; ", parts.Skip(1))}");
        }
        return Task.CompletedTask;
    });
    Console.WriteLine($"  [api] {ctx.Request.Method} {ctx.Request.Path} Origin={(ctx.Request.Headers.Origin.ToString() is { Length: > 0 } o ? o : "-")} Host={ctx.Request.Host}");
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapPost("/passkey/creation-options", async (Who who, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
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

api.MapPost("/passkey/register", async (HttpRequest request, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    var credential = await new StreamReader(request.Body).ReadToEndAsync();
    var attested = await signIn.PerformPasskeyAttestationAsync(credential);
    if (!attested.Succeeded) return Results.Problem($"attestation failed: {attested.Failure.Message}", statusCode: 400);
    var user = await users.FindByIdAsync(attested.UserEntity.Id);
    if (user is null) return Results.Problem("no such user", statusCode: 400);
    var stored = await users.AddOrUpdatePasskeyAsync(user, attested.Passkey);
    return stored.Succeeded ? Results.Ok(new { registered = true }) : Results.Problem("store failed");
});

api.MapPost("/passkey/request-options", async (SignInManager<IdentityUser> signIn) =>
    Results.Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json"));

api.MapPost("/passkey/login", async (HttpRequest request, SignInManager<IdentityUser> signIn) =>
{
    var credential = await new StreamReader(request.Body).ReadToEndAsync();
    var result = await signIn.PasskeySignInAsync(credential);
    return result.Succeeded ? Results.Ok(new { signedIn = true }) : Results.Problem($"sign-in refused: {result}", statusCode: 401);
});

// Diagnosis only: the same assertion, through PerformPasskeyAssertionAsync, so a refusal names
// its reason (PasskeySignInAsync returns a bare Failed). Nothing signs in here.
api.MapPost("/passkey/login-diagnose", async (HttpRequest request, SignInManager<IdentityUser> signIn) =>
{
    var credential = await new StreamReader(request.Body).ReadToEndAsync();
    var assertion = await signIn.PerformPasskeyAssertionAsync(credential);
    var why = assertion.Succeeded ? "assertion ok" : assertion.Failure.Message;
    Console.WriteLine($"  [api] login-diagnose: {why}");
    return Results.Ok(new { why });
});

api.MapGet("/me", (HttpContext http) => Results.Ok(new { user = http.User.Identity?.Name }))
    .RequireAuthorization();

// The page, for the direct-to-API case: the same file Vite serves.
app.MapGet("/", () => Results.File(Path.Combine(AppContext.BaseDirectory, "index.html"), "text/html"));

app.Run();

sealed record Who(string UserName);

sealed class SpikeDb(DbContextOptions<SpikeDb> options) : IdentityDbContext<IdentityUser>(options);
