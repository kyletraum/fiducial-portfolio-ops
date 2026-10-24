# Seat: Red team

Review of `specs/001-portfolio-platform/slice-03.md` (commission, Spikes H–K, build outline, DoD) and Spike H as run, against what exists today on branch `slice-03-spike-h` (`f3b5c2b`). Attack paths only, through the design as written. Fresh eyes: nothing under `specs/review/` and no other seat's output was read.

## Method

**Read (repository, at `f3b5c2b`):** `specs/001-portfolio-platform/slice-03.md`; `security.md` (threat model, API, Frontend, Audit); `slice-02.md` "Dates and time" and "Cross-site writes"; `spikes/spike-h-auth/RESULTS.md`, `run.sh`, `SpikeH.Web/Program.cs`, `SpikeH.Driver/Program.cs`; `src/Api/CrossSiteWrites.cs`, `Program.cs`, `Endpoints.cs`, `ImportEndpoints.cs`, `appsettings.json`; `src/AppHost/AppHost.cs`, `Properties/launchSettings.json`; `src/ServiceDefaults/Extensions.cs`; `src/Infrastructure/Migrations/20261018162121_IngestRun.cs` (the immutability trigger); `web/vite.config.ts`, `web/package.json` (vite `^8.3.2`); `tests/E2E/CrossSiteBrowserTests.cs`, `AppFixture.cs`; `.githooks/pre-commit`, `scripts/install-hooks.sh`, `scripts/hygiene.py`; `deploy/.env.example`; `.gitignore`; `README.md`; `.specify/memory/constitution.md`. `aspire-output/docker-compose.yaml` and the key names in `aspire-output/.env` were read (git-ignored, read-only; values not recorded).

**Read (vendor source, pinned):** `dotnet/aspnetcore` at tag `v10.0.13`, fetched raw into my scratch area:
- `src/DefaultBuilder/src/WebHost.cs`, `ForwardedHeadersOptionsSetup.cs`, `ForwardedHeadersStartupFilter.cs`, `HostFilteringStartupFilter.cs`; `src/Hosting/Hosting/src/GenericHost/GenericWebHostService.cs` (startup-filter order); `src/Middleware/HttpOverrides/src/ForwardedHeadersMiddleware.cs`; `src/Middleware/HostFiltering/src/HostFilteringMiddleware.cs`.
- `src/Identity/Core/src/SignInManager.cs`, `PasskeyHandler.cs`, `IdentityPasskeyOptions.cs`, `Passkeys/PasskeyAssertionState.cs`, `IdentityServiceCollectionExtensions.cs`, `SecurityStampValidator.cs`, `SecurityStampValidatorOptions.cs`; `src/Identity/Extensions.Core/src/PasswordOptions.cs`, `LockoutOptions.cs`.
- `src/Security/Authentication/Cookies/src/CookieAuthenticationOptions.cs`, `CookieAuthenticationDefaults.cs`, `CookieAuthenticationHandler.cs`.
- `src/Antiforgery/src/AntiforgeryOptions.cs`, `Internal/AntiforgeryOptionsSetup.cs`, `Internal/DefaultAntiforgeryTokenStore.cs`, `Internal/DefaultAntiforgeryTokenGenerator.cs`.

Below, `SOURCE@aspnetcore@v10.0.13:<path>` abbreviates `SOURCE@dotnet/aspnetcore@v10.0.13:src/...`.

**Read (documentation, 2026-10-21):**
- Data Protection key management defaults: https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings
- `DataProtectionScope` (DPAPI `CurrentUser`): https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope
- RFC 6265 §8.5 "Cookies do not provide isolation by port": https://www.rfc-editor.org/rfc/rfc6265#section-8.5
- Vite server options (`server.host`, `server.cors`, `server.allowedHosts`, `server.fs.*`): https://vite.dev/config/server-options
- node-http-proxy README, `changeOrigin` ("changes the origin of the host header to the target URL"): https://github.com/http-party/node-http-proxy
- Chrome content scripts (DOM access, isolated world): https://developer.chrome.com/docs/extensions/develop/concepts/content-scripts
- Moby CVE-2025-54388 (127.0.0.1-published ports reachable from the LAN after a firewalld reload; fixed 28.3.3): https://nvd.nist.gov/vuln/detail/CVE-2025-54388

**Ran:** nothing against the app (ground rule 4). No throwaway experiment was needed; every path below that is NOT refused rests on SOURCE or DOCS. The three outcomes I could reach only at INFERRED are in "Gates", not in "Findings".

**Not read:** `specs/review/`, the committee folder, the planning repository.

---

## Attack paths

Each path ends "refused by X at <where>" or "NOT refused". Severity is assigned in "Findings"; a path refused only by accident is also a finding.

### P1. A local process running as the user (under `aspire run`, and in the published stack)

**Preconditions.** Any process running under Kyle's Windows account: a malicious npm or NuGet dependency's postinstall, a trojaned CLI, a script. No admin, no keyboard. `slice-03.md` "What the slice is" names this attacker: "the API on loopback stops trusting whatever on the machine talks to it. Today any local process ... can read and write the financial record."

