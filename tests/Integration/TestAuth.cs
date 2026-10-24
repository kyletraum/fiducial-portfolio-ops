using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Portfolio.Integration;

/// <summary>
/// The signed-in client for every HTTP test class that is not about authentication (slice-03.md
/// step 2, D11). Those classes exercise imports, balances and the clock; the passkey ceremony is
/// <see cref="AuthApiTests"/>' subject, and repeating it in each would test nothing new.
/// </summary>
/// <remarks>
/// Hardened per round 2:
/// - The scheme authenticates ONLY a request carrying <see cref="Header"/>, and answers NoResult
///   otherwise, so an anonymous request on the same factory behaves as production does (TST-2).
/// - A policy scheme makes it the default, or it would never be consulted (SRC-10, EXECUTED there).
/// - The principal's NameIdentifier is the user name given, because antiforgery binds a token to
///   that claim (step 3).
/// - It exists only through ConfigureTestServices, in this project, which nothing under src/ can
///   reference; <see cref="AuthSchemeGuardTests"/> holds the app's own scheme set to Identity's.
/// </remarks>
public static class TestAuth
{
    public const string Header = "X-Test-User";
    const string Selector = "TestOrIdentity";

    public static IWebHostBuilder WithTestAuth(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddTestScheme()
                .AddPolicyScheme(Selector, Selector, o => o.ForwardDefaultSelector = ctx =>
                    ctx.Request.Headers.ContainsKey(Header) ? TestScheme : IdentityConstants.ApplicationScheme);
            services.PostConfigure<AuthenticationOptions>(o => o.DefaultScheme = Selector);
        });

    /// <summary>The header-only scheme alone, for a test app with no Identity (the CSRF filter's).</summary>
    public static AuthenticationBuilder AddTestScheme(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, Handler>(TestScheme, _ => { });

    public const string TestScheme = "Test";

    /// <summary>
    /// A client whose every request is signed in as <paramref name="userName"/>, and whose every
    /// unsafe request carries a CSRF token issued to that user, with its cookie (step 3). The token
    /// is fetched once, on the first unsafe request.
    /// </summary>
    public static HttpClient CreateSignedInClient(this WebApplicationFactory<Program> factory, string userName = "owner")
    {
        // Outermost first: the token handler's own fetch passes through the cookie handler, so the
        // antiforgery cookie it is issued with is kept and sent with every later request.
        var client = factory.CreateDefaultClient(new CsrfTokenHandler(userName), new CookieContainerHandler());
        client.DefaultRequestHeaders.Add(Header, userName);
        return client;
    }

    sealed class CsrfTokenHandler(string userName) : DelegatingHandler
    {
        string? token;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.Method;
            if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options && method != HttpMethod.Trace)
            {
                token ??= await FetchAsync(request.RequestUri!, ct);
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
            }
            return await base.SendAsync(request, ct);
        }

        async Task<string> FetchAsync(Uri like, CancellationToken ct)
        {
            using var fetch = new HttpRequestMessage(HttpMethod.Get, new Uri(like, "/api/v1/auth/csrf-token"));
            fetch.Headers.Add(Header, userName);
            using var response = (await base.SendAsync(fetch, ct)).EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<Api.CsrfTokenResponse>(ct))!.Token;
        }
    }

    sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[Header].ToString() is not { Length: > 0 } user)
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
