// Spike J: CSRF tokens in a single-page app calling minimal APIs, refused by the application's
// own endpoint filter (slice-03.md step 3; round-1 C3-1; round-2 R1).
//
//   dotnet run --urls "http://localhost:5293;http://localhost:5294"     (run.sh starts it)
//
// The pipeline is step 3's order: headers in OnStarting, exception handler, explicit
// UseAuthentication/UseAuthorization, then the endpoints. Two groups:
//   /api  - the slice's filter: every method but GET/HEAD/OPTIONS/TRACE is validated with
//           IAntiforgery.ValidateRequestAsync and refused 403 with its own title, unless the
//           endpoint carries the exemption (the anonymous sign-in route, per R1).
//   /fw   - a control: the framework's own UseAntiforgery() and [RequireAntiforgeryToken]
//           metadata, nothing else, to observe C3-1 (validates, never refuses) rather than cite it.
// Sign-in is a spike-only route that signs in a named principal with a cookie; the passkey
// ceremony is Spike H's and Spike I's, and the token binds to any authenticated principal.
// Each write increments a counter, so "nothing was written" is a number, not an inference.

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "__Host-portfolio";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.HttpOnly = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.SuppressReadingTokenFromFormBody = true; // the header is the only carrier
    o.SuppressXFrameOptionsHeader = true;      // the headers middleware owns X-Frame-Options
    o.Cookie.Name = "portfolio-csrf";          // not __Host-: it cannot be Secure over plain HTTP
});

var app = builder.Build();
var writes = new Dictionary<string, int>();
int Write(string what) { lock (writes) return writes[what] = writes.GetValueOrDefault(what) + 1; }

app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        return Task.CompletedTask;
    });
    await next();
});
app.UseExceptionHandler();
// Server-side truth for the attacker case: did the session cookie arrive, and did it
// authenticate? (Playwright's own report of request cookies is what is under test.)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/upload") || ctx.Request.Path.StartsWithSegments("/fw/upload"))
        Console.WriteLine($"  [api] {ctx.Request.Method} {ctx.Request.Path} from Origin={ctx.Request.Headers.Origin} " +
            $"Sec-Fetch-Site={ctx.Request.Headers["Sec-Fetch-Site"]} session cookie arrived={ctx.Request.Cookies.ContainsKey("__Host-portfolio")}");
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery(); // for the /fw control group only; /api never relies on it

// The page: the app at :5293, an "attacker" on another localhost port at :5294 (same site).
app.MapGet("/", (HttpContext http) => Results.File(Path.Combine(AppContext.BaseDirectory,
    http.Request.Host.Port == 5294 ? "attacker.html" : "index.html"), "text/html"));
app.MapGet("/writes", () => { lock (writes) return Results.Ok(new Dictionary<string, int>(writes)); });

var api = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
{
    var http = ctx.HttpContext;
    var safe = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
        || HttpMethods.IsOptions(http.Request.Method) || HttpMethods.IsTrace(http.Request.Method);
    if (safe || http.GetEndpoint()?.Metadata.GetMetadata<AntiforgeryExempt>() is not null)
        return await next(ctx);
    try
    {
        await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException e)
    {
        Console.WriteLine($"  [api] filter refused {http.Request.Method} {http.Request.Path}: {e.Message}");
        // Which layer refused, in a header: an opaque no-cors response's body cannot be read by
        // the test (G-19), so this is checked as the alternative oracle.
        http.Response.Headers["X-Refused-By"] = "antiforgery";
        return Results.Problem(title: "CSRF token missing or invalid", statusCode: 403);
    }
    return await next(ctx);
});

api.MapPost("/auth/sign-in", async (HttpContext http, Who who) =>
{
    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, who.User), new Claim(ClaimTypes.Name, who.User)],
        CookieAuthenticationDefaults.AuthenticationScheme));
    await http.SignInAsync(principal);
    return Results.Ok(new { signedIn = who.User });
}).WithMetadata(new AntiforgeryExempt());

api.MapPost("/auth/sign-out", async (HttpContext http) => { await http.SignOutAsync(); return Results.Ok(); });

api.MapGet("/antiforgery/token", (HttpContext http, IAntiforgery af) =>
    Results.Ok(new { token = af.GetAndStoreTokens(http).RequestToken })).RequireAuthorization();

api.MapGet("/read", () => Results.Ok(new { read = true }));
api.MapPost("/json", (Thing t) => Results.Ok(new { wrote = Write("api json"), t.Name }));
api.MapPut("/json", (Thing t) => Results.Ok(new { wrote = Write("api put"), t.Name }));
api.MapDelete("/thing/{id}", (int id) => Results.Ok(new { wrote = Write("api delete"), id }));
api.MapPost("/upload", async (HttpRequest r) =>
{
    var body = await new StreamReader(r.Body).ReadToEndAsync();
    return Results.Ok(new { wrote = Write("api upload"), bytes = body.Length, contentType = r.ContentType });
});

// The control: what the framework does with its own metadata and middleware, nothing added.
var fw = app.MapGroup("/fw").WithMetadata(new RequireAntiforgeryTokenAttribute());
fw.MapPost("/json", (Thing t) => Results.Ok(new { wrote = Write("fw json"), t.Name }));
fw.MapDelete("/thing/{id}", (int id) => Results.Ok(new { wrote = Write("fw delete"), id }));
fw.MapPost("/upload", async (HttpRequest r) =>
    Results.Ok(new { wrote = Write("fw upload"), bytes = (await new StreamReader(r.Body).ReadToEndAsync()).Length }));

app.Run();

sealed record Who(string User);
sealed record Thing(string Name);
sealed class AntiforgeryExempt;