**Steps, `aspire run`.**
1. The API is a `dotnet` process with a user profile, so its Data Protection key ring is at `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, "encrypted at rest using DPAPI" [DOCS@2026-10-21 default-settings, item 2].
2. DPAPI `CurrentUser`: "Only threads running under the current user context can unprotect the data" [DOCS@2026-10-21 DataProtectionScope]. The attacker *is* that user. It unprotects the key ring.
3. With the key ring, the attacker mints an `Identity.Application` ticket for the one user: the cookie is a self-contained Data-Protection-encrypted ticket with no server-side session unless `SessionStore` is set [SOURCE@aspnetcore@v10.0.13:Security/Authentication/Cookies/src/CookieAuthenticationOptions.cs:122, `ITicketStore? SessionStore`]. It mints a matching antiforgery cookie/request-token pair the same way (antiforgery tokens are Data Protection payloads [SOURCE@aspnetcore@v10.0.13:Antiforgery/src/Internal/DefaultAntiforgeryTokenGenerator.cs]).
4. It sends `POST /api/v1/imports` with no `Sec-Fetch-Site` header (allowed: `CrossSiteWrites.cs` passes a missing header), the minted cookie, the minted token, `Content-Type: text/csv`. Every layer the slice adds is satisfied.

**Steps, published stack.** Shorter: `aspire-output/.env.<Environment>` holds `POSTGRES_PASSWORD` in plaintext on the same disk (README "Deploying locally"; `deploy/.env.example`), and the user who ran `aspire deploy` is in Docker's user group, so `docker exec <api> ...` and `docker exec <postgres> psql` are one command each. The compose file I read connects both `api` and `migrator` as `Username=postgres`, the superuser [SOURCE@aspire-output/docker-compose.yaml, git-ignored, read-only].

**Outcome. NOT refused.** Nothing in the design can refuse it: the session secret (the key ring) and the database credential live in the attacker's own security context. What the tier *does* refuse is every attacker without that context: a web page (cross-site, same-site), a sandboxed or AppContainer process, another Windows account, a process that only reaches the TCP port (WSL, a container on another network). The design's sentence claims the first class; the tier delivers the second. `security.md`'s out-of-scope line ("a compromised host with an active attacker at the keyboard") does not cover this attacker, who has no keyboard and needs none.

**Rests on.** DOCS ×2 (key location and DPAPI scope), SOURCE (stateless cookie ticket), SOURCE (compose connects as `postgres`). The one sub-step at INFERRED (that Aspire's `aspire run` Postgres is also reached with a password a same-user process can read) is confirmatory only and is Gate G7.

### P2. Published stack: any client spoofs its IP through `X-Forwarded-For`

**Preconditions.** The published compose stack. Any client that can reach `127.0.0.1:8080`: a local process, a browser via a form post (no preflight is needed for a form; the header can't be set by a page, so this half is curl-class only).

**Steps.**
1. The compose file sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"` on `api` (Aspire's publish default) [SOURCE@aspire-output/docker-compose.yaml].
2. With that value, `ForwardedHeadersOptionsSetup` sets `XForwardedFor | XForwardedProto` and **clears `KnownIPNetworks` and `KnownProxies`** ("Clear that restriction because forwarders are being enabled by explicit configuration") [SOURCE@aspnetcore@v10.0.13:DefaultBuilder/src/ForwardedHeadersOptionsSetup.cs]. `ForwardedHeadersStartupFilter` then adds `UseForwardedHeaders()` ahead of the app's pipeline [SOURCE@aspnetcore@v10.0.13:DefaultBuilder/src/ForwardedHeadersStartupFilter.cs].
3. In the middleware, `checkKnownIps = KnownIPNetworks.Count > 0 || KnownProxies.Count > 0`; with both empty the known-proxy check is skipped and the header is applied from any remote address [SOURCE@aspnetcore@v10.0.13:Middleware/HttpOverrides/src/ForwardedHeadersMiddleware.cs:216-224].
4. The attacker sends `POST /api/v1/auth/password` with `X-Forwarded-For: <fresh address each request>`. `HttpContext.Connection.RemoteIpAddress` becomes whatever it chose.

**Outcome.** Against a rate limiter partitioned by `RemoteIpAddress` (the usual sample): **NOT refused**, unlimited guesses. Against a global limiter: refused by the limiter, but the audit log's "environment"/actor address is still attacker-chosen for every event, and `X-Forwarded-Proto: https` flips `Request.IsHttps` for anything keyed on it. Step 3 of the build has not chosen the partition key, so as designed this is open. Note also that on loopback every honest client is the same address anyway (127.0.0.1 under `aspire run`; the bridge gateway inside the container), so a per-IP partition is the wrong key for this app regardless.

**Rests on.** SOURCE ×3.

### P3. Passkey assertion replay and challenge reuse

**Preconditions.** An attacker holding one captured login exchange: the `Identity.TwoFactorUserId` cookie value sent with `POST /passkey/login` and that request's body. Who captures it: a browser extension with `webRequest` on localhost, a debugging proxy, or a Playwright trace zip (the repository's own `.gitignore` comment: a trace "carries every API response body, the rendered DOM and the session cookie in one zip").

