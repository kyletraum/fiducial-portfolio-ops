# Slice 03 review, round 2: test automation seat (TST)

**Seat:** test automation. **Date:** 2026-10-24. **Under review:** PR #74,
`git diff slice-03-dispositions -- specs/001-portfolio-platform/` (`slice-03.md` "The build"
1–7 and "Definition of done" 1–10; the *(slice 03)* corrections in `security.md`).

**Read:** `slice-03.md` (whole); the `security.md` diff; `src/Api/{Program,CrossSiteWrites,Endpoints,ImportEndpoints}.cs`,
`src/Api/appsettings*.json`; `src/ServiceDefaults/Extensions.cs`; `src/AppHost/AppHost.cs` and its launch settings;
`web/vite.config.ts`; every file under `tests/Integration`, `tests/E2E`, `tests/Architecture`, and
`tests/Contract/OpenApiDocumentTests.cs`; `.github/workflows/{ci,hygiene}.yml`; `.githooks/pre-commit`;
`scripts/{install-hooks.sh,hygiene.py}`; `.gitignore`; `deploy/.env.example`;
`spikes/spike-h-auth/SpikeH.{Web,Driver}/Program.cs` (from `origin/slice-03-spike-h`). I read
`specs/review/slice-03-committee.md` only to check the cited ids G-14, G-19, G-20, G-22, G-24, G-29, G-30, C3-2, C3-12,
C3-13, C3-20, C3-26, C3-28, C3-29, C3-31, C3-40 and C3-43. Each says what the spec attributes to it, except where TST-11 notes otherwise.

**Ran:**
- `dotnet test --project tests/Architecture`: 52/52 passed.
- `tests/Integration/bin/Debug/net10.0/Integration.exe -list full`, to count the cases DoD 9 turns red.
- `dotnet run cc.cs`, a scratch `CookieContainer` probe (TST-6).
- `python3 --version` and `command -v python3` in Git Bash (TST-16).
- The repository's own `.githooks/pre-commit`, installed by `scripts/install-hooks.sh` in a scratch repository, on a clean commit (TST-16).
- Read-only `WebFetch` of the `dotnet/aspnetcore@v10.0.13` sources cited below.

---

## Findings

**Count:** 0 BLOCKING, 14 MUST FIX, 8 SHOULD FIX, 3 NOTE.

### TST-1 — MUST FIX (steps 2, 3, 4; DoD 9): step 2's `FallbackPolicy` *is* the switch, and step 3's filter breaks every write before step 4

**Markers:** `SOURCE@dotnet/aspnetcore@v10.0.13:src/Security/Authorization/Core/src/AuthorizationPolicy.cs:189-197`. The fallback applies when no authorize data or policy is present, which is true of every existing endpoint (`src/Api/Endpoints.cs:23-34`, `ImportEndpoints.cs:22-42`; none carries authorization metadata).

**The defect.** Step 2 sets `FallbackPolicy = RequireAuthenticatedUser()`. Step 2 also says "the session requirement on the existing endpoints is switched on in step 4, not here". Those two cannot both hold. The fallback policy switches every existing endpoint on the moment it lands. Step 3's antiforgery filter on `/api` then refuses every token-less write, whether or not a session exists. The web client sends no token until step 4. I counted what goes red, by reading the code and `Integration.exe -list full`:

| When | Goes red | Why |
|---|---|---|
| Step 2 (fallback) | **Integration: 4 classes, 20 methods, 36 cases.** `ImportApiTests` 12 methods/19 cases, `CrossSiteWriteTests` 4/12, `ClockTests` 2/3, `BalanceIdempotencyTests` 2/2. All use a bare `WebApplicationFactory` client (`ImportApiTests.cs:14-26`, `BalanceIdempotencyTests.cs:18-23`, `ClockTests.cs:20-26`) | 401 |
| Step 2 | **E2E: every class but `AppModelTests`, 13 cases.** `AppFixture` itself waits for `api` healthy on `/health` (`AppFixture.cs:35`, `AppHost.cs` `.WithHttpHealthCheck("/health")`), so the fixture hangs for 5 min (see TST-4) | 401 on `/health` |
| Step 3 (filter), even with a session | Every token-less write: `BalanceIdempotency` 2, `ClockTests` balance case 1, `CrossSiteWriteTests.A_write_from_the_app…` 3, about 16 of `ImportApiTests`' 19; E2E `NewAccountAsync`/`TimeZoneTests` setup (`CrossSiteBrowserTests.cs:76,120-136`, `TimeZoneTests.cs:60,86-90`); the running web app | 403 |
| Stay green | `HostFilteringTests` 7 cases, but only if `GetHealth` is on the allow-list. Every DB-only Integration class. Unit, Architecture, Contract | — |

**What to do.** Write the sequence into steps 2–4:
- **Step 2** lands `SignedInClient` and moves the four HTTP classes onto it, but leaves `FallbackPolicy` *out*.
- **Step 3** gives `SignedInClient` a `DelegatingHandler` that fetches the token, keeps the antiforgery cookie and sets `X-CSRF-TOKEN` on unsafe methods. Steps 3 and 4 land in one PR, because E2E is local-only (`ci.yml:3-5`) and CI cannot see the web app break between them.
- **Step 4** adds `FallbackPolicy` and the E2E signed-in helper.

DoD 9 then says "the 36 Integration cases run on `SignedInClient`; the 13 E2E cases on the signed-in context helper".

### TST-2 — MUST FIX (step 2, DoD 2): on the test-scheme factory, the allow-list test goes green for the wrong reason

**Marker:** `INFERRED`. When the test scheme is the default, it also becomes the default challenge scheme, so the 401 comes from the test handler. Gate GT-1.

