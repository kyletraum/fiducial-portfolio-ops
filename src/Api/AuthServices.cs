using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Portfolio.Infrastructure;

namespace Portfolio.Api;

/// <summary>
/// Slice 03 steps 2 and 4: Identity with passkeys, configured as slice-03.md specifies, and the
/// session requirement on every endpoint.
/// </summary>
public static class AuthServices
{
    /// <summary>Every Identity cookie, by scheme, and the name it is issued under.</summary>
    /// <summary>The session cookie's name.</summary>
    public const string SessionCookie = "__Host-portfolio";

    static readonly (string Scheme, string Cookie)[] IdentityCookies =
    [
        (IdentityConstants.ApplicationScheme, SessionCookie),
        (IdentityConstants.TwoFactorUserIdScheme, "__Host-portfolio-challenge"), // the passkey ceremony's state
        (IdentityConstants.ExternalScheme, "__Host-portfolio-external"),
        (IdentityConstants.TwoFactorRememberMeScheme, "__Host-portfolio-remember"),
    ];

    /// <summary>A ceremony may take this long, and the challenge cookie lives as long (R3).</summary>
    static readonly TimeSpan CeremonyTimeout = TimeSpan.FromMinutes(10);

    /// <summary>D10: WCAG 2.2.1's 20-hour exception. Renewed on use.</summary>
    static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public static void AddPortfolioAuthentication(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        // Step 4, the switch (D11): every endpoint requires a session unless it says AllowAnonymous.
        // Static files are served before authorization, so the page itself is not covered (step 3).
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddIdentityCore<IdentityUser>(o =>
            {
                // C3-8, restated by SRC-11: no passkey failure is counted today, but passkey sign-in
                // runs the lockout check, so a future password path could lock the passkey out.
                o.Lockout.AllowedForNewUsers = false;
            })
            .AddEntityFrameworkStores<PortfolioDbContext>()
            .AddSignInManager();
        PortfolioDbContext.AddIdentitySchema(services); // Version3: passkeys (step 1)

        var origins = (config["Auth:AllowedOrigins"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        services.Configure<IdentityPasskeyOptions>(o =>
        {
            // The RP ID is otherwise the Host header, which differs through the Vite proxy and
            // for 127.0.0.1 (C3-10).
            o.ServerDomain = "localhost";
            // Written down so a change to "preferred" is a visible one.
            o.UserVerificationRequirement = "required";
            o.AuthenticatorTimeout = CeremonyTimeout;
            // Replaces Identity's default, which accepts any origin equal to the request's own
            // Origin header (C3-23). The origins come from the AppHost: Aspire allocates the Vite
            // port, and the published port is a .env value (SRC-12). EXECUTED through the proxy
            // by Spike I (G-5).
            o.ValidateOrigin = ctx => ValueTask.FromResult(!ctx.CrossOrigin && origins.Contains(ctx.Origin, StringComparer.Ordinal));
        });

        foreach (var (scheme, cookie) in IdentityCookies)
            services.Configure<CookieAuthenticationOptions>(scheme, o =>
            {
                // C3-6: none of these is the framework's default (Lax, SameAsRequest). __Host- needs
                // Secure, Path=/ and no Domain, and the browsers accept it on http://localhost.
                o.Cookie.Name = cookie;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.HttpOnly = true;
            });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o => o.ExpireTimeSpan = CeremonyTimeout);
        services.ConfigureApplicationCookie(o =>
        {
            o.ExpireTimeSpan = SessionLifetime;
            o.SlidingExpiration = true;
            // SRC-2: passkey sign-in would issue a browser-session cookie; the session is persistent.
            o.Events.OnSigningIn = ctx => { ctx.Properties.IsPersistent = true; return Task.CompletedTask; };
            // An API answers 401/403. DisableCookieRedirect would still set Location (SRC-15).
            o.Events.OnRedirectToLogin = ctx =>
            {
                Refusals.Log(ctx.HttpContext, Refusals.NoSession);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        // D4: stateless sessions. Sign-out rotates the security stamp; a copied cookie is refused at
        // its next check, at most this long after. Configurable so the E2E can make it zero.
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = config.GetValue("Auth:StampValidationInterval", TimeSpan.FromMinutes(1)));

        // D3, C3-11: the key ring is the root of trust for every cookie and token. Under `aspire run`
        // it stays in the user profile; the container gives it a named volume (step 7).
        var keys = services.AddDataProtection().SetApplicationName("portfolio");
        if (config["DataProtection:KeyPath"] is { Length: > 0 } path)
            keys.PersistKeysToFileSystem(new DirectoryInfo(path));
    }
}