**Steps.**
1. `MakePasskeyRequestOptionsAsync` stores the challenge in `AuthenticationProperties.Items` and signs that in under `IdentityConstants.TwoFactorUserIdScheme`, a cookie [SOURCE@aspnetcore@v10.0.13:Identity/Core/src/SignInManager.cs:696-703]. `PasskeyAssertionState` is only `{Challenge, UserId}` [SOURCE@...:Identity/Core/src/Passkeys/PasskeyAssertionState.cs]. That cookie's `ExpireTimeSpan` is 5 minutes [SOURCE@...:Identity/Core/src/IdentityServiceCollectionExtensions.cs:80-87].
2. `RetrievePasskeyAuthenticationInfoAsync` reads the cookie then calls `SignOutAsync(TwoFactorUserIdScheme)` [SOURCE@...:SignInManager.cs:706-712]. Sign-out with no `SessionStore` only deletes the client's cookie; the ticket itself is never marked consumed [SOURCE@...:Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs:391-393, the only server-side removal is `SessionStore.RemoveAsync`].
3. The attacker resends the captured POST with the captured cookie inside 5 minutes. The challenge compares equal (`FixedTimeEquals(clientData.Challenge, originalChallenge)`) [SOURCE@...:Identity/Core/src/PasskeyHandler.cs:591]. The default origin check compares `clientData.origin` to the request's own `Origin` header, which the attacker sets to match [SOURCE@...:PasskeyHandler.cs:651-675].
4. Sign count: the check runs only "If authData.signCount is nonzero or credentialRecord.signCount is nonzero" and fails when `authData.signCount <= stored` [SOURCE@...:PasskeyHandler.cs:520-531]. A counting authenticator (the CDP virtual authenticator counts) refuses the replay here. A zero-counter authenticator passes.

**Outcome.** Refused by the sign-count check at `PasskeyHandler.PerformAssertionAsync` step 22 **for authenticators that count**; **NOT refused** for authenticators that report 0 (which authenticators do so is Gate G1). The value to the attacker is bounded: anyone who captured this exchange also captured the session cookie that followed, so replay buys re-entry for 5 minutes after capture, including after a logout. Also found on the way: the design's "Secure, SameSite=Strict" session cookie is one of four Identity cookies; `TwoFactorUserId` defaults to `SameSite=Lax`, `SecurePolicy=SameAsRequest` (not `Secure` over http) [SOURCE@...:CookieAuthenticationOptions.cs:14-23], and the antiforgery cookie to `SecurePolicy=None` [SOURCE@...:Antiforgery/src/AntiforgeryOptions.cs:20-31]. A test on the session cookie's attributes alone passes while the other three keep their defaults.

**Rests on.** SOURCE throughout; the severity rests on Gate G1.

### P4. Logout and revocation: a stolen cookie after logout

**Preconditions.** A copy of `.AspNetCore.Identity.Application` taken at any time (extension, trace zip, a copied browser profile). The user then logs out.

**Steps.** Present the copy. The application cookie is a stateless ticket with `ExpireTimeSpan` 14 days and `SlidingExpiration` [SOURCE@...:CookieAuthenticationDefaults.cs:30-32]. `SecurityStampValidator` re-validates only when `timeElapsed > ValidationInterval` (30 minutes) [SOURCE@...:Identity/Core/src/SecurityStampValidator.cs:146; SecurityStampValidatorOptions.cs:17], and only rejects if the stamp changed, which logout does not do unless the build calls `UpdateSecurityStampAsync`. The design names a `sessions` table in migration 5 but nothing says each request consults it, and DoD 2 asserts only that *no* session gives 401, never that a *revoked* one does.

**Outcome. NOT refused as written.** Refused only if step 2 builds what the table implies: a row per login, a session id in the ticket, `OnValidatePrincipal` (or an `ITicketStore`) rejecting a missing row on every request, logout deleting the row.

**Rests on.** SOURCE.

### P5. A same-site page (another `localhost` port) with the user's valid session in the browser

**Preconditions.** Any server on another localhost port the user visits: a dev tool, a docs server, the Aspire dashboard, Spike H's own `:5299`. Cookies do not isolate by port [DOCS@2026-10-21 RFC 6265 §8.5], so the user's session cookie accompanies every request from that page to the API.