**The defect.** `SignedInClient`'s scheme comes from `ConfigureTestServices`. If DoD 2's "by request" test, or DoD 6's 401 case, runs on that factory, the anonymous 401 is the *test handler's* challenge. The cookie handler's `.DisableCookieRedirect()` / `OnRedirectToLogin` never runs, and Spike H found that the cookie handler redirects by default (`slice-03.md:143-144`). A real `302 → /Account/Login` would pass.

**What to do.** DoD 2 names its factory: the unmodified Production pipeline (the `HostFilteringTests.ApiFactory` shape plus a database), with no `ConfigureTestServices`. Assert `401`, no `Location` header, and a `ProblemDetails` body. Also write the test handler to authenticate **only** a request carrying a marker header (`X-Test-User: <id>`), and to answer `NoResult` otherwise. Then even an anonymous request on the shared factory behaves as production does.

### TST-3 — MUST FIX (step 2): the test-scheme guard is vacuous as written, and its compose half has no input in CI

**Markers:** `tests/Architecture/Architecture.csproj` (no project references, so it reads files); `.gitignore` (`aspire-output/`, `publish/` ignored); `ci.yml:37-38` ("no Aspire CLI is installed here").

**The defect.**
- "Nothing under `src/` registers it" is guaranteed by the project graph. The handler lives in `tests/Integration`, which no `src` project can reference. A grep for the scheme name catches only a second, identically named handler. A config-driven bypass under any other name (`if (config["Auth:Bypass"]) …AddScheme<…>("x")`, or `AllowAnonymous` toggled by configuration) passes.
- "The published compose file carries no switch": the design has no switch, so there is nothing to find. The compose file is also never generated or committed where a test can read it. The same applies to step 1's "a test asserts the published compose file carries none [setup code]".

**What to do.** Replace both with tests that can fail:
- **(a) Integration, unmodified Production factory.** `IAuthenticationSchemeProvider.GetAllSchemesAsync()` names equal exactly the expected Identity set. `GetDefaultAuthenticateSchemeAsync()` and `GetDefaultChallengeSchemeAsync()` are `Identity.Application`. `AuthorizationOptions.FallbackPolicy` requires an authenticated user. Run the same test in Development too.
- **(b) Architecture.** No `src/*.csproj` has a `ProjectReference` into `tests/`, using the existing `ProjectFile` helper (`AspireBoundaryTests.cs:89-113`).
- **(c) Compose.** Either generate it in the test (gate GT-2: whether `dotnet run --project src/AppHost -- --operation publish …` works without the CLI), or assert over the app model in publish mode in `tests/E2E`: the `api` resource's environment holds the setup code only as a parameter reference, never a literal. Say which, and that (c) is local-only.

### TST-4 — MUST FIX (step 2, DoD 2): the Development allow-list misses `/health` and `/alive`, which have no names, and the AppHost probes `/health`

**Markers:** `src/ServiceDefaults/Extensions.cs:109-125` (`MapHealthChecks`, Development only, no `WithName`); `src/AppHost/AppHost.cs` `.WithHttpHealthCheck("/health")`; `src/Api/Program.cs:39-42` (`MapOpenApi` in Development).

**The defect.** "An explicit allow-list of route names" naming "the anonymous health route" (singular) is ambiguous: `GetHealth` at `/api/v1/system/health`, or `/health`? The Development endpoints `/health`, `/alive` and `/openapi/{documentName}.json` carry no endpoint name, so a name-keyed list cannot express them. If they fall under the fallback, `aspire run` never sees `api` healthy, `web` (`WaitFor(api)`) never starts, and `AppFixture` times out. That is every E2E.

