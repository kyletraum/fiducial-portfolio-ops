# Slice 03 review, round 2 — the rewritten spec (PR #74)

**Date:** 2026-10-24. **Under review:** PR #74, the rewrite of `slice-03.md` to the round-1
rulings, and the *(slice 03)* corrections in `security.md`.

**Seats (3):**

| Seat | Findings | Raised by its own runs |
|---|---|---|
| ASP.NET source reader (SRC) | 18 | three throwaway .NET probes; `docker compose config`; `docker run` against `api:latest` |
| Test automation (TST) | 25 | the Architecture suite; the Integration case list; a `CookieContainer` probe; the repo's own hook in a scratch repo |
| Scope realist (SCO) | 17 | none |

**Raw findings:** 60 (MUST FIX 26, SHOULD FIX 23, NOTE 11, no BLOCKING).
**Consolidated:** 31. The seat files under `slice-03-r2-seats/` are the evidence.

**Method:**
- **Fresh eyes:** no seat read another's file. The SRC and TST seats read
  `slice-03-committee.md` only to check that cited ids say what the spec attributes to them.
- **Source pins:** `dotnet/aspnetcore@v10.0.13`, `dotnet/runtime@v10.0.13` and
  `dotnet/aspire@v13.6.1`.
- **Consolidation:** where seats reached the same defect independently, the count is
  given. The highest severity a seat gave is kept. Each fix is attributed to its seat.
- **Dispositions:** proposed here under `process.md` rule 2. Kyle rules the four decisions
  at the end. Every other row is mechanical once those are made.

## The headline

The rewrite carries every round-1 disposition: the scope seat's fidelity table found no FIX
missing at the level of findings, and nothing CUT or DEFER returned as build work. The
problems sit in the new mechanics nobody had run:

- **Ordering.** The session requirement and the CSRF filter switch on before a sign-in page
  exists (A).
- **The pipeline.** The SPA, and the headers on refusals, fail under either order the spec
  leaves open; probes A, B and C show this (B).
- **A filter that refuses its own sign-in** (C).
- **A single-use rule that `reset-owner` erases** (E).
- **A test client that never returns `Secure` cookies** (F).
- **Three framework facts, all reached from the source and two also executed:**
  - passkey sign-in is a browser-session cookie;
  - a 10-minute ceremony outlives Identity's 5-minute challenge cookie and ends in a `500`;
  - migration 5 would be generated without the passkey table.

**Cost (SCO):** about **17.75 logged / 9.8 measured agent hours**, against the middle cut's
13.5 / 7.5. That is back to the "as written" figure. Dropping the password fallback (D7) was
cost-neutral (−1.5 h, +1.9 h of consequences). The growth is the 44 round-1 FIXes, led by
the sign-in dialog over the preserved page (≈ 1.0 h). Step 4 is now where the slice can
double.

## Consolidated findings and proposed dispositions

**Severity** is the highest any seat gave. **Disp.** is the proposed disposition:
- **FIX** — in PR #74, before step 1 starts.
- **FIX@n** — the build step must do it, and the spec says so.
- **CUT, DEFER, ACCEPT** — as in `process.md`.