**Steps and outcomes, per request shape.**
- (a) `fetch` with `Content-Type: application/json` or `text/csv`, or any custom header (the antiforgery header): preflight; the API grants no CORS; never sent. Refused by the browser's CORS preflight (slice 02 EXECUTED, `CrossSiteBrowserTests`).
- (b) `<form>` post: `415` by the content-type allowlist and `403` by `Sec-Fetch-Site: same-site` [`CrossSiteWrites.cs`; EXECUTED by slice 02's test].
- (c) `fetch` `mode: 'no-cors'`: content type collapses to `text/plain`, same as (b).
- (d) **The CSRF token is never consulted** in any of the three: the request is stopped before a token is read. A same-site page cannot, from a real browser, reach the antiforgery layer at all. That is the test attack in P16.
- (e) **Cookie tossing.** The same-site page runs `document.cookie = '.AspNetCore.Identity.Application=x; Path=/'` (or the antiforgery cookie's name, which is derivable). No port isolation, so the API receives it; decryption fails; the user is logged out, or every write fails antiforgery until reload. **NOT refused.** Availability only (asset 4). The standard answer is the `__Host-` cookie-name prefix; whether browsers honour it over `http://localhost` is Gate G2 (Spike I territory).
- (f) **Driving the user's passkey from the attacker page.** The RP ID defaults to `Request.Host.Host` = `localhost` [SOURCE@...:PasskeyHandler.cs:677-678], and `localhost` is a registrable suffix of `http://localhost:9999`, so the browser lets the attacker page call `navigator.credentials.get` for this passkey (with the user's touch). The default origin validation is self-consistency, not pinning: it compares `clientData.origin` with the attacker page's own `Origin` header, which match [SOURCE@...:PasskeyHandler.cs:666-675]. What refuses it: the attacker cannot read the challenge (the options response is opaque without CORS), so its assertion signs the wrong challenge. **Refused by the same-origin policy at the options endpoint** and by nothing else. Through the Vite proxy the legitimate page passes the same check because `changeOrigin` rewrites only the `Host` header [DOCS@2026-10-21 http-proxy README] and `Origin` is forwarded; so an explicit `ValidateOrigin` pinned to the app's origin costs nothing and turns one layer into two.

**Rests on.** EXECUTED (slice 02), DOCS, SOURCE.

### P6. The first-run enrolment window and the setup code (slice-03.md step 1)

**Preconditions.** Fresh database. The migrator has exited 0; the API is up; no user row. `POST /api/v1/auth/enrol` is one of the three anonymous endpoints (DoD 2).

**Steps.** A local process (P1's attacker, or any process that merely reaches the port) polls `/enrol` from the moment the API answers `/health`, faster than a human opens the page.

**Outcome, with no code.** NOT refused: the first caller owns the account for good. **With the setup code as sketched.** Refused by the code, to the extent the code is strong, single-use, rate-limited and expiring; none of which is written down. Three holes the sketch leaves:
1. **Channel.** "Shown in the AppHost's console" exists under `aspire run` (the terminal and the Aspire dashboard's console-log page). The published stack has no AppHost: the only console is the `api` container's stdout, which is `docker compose logs`, Docker's json-file log on the host disk, and the OpenTelemetry log pipeline (`ServiceDefaults` exports logs when `OTEL_EXPORTER_OTLP_ENDPOINT` is set). A secret in the log is exactly what DoD 7 forbids; either the redaction test exempts this line (a hole with a name) or the code needs another channel.
2. **Strength and counting.** A short, human-typeable code (6 digits is the instinct) is brute-forced through an anonymous endpoint unless enrolment is inside the authentication rate limit and the code dies on first success or after N failures.
3. **Atomicity.** If enrolment creates the user and then registers the first credential in a second request, a closed tab leaves a user with no credential: either `/enrol` is now closed (lockout, with a database edit as the only recovery) or still open (the race again). The user row and the first credential must be one transaction, and `/enrol` must answer `404`/`410` forever once a user exists.

**Rests on.** The design text (no external claim); the compose topology [SOURCE@aspire-output/docker-compose.yaml]; `ServiceDefaults/Extensions.cs` for the OTLP export.

### P7. The password fallback as a downgrade path, and its brute-force surface

**Preconditions.** Any client that reaches the port and has no passkey: P1's process, another Windows account, WSL, a container on the `aspire` network (Kestrel binds `0.0.0.0` inside; only the host publish is loopback, per the AppHost comment).

**Steps.**
1. Online guessing of `POST /auth/password`. Throughput is set by the rate limiter, not by argon2: 108 ms per hash is irrelevant at 5 attempts a minute. The design names no password policy; Identity's default is `RequiredLength = 6` with character-class rules [SOURCE@...:Identity/Extensions.Core/src/PasswordOptions.cs:14-43]. For every non-browser attacker the tier's strength is the password's, full stop; the passkey adds nothing against them.
2. **Lockout as a denial.** If step 2 uses Identity's lockout (`MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 5 min` [SOURCE@...:LockoutOptions.cs:28-34]), `PasskeySignInCoreAsync` runs `PreSignInCheck`, which returns `LockedOut` for a locked user [SOURCE@...:SignInManager.cs:659-671, 1317-1328]. Five wrong passwords from any local process lock the *passkey* login too, every five minutes, forever. The only user is denied the whole app.
3. **Memory.** Each verify holds 64 MiB (Spike H). Under the 512 MB container limit, eight concurrent password attempts plus an in-memory upload is an OOM kill, and `restart: unless-stopped` makes it a loop. Spike H says the rate limit "is also what keeps concurrent hashes inside that limit"; a token bucket does not bound concurrency, a `ConcurrencyLimiter` does.

**Outcome. NOT refused as designed** (1 and 2 are unspecified; 3 is acknowledged but the limiter kind is not chosen). Refused once step 2 fixes a minimum length (12+, or a generated passphrase shown once), throttles the *endpoint* globally rather than locking the *user*, keeps lockout off the passkey path, and bounds hashes in flight.

**Rests on.** SOURCE; Spike H EXECUTED for the 64 MiB figure.

### P8. The audit log and `ingest_run` as targets

**(a) Tamper.** `ingest_run`'s immutability is a `BEFORE UPDATE OR DELETE` trigger plus a `BEFORE TRUNCATE` trigger [SOURCE@src/Infrastructure/Migrations/20261018162121_IngestRun.cs:84-102]; the design says `audit_event` is "immutable as `ingest_run` is". The API connects as `postgres`, the superuser [SOURCE@aspire-output/docker-compose.yaml; `AppHost.cs` uses `AddPostgres` with no app role]. A superuser runs `ALTER TABLE audit_event DISABLE TRIGGER ALL; DELETE ...; ENABLE TRIGGER ALL` on the app's own connection. Anyone holding the connection string (P1) rewrites history; so would any future SQL injection, of which there is none today (EF Core, no raw user SQL). **NOT refused.** `security.md` "Database" already requires a non-superuser app role, and its own banner says why: "`GRANT` cannot be argued with". A role with `INSERT, SELECT` on `audit_event` and nothing else is immutability nobody can switch off from inside the app.

**(b) Flood.** Every failed anonymous login writes a row; the only bound is the rate limiter; the volume is host disk. Refused by the limiter to the extent it is global (P2). NOTE.

**(c) Secrets written in.** The "actor/subject" of a failed login is whatever was typed in the username box; users type their password there. The setup code, pasted into the wrong field, is the same path. DoD 7 injects secret-shaped values and asserts the capture clean; a test that injects into the *password* field passes while the *username* field writes the secret into `audit_event.detail` and the log. **NOT refused as written.**

**Rests on.** SOURCE (migration, compose); design text.

### P9. Session cookie theft on plain HTTP

Loopback traffic never leaves the host; capturing it needs a kernel driver (admin), which is the out-of-scope attacker. The LAN: the published `api` is `127.0.0.1:${API_PORT}:${API_PORT}` [SOURCE@aspire-output/docker-compose.yaml]; under `aspire run` Kestrel's profile is `http://localhost:5545` and Vite's `server.host` defaults to `'localhost'` [DOCS@2026-10-21 vite server-options]. **Refused by the bind addresses**, with one caveat at INFERRED: Docker has shipped bugs where 127.0.0.1-published ports became LAN-reachable (CVE-2025-54388, Linux + firewalld, fixed 28.3.3 [DOCS@2026-10-21 NVD]); Docker Desktop on Windows uses a different forwarding path. Gate G4.

### P10. DNS rebinding against `AllowedHosts`

Attacker page on `evil.example`, DNS re-pointed to 127.0.0.1. The browser sends `Host: evil.example`. The API: `HostFilteringMiddleware` checks the raw `Host` header against `localhost;127.0.0.1;[::1]` and answers `400` [SOURCE@aspnetcore@v10.0.13:Middleware/HostFiltering/src/HostFilteringMiddleware.cs:55-97]. In the published stack, forwarded headers cannot help the attacker: only `XForwardedFor | XForwardedProto` are enabled (no `XForwardedHost`) [SOURCE@...:ForwardedHeadersOptionsSetup.cs], and host filtering is registered before forwarded headers [SOURCE@...:WebHost.cs:272-273], with `Enumerable.Reverse(StartupFilters)` wrapping so that the first registered runs first [SOURCE@...:Hosting/Hosting/src/GenericHost/GenericWebHostService.cs:135-138]. The Vite dev server: `server.allowedHosts` admits `localhost`, `.localhost` and IP addresses only [DOCS@2026-10-21 vite]. **Refused by host filtering at the API and by `allowedHosts` at Vite.**

### P11. The deployed compose stack

- Port exposure: loopback-only publish, Postgres `expose` only. Refused by the compose file (slice 01 DoD 3).
- Inside the `aspire` bridge network, Kestrel binds `0.0.0.0`; any container on that network reaches the API. After slice 03 it needs a session. Refused by the session requirement (DoD 2), not by the network.
- The setup code's distribution: P6.
- **Key ring in the container.** The `api` service mounts no volume. Data Protection "When hosting in a Docker container, keys should be persisted in a folder that's a Docker volume"; otherwise a user-profile path inside the container (unencrypted at rest) or, with no profile, process-only keys [DOCS@2026-10-21 default-settings]. Either way, recreating the container (`docker compose up` after an image change, which every deploy is) discards the key ring: every session, antiforgery token and in-flight passkey ceremony dies, and the user is logged out on every deploy. Not an attack, but DoD 3 ("login in the published stack") will pass on a fresh stack and the defect appears on the second deploy. Which of the two behaviours the image shows is Gate G5.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: P2.

### P12. A malicious browser extension

**Preconditions.** An extension with host permission on `localhost` (or `<all_urls>`), which most "productivity" extensions request. Content scripts "read details of the web pages the browser visits, make changes to them" via the DOM, in an isolated world [DOCS@2026-10-21 Chrome content-scripts].

**Steps.** The content script runs inside the app's own page: same origin, cookies attached, `Sec-Fetch-Site: same-origin`, and it reads the antiforgery token wherever the page holds it (DOM, a meta tag, or by calling the page's own token endpoint same-origin). It then writes anything; it reads every response.

**Outcome. NOT refused.** The slice's motivation sentence ("any extension can read and write the financial record") stays true after the slice for every extension holding a localhost host permission; the tier refuses only extensions without one. Whether the page's CSP binds content scripts (Chrome exempts them; the exact rule is Gate G3) does not change the outcome, since the content script needs no inline script. This is the same honesty point as P1: say which attacker the tier refuses.

### P13. Logout

If logout is a `GET`, a cross-site `<img src="http://localhost:8080/api/v1/auth/logout">` logs the user out (annoyance). Must be a `POST` under the token. Logout must also delete the server-side row (P4), and clear `Identity.TwoFactorUserId` and the antiforgery cookie. NOTE.

### P14. The raw-body upload under a session

Cross-site: a form cannot send `text/csv`; `no-cors` strips it; a CORS fetch is preflighted. Refused three times (slice 02 EXECUTED). Same-origin with the token: the antiforgery store reads the header first and the form body only for form content types [SOURCE@...:Antiforgery/src/Internal/DefaultAntiforgeryTokenStore.cs:45-76], so a `text/csv` body with the header token validates without the body being touched; Spike J should confirm `RequireAntiforgery` on this endpoint does not try to read the body as a form. The memory interaction with argon2 is P7(3).

### P15. The Vite dev server as an attack surface

- Cross-site reads through the proxy: refused by `cors: false` (slice 02 EXECUTED).
- Repository files via `/@fs/`: `server.fs.strict` true, `fs.deny` includes `.env*`, `*.key`/`*.pem` and `**/.git/**` [DOCS@2026-10-21 vite]; the key ring is outside the workspace root. Refused by `server.fs`.
- The HMR WebSocket: Vite ≥ 6.0.9 checks `Origin` on the WebSocket; vite here is `^8.3.2`. INFERRED, Gate G6; harm is limited to HMR messages, not the API.
- The real dev-server risk is not a request shape: a malicious npm dependency runs in the Vite process as the user, which is P1. The slice cannot address it and should not claim to.

### P16. Attacking the tests: DoD assertions that pass while the attack still works

| DoD | Passes while... | Path |
|---|---|---|
| 2 "no endpoint but health, enrolment and login answers without a session" | a **revoked** or **logged-out** cookie still answers `200` for 14 days | P4 |
| 2, if asserted on endpoint metadata | the metadata is right but a middleware short-circuit (file server, `MapOpenApi` in dev) answers before authorization; assert by request, not by metadata | – |
| 4 "the CSRF token refusing the write" from another localhost port | the antiforgery validation is **absent**: the browser never delivers a same-site write past CORS/`Sec-Fetch-Site`, so the token layer is never exercised. The token must be shown in the integration test with the other layers' conditions met (valid session, `Sec-Fetch-Site: same-origin`, accepted content type) and a missing or foreign token answered `400`; the browser test records *which* layer answered (status + problem title) | P5(d) |
| 5 "repeated failed logins are throttled" | the partition is per-IP and the published stack honours `X-Forwarded-For` from anyone; or the throttle is Identity lockout, which also denies the passkey | P2, P7 |
| 6 "every response carries the security headers" | the early responses do not: `HostFiltering` `400`, `CrossSiteWrites` `403` (it runs before any header middleware the build adds after it in `Program.cs`), `StatusCodePages` `404`, the exception handler's `500`. The test must enumerate those statuses, not only `200`s | – |
| 6 "no `unsafe-inline`" | the dev policy with a nonce is the one deployed, or `connect-src`/`object-src`/`base-uri` are missing; assert the whole directive set | Spike K |
| 7 "no secret reaches a log" | the secret is typed in the **username** field, or is the **setup code**, or is in a **query string** (the `security.md` banner's own "Missing" item) | P6, P8(c) |
| 7 "`audit_event` refuses UPDATE/DELETE/TRUNCATE" | the test runs as the superuser and the trigger fires; the same superuser disables it in one statement | P8(a) |
| "sessions are Secure, SameSite=Strict" (step 2's test, implied) | only `Identity.Application` is configured; `TwoFactorUserId`, `External`, `TwoFactorRememberMe` and the antiforgery cookie keep their defaults | P3 |
| 8 "the hook is installed by the documented command and refuses a staged secret" | the test installs into a temp clone and passes in CI while *this* clone stays uninstalled (the slice admits it); `git commit --no-verify` bypasses it by design. The CI scan is the backstop and should be named as such in the DoD | – |

---

## Findings

Only paths NOT refused, or refused by accident, appear. Severity is for defects; "before <step>" names the build step in `slice-03.md`.

| ID | Severity | Finding | Rests on | What to do | Where |
|---|---|---|---|---|---|
| RED-1 | MUST FIX before step 1 | The slice claims to stop "any local process" and "any extension". A process in the user's own security context reads the Data Protection key ring (DPAPI `CurrentUser`) and mints sessions and CSRF tokens; in compose it reads `.env.<Env>` and `docker exec`s. An extension with a localhost host permission runs inside the page. Neither can be refused by this tier, and the assessment's "fit to hold a real balance" question would be framed on a false premise | P1, P12: DOCS (key location, DPAPI scope), SOURCE (stateless ticket), SOURCE (compose as `postgres`) | State the boundary in `slice-03.md` "What the slice is" and in `security.md` "Threat model": the tier refuses browser-delivered attacks (cross-site, same-site, and same-origin script under the CSP), non-browser clients without the user's cookie, and processes outside the user's security context; a process *as the user* and an extension *with localhost permission* are equivalent to the keyboard and are out of scope with the existing line. Then the assessment's "which attacks were shown refused" can be answered honestly | `slice-03.md` §"What the slice is"; `security.md` "Threat model", "Out of scope" |
| RED-2 | MUST FIX before step 3 | The published stack trusts `X-Forwarded-For`/`-Proto` from any client: Aspire publishes `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, which clears `KnownProxies`/`KnownIPNetworks`. A per-IP rate limiter is bypassed per request; the audit log's actor address is attacker-chosen; `IsHttps` is spoofable | P2: SOURCE ×3 | In `AppHost.cs`'s `api` publish callback remove the variable (or set `ForwardedHeaders_Enabled=false`); there is no proxy in front. Independently, do not partition the authentication limiter by remote address at all (on loopback every client shares one); make it global, sliding, with a concurrency limiter on the argon2 endpoint. Add a published-stack assertion that `X-Forwarded-For` does not change the audited address | `src/AppHost/AppHost.cs`; step 3; DoD 5 |
| RED-3 | MUST FIX before step 2 | Logout does not revoke: the Identity cookie is a stateless 14-day sliding ticket, the security-stamp check runs every 30 minutes and only on a stamp change, and nothing says the `sessions` table is consulted per request. DoD 2 cannot see this | P4: SOURCE | Define the session row (id, user, created, last seen, revoked) in migration 5; put the id in the ticket; validate on every request in `OnValidatePrincipal` (or an `ITicketStore`), rejecting missing/revoked rows; logout revokes the row. Add to DoD 2: "a cookie from a logged-out session answers `401` on every endpoint", asserted by request | step 1 (schema), step 2; DoD 2 |
| RED-4 | MUST FIX before step 3 (DoD wording) | DoD 4's browser test cannot exercise the CSRF token: a same-site page's write is stopped by CORS preflight or `Sec-Fetch-Site` before any token is read. The assertion passes with antiforgery validation entirely absent | P5(d), P16: EXECUTED (slice 02's own test shows the refusal points), SOURCE (token store) | Split DoD 4: (i) integration, every unsafe endpoint, valid session + `Sec-Fetch-Site: same-origin` + accepted content type + missing/foreign/other-user token → `400`, nothing written; (ii) the browser test keeps the same-site attacker and records which layer answered. Say plainly that the token's purpose is same-origin script and the "same-site" story is layers 1 and 2 | `slice-03.md` DoD 4; Spike J "The result decides" |
| RED-5 | MUST FIX before step 1 | The setup code is a sketch with three open holes: its channel in the published stack is the container log (which DoD 7 forbids), its strength and failure counting are unstated, and enrolment's atomicity is unstated (a user with no credential is a lockout or a reopened race) | P6: design text; SOURCE (compose topology) | Specify: a ≥128-bit code generated at start-up only while no user exists; constant-time compare; counted by the authentication limiter; dead after first success; user + first credential in one transaction; `/enrol` answers `410` once a user exists. Channel: under `aspire run` the console is fine; in compose, write it to a file on a named volume (mode 0600) and say so in the README, or accept the log channel explicitly and give the redaction test a named, single exemption | step 1; DoD 7; README deploy section |
| RED-6 | SHOULD FIX before step 1 | `audit_event`'s immutability will be a trigger on a table the app's superuser connection can disable; the DoD 7 refusal test passes while the same role disables it | P8(a): SOURCE (migration trigger; compose role) | Migration 5 creates an `app` role with `INSERT, SELECT` on `audit_event` (and `ingest_run`) and no `UPDATE/DELETE/TRUNCATE`; the API connects as it, the migrator as the owner. Test the refusal **as the app role**. This is `security.md` "Database" finally built where it matters most | step 1; `AppHost.cs` (role/connection string); DoD 7 |
| RED-7 | SHOULD FIX before step 2 | The password fallback is the whole tier for every non-browser attacker, and no policy is set (Identity default: 6 characters). If Identity lockout is the throttle, five wrong passwords lock the passkey too, repeatably: a persistent denial of the only user | P7: SOURCE | Minimum length 12+ (or a generated passphrase shown once); throttle the endpoint, not the user; lockout off, or not applied to `PasskeySignInAsync`; a `ConcurrencyLimiter` (1–2 in flight, queue 0) on argon2; state whether the password can be disabled once a passkey exists | step 2, step 3; DoD 5 |
| RED-8 | SHOULD FIX before step 2 | The passkey challenge lives in a 5-minute cookie that is never consumed server-side; a captured login exchange replays until the cookie expires, refused only by the sign count (skipped when both counts are 0). The default origin check is self-consistency against the request's `Origin`, not a pin. Three of four Identity cookies and the antiforgery cookie keep non-`Secure`/`Lax` defaults | P3, P5(f): SOURCE; severity on Gate G1 | Record the last-used challenge (or a nonce) per credential in the sessions/credentials table and refuse a repeat; set `IdentityPasskeyOptions.ValidateOrigin` to the app's exact origins; configure `Secure`/`Strict`/`HttpOnly` on all four Identity cookies and the antiforgery cookie, and test all five | step 2; DoD "sessions" test |
| RED-9 | SHOULD FIX before step 5 | A failed login's audit row and log line carry the typed username, which is where passwords get typed; DoD 7's injection into the password field passes while this path leaks | P8(c) | Audit "unknown user" unless the name matches the one existing user; the redaction test injects the secret shape into every field of every anonymous endpoint, and into a query string | step 5; DoD 7 |
| RED-10 | SHOULD FIX before step 7 | The `api` container has no key-ring volume: every deploy (container recreate) discards Data Protection keys, killing all sessions, tokens and in-flight ceremonies; keys that do persist sit unencrypted in the container FS | P11: DOCS | Mount a named volume for the key ring (`PersistKeysToFileSystem`) in the publish callback, next to the Postgres volume; assert in DoD 3 that a session survives `docker compose restart api` | `AppHost.cs`; step 7; DoD 3 |
| RED-11 | NOTE | Cookie tossing from any localhost port logs the user out or breaks every write until reload (availability, asset 4); a `GET` logout is a cross-site logout | P5(e), P13: DOCS (RFC 6265) | `POST` logout under the token; consider `__Host-` names (Gate G2) | step 2, step 4 |
| RED-12 | NOTE | DoD 6 can pass on `200`s while `400`/`403`/`404`/`500` and the `CrossSiteWrites` short-circuit carry no headers; DoD 8 passes in a temp clone while this one is uninstalled | P16 | Put the headers middleware first in `Program.cs`; assert on every status the API can produce; name the CI scan as the backstop in DoD 8 | step 3, step 6 |
| RED-13 | NOTE | Anonymous failed logins write unbounded audit rows; the only bound is the limiter | P8(b) | Fine once RED-2's global limiter exists; mention it in step 5 | step 5 |

Refused, no finding: DNS rebinding (P10), LAN exposure (P9, with Gate G4), the upload's cross-site shapes (P14), dev-server file reads (P15).

---

## Gates

Outcomes I could reach only at INFERRED, with what settles each. None of the findings above rests on these; they change severities or close paths already refused.

| Gate | Claim at INFERRED | Settles | How |
|---|---|---|---|
| G1 | Platform passkeys (Apple Keychain, Windows Hello, Google Password Manager) report `signCount = 0`, so Identity's step-22 check never runs for them | Whether RED-8's replay is refused in practice (SHOULD FIX stays) or never refused (raise) | Register with one real platform passkey, sign in twice, read `sign_count` from the credentials table; or in the E2E, CDP `WebAuthn.addCredential` with `signCount: 0` and attempt a replay |
| G2 | Chromium and Firefox accept `Secure` and `__Host-` cookies set over `http://localhost` | Spike I claim 3 and RED-11's mitigation | Spike I: set a `__Host-` cookie from the API over `http://localhost`, read it back in both browsers |
| G3 | An extension's content-script `fetch` carries `Sec-Fetch-Site: same-origin` and the page's CSP does not bind content scripts | P12's mechanics (the outcome is NOT refused either way; this only fixes the wording of RED-1) | Playwright with `--load-extension` on a throwaway unpacked extension, logging headers at a test endpoint |
| G4 | Docker Desktop on Windows honours `127.0.0.1:` publishes from the LAN side | P9 | From a second LAN host, `Test-NetConnection <laptop> -Port 8080` against the running published stack (not run here: ground rule 4) |
| G5 | In the `api` image the key ring is per-container (user-profile path, unencrypted) rather than process-only | RED-10's exact behaviour, not its existence | `docker compose logs api` on first start (the Data Protection warning text), then `docker compose restart api` vs `up --force-recreate` and check whether a session survives |
| G6 | Vite 8's HMR WebSocket enforces `Origin`/`allowedHosts` | P15 | Vite's changelog for 6.0.9 / CVE-2025-24010 and a same-site page opening `ws://localhost:<vite>/` |
| G7 | Under `aspire run` the Postgres password is in the AppHost's user-secrets (plaintext) and the port is published on localhost | Confirmatory for P1 (already NOT refused via the key ring) | `aspire run`, then `docker port` the postgres container and read `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` |

---

## Axis 2 — scope, buildability, teaching value

**Scope.** This is the right slice, and the one sentence to change is the first one in "What the slice is". The tier's real customers are the browser-delivered classes (cross-site, same-site, same-origin script under the CSP), the accidental non-browser client, and processes outside the user's security context. Written that way, the slice is honest and still worth building; written as "stops trusting whatever on the machine talks to it", it promises what no loopback tier can deliver and the assessment will be asked to confirm a false premise. The same honesty serves the "which attacks were refused in a real browser, and which only argued" question: a taxonomy of attacker classes, each with the layer that refused it and the test that showed it, is the useful artefact of this slice, more than any one control.

**Buildability.** Identity's in-box passkeys were the right call from Spike H, with three consequences the design has not yet absorbed: (1) Identity's session is a stateless cookie, and the `sessions` table in migration 5 is meaningless until something consults it per request (RED-3): that is a design choice between `ITicketStore` and an `OnValidatePrincipal` lookup, worth naming in step 2 rather than discovering mid-build. (2) There are four Identity cookies plus the antiforgery cookie, not one "session cookie". (3) Antiforgery tokens are bound to the authenticated principal's claim UID [SOURCE@aspnetcore@v10.0.13:Antiforgery/src/Internal/DefaultAntiforgeryTokenGenerator.cs:57-66, 139-169]: a token the page fetched before login fails after login with a claims-mismatch error, so the SPA must re-fetch the token on every sign-in and sign-out. Spike J should include that step or the first E2E login will fail in a way that looks like CSRF working. Spike J's third claim (same-site is not stopped by `SameSite=Strict`) is already EXECUTED by slice 02's browser test and can be restated rather than re-run; what J actually needs to decide is how to show the token layer at all (RED-4). Aspire's publish defaults (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`, no key-ring volume, superuser connection) are the kind of platform fact Constitution XII exists for; each was found by reading the generated compose file, which the slice should keep doing after every `aspire` bump. Step 1's role split (RED-6) touches `AddPostgres`, the migrator, and the connection strings in both run and publish modes; it is the largest unplanned piece and the one most worth doing, because it is the only control in this slice that holds against the attacker who has the app's own connection.

**Teaching value.** Three techniques this slice can teach that slices 01 and 02 did not, each with a commit that would demonstrate it: showing *which* layer refused a request rather than that something did (RED-4's split, the problem-title assertion); grant-based immutability over trigger-based, with the test run as the unprivileged role (RED-6); and revocation as a per-request fact rather than a cookie deletion (RED-3). The passkey replay (RED-8) is a fourth if Gate G1 lands the wrong way: a single-use challenge store is a small, teachable piece of state. The setup code (RED-5) teaches the least and costs the most words; a generated code written to a 0600 file on the volume is the version that needs no exemption in the redaction test, which is the version worth building.
