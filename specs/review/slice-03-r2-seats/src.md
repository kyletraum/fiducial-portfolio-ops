# Slice 03 r2: ASP.NET source reader seat (SRC)

**Seat:** ASP.NET source reader. **Date:** 2026-10-24. **Under review:** PR #74, `git diff slice-03-dispositions -- specs/001-portfolio-platform/slice-03.md specs/001-portfolio-platform/security.md` on `slice-03-spec-edits`.

**Read** (raw.githubusercontent.com, pinned):
- `dotnet/aspnetcore@v10.0.13` (tag sha `0ef4bbf`): Identity `PasskeyHandler`, `IdentityPasskeyOptions`, `SignInManager`, `SecurityStampValidator(+Options)`, `IdentityCookiesBuilderExtensions`, `IdentityConstants`, `LockoutOptions`, EF `IdentityUserContext`; Cookies `CookieAuthenticationHandler`, `CookieAuthenticationOptions`, `CookieAuthenticationEvents`, `RequestPathBaseCookieBuilder`; `CookieBuilder`; `CookieRedirectEndpointConventionBuilderExtensions`; Antiforgery `AntiforgeryOptions`, `AntiforgeryMiddleware`, `DefaultAntiforgery`, `DefaultAntiforgeryTokenGenerator`, `DefaultAntiforgeryTokenStore`, `DefaultClaimUidExtractor`; `RequestDelegateFactory`; `AuthorizationPolicy`, `AuthorizationMiddleware`; RateLimiting `RateLimiterOptions(+Extensions)`, `RateLimitingMiddleware`; `CorsMiddleware`; `WebApplicationBuilder`; `ExceptionHandlerMiddlewareImpl`; `ForwardedHeadersOptionsSetup`.
- `dotnet/runtime@v10.0.13`: `FixedWindowRateLimiter.cs`.
- `dotnet/aspire@v13.6.1`: `Aspire.Hosting/ProjectResourceBuilderExtensions.cs`, `api/Aspire.Hosting.cs`; `Aspire.Hosting.Docker/` `DockerComposeEnvironmentResource.cs`, `DockerComposePublishingContext.cs`, `DockerComposeEnvironmentContext.cs`, `DockerComposeServiceExtensions.cs`, `api/Aspire.Hosting.Docker.cs`; `Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs`.
- `DOCS@2026-10-24 https://www.w3.org/TR/webauthn-3/` §15.1.
- Repo: `src/Api/Program.cs`, `src/AppHost/AppHost.cs`, `src/Infrastructure/DesignTimeDbContextFactory.cs`, `src/Migrator/Program.cs`, `spikes/spike-h-auth/SpikeH.Web/Program.cs`, `web/src/api/queries.ts`, `.gitignore`.