| # | Finding | Seats | Sev. | Evidence | Disp. | What changes in the spec |
|---|---|---|---|---|---|---|
| A | **Build order.** Step 2's `FallbackPolicy` *is* the session switch. Step 3's filter breaks every token-less write before step 4. 36 Integration cases and 13 E2E cases go red, and `AppFixture` hangs on `/health` | SCO-1, SCO-2, TST-1 | MUST | `SOURCE@aspnetcore@v10.0.13 AuthorizationPolicy.cs:189-197`; case count from `Integration.exe -list full` | FIX | Step 2 registers authentication, the cookie, the named `AllowAnonymous` routes and `SignedInClient`, moving the 4 HTTP classes onto it. Step 3 adds the filter and the client's token handler, enforced when step 4 lands. Step 4 adds `FallbackPolicy` and the E2E helper. The pairing follows the D11 pattern |
| B | **Pipeline order unstated.** Static files served after authorization get `302`/`401`, so the SPA never loads. Auto-inserted auth runs ahead of the headers middleware. Headers set before `next()` are wiped on a `500` | SRC-1, SRC-8, TST-5, TST-9, TST-10, SCO-17 | MUST | `EXECUTED` probes A/B/C (SRC); `SOURCE WebApplicationBuilder.cs:434-453`, `AuthorizationMiddleware.cs:97-150`, `ExceptionHandlerMiddlewareImpl.cs:291-293` | FIX | Step 3 writes the order as a list: headers (in `Response.OnStarting`) → exception handler and status pages → cross-site refusal → `UseFileServer` → **explicit** `UseAuthentication`/`UseAuthorization` → rate limiter → endpoints with the filter. The list says a host-filter `400` carries no headers, and why. A temporary-web-root Integration test checks anonymous `GET /` and assets `200` with the exact CSP, and `/api` `401` |
| C | **The filter refuses the anonymous enrolment and sign-in `POST`s**, and the only token endpoint requires a session. Nobody could sign in | SRC-5, SCO-3 | MUST | spec-internal; Spike H's endpoints are `POST` | **Decision R1** | DoD 2's "every unsafe endpoint" reworded to match the ruling |
| D | **The page retries on an antiforgery `400`; the filter answers `403`** | SRC-6, SCO-5, TST-23 | MUST | spec-internal | FIX | Step 4 keys the retry on the filter's `ProblemDetails.title`; `403` stated once |
| E | **`reset-owner` erases the single-use guard.** The setup code's hash lives on the user row, which reset deletes, so the old code works again | SCO-4, TST-20 | MUST | spec-internal | FIX | The used-code hash is kept outside Identity's rows (an `app_setting` row). Integration test: enrol with A → reset → A refused and nothing created → B succeeds, each step audited |
| F | **Test HTTP clients never send `Secure` cookies** over `http://localhost`, so every `__Host-` cookie is dropped | SRC-9, TST-6 | MUST | `EXECUTED` `CookieContainer` probe (TST) and TestServer probe (SRC) | FIX | `AuthFixture` uses `https://localhost`, and the spec states that `IsHttps` is then true in tests only. The DoD 2 replay sends the cookie by hand. The E2E's bare `HttpClient` helpers become `page.EvaluateAsync(fetch…)` inside the signed-in page |
| G | **The test-only scheme:** never consulted unless it is the default; the DoD 2 test goes green for the wrong reason on that factory; the "nothing under `src/` / compose" guard is vacuous | SRC-10, TST-2, TST-3 | MUST | `EXECUTED` probe D/E (SRC); `tests/Architecture` has no references; compose is not generated in CI | FIX | The handler authenticates only requests carrying `X-Test-User` and answers `NoResult` otherwise. It is selected by a policy scheme, and its principal's `NameIdentifier` is the real user id. DoD 2 and DoD 6's `401` run on the **unmodified** Production factory. The guard becomes: (a) the scheme set and defaults asserted on the Production and Development factories; (b) no `src/*.csproj` references `tests/`. The compose half is local-only, via the app model in publish mode |
| H | **"The real ceremony" in Integration names no authenticator** | TST-7, SCO-8 | MUST | `INFERRED` (GT-3: Identity accepts a `none` attestation from a software authenticator) | **Decision R2** | — |
| I | **`ValidateOrigin`'s origins are not constants.** Aspire allocates the Vite port per run; the published port is `${API_PORT}` | SRC-12, TST-8 | MUST | `SOURCE@aspire@v13.6.1 JavaScriptHostingExtensions.cs` (`AddViteApp` has no fixed port); `AppHost.cs` | FIX | The AppHost passes the web endpoint's URL and the published origin to the API as configuration; `ValidateOrigin` compares against that. G-5's spike settles the run-mode half |
| J | **The limiter in tests:** one window shared by the whole suite; trip tests depend on timing; canary injection is refused by the limiter before any handler logs; `Retry-After` is the whole window | SCO-9, TST-12, TST-13, SRC-14 | MUST | `SOURCE FixedWindowRateLimiter.cs:209-216`; `RateLimiterOptionsExtensions.cs:56-70` | FIX | Name the API: `AddFixedWindowLimiter("auth")` plus `RequireRateLimiting("auth")`, not `GlobalLimiter`. Limits bound from configuration. The DoD 5 test owns its window (one hour, 3 permits). A second test pins the production numbers. The canary and lockout tests raise the limit and assert that no response was `429`. `Retry-After` means the window length |
| K | **The lockout rationale is false.** No passkey failure is ever counted (only the password and 2FA paths call `AccessFailedAsync`), so the test is vacuous | SRC-11, TST-21 | SHOULD | `SOURCE SignInManager.cs:659-673,497-499,920-922,984-986` | FIX | Keep `AllowedForNewUsers = false`, restated: so that the deferred password path cannot lock the passkey. Drop the test, or set lockout on the user and assert that the passkey still signs in. Correct `security.md`'s "five failures" sentence |
| L | **Passkey sign-in issues a browser-session cookie** (`isPersistent: false` hard-coded), so the "7 days" sentence is false | SRC-2 | MUST | `SOURCE SignInManager.cs:642-683`; `CookieAuthenticationHandler.cs:328-334` | FIX | `OnSigningIn` sets `IsPersistent = true` on the application cookie, for enrolment's sign-in as well. Test: `Set-Cookie` carries `Expires` about 7 days out. With the one-minute stamp check, the session slides to within a minute of the last request (SRC-16) |
| M | **A 10-minute ceremony outlives the 5-minute challenge cookie**, and the expiry is an unhandled `500` (also for any finish `POST` without the cookie) | SRC-3 | MUST | `SOURCE IdentityCookiesBuilderExtensions.cs:98-108`; `SignInManager.cs:555-563,706-724` | **Decision R3**; the `500` half is FIX | The finish endpoints catch the missing-ceremony case and answer `400` with their own title. The replay window is restated from the value chosen |
| N | **Migration 5 would be generated without the passkey table.** Design time and the Migrator have no `IdentityOptions`, so the model is `Version1` | SRC-4 | MUST | `SOURCE IdentityUserContext.cs:135-160`; `DesignTimeDbContextFactory.cs`, `Migrator/Program.cs` | FIX | `PortfolioDbContext` overrides `SchemaVersion => IdentitySchemaVersions.Version3`. A test checks that the migrated database has the passkey table |
| O | **The key-ring volume through `PublishAsDockerComposeService` gives an invalid compose file**, and a fresh volume is root-owned while the API runs as UID 1654 | SRC-7 | MUST | `EXECUTED` `docker compose config` ("undefined volume"); `EXECUTED` `docker run … -v`: `touch` denied; `SOURCE DockerComposePublishingContext.cs:100-127` | FIX@7 | Declare it through a `ContainerMountAnnotation` (or `ConfigureComposeFile`). Mount it under `/home/app`. The path comes from configuration under `aspire run` |
| P | **The Development allow-list misses `/health` and `/alive`**, which carry no names, and the AppHost probes `/health` | TST-4 | MUST | `ServiceDefaults/Extensions.cs:109-125`; `AppHost.cs` `.WithHttpHealthCheck` | FIX; `/openapi` is **Decision R4** | Two lists keyed by endpoint name, or by route pattern where unnamed: Production, and Development (adds `/health`, `/alive`). "Every entry exists" checked per environment |
| Q | **The CSP oracle is vacuous** if axe's `BypassCSP` shares the context, and it has no positive control. Round 1's "separate contexts" clause was dropped | TST-11, SCO-12 (part) | MUST | G-29; `INFERRED` GT-9 | FIX | CSP context with `BypassCSP = false`; axe in another. Exact header match. A `securitypolicyviolation` recorder. A deliberate inline-script violation must register first |
| R | **The hook refuses every commit on this machine today.** `python3` is the Microsoft Store stub, so a presence check never falls back. The test also needs isolated git config, a local identity and a secret built at runtime | TST-16 | MUST | `EXECUTED` Git Bash `python3 --version` exit 49; `EXECUTED` the hook in a scratch repo refused a clean commit | FIX@6 | The hook picks its interpreter by running it. The test runs three cases (secret refused; clean committed; a `python3` shim exiting 49 still commits clean) with `GIT_CONFIG_GLOBAL` empty and `GIT_CONFIG_NOSYSTEM=1` |
| S | **Steps out of order:** audit kinds asserted in step 5 for commands built in step 7; enrolment's ceremony in step 1 before step 2 configures passkey options; G-12 in step 0 needs the published stack | SCO-6, SCO-7, SCO-10 | SHOULD | spec-internal | FIX | Enrolment's endpoints move to step 2 (step 1 keeps the schema and code rules). The two command event kinds are asserted in step 7. G-12 moves to step 7 |
| T | **DEFER halves missing from the obligations table:** C3-44, C3-50's healthcheck, C3-39's nonce, D4's table, D8's redirect, Firefox E2E | SCO-11 | SHOULD | spec-internal | FIX | One row, "Deferred by the review", listing each with its trigger |
| U | **Three round-1 FIX clauses dropped:** no ceremony on page load (C3-34); cookies cleared on sign-out (C3-36); separate CSP/axe contexts (C3-20, now Q) | SCO-12 | SHOULD | spec-internal | FIX | One clause each |
| V | **No anonymous route for the pre-sign-in state** (set-up or sign-in; the environment label) | SCO-13 | SHOULD | spec-internal | FIX | One anonymous `GET /api/v1/auth/state` (enrolled or not, and the environment), on the allow-list |
| W | **The setup code's producer is unnamed**, and "bounded" has lost its bound now there is no TTL | SCO-14 | SHOULD | spec-internal; SRC-17: the value reaches `.env.<env>` at **deploy**, not publish | FIX | The AppHost generates it as a secret parameter default, gated on Aspire's generator giving ≥ 128 bits (new gate). "Bounded by single use, not by time." "At publish" becomes "at deploy". The readability alphabet requirement holds only if the generator allows it, otherwise it is dropped with a reason |
| X | **The DoD 6 status producers are unnamed** (`400`…`500`), and the Production app has no throwing endpoint | TST-10 | SHOULD | `HostFilteringTests`, `ImportApiTests` patterns | FIX | DoD 6 names the producer per status (TST-10's table); the `500` comes from a closed-port connection string |
| Y | **DoD 7's capture recipe:** structured state, scopes, exception chains and Activity tags; base64 at all three alignments; producers for the `500` and the malformed assertion; "seen failing" committed | TST-14 | SHOULD | `ServiceDefaults/Extensions.cs:46-52`; `appsettings.json` | FIX@5 | Step 5 names what is captured and matched |
| Z | **Deterministic E2E producers** for pending, failure, unsupported and "ticket expired" | TST-15 | SHOULD | `INFERRED` GT-12, GT-13 | FIX@4 | Route-hold for pending. Init-script rejections for `NotAllowedError`/`SecurityError`/unsupported. `ValidationInterval` from configuration (zero in the E2E fixture) for expiry |
| AA | **The DoD 2 filter assertion needs a body table**, and "`DELETE` included" is vacuous (no `DELETE` endpoint exists) | TST-17 | SHOULD | `grep MapDelete` empty | FIX | A table per unsafe endpoint (seeded from `CrossSiteWriteTests.TargetAsync`); a missing row fails. `DELETE` is shown by a filter-level test on a test-only `MapDelete` |
| AB | **The signed-out-cookie test needs a control:** `200` at +30 s, `401` at +61 s | TST-18 | SHOULD | `SOURCE` TimeProvider reaches both validators; `ClockTests` already swaps it | FIX | As TST-18. Also: `SignOutAsync` does not rotate the stamp, so sign-out calls `UpdateSecurityStampAsync`, which signs out every browser (SRC-16) |
| AC | **The OpenAPI credential-name test** sits in an advisory job, may scan no auth schema, and trips on the enrolment request's code field | TST-19 | SHOULD | `ci.yml:97-101` | FIX@5 | Moves to `tests/Architecture` over the committed document, response schemas only, with a non-vacuity count |
| AD | **The commands are untestable as `args` modes**, and audit counts accumulate per class | TST-22 | SHOULD | `PostgresFixture.cs:29`; top-level `Program.cs` | FIX@7 | The command bodies live in a service invoked from `factory.Services` in Integration, plus one process-level test. Counts are per-action deltas |
| AE | **Notes, each folded into the step named:** | | NOTE | | FIX | *(see the five items below the table)* |

The five notes in row AE:
- `HeaderName = "X-CSRF-TOKEN"` is set explicitly, since the default is
  `RequestVerificationToken` (SRC-13).
- `.DisableCookieRedirect()` still emits `Location`. Set `OnRedirectToLogin`/`AccessDenied`
  globally to a bare `401`/`403` (SRC-15).
- Citation fixes:
  - Aspire refs and lines;
  - `PasskeyHandler.cs:677-678` for the RP ID rule;
  - `CorsMiddleware` at about `:150-190`;
  - the compose project name is `PathSha256` only under `aspire deploy` (SRC-17).
- Enrolment's "one transaction" is an explicit `BeginTransactionAsync` with the
  no-user check inside it. The token binds to the user id, not the session, so
  "discarded on sign-out" is a client convention (SRC-18).
- The `Set-Cookie` test asserts no `__Host-` on the antiforgery cookie, and names its
  oracle (TST-24). The E2E helper enrols once per fixture and reuses `StorageState`
  (TST-25).
- The failed-sign-in audit's "unknown user" clause is password residue: there is no typed
  username at a passkey sign-in (SCO-15).
- DoD 4's "the token's job is same-origin script" names the wrong attacker. Use D12's
  sentence: a client sending no `Sec-Fetch-Site`, or layer 2 removed by a later change
  (SCO-16). DoD 4's "a `401` fails the test" applies to same-site cases only. Cross-site
  cases expect the `Strict` cookie *not* sent (TST-9).

## Decisions for the owner

- **R1. How the anonymous enrolment and sign-in `POST`s pass the CSRF filter** (C; SRC-5,
  SCO-3).
  - (a) **Exempt the allow-listed anonymous routes.** They are covered instead by layers 1
    and 2, `ValidateOrigin`, the limiter and the `Strict` challenge cookie. Smallest: DoD 2
    asserts both halves.
  - (b) **An anonymous token issued before sign-in** and refetched after it. One more route
    and client path; uniform coverage.
  - Round 1's C3-22 allowed either ("exempt or a pre-login token consistently").
- **R2. Who runs the real passkey ceremony in CI** (H; TST-7, SCO-8).
  - (a) **A C# software authenticator in `tests/Integration`**: ES256, `none` attestation,
    CBOR, ~150–250 lines, and the hostile variants (foreign origin, `crossOrigin`, UV off,
    replayed counter, malformed CBOR). About +0.5 h. With no E2E in CI (D13), it is the only
    way CI holds the passkey path, the origin check and the canaries for an assertion. SCO
    marks it KEEP. Gate GT-3: Identity accepts its `none` attestation.
  - (b) **The ceremony only in the local E2E** (CDP authenticator). Integration covers
    everything else through the test scheme. Origin, UV and replay rejection are then shown
    only by hand per D13.
- **R3. The ceremony timeout against the challenge cookie** (M; SRC-3).
  - (a) **Raise the `TwoFactorUserId` cookie to 10 minutes**, matching `AuthenticatorTimeout`
    (round 1's C3-34, WebAuthn §15.1). The replay window grows to 10 minutes.
  - (b) **Keep both at 5 minutes** (Identity's defaults; inside §15.1's range). The hint says
    "up to 5 minutes".
- **R4. `/openapi` in Development** (P; TST-4). Anonymous in Development only, or behind the
  session like any other endpoint.

### Ruled, 2026-10-24

Kyle's rulings:

| | Ruling |
|---|---|
| **R1** | (a) **The allow-listed anonymous routes are exempt from the filter.** DoD 2 asserts both halves |
| **R2** | (a) **A C# software authenticator** in `tests/Integration`, with the hostile variants |
| **R3** | (a) **Both at 10 minutes**: the challenge cookie is raised to match `AuthenticatorTimeout` |
| **R4** | **`/openapi` is behind the session** in Development |

With those, every row above is **FIX** (or FIX@n), and none is CUT, DEFER or ACCEPT. The
DEFER items T adds to the obligations table were already dispositioned in round 1.

## Gates added by this round

| Gate | Claim at INFERRED | Raise by |
|---|---|---|
| GT-3 | Identity accepts a `none`-format attestation from a C# software authenticator (only if R2 (a)) | the first `SoftAuthenticator` registration in step 2 |
| GT-6 | Playwright's request headers show the cookies Chromium sent, and restored `StorageState` `Secure` cookies are sent to `http://localhost` | one line in the step 4 helper |
| GT-9 | Chromium's CSP violations reach `page.Console`; CDP init scripts are not blocked by the page's CSP | Q's positive control |
| GT-10 | `FixedWindowRateLimiter` takes no `TimeProvider` | read the 10.0.13 source when the step 3 test is written |
| GT-12/13 | Virtual-authenticator timing and failure modes; the test builder can set the API's environment | the step 4 producers |
| GT-W | Aspire's generated parameter default can give ≥ 128 bits | step 1, printing a generated value's length and alphabet |

The seat files' other gates (SRC's, TST's GT-1, -2, -4, -5, -7, -8, -11, -14, -15) are
carried in those files at the step they name.

## Confirmed by the seats (do not re-check)

**SRC:**
- `__Host-` works on every Identity cookie, `TwoFactorUserId` included.
- `.DisableCookieRedirect()` exists in 10.0.13.
- `IdentityPasskeyOptions`' names, types and defaults are as the spec uses them.
- A `ValidateOrigin` override replaces the default check, so the app must reject
  `crossOrigin` itself.
- `SuppressXFrameOptionsHeader` exists.
- Fixed-window rejections carry `RetryAfter`.
- `DisableForwardedHeaders()` exists, and the publish default trusts forwarded headers from
  any client.
- "A copied cookie is refused within a minute of sign-out" holds, at the cost of at most one
  stamp check and one re-issued cookie per client per minute.

**TST:**
- The validation interval is testable without sleeping (`TimeProvider` reaches both
  validators; `ClockTests` already swaps it).
- `audit_event`'s immutability can reuse the `ingest_run` trigger test unchanged.
- The CrossSite test patterns extend to DoD 4.

**SCO:**
- Every C3-n is mapped, and nothing CUT or DEFER reappears as build work.
- `security.md` and the README agree with the spec.
- The gate's techniques limb still has at least three candidates:
  - passkeys end to end, with RP ID and origins pinned;
  - "the framework validates, the application refuses" for CSRF;
  - default-deny authorization tested by request over every endpoint.

  It is thinner than before: D6 cost grant-based immutability.

## Axis 2 (no severity)

**Cost and the step to watch.** The re-estimate is in the summary above. **Step 4** is the
step to watch. SCO's stop rule: past about 4 h logged on step 4, re-cut the replay-and-retry
logic, not the dialog.

**The test-scheme design (G).** The hardened form costs about 0.1 h more than the original.
TST's case for it is that the original guard could not fail.

**The hook (R)** is broken on this machine today, outside this slice. It is not installed
here, so nothing is blocked. Step 6 fixes it.