**What to do.** Key the list on endpoint name, or on `RoutePattern.RawText` for unnamed endpoints, with **two** lists: Production `{GetHealth, enrolment…, passkey options, passkey sign-in}`; Development adds `/health`, `/alive` and (Kyle's call) `/openapi/{documentName}.json`. Do not give those `.WithName` in ServiceDefaults. "Every entry must exist" runs per environment.

### TST-5 — MUST FIX (steps 2/3, DoD 2): the fallback policy covers static files, so the published sign-in page answers 401, and no CI test can see it

**Markers:**
- `SOURCE@dotnet/aspnetcore@v10.0.13:src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs:~113-151`: a null endpoint gives empty metadata, and `CombineAsync` then returns the fallback.
- `SOURCE@dotnet/aspnetcore@v10.0.13:src/DefaultBuilder/src/WebApplicationBuilder.cs:~418-460`: the auto-added `UseAuthentication`/`UseAuthorization` run *before* the user's middleware.
- `src/Api/Program.cs:50` (`UseFileServer()` is user middleware).

**The defect.** "Static files are anonymous by design" (DoD 2) is false of today's pipeline once `FallbackPolicy` lands. `GET /` and `/assets/*.js` reach `UseFileServer` with no endpoint, after authorization, and get 401. Integration has no `wwwroot` (`src/Api/wwwroot` absent). E2E serves the page from Vite, which never asks the API for it. So only the hand-run published stack would find this.

**What to do.** Add an Integration test on the Production factory with a temporary web root: `UseSetting(WebHostDefaults.WebRootKey, tmp)` holding `index.html` and `assets/app.js`.
- Anonymous `GET /` and `GET /assets/app.js` answer `200`.
- `GET /` carries the exact published CSP string (DoD 6's header half).
- Anonymous `GET /api/v1/accounts` answers `401`.

In step 3, place `UseFileServer` (or an `AllowAnonymous` static-assets endpoint) ahead of authorization explicitly.

### TST-6 — MUST FIX (step 2, `AuthFixture`; DoD 2, 7): .NET's `CookieContainer` stores a `Secure` cookie from `http://localhost` and never sends it back

**Marker:** `EXECUTED 2026-10-24` `dotnet run cc.cs` (.NET 10.0.402). After `SetCookies(http://localhost/, "__Host-sess=abc; Path=/; Secure; …")`, `Count=2`, but `GetCookieHeader(http://localhost/)` returned only `plain=1`. For `https://localhost/` it returned both.

**The defect.** `WebApplicationFactory.CreateClient()` handles cookies with a `CookieContainer` on base address `http://localhost`. Every `__Host-`, `SecurePolicy=Always` Identity cookie is dropped on the way back. The real-ceremony tests would then see:
- the passkey challenge cookie (`TwoFactorUserId`) missing at assertion;
- the session missing after sign-in;
- the DoD 2 "signed-out cookie" 401 for the wrong reason.

The E2E's `new HttpClient { BaseAddress = fixture.ApiUrl }` helpers (`CrossSiteBrowserTests.cs:76`, `TimeZoneTests.cs:60,86`) have the same flaw.

**What to do.** `AuthFixture`'s factory sets `ClientOptions.BaseAddress = new("https://localhost")` (TestServer then reports `IsHttps`). Say in the spec that the antiforgery "not `Secure` over HTTP" behaviour is therefore exercised only by the E2E. For the DoD 2 replay, send the copied cookie by hand (`request.Headers.Add("Cookie", …)`), so the container is not in the path. In the E2E, do setup writes and "nothing written" reads with `page.EvaluateAsync(fetch…)` inside the signed-in app page, which carries the cookie and the token, and remove the bare `HttpClient` helpers.

### TST-7 — MUST FIX (step 2): "the auth classes run the real ceremony" names no authenticator

**Markers:** `INFERRED`. Identity accepts a `none`-format attestation from a software authenticator: gate GT-3. `tests/Integration/Integration.csproj` holds no Playwright, and TestServer has no socket a browser could reach.

**The defect.** Integration cannot run the CDP virtual authenticator. A real ceremony there needs a C# software authenticator that:
- generates an ES256 key;
- builds `clientDataJSON` with the configured origin;
- builds `authenticatorData` (rpIdHash of `localhost`, flags UP|UV, sign count);
- CBOR-encodes a `none` attestation object;
- signs assertions.

That takes about 150–250 lines and a `System.Formats.Cbor` dependency (`INFERRED`: not in the shared framework; GT-3). Without it, the `AuthFixture`, DoD 2's replay, DoD 7's canaries (the session cookie and passkey assertion), and step 2's lockout, `rp.id` and origin tests have nothing to drive them.

**What to do.** Name `SoftAuthenticator` in `tests/Integration` in step 2. It must also produce the hostile variants: foreign origin, `crossOrigin: true`, UV flag off, replayed counter, malformed CBOR. Those are what make `ValidateOrigin`, "UV `required`" and the "malformed assertion" canary testable (TST-9, TST-14).

### TST-8 — MUST FIX (step 2): "the Vite port" is not a constant, so a hard-coded `ValidateOrigin` list fails every E2E ceremony

**Markers:** `src/AppHost/AppHost.cs` (`AddViteApp("web", …)` with no port); `AppHost.cs` comment: "DistributedApplicationTestingBuilder randomises ports"; `AppFixture.cs:37` (`WebUrl` read at runtime); `INFERRED` that `aspire run` also allocates the Vite port per start (gate GT-4; G-5 is the place to observe it).

**The defect.** `ValidateOrigin` "accepts exactly … the Vite port under `aspire run` and `http://localhost:8080` published". Under the test builder the Vite origin differs on every run, so a constant list rejects every E2E ceremony, and DoD 3's E2E cannot pass.

**What to do.** The allowed origins come from configuration (`Passkeys:Origins`), set by the AppHost from `web`'s endpoint reference in run mode and from the published port at publish. Whether an `api → web` endpoint reference beside `web → api` is allowed is gate GT-4. Add an Integration test with `SoftAuthenticator`: an assertion whose `clientDataJSON.origin` is one configured origin succeeds; another `localhost` port fails; `crossOrigin: true` fails.

### TST-9 — MUST FIX (step 3, DoD 4): the pipeline order is unstated, and DoD 4's oracles depend on it

**Markers:** `SOURCE@dotnet/aspnetcore@v10.0.13:src/DefaultBuilder/src/WebApplicationBuilder.cs:~418-455` (auto auth before user middleware); `src/Api/Program.cs:33-37`; `CrossSiteBrowserTests.cs:113-119` (cases include `attacker.localhost`).

**The defect.** DoD 9 says `CrossSiteWriteTests` is "re-pinned to the new refusal order", but no step states that order. Two DoD 4 lines depend on it:
- **"The cookie is proven sent; a 401 fails the test."** If authorization runs before `UseCrossSiteWriteRefusal` (today's default once auth services exist), a same-site case's layer-2 403 title *does* prove the cookie authenticated. If layer 2 runs first, the 403 proves nothing about the cookie. And for the `attacker.localhost` (cross-site) cases, the `Strict` cookie is *not sent by design*. Under authorization-first they get 401, so "a 401 fails the test" fails the existing cross-site cases.
- **"A passkey sign-in attempt" from another `localhost` port.** The browser permits the ceremony (rp.id `localhost` is a suffix of the attacker's host: `INFERRED`, GT-5). Its `POST` is refused by layer 2 (`same-site`) before `ValidateOrigin` is reached. So the browser case says nothing about origin validation. That belongs to TST-8's Integration test.

**What to do.** Step 3 writes the pipeline as a list:
1. headers;
2. exception handler / status pages;
3. host filtering (TST-10);
4. `UseCrossSiteWriteRefusal`;
5. `UseAuthentication`;
6. `UseAuthorization`;
7. rate limiter;
8. static files (TST-5);
9. endpoints, with the antiforgery filter.

DoD 4's browser half gets a table of (attacker host × target × request kind) → expected refusal title and cookie expectation (sent / not sent). Prove "sent" with Playwright's `IRequest.AllHeadersAsync()["cookie"]` containing the session cookie's name (`INFERRED` that Chromium reports it through Playwright: GT-6), and assert *not sent* for the cross-site cases.

### TST-10 — SHOULD FIX (step 3, DoD 6): "one middleware placed first in `Program.cs`" is not first for 400, 401 or 500, and DoD 6 names no way to produce each status

**Markers:**
- `SOURCE@dotnet/aspnetcore@v10.0.13:src/DefaultBuilder/src/WebHost.cs:~273` (`HostFilteringStartupFilter` registered as an `IStartupFilter`, so it wraps the app's pipeline: `INFERRED` ordering, GT-7).
- `…/src/Middleware/Diagnostics/src/ExceptionHandler/ExceptionHandlerMiddlewareImpl.cs:~291-293` (`ClearHttpContext` calls `context.Response.Clear()`; `INFERRED` that this clears headers, GT-7).
- `WebApplicationBuilder.cs:~440-455` (auto auth precedes user middleware).

**The defect.** The DoD 6 test will rightly fail. That is good, but the step 3 design as written cannot pass it:
- A host-filter `400` never reaches the middleware.
- A `401` from the auto-added authorization never reaches it.
- A `500` has its headers wiped by `Response.Clear()` if they were set eagerly.

DoD 6 also does not say how a test produces each status. The Production app has no endpoint that throws.

**What to do.** Step 3: set the headers in `Response.OnStarting`, call `UseAuthentication`/`UseAuthorization` explicitly after the headers middleware, and either accept that a host-filter 400 carries no headers (and say so) or register the headers middleware as an `IStartupFilter` too. DoD 6's test is one theory over a producer table, on the Production factory:

| Status | Producer |
|---|---|
| 200 | anonymous `GET /api/v1/system/health` |
| 400 | signed-in `POST /api/v1/accounts` with `{`; and `Host: evil.example` (`HostFilteringTests.cs:33-41`) |
| 401 | anonymous `GET /api/v1/accounts` (unmodified factory, TST-2) |
| 403 | signed-in write with `Sec-Fetch-Site: same-site`; and a token-less write |
| 404 | signed-in `POST /api/v1/accounts/{new Guid}/sources` (`ImportApiTests` "Binding_to_an_unknown_account_is_404") |
| 415 | signed-in `POST /api/v1/imports` as `text/plain` (`CrossSiteWriteTests.cs:247`) |
| 500 | signed-in `GET /api/v1/accounts` on a factory whose connection string points at a closed port, with Aspire's retry disabled by setting (`INFERRED` key name, GT-8), so the test does not wait out the retry back-off |

Assert every header and `Strict-Transport-Security` absent on each.

### TST-11 — MUST FIX (DoD 6, step 7): the CSP oracle is vacuous if axe's `BypassCSP` shares its context, and it has no positive control

**Markers:** G-29 (the committee's gate). DoD 6 as written dropped the C3-20 disposition's "separate CSP and axe contexts if G-29 needs `BypassCSP`". `INFERRED`: Chromium's CSP violation messages reach Playwright's `page.Console` (GT-9).

**The defect.** "Zero 'Refused to' console messages" passes whenever:
- the context runs with `BypassCSP = true` (which G-29 may require for axe);
- the CSP header is missing altogether (TST-5);
- the console listener is attached late or misses `Log.entryAdded`-sourced entries.

Nothing in DoD 6 can make it fail.

**What to do.** The CSP context has `BypassCSP = false`, and axe runs in a separate context. In the CSP context:
- The document response's `Content-Security-Policy` equals the published string, by exact comparison.
- An init script records `securitypolicyviolation` events into `window.__csp` alongside the console filter.
- **Positive control first:** `page.AddScriptTagAsync(new() { Content = "window.__x=1" })` must produce exactly one violation, and `window.__x` must stay undefined.
- Then run the functional path, then assert zero further violations.

Because the built bundle is served by the published stack (step 7), state that this test runs by hand and is evidenced per D13.

### TST-12 — MUST FIX (step 3, DoD 5): a fixed-window trip is timing-dependent unless the test owns the window

**Marker:** `INFERRED`: `FixedWindowRateLimiter` replenishes on its own timer and takes no `TimeProvider` (GT-10). `SOURCE@dotnet/aspnetcore@v10.0.13:src/Middleware/RateLimiting/src/RateLimiterOptions.cs:41` (as the spec cites).

**The defect.** "Assert the trip, never the reset" is still flaky with the production numbers. A burst of N+1 that straddles a window boundary sees no 429. The limiter is a singleton per factory, so every test in a class (and E2E, with one app per class) shares one budget.

**What to do.** Bind the limiter's numbers to options (`RateLimits:Auth:PermitLimit`, `:Window`). DoD 5's trip test overrides `Window` to one hour and `PermitLimit` to 3 on its own factory, sends 4, and asserts `429`, `Retry-After` present and numeric, the exact title, no partition key or body in the captured log (TST-14's capture), and that the 4th request reached no handler (no audit row). A second test, on the unmodified Production factory, asserts that the options hold the recorded numbers. The page half uses `page.RouteAsync` to fulfil a 429 with that body (`SectionFailureTests.cs:278-279`) and asserts the title and "try again after" text.

### TST-13 — MUST FIX (step 5): the canary injection trips the limiter, so canaries never reach the handler and the test passes

**Markers:** `slice-03.md:422-426` (canaries injected "into every field of every anonymous endpoint"), `:352-357` (the limiter covers exactly those endpoints).

**The defect.** Three or more anonymous endpoints × every field × three canaries, plus enrolment, sign-in, failed sign-in and malformed assertion, add up to dozens of requests to a limiter sized for a human. Most get a `429` from the limiter before any handler, binder or EF logs the value. The "clean" result is the limiter's.

**What to do.** The canary factory raises `PermitLimit` (TST-12's options). The test asserts that **no** response was `429`, and that each injected request got the status its handler produces (`400`/`401`/`409`), so every canary provably reached the code that might log it. Do the same in step 2's lockout test (TST-21) and for any E2E class that signs in more than once.

### TST-14 — SHOULD FIX (step 5, DoD 7): the capture recipe must name what it reads, and the base64 check must cover all three alignments

**Markers:** `src/ServiceDefaults/Extensions.cs:46-52` (`IncludeScopes = true`; OTel logging provider); `src/Api/appsettings.json` (`Microsoft.AspNetCore: Warning` overrides `SetMinimumLevel`); G-22, G-24 (gates already named). `INFERRED`: OTel AspNetCore 1.20 records `url.query` redacted by default (GT-11).

**The defect.** "Every captured log line" is ambiguous. A provider that keeps only `formatter(state, ex)` misses:
- structured state values not in the template;
- scope values;
- `Exception.ToString()`, inner exceptions and `Data`;
- Activity tags, events and status descriptions.

A canary embedded in a larger base64 blob appears as `base64(canary)` only at offset ≡ 0 mod 3. The test also does not say how it produces "a `500`" or "a malformed assertion".

**What to do.**
- **Capture:** every state `KeyValuePair`, every scope value (through `IExternalScopeProvider`), the exception chain, and every Activity's tags, events and status from the in-memory exporter.
- **Levels:** `UseSetting("Logging:LogLevel:Default","Trace")` plus the `Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore` keys.
- **Non-empty guard:** assert ≥1 `Microsoft.AspNetCore.Hosting` Trace line *and* one span for `GET /api/v1/system/health` (G-22).
- **Base64:** match raw, `Uri.EscapeDataString`, and for k ∈ {0,1,2} the base64 and base64url of `k` filler bytes + canary with the edge characters trimmed.
- **Producers:** the `500` from TST-10's producer; the malformed assertion from `SoftAuthenticator` (TST-7).
- **"Seen failing once":** keep it as a committed test that logs one canary at `Debug` and asserts the scanner finds it. A one-off is not enough.

### TST-15 — SHOULD FIX (step 4, DoD 6): pending, failure and "ticket expired" need deterministic producers, and none is named

**Markers:** `INFERRED` for the virtual-authenticator behaviour (answers at once with `automaticPresenceSimulation` on; UV-off gives `NotAllowedError`; GT-12). `INFERRED`: the test builder can add environment to the `api` project resource (GT-13).

**The defect.**
- With `isUserVerified: true` (as `SpikeH.Driver/Program.cs` configures it), "Waiting for your passkey…" lasts milliseconds, so a focus/text assertion on *pending* is a race.
- `NotAllowedError` vs `SecurityError` vs unsupported cannot all be produced by one authenticator configuration.
- "One E2E case tells 'no cookie sent' from 'ticket expired'" needs a really expired ticket in a separate API process, where the test cannot move the clock. A sleep past the one-minute `ValidationInterval` is what the spec otherwise implies.

**What to do.**
- **Success:** one real ceremony.
- **Pending:** hold the sign-in `POST` with `page.RouteAsync` until the assertions are done, then `ContinueAsync`.
- **Failure branches:** an `AddInitScript` that replaces `navigator.credentials.get` with a rejecting `DOMException` (`NotAllowedError`, `SecurityError`) or deletes `PublicKeyCredential` for "unsupported". Init scripts are injected over CDP, so the built-bundle CSP does not block them (`INFERRED`, GT-9).
- **Expiry without sleeping:** bind `ValidationInterval` to configuration. The E2E fixture sets it to `00:00:00` on `api`. Copy context A's cookies to context B, sign out in A (which rotates the stamp), and B's next request is a `401` *with* the cookie sent, which is "ticket expired". `ClearCookiesAsync` gives "no cookie sent".

### TST-16 — MUST FIX (step 6, DoD 8): on this machine `python3` *exists* and fails, so "falls back where `python3` is absent" never falls back, and the hook refuses every commit today

**Markers:**
- `EXECUTED 2026-10-24` Git Bash: `command -v python3` → `/c/Users/kylet/AppData/Local/Microsoft/WindowsApps/python3`, exit 0; `python3 --version` → "Python was not found; run without arguments to install from the Microsoft Store…", exit 49.
- `EXECUTED 2026-10-24`: `.githooks/pre-commit` installed by `scripts/install-hooks.sh` in a scratch repository, on a clean file: commit exit 1, with the same message.

**The defect.** The Store alias is a stub, so a presence check (`command -v`) picks it and the hook still fails. Today's hook (`.githooks/pre-commit:8`, `exec python3 …`) refuses even a clean commit here. Only the clean twin would tell, which is why the twin matters. The spec also leaves out what the test harness needs:
- The hook runs `scripts/hygiene.py` relative to the repository, so the temp repository needs `.githooks/`, `scripts/hygiene.py` and `scripts/install-hooks.sh` copied in.
- A CI runner has no `user.name`/`user.email`, so `git commit` fails for that reason alone.
- A developer's global config (signing, templates, `core.hooksPath`) leaks in.
- A secret-shaped literal in the test's own source is refused by `hygiene.py --range` in CI (`hygiene.yml`) and by the hook itself (compare the precaution at `ImportSupport.cs:46-49`).
- CI (Ubuntu, real `python3`) never exercises the fallback branch.

**What to do.**
- The hook selects the interpreter by *running* it: `for p in python3 python; do "$p" -c '' 2>/dev/null && exec "$p" scripts/hygiene.py --staged; done; echo "no python" >&2; exit 2`.
- The Architecture test runs with `GIT_CONFIG_GLOBAL` pointed at an empty file and `GIT_CONFIG_NOSYSTEM=1`, sets a local identity in the temp repository, and builds the secret value at runtime (`"-----BEGIN " + "PRIVATE KEY-----"`).
- It runs three cases: secret refused, with exit ≠ 0 and the text "CONSTITUTION IX: refusing this change."; clean, exit 0 with "hygiene: clean"; and the fallback, run with a `PATH` whose first `python3` is a shim that exits 49, still committing clean.
- It invokes `sh` and `git` from `PATH` on both OSes.

### TST-17 — SHOULD FIX (DoD 2): "every unsafe-method endpoint carries the filter, `DELETE` included" needs a body table, and the `DELETE` clause is vacuous

**Markers:** `INFERRED`: endpoint filters are compiled into the `RequestDelegate` and are not visible in metadata. JSON body binding (`415` / bad JSON `400`) runs before filters, and routing's `Accepts` 415 runs before the endpoint (`ImportEndpoints.cs:32-36`). Gate GT-14. Repository: `grep MapDelete|MapPut|MapPatch src` returns nothing.

**The defect.** No `DELETE`, `PUT` or `PATCH` endpoint exists, so "`DELETE` included" asserts nothing. "By request" over `EndpointDataSource` works only if each request carries an accepted content type and a body that binds. Otherwise the binder answers first and the filter is never observed. A generic enumeration that skips endpoints it cannot build a body for is green for the wrong reason.

**What to do.** Keep a table keyed by endpoint name: method, sample URL, content type and valid body (`CrossSiteWriteTests.TargetAsync`, `:253-263`, is the seed). The test fails when an unsafe endpoint in `EndpointDataSource` has no table row. Each row, signed in, `same-origin`, without a token, must give `403` with the filter's title. Keep the `DELETE` clause as a filter-level Unit or Integration test (the filter applied to a test-only `MapDelete` in a minimal `WebApplication`), or strike it.

### TST-18 — SHOULD FIX (DoD 2): the signed-out-cookie test is feasible without sleeping, but needs a control at T+30 s

**Markers:**
- `SOURCE@dotnet/aspnetcore@v10.0.13:src/Identity/Core/src/IdentityBuilderExtensions.cs:~45-47` (`AddSignInManager` registers `PostConfigureSecurityStampValidatorOptions`) and `:…PostConfigure` `options.TimeProvider ??= TimeProvider`.
- `…/src/Identity/Core/src/SecurityStampValidator.cs:~33,138,146`.
- `…/src/Security/Authentication/Core/src/AuthenticationBuilder.cs:~127-139` (`PostConfigureAuthenticationSchemeOptions`: `options.TimeProvider ??= timeProvider`).
- `tests/Integration/ClockTests.cs:25` (the DI swap already used); `ImportSupport.cs:9-13` (`FixedClock.Now` is settable).

**The defect.** Advancing the clock alone, a test that sees 401 cannot tell "stamp rotated" from "cookie never valid" or "cookie not sent" (TST-6). If sign-out stops rotating the stamp, the 401 must disappear; only a before-and-after pair shows that.

**What to do.** On the `AuthFixture` factory (https base, `FixedClock` through `ConfigureTestServices`):
1. Run the real ceremony.
2. Copy the `Cookie` value.
3. Call `POST` sign-out.
4. Set `Now += 30 s` and send the copy by hand: `200` (D4's accepted lag, documented).
5. Set `Now += 31 s` and repeat over every non-allow-listed endpoint (GETs; writes need TST-17's table and a token): `401`.

### TST-19 — SHOULD FIX (step 5): the OpenAPI credential-name test never gates, may see none of the auth endpoints, and trips on the enrolment request

**Markers:** `ci.yml:97-101` (Contract job `continue-on-error: true`); `spikes/spike-h-auth/SpikeH.Web/Program.cs` (options returned as `Results.Content(options, "application/json")`, so the endpoints are untyped).

**The defect.** The test sits in an advisory job. Passkey endpoints returning raw content contribute no schema, so the test scans nothing that matters. The enrolment request *must* carry a setup-code property, so "no schema property has a credential-like name" either fails by design or invites renaming the field to dodge it.

**What to do.** Put it in `tests/Architecture` (a pure read of `src/Api/openapi/portfolio-api.json`, which is required in CI). Scan **response** schemas only. Assert non-vacuity: the auth operations by name are present and at least *N* response properties were scanned.

### TST-20 — MUST FIX (step 1): "the hash is stored with the user" defeats "`reset-owner` reopens enrolment only for a different value"

**Marker:** spec-internal (`slice-03.md:283-284` with `:270-271`, `:448-449`).

**The defect.** `reset-owner` clears the user. If the setup code's hash lives on the user row, it goes with the user, and the old code in `.env.*` is accepted again. The test step 1 implies is: reset, re-enrol with the same code, expect refusal. Against the design as written, that test can only fail.

**What to do.** Store the hash outside the Identity rows (for example `app_setting`, `src/Domain/AppSetting.cs`). Name the Integration test: enrol with code A; `reset-owner`; enrolment with A answers a refusal, and nothing is created; with B it succeeds; each step is audited.

### TST-21 — SHOULD FIX (step 2): the no-lockout test is vacuous unless failed passkey sign-ins are shown to count

**Marker:** `INFERRED`: whether `PasskeySignInAsync` failure calls `AccessFailedAsync` (GT-15); `SOURCE@…SignInManager.cs:659-673` as cited.

**The defect.** "After repeated failures, the passkey still signs in" passes whether lockout is off *or* the failures never increment `AccessFailedCount`. It also collides with the limiter (TST-13).

**What to do.** A twin factory with `AllowedForNewUsers = true` and the same failure count must lock the user out, which proves the failures count. On the real factory, assert sign-in succeeds and `AccessFailedCount`/`LockoutEnd` stay as expected. The limiter is raised for both.

### TST-22 — SHOULD FIX (step 7, DoD 7): `revoke-sessions` / `reset-owner` are "tested" at no named layer, and audit counts accumulate per class

**Markers:** `tests/Integration/PostgresFixture.cs:29` (one database per test class); `src/Api/Program.cs` (top-level; `WebApplicationFactory` passes no mode argument).

**The defect.** A mode dispatched from `args` in `Program.cs` cannot be reached by `WebApplicationFactory`. "Each event kind asserted by count" on a class-shared database counts the other tests' rows.

**What to do.** Put the command bodies in an injectable service, invoked from `factory.Services.CreateScope()` in Integration. Keep one process-level test that runs the built Api with `revoke-sessions` (the `Process` pattern of `OpenApiDocumentTests.cs:67-69`). Counts are deltas per action, filtered by a per-test subject. Immutability follows `ImportPipelineTests.cs:189-201` verbatim for `audit_event`.

### TST-23 — NOTE (step 4 vs step 3): "an antiforgery `400`" vs the filter's `403`

Step 3 (`slice-03.md:347`) refuses with `403`; step 4 (`:398`) handles "an antiforgery `400`". The client logic, and its E2E, must key on the filter's `ProblemDetails.title`, not a status. Fix the wording to one status.

### TST-24 — NOTE (step 2): the `Set-Cookie` test should assert the antiforgery cookie carries no `__Host-` prefix, and name its oracle

A `__Host-` cookie without `Secure` is rejected by browsers (`INFERRED`, GT-6). The sentence "every Identity cookie … `__Host-` name … the antiforgery cookie is the one exception" leaves its name open. "Asserted on the server-seen `Cookie` header" (G-14) has no server echo in the E2E app. Either the API logs cookie *names* at Debug (which TST-14 would then scan), or the oracle is Playwright's request headers (GT-6). Say which.

### TST-25 — NOTE (step 4): the E2E signed-in helper

Enrol once per `AppFixture`, with the setup code set as a test-builder parameter. Sign in once, save `context.StorageStateAsync()`, and open new contexts with `StorageState`. That gives one enrolment and one sign-in per class, inside the limiter (`INFERRED` that restored `Secure` cookies are sent to `http://localhost`, GT-6).

---

## DoD table

| DoD | Test(s) | Layer | Oracle | Can it fail when false? |
|---|---|---|---|---|
| 1 | Spikes' `RESULTS.md` | — | restated markers | n/a (not a test) |
| 2a allow-list | enumeration over `EndpointDataSource` × {Production, Development} | Integration, unmodified factory | `401`, no `Location`, ProblemDetails; per-env lists (TST-4); every entry exists | **Only with TST-2/4.** On the test-scheme factory it is green for the wrong reason |
| 2b static anonymous | temp web root `GET /`, `/assets/app.js` | Integration | `200` + CSP | **Missing today (TST-5)** |
| 2c filter on every unsafe endpoint | body table, token-less | Integration | `403` + filter title; unlisted endpoint fails | Yes with TST-17; `DELETE` clause vacuous |
| 2d signed-out cookie | `AuthFixture` + `FixedClock` | Integration | `200` at +30 s, `401` at +61 s | Yes (TST-18); needs TST-6/7 |
| 3 | passkey enrol/sign-in | E2E (local), published by hand | UI + `/me`-equivalent `200` | Yes, if origins are config-driven (TST-8) |
| 4 int. | missing / foreign / other-user token; layer 2 alone | Integration, `SignedInClient` + token handler | status + title + `WrittenAsync` unchanged (`CrossSiteWriteTests.cs:266-271`) | Yes. "Another user's" is possible only with the test scheme (two principals) |
| 4 browser | attack page × cases | E2E (local) | title via `IResponse.TextAsync` (G-19), cookie header, page-read counts | Yes with TST-9's per-case table; today's "a 401 fails" contradicts the cross-site cases |
| 5 | trip on an owned window; options pinned | Integration + E2E route-fulfil | `429`, `Retry-After`, title | Yes with TST-12; flaky as written |
| 6 headers | producer table, 7 statuses | Integration | every header, no HSTS | Yes, and it will be red against step 3's design (TST-10) |
| 6 CSP | built bundle, own context | E2E vs published stack (by hand) | exact header + positive control + 0 violations | **Only with TST-11** |
| 6 axe / focus | both schemes; route-held / init-script producers | E2E (local) | axe 0; `activeElement`, exact status text | Yes with TST-15 |
| 7 canaries | capture at Trace, Production | Integration | canaries absent in 3 forms ×3 offsets; non-empty guard; no `429` | Yes with TST-13/14; vacuous under the limiter as written |
| 7 audit | six kinds by delta and shape; trigger refusal | Integration | row deltas; `RestrictViolation` (`ImportPipelineTests.cs:189-201`) | Yes; commands need TST-22 |
| 8 | temp repo: secret / clean / fallback | Architecture (Win + CI) | exit codes + scanner text | Yes with TST-16; today's hook fails clean here |
| 9 | existing suites | all | CI green, D13 evidence | Yes, if TST-1's sequence is followed |
| 10 | obligations table | — | — | n/a |

## Gates (INFERRED claims this seat rests on)

| Id | Claim | Finding | Raised by |
|---|---|---|---|
| GT-1 | With the test scheme as default, the challenge is the test handler's, not the cookie's | TST-2 | Run DoD 2's 401 case on both factories; compare `Location` |
| GT-2 | The compose file can be generated in a test without the Aspire CLI | TST-3 | One `dotnet run --project src/AppHost -- …publish…` attempt on CI's image |
| GT-3 | Identity accepts a `none`-format attestation from a software authenticator; `System.Formats.Cbor` is a package, not in-box | TST-7 | The first `SoftAuthenticator` enrolment against the `AuthFixture` |
| GT-4 | The Vite port changes per `aspire run`; an `api → web` endpoint reference is allowed beside `web → api` | TST-8 | G-5, run twice; log the configured origins |
| GT-5 | A page on another `localhost` port may run a ceremony with `rp.id = localhost` | TST-9 | One case in `CrossSiteBrowserTests` |
| GT-6 | Playwright's request headers show Chromium's `Cookie`; restored `Secure` storage-state cookies are sent to `http://localhost`; a non-`Secure` `__Host-` cookie is rejected | TST-9, 24, 25 | A three-line probe in the E2E fixture |
| GT-7 | Host filtering wraps the app pipeline; `Response.Clear()` clears headers | TST-10 | DoD 6's own 400/500 cases |
| GT-8 | The Aspire Npgsql EF setting key that disables retry | TST-10 | Read `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` settings at 13.6.1 |
| GT-9 | CSP violations reach `page.Console`; init scripts are not CSP-bound | TST-11, 15 | TST-11's positive control |
| GT-10 | `FixedWindowRateLimiter` takes no `TimeProvider` | TST-12 | Read `System.Threading.RateLimiting` at 10.0 |
| GT-11 | OTel AspNetCore 1.20 redacts `url.query` by default | TST-14 | The canary test's query-string case |
| GT-12 | Virtual-authenticator timing and the UV-off error | TST-15 | One CDP probe |
| GT-13 | The test builder can add environment to the `api` project resource | TST-15 | `builder.CreateResourceBuilder<ProjectResource>("api").WithEnvironment(...)` in `AppFixture` |
| GT-14 | Filters are invisible in metadata; JSON binding precedes filters | TST-17 | The body-table test with one row's body broken |
| GT-15 | A failed passkey sign-in increments `AccessFailedCount` | TST-21 | The lockout-on twin |

## Confirmed

- **TimeProvider reach (DoD 2d):** a DI `TimeProvider` reaches both the security-stamp validator and the cookie handler (sources in TST-18), and the repository already swaps it (`ClockTests.cs:25`) with a mutable `FixedClock` (`ImportSupport.cs:9-13`). No sleep is needed in Integration.
- **The existing immutability pattern** carries straight over to `audit_event` (`ImportPipelineTests.cs:189-201`; migration 4's triggers).
- **The CrossSite patterns fit DoD 4:** a real loopback attacker listener (`CrossSiteBrowserTests.cs:198-251`), request/response/failed listeners (`:133-135`), the per-layer integration split (`CrossSiteWriteTests.cs:281-311`) and `WrittenAsync` all extend to DoD 4 once TST-6/9 are applied.
- **axe in both schemes** is one `ColorScheme` context option beside the existing `RunAxe` call (`SectionFailureTests.cs:296-301`).
- **The hygiene scanner's refusal text is stable and assertable** (`scripts/hygiene.py:264`, `"CONSTITUTION IX: refusing this change."`; clean: `"hygiene: clean"`, `:261`), and it fails closed on git errors (`:291-295`).
- **Architecture runs green (52/52) and reads files only,** so the hook and graph tests fit there without new references.

## Axis 2 (scope and cost; no severity)

- **`SoftAuthenticator` (TST-7)** is the largest single test cost in the slice, roughly 200 lines plus a CBOR dependency. Every real-ceremony Integration test (`AuthFixture`, DoD 2d, DoD 7 canaries, origin, UV, lockout, replay) rests on it. The alternative is fewer real-ceremony Integration tests and more E2E, which D13 keeps out of CI.
- **Two factories, not one:** `SignedInClient` (test scheme, http) for the 36 existing cases and the DoD 4/6 producers, and `AuthFixture` (no test scheme, https base, `FixedClock`) for everything about the real cookie. Each starts its own Postgres per class (`PostgresFixture`). Expect Integration wall time to grow by about the number of new classes × container start.
- **Configuration hooks the tests need** (TST-8, 12, 15): allowed origins, limiter numbers and `ValidationInterval` bound to options. Each is a small production change made for testability. Each also gets a Production-config test pinning the real values.
- **Local-only evidence grows:** DoD 3, 4-browser, 6-CSP/axe/focus and the compose assertions are all outside CI. The built-bundle CSP test needs the published stack. A cheaper twin is the test builder with `ASPNETCORE_WEBROOT` pointed at `web/dist` and the API's own port, but it needs TST-8's origins and is still local.
- **Steps 3 and 4 in one PR** (TST-1) is the cheapest way to keep `main` usable. The alternative is a feature flag for the filter, which is more code to test and remove.