**Ran** (scratch `…\scratchpad\r2-src\`, nothing in the repo):
- `probe/`: a TestServer app with `FallbackPolicy`, a header middleware first, `UseFileServer`, `.DisableCookieRedirect()`, a test scheme. Five pipeline variants (A to E).
- `probe2/`: header middleware before `UseExceptionHandler`, with an endpoint that throws.
- `probe3/`: `Mvc.Testing`'s `CookieContainerHandler` over `http://` vs `https://`.
- `docker compose config` on a service that mounts a named volume the file never declares.
- `docker run` of the repo's own `api:latest` image (`User=1654`) with a fresh named volume, at a new path and at `/home/app`.

---

## Findings

### SRC-1: the pipeline order is unstated, and both orders the spec allows fail its own DoD
**MUST FIX (steps 2 and 3).** `EXECUTED` (probe A/B/C). `SOURCE@dotnet/aspnetcore@v10.0.13:src/DefaultBuilder/src/WebApplicationBuilder.cs:434-453`, `src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs:97-150`.
**Where:** slice-03.md step 2 "Authorization", step 3 "Headers … placed first in `Program.cs`", DoD 2 "Static files are anonymous by design", DoD 6. security.md "API", the headers bullet.

**The defect:**
- `AuthorizationMiddleware` applies `FallbackPolicy` when there is **no endpoint** too, so a static file served after `UseAuthorization` is challenged.
- Today's `Program.cs` calls `UseFileServer()` last. In probe B, with that order and `FallbackPolicy`, `/` and `/index.html` answer `302` to `/Account/Login`. The SPA never loads.
- If the app does not call `UseAuthentication`/`UseAuthorization` itself, `WebApplication` inserts both **ahead of every user middleware**. In probe A, the `401` then carries no header from the "first" middleware, which breaks DoD 6's `401`.
- The spec never fixes either order.

**What to do:** write the order into step 3:
1. headers (see SRC-8);
2. `UseExceptionHandler`, `UseStatusCodePages`;
3. the cross-site refusal;
4. `UseFileServer`;
5. **explicit** `UseAuthentication()` and `UseAuthorization()`;
6. `UseRateLimiter()`;
7. endpoints.

Probe C, which uses this order, gave static files `200`, `/api` `401`, and headers on every response.

### SRC-2: passkey sign-in issues a browser-session cookie, so the 7-day sentence is false as built
**MUST FIX (step 2).** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Identity/Core/src/SignInManager.cs:642-683` (`PasskeySignInCoreAsync` calls `SignInOrTwoFactorAsync(…, isPersistent: false, …)` and takes no `isPersistent` parameter); `src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs:276-279,328-334` (`Expires` is written only when `IsPersistent`).
**Where:** step 2 "Sessions", the sign-in page sentence "You stay signed in for 7 days after you last use the app, or until you sign out" (`D10`).

**The defect:**
- The cookie carries no `Expires`, so closing the browser ends the session unless the browser restores session cookies.
- `ExpireTimeSpan` bounds only the ticket inside the cookie.
- The sentence promises what the cookie cannot do.

**Otherwise the sentence does hold, more precisely than sliding expiration alone gives:**
- Sliding renewal alone fires only once half the window has passed, at `CookieAuthenticationHandler.cs:99-113`. That alone would mean "3.5 to 7 days after last use".
- But `SecurityStampValidator` sets `ShouldRenew` on every successful stamp check (`SecurityStampValidator.cs:110-118`), and `RequestRefresh` re-bases the ticket to now + 7 days (`CookieAuthenticationHandler.cs:117-130`).
- With a one-minute `ValidationInterval`, the ticket therefore slides to within a minute of the last request.

**What to do:** make the application cookie persistent. The smallest change is `OnSigningIn = ctx => { ctx.Properties.IsPersistent = true; … }` on the application scheme, which runs before the `IsPersistent` check at `:328-330`. The other way is to call `PerformPasskeyAssertionAsync`, then `AddOrUpdatePasskeyAsync`, then `SignInAsync(user, isPersistent: true)` yourself. Enrolment's sign-in must make the same choice. Add a test that the `Set-Cookie` carries `Expires` about 7 days out.

### SRC-3: `AuthenticatorTimeout` of 10 minutes outlives the 5-minute challenge cookie, and the expiry is a 500
**MUST FIX (step 2).** `SOURCE@dotnet/aspnetcore@v10.0.13`:
- `src/Identity/Core/src/IdentityCookiesBuilderExtensions.cs:98-108`: `TwoFactorUserId` has `ExpireTimeSpan = 5 min`.
- `src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs:181-194`: an expired ticket is a failure.
- `src/Identity/Core/src/SignInManager.cs:555-563,599-607,706-724`: a missing or expired state throws `InvalidOperationException` "No passkey assertion is underway".
- `src/Identity/Core/src/IdentityPasskeyOptions.cs`: `AuthenticatorTimeout` defaults to 5 min.

`DOCS@2026-10-24 https://www.w3.org/TR/webauthn-3/` §15.1: the recommended range is 300000 to 600000 ms, so the spec's citation holds.

**Where:** step 2 "Passkey options" (`AuthenticatorTimeout` is 10 minutes), step 2 "The replay model" ("lives for 5 minutes"), and step 4 "The passkey button" ("allows up to 10 minutes").

**The defect:** a user who takes between 5 and 10 minutes, which the page tells them they may, gets an unhandled exception. That exception is a `500` logged at Error. Any `POST` to the finish endpoints without the cookie gets the same: `curl`, a replay, or DoD 4's cross-site "passkey sign-in attempt".

**What to do:**
- Set the `TwoFactorUserId` scheme's `ExpireTimeSpan` to at least `AuthenticatorTimeout`, or keep both at 5 minutes and change the hint.
- Have the attestation and assertion endpoints catch the missing-ceremony case and answer `400` with a `ProblemDetails.title` of their own.
- Restate the replay window from whichever value is chosen.

### SRC-4: migration 5 would be generated without the passkey table
**MUST FIX (step 1).** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Identity/EntityFrameworkCore/src/IdentityUserContext.cs:135-160`. The schema version is read from `IOptions<IdentityOptions>` in the context's *application* service provider, else `Version1`. `EXECUTED` (read): `src/Infrastructure/DesignTimeDbContextFactory.cs` builds the context with no application services, and `src/Migrator/Program.cs` registers no Identity.
**Where:** step 1 "Migration 5" ("Spike H did not run `Version3` through the Migrator").

**The defect:**
- `dotnet ef migrations add` through the design-time factory models `Version1`, which has no `AspNetUserPasskeys` table.
- The API, with `Stores.SchemaVersion = Version3`, models a different schema from the one the snapshot records.
- This is the balloon the spec anticipates, and it has a known shape.

**What to do:** in `PortfolioDbContext`, `protected override Version SchemaVersion => IdentitySchemaVersions.Version3;`, which the source makes `protected virtual` at `:135`. Then every host (the API, the Migrator, design time) models the same schema. Add an architecture or integration test that the migrated database has the passkey table.

### SRC-5: the antiforgery filter covers anonymous unsafe endpoints that cannot obtain a token
**MUST FIX (step 3).** Internal consistency: slice-03.md step 3 against DoD 2. Supported by `EXECUTED` (read) `src/Api/Program.cs` ("Every route the browser calls lives under /api: the Vite dev proxy forwards that prefix only") and Spike H's endpoints (`spikes/spike-h-auth/SpikeH.Web/Program.cs`: options and finish are both `POST`).
**Where:** step 3 "The antiforgery filter … on the `/api` group … The token endpoint requires a session (`C3-22`)"; DoD 2 "Every unsafe-method endpoint carries the antiforgery filter".

**The defect:**
- Enrolment, passkey options and passkey sign-in are anonymous `POST`s, and they must live under `/api`.
- The filter refuses them without a token, and the only token source requires a session. No one could sign in.

**What to do:** choose one, and amend DoD 2's "every" to match:
- (a) Exempt the named anonymous endpoints from the filter. Their protection is then layers 1 and 2, plus the `Strict` challenge cookie the ceremony requires.
- (b) Allow an anonymous token, issued before sign-in and refetched after it. The spec already refetches.

### SRC-6: the client handles an antiforgery `400` that the server never sends
**MUST FIX (step 4).** Internal consistency.
**Where:** step 3: the filter "refuses with `403` and a `ProblemDetails.title` of its own". Step 4 "Expiry": "An antiforgery `400` means: refetch the token, retry once, then sign in". DoD 4: "Each refusal is identified by its `ProblemDetails.title`, not its status".

**The defect:** the refetch-and-retry path keys on a status the filter never answers. A `403` from layer 2 is indistinguishable by status.

**What to do:** key step 4 on the filter's title, and say `403` once.

### SRC-7: "the key-ring volume through `PublishAsDockerComposeService`" produces an invalid compose file, and the mount is unwritable
**MUST FIX (step 7).**
- `SOURCE@dotnet/aspire@v13.6.1:src/Aspire.Hosting.Docker/DockerComposePublishingContext.cs:100-127,230-247`: top-level `volumes:` come from `serviceResource.Volumes`, **before** the customization callback runs.
- `src/Aspire.Hosting/api/Aspire.Hosting.cs:244-248`: `WithVolume<T>` requires `T : ContainerResource`, so a project cannot call it.
- `EXECUTED` `docker compose config`: "service "api" refers to undefined volume portfolio-keys: invalid compose project".
- `EXECUTED` `docker run --entrypoint sh -v <fresh>:/keys api:latest`: `uid=1654(app)`, `/keys` is `root:root 755`, and `touch` is denied. Mounted at `/home/app`, which exists in the image and is owned by `app`, the write succeeds.

**Where:** step 7 "The app model"; step 2 "The key ring" (`D3`).

**The defect:**
- A volume added to `service.Volumes` in the callback is never declared at the top level.
- Even when declared, a fresh named volume at a path the image lacks is owned by root, and the API runs as UID 1654. Data protection cannot write the ring.

**What to do:**
- Declare the volume as well, through `ConfigureComposeFile` on the compose environment. The other route is `api.WithAnnotation(new ContainerMountAnnotation("<name>", "<target>", ContainerMountType.Volume, false))`, which `DockerComposeEnvironmentContext.cs:68-117` turns into both entries.
- Target a directory the image already owns as `app`: `/home/app` or a path below it that exists. Name that target in the spec.
- Under `aspire run` the API is a process, so `PersistKeysToFileSystem` must take its path from configuration.

### SRC-8: headers set before `next()` are erased on a `500`
**SHOULD FIX (step 3; security.md headers bullet).** `EXECUTED` (probe2): set before `next()`, `/boom` answered `500 nosniff=False`; set in `Response.OnStarting`, `500 nosniff=True`. `SOURCE@dotnet/aspnetcore@v10.0.13:src/Middleware/Diagnostics/src/ExceptionHandler/ExceptionHandlerMiddlewareImpl.cs:163,291-293` (`Response.Clear()`).
**Where:** step 3 "placed first in `Program.cs`, so every status carries them"; security.md "The headers come from one middleware placed first in the pipeline, so error and refusal responses carry them too".

**The defect:** being placed first is not sufficient, so the "so" in both sentences is false. DoD 6 would catch it, at build.

**What to do:** say the middleware writes the headers in `Response.OnStarting`.

### SRC-9: the integration client drops every `Secure` cookie over `http://localhost`
**SHOULD FIX (step 2, "Test sessions" and the auth classes).** `EXECUTED` (probe3): with `CookieContainerHandler` and base address `http://localhost`, the server saw `Cookie=[plain=2]`, without the `Secure` `__Host-sess`; with `https://localhost` it saw both, and `IsHttps=True`. Existing tests use `factory.CreateClient()`, whose default base is `http://localhost` (`tests/Integration/BalanceIdempotencyTests.cs:38`).
**Where:** step 2 "The auth classes run the real ceremony, on one `AuthFixture`"; DoD 2 "A cookie from a signed-out session answers `401`".

**The defect:**
- With `SecurePolicy=Always`, the challenge cookie and the session cookie never return.
- The ceremony then fails with SRC-3's exception, and every signed-in assertion fails.
- Switching to `https://localhost` makes `Request.IsHttps` true in tests and false in production.

**What to do:** state that the auth fixture uses `BaseAddress = https://localhost`, and note the divergence. Antiforgery's `CheckSSLConfig` and anything else that reads `IsHttps` are then untested on the production path.

### SRC-10: a scheme registered only by `ConfigureTestServices` is never consulted
**SHOULD FIX (step 2 "Test sessions").** `EXECUTED` (probe D/E): with the test scheme only registered, a request carrying its header got `/api/me` `401`. With `AuthenticationOptions.DefaultScheme`, `DefaultAuthenticateScheme` and `DefaultChallengeScheme` set to it, the same request got `200`.
**Where:** step 2 "authenticates through a test scheme registered only by `ConfigureTestServices`".

**The defect:**
- `AddAuthentication(IdentityConstants.ApplicationScheme)` fixes the default scheme.
- `RequireAuthenticatedUser()` with no named schemes authenticates only that default.

**What to do:**
- Say that the test services also make the test scheme the default, or name it in a policy scheme.
- The test principal should carry a `NameIdentifier` equal to the real user's id. Antiforgery binds tokens to that claim: `SOURCE@…/Antiforgery/src/Internal/DefaultClaimUidExtractor.cs:39-75`.

### SRC-11: the lockout rationale is false: no passkey failure is ever counted
**SHOULD FIX (step 2 "No lockout"; security.md rate-limiting bullet).** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Identity/Core/src/SignInManager.cs:659-673` (a failed assertion returns `Failed` **before** `PreSignInCheck`); `:497-499,920-922,984-986` (the only `AccessFailedAsync` callers are the password and two-factor paths).
**Where:** step 2 "a lockout would let any local `curl` deny the only user"; the test "after repeated failures, the passkey still signs in". security.md: "five failures from any local process would deny the only user".

**The defect:**
- With the password deferred (`D7`), nothing increments the failure count. `C3-8`'s path was the password.
- The test passes whether lockout is on or off, so it proves nothing.
- `AllowedForNewUsers = false` is still right, for the day the fallback returns.

**What to do:**
- Restate it: lockout is off so that the deferred password path cannot lock the passkey; today no passkey failure is counted.
- Drop the vacuous test, or make it set lockout on the user and assert that the passkey still signs in.

### SRC-12: the origins `ValidateOrigin` must accept are not static strings
**SHOULD FIX (step 2 "Passkey options").** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Identity/Core/src/PasskeyHandler.cs:651-675`: the delegate is `Func<PasskeyOriginValidationContext, ValueTask<bool>>` with `Origin`, `CrossOrigin` and `TopOrigin`, and setting it **replaces** the default, including its `CrossOrigin` refusal. The spec's "rejects `crossOrigin`" is therefore required, and correct. `SOURCE@dotnet/aspire@v13.6.1:src/Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs` `AddViteApp`: `.WithHttpEndpoint(env: "PORT")`, with no port. `INFERRED`: Aspire allocates the port per run. `EXECUTED` (read): `src/AppHost/AppHost.cs` publishes `"${API_PORT}"`.
**Where:** "the Vite port under `aspire run` and `http://localhost:8080` published".

**The defect:**
- The Vite origin is probably a different port each run.
- The published port is a `.env` value, not a literal 8080.

**What to do:** have the AppHost pass the web endpoint's URL to the API, and the published origin from the same parameter as `API_PORT`. `ValidateOrigin` then compares against configuration. `G-5`'s spike settles the run-mode half.

### SRC-13: `X-CSRF-TOKEN` is not the framework's header
**NOTE (step 3).** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Antiforgery/src/AntiforgeryOptions.cs` (the `HeaderName` default is `RequestVerificationToken`).
**What to do:** step 3 names `AntiforgeryOptions.HeaderName = "X-CSRF-TOKEN"`.

### SRC-14: limiter wording and `Retry-After`
**NOTE (step 3).** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Middleware/RateLimiting/src/RateLimiterOptionsExtensions.cs:56-70` (`AddFixedWindowLimiter` partitions on a `PolicyNameKey`, one limiter for every caller); `RateLimitingMiddleware.cs:114-146` (the status is set before `OnRejected`, and the lease is passed). `SOURCE@dotnet/runtime@v10.0.13:src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs:209-216` (a failed lease's `RetryAfter` is `Window × n`).
**The defect:**
- "Global" reads as `RateLimiterOptions.GlobalLimiter`, which would throttle every request.
- `Retry-After` from the lease is the whole window, not the time left in it.

**What to do:** name the API, `AddFixedWindowLimiter("auth", …)` plus `RequireRateLimiting("auth")` on the three endpoints. Say that `Retry-After` is the window length, which fits "about a minute" only if the window is a minute.

### SRC-15: `.DisableCookieRedirect()` still emits `Location`, and only on `/api`
**NOTE (step 2 "Authorization").** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Security/Authentication/Cookies/src/CookieAuthenticationEvents.cs:45-57` (the `401` keeps `Location = RedirectUri`); `src/Http/Http.Extensions/src/CookieRedirectEndpointConventionBuilderExtensions.cs:24-28`. The method exists with that name. `EXECUTED` (probe C): `/api/me` answered `401` with `loc=http://localhost/Account/Login?ReturnUrl=%2Fapi%2Fme`.
**The defect:** every `401` names a login path that does not exist. A non-`/api` miss still answers `302`.
**What to do:** consider setting `OnRedirectToLogin`/`OnRedirectToAccessDenied` on the application cookie to a bare `401`/`403`, globally, instead of or as well as the group convention.

### SRC-16: what the stamp check costs, and what it does not say
**NOTE (step 2 "Sessions"; security.md "Sessions").** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Identity/Core/src/SecurityStampValidator.cs:136-162`, `SignInManager.cs:304-326`.
- **Within a minute holds.** The check runs when `now − IssuedUtc > ValidationInterval`. Every success re-issues the cookie (SRC-2), so even a copied cookie that keeps refreshing is at most a minute old. It is refused on its first request after that minute.
- **Cost:** at most one stamp check per client per minute. That is `FindByIdAsync` plus the claims factory, whose user-claims query is a second round trip. It comes with one re-issued `Set-Cookie`. Within the minute there is nothing. `INFERRED` for the second query: the store supports claims, so the factory calls `GetClaimsAsync`.
- `SignInManager.SignOutAsync` does **not** rotate the stamp. The sign-out endpoint must call `UpdateSecurityStampAsync`, and doing so signs out every browser.
- Every request that passes `UseAuthentication` slides the session, anonymous `/health` included. "Use" means any request that carries the cookie.

### SRC-17: citation precision
**NOTE.**
- `SOURCE@dotnet/aspire src/Aspire.Hosting/ProjectResourceBuilderExtensions.cs` (step 7, and security.md "Forwarded headers") has no ref or lines. It should be `@v13.6.1:…:633-643,960-964`, confirmed below.
- `[SOURCE@dotnet/aspire DockerComposeEnvironmentResource.cs]` (step 1) should be `@v13.6.1:src/Aspire.Hosting.Docker/DockerComposeEnvironmentResource.cs:616-632`.
  - The hash applies to the project name that `aspire deploy` uses (`AppHost:PathSha256`).
  - A `docker compose up` run by hand in the output directory takes the directory name instead.
  - The runbook should say which one it uses.
- `PasskeyHandler.cs:51-55`, cited for "RP ID is `ServerDomain ?? Request.Host.Host`": the rule is at `:677-678`; `:51-55` only assigns it.
- `CorsMiddleware.cs:116-130` is the `[DisableCors]` branch. The "never refuses" claim for a policy is at about `:150-190`, where both branches call `_next` for non-preflight requests.
- The setup-code value reaches `.env.<environment>` at **deploy** (`DockerComposeEnvironmentResource.cs:548-578`, `includeValues: true`). `aspire publish` writes `.env` keys-only (`DockerComposePublishingContext.cs:151-163`). Step 1's "at publish" should say deploy.

### SRC-18: enrolment's "one transaction" and the token's binding
**NOTE (steps 1 and 4).**
- `UserManager.CreateAsync` and `AddOrUpdatePasskeyAsync` each save through the store, so "in one transaction" needs an explicit `Database.BeginTransactionAsync`. The "no user exists" check needs to sit inside it, under a lock or a constraint, or two concurrent enrolments can both pass. `INFERRED` (EF store `AutoSaveChanges` default; not run).
- The antiforgery token binds to the **user id** (`DefaultClaimUidExtractor.cs:39-75`), not to the session. A token from before sign-out validates in the same user's next session. Step 4's "discarded on sign-out" is a client convention, which is fine; say so.

---

## Gates

| Claim | Marker now | Finding | What raises it |
|---|---|---|---|
| Aspire allocates the Vite endpoint's port per run (no fixed port) | INFERRED | SRC-12 (SHOULD FIX rests partly on it) | `G-5`'s spike: two `aspire run`s, log `Request.Headers.Origin` on the options endpoint both times |
| The claims factory issues a second query per stamp check | INFERRED | SRC-16 (NOTE) | EF command logging during one validated request |
| `UserManager.CreateAsync` and `AddOrUpdatePasskeyAsync` each `SaveChanges`; the concurrent enrolment race | INFERRED | SRC-18 (NOTE) | Step 1 integration test: two parallel enrolments, assert one `409` |
| Data protection fails at first protect when the key directory is unwritable (`500`, not a silent in-memory ring) | INFERRED (the unwritable mount itself is EXECUTED) | SRC-7 | Published-stack sign-in with the volume at a root-owned path |

---

## Confirmed (do not re-check)

- `IdentityPasskeyOptions` (`SOURCE@aspnetcore@v10.0.13:src/Identity/Core/src/IdentityPasskeyOptions.cs`):
  - `ServerDomain` is `string?`.
  - `ValidateOrigin` is `Func<PasskeyOriginValidationContext, ValueTask<bool>>?`.
  - `AuthenticatorTimeout` defaults to 5 min; `UserVerificationRequirement` defaults to `"required"`; `ResidentKeyRequirement` to `"preferred"`; `ChallengeSize` is 32.
  - With UV `required`, the handler refuses a missing UV flag: `PasskeyHandler.cs:637-642`.
- The default origin check accepts only an origin equal to the request's `Origin` header, and refuses `crossOrigin`: `PasskeyHandler.cs:651-675`. The RP ID is `ServerDomain ?? Request.Host.Host`: `:677-678`.
- The sign counter is checked only when either count is non-zero: `PasskeyHandler.cs:520-530`.
- The challenge state is stored in `TwoFactorUserId` cookie properties and signed out on read: `SignInManager.cs:696-732`. That cookie lives 5 minutes: `IdentityCookiesBuilderExtensions.cs:98-108`.
- `PasskeySignInAsync` runs `PreSignInCheck` after a successful assertion (`SignInManager.cs:669`). `Lockout.AllowedForNewUsers` exists, default `true`: `LockoutOptions.cs:19`.
- `__Host-` is compatible with every Identity cookie:
  - `RequestPathBaseCookieBuilder` emits `Path=/` when there is no `PathBase` (`src/Security/Authentication/Core/src/RequestPathBaseCookieBuilder.cs:22-38`), and no `Domain` unless set.
  - `CookieBuilder.cs:117` emits `Secure` for `Always`.
  - `Identity.TwoFactorUserId` is a real cookie name: `IdentityConstants.cs:41`, `IdentityCookiesBuilderExtensions.cs:102`.
  - Setting `Cookie.Path = "/"` explicitly would make this independent of `PathBase`.
- Cookie authentication defaults to `SameSite=Lax`, `SecurePolicy=SameAsRequest`, `HttpOnly`, a 14-day ticket and sliding expiration: `CookieAuthenticationOptions.cs:14-33`.
- `ValidationInterval` defaults to 30 min (`SecurityStampValidatorOptions.cs:17`). Validation is gated on elapsed time since `IssuedUtc`: `SecurityStampValidator.cs:138-162`.
- Antiforgery:
  - the cookie defaults to `Strict`, `HttpOnly`, `SecurePolicy.None` (`AntiforgeryOptions.cs`);
  - it throws when `Always` is set and the request is not HTTPS (`DefaultAntiforgery.cs:256-263`);
  - `SuppressXFrameOptionsHeader` exists, and antiforgery adds `SAMEORIGIN` only if no `X-Frame-Options` is present (`:247-253`);
  - the header is read first, and the form only for form content types (`DefaultAntiforgeryTokenStore.cs:44-74`);
  - the token binds to `sub`/`NameIdentifier` (`DefaultAntiforgeryTokenGenerator.cs:55-68`, `DefaultClaimUidExtractor.cs`);
  - the middleware validates only form methods on endpoints with metadata, and calls `next` on failure (`AntiforgeryMiddleware.cs:25-51`); minimal APIs infer that metadata only for form reads (`RequestDelegateFactory.cs:397-402`);
  - `WebApplicationBuilder` at this tag auto-inserts authentication and authorization, **not** antiforgery (`WebApplicationBuilder.cs:434-453`).
- `.DisableCookieRedirect()` exists in 10.0.13, and JSON-body endpoints infer the same metadata: `RequestDelegateFactory.cs:404-409`.
- `FallbackPolicy` is used only when an endpoint has no authorize metadata (`AuthorizationPolicy.cs:189-197`), and applies to null endpoints (`AuthorizationMiddleware.cs:97-150`; `EXECUTED` probe).
- `RejectionStatusCode` defaults to `503` (`RateLimiterOptions.cs:41`). Fixed-window failed leases carry `RetryAfter` metadata (`FixedWindowRateLimiter.cs:209-216`, `:421-440`).
- Aspire `DisableForwardedHeaders()` exists on `IResourceBuilder<ProjectResource>`:
  - `ProjectResourceBuilderExtensions.cs:960-964`; publish mode sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` unless it is called (`:633-643`);
  - with that set, ASP.NET trusts `X-Forwarded-For`/`-Proto` from any address (`aspnetcore@v10.0.13:src/DefaultBuilder/src/ForwardedHeadersOptionsSetup.cs`: `KnownIPNetworks` and `KnownProxies` cleared).
- `PublishAsDockerComposeService<T>(Action<DockerComposeServiceResource, Service>)` exists, and `Service.Volumes` is a mutable list (`DockerComposeServiceExtensions.cs:35`, `Resources/ComposeNodes/Service.cs:134`). See SRC-7 for why that alone is not enough.
- `IdentityUserContext<TUser>` carries `UserPasskeys` under `Version3`: `IdentityUserContext.cs:130,170-172`.
- WebAuthn L3 §15.1's recommended timeout range is 5 to 10 minutes (`DOCS@2026-10-24`).
- security.md and slice-03.md agree on cookies, the stamp interval, CORS, forwarded headers and the limiter's partitioning. They share the two defects in SRC-8 and SRC-11.

---

## Axis 2 (no severity)

- SRC-2's smallest fix, `OnSigningIn`, also makes the enrolment sign-in and any future password sign-in persistent. That is one rule in one place, instead of a hand-rolled assertion path that copies `PasskeySignInCoreAsync`.
- SRC-1 and SRC-8 together suggest one pipeline test, cheap and permanent: assert the order through `IApplicationBuilder` descriptions, or by request, with static `200`, `/api` `401`, a throwing endpoint's `500`, and each response's headers.
- SRC-9's `https://localhost` choice means the integration tier never exercises the production `IsHttps == false` path. If that matters, the published-stack checks in step 7 are where it is covered.
