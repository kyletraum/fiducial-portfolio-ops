# Slice 03 — the security tier, over invented balances

## The commission

**Recorded here, in this repository, 2026-10-21.** Slices are commissioned and gated in
this repository's own documents: the planning repository's decision `D-043` (2026-10-20)
decoupled the two projects, so it no longer commissions, gates or tracks the platform. This
section takes the place of the decision file that slice 01's and slice 02's gates required
there. The platform may still read the planning repository, but nothing flows back.

**Commissioned by Kyle's gate decision on slice 02, 2026-10-19**
([`slice-02-assessment.md`](slice-02-assessment.md) §5 and §6): seven techniques ticked
against a limb that needs three, and a third slice wanted, **for the security tier**. Kyle's
rulings the same day, each chosen from options put to Kyle:

| Ruling | Kyle's choice |
|---|---|
| Login | **Passkey (WebAuthn), with an argon2id password as the fallback**, as `security.md` specifies both. *Re-ruled 2026-10-24: passkey only; see below* |
| Scope beyond `security.md`'s "API" section | **All four additions**: security headers and a strict CSP; an audit log of authentication events; rate limiting on login; secrets kept out of logs |
| Real data | **Invented data only.** This slice does not end by admitting a real balance |
| Who builds, and the gate | **The agent builds, under the same gate** as slices 01 and 02 |

**Re-ruled 2026-10-24, after the committee review**
([`../review/slice-03-committee.md`](../review/slice-03-committee.md), "Rulings and
dispositions"). Kyle **deferred the password fallback** (`D7`) and adopted the scope
realist's middle cut as the baseline. The trigger to build the fallback is an environment
where a passkey is impractical, or the first lost authenticator. Sixteen further rulings
(`D1`–`D17`) are recorded there with the options they were chosen from. This document is
written to those rulings. Where a sentence below cites `C3-n`, `Dn` or `G-n`, that is the
review's finding, decision or gate.

**Bound by Kyle's ruling of 2026-10-18** ([`slice-02.md`](slice-02.md), "What the slice is
not"): no real balance enters the platform before a slice that builds `security.md`'s tier.
**This is that slice.** Completing it meets the ruling's prerequisite. It does not itself
admit real data; admitting the first real balance is its own later commission, recorded here.

**Method:** `D-029`: claim markers, and an `INFERRED` claim the design rests on is a gate.
The planning repository's `D-043` leaves `D-029` untouched, as general method. **Built by:**
the agent. **Estimate:** none, as in slice 02.
`LOG.md` records cost. **Ends in:** a written assessment and a full stop.

---

## What the slice is

One technique, carried through every layer once: **securing a local, single-user
application end to end**, so that the API on loopback refuses whatever reaches it from
outside the user's own session. Today any local process, any web page or any extension can
read and write the financial record. Slice 02's two layers stop cross-site **writes** only.

**The boundary, stated (`C3-4`).** The tier refuses:

- browser-delivered attacks: cross-site, same-site from another `localhost` port, and
  same-origin script under the published CSP;
- clients that do not hold the user's session cookie;
- processes outside the user's security context: another Windows account, WSL, a container
  on another network, a sandboxed process.

It does **not** refuse a process running *as the user*, or a browser extension holding a
`localhost` permission. Either can read the data-protection key ring or the page itself,
which makes it equivalent to the keyboard: out of scope, under `security.md`'s line. On this
machine, Docker-daemon access is root-equivalent for the stack, and `docker-users` has one
member. [`EXECUTED 2026-10-21`, committee OPS: `net localgroup docker-users`;
`DOCS@2026-10-21` ASP.NET Core data-protection default settings]

| Layer | What gets built |
|---|---|
| **Data** | Migration 5: the user and their passkey credentials (Identity's `Version3` store, no roles), and an append-only `audit_event`, immutable as `ingest_run` is, recording its actor as a value. No password hash (`D7`) and no `sessions` table (`D4`) |
| **API** | Enrolment by a setup code, passkey sign-in, sign-out. **Every existing endpoint requires a session.** CSRF tokens on every unsafe method, refused by the slice's own filter; a limiter on the anonymous authentication endpoints; security headers on every response and a strict CSP on the published page |
| **Web** | Sign-in and first-run set-up views, sign-out, and an expired session answered by a sign-in dialog over the page. Every write carries the CSRF token |
| **Logging** | A test that injects secret-shaped values and asserts every captured log line, span and error body clean. The redaction filter is written only if that test goes red |
| **Repository** | The pre-commit hook **installed and tested**. Today `.githooks/pre-commit` exists, but `core.hooksPath` does not survive a clone, and this clone does not have it set |
| **Tests** | Unit, integration, E2E with a virtual authenticator (Chromium only, by tooling: `C3-49`), and the security properties as tests, not a checklist |
| **Deploy** | The published stack runs the same tier on `http://localhost` with no TLS (`D1`), with forwarded headers off, the key ring on a named volume, and `revoke-sessions` and `reset-owner` as commands |

## What the slice is not

- **No real data.** Invented balances only, as in slices 01 and 02 (the commission).
- **No password fallback** (`D7`). Recovery when the passkey is lost is a second passkey
  (a hardware key) or `reset-owner`, which reopens enrolment.
- **No credential storage for data sources.** `security.md`'s envelope encryption protects
  *connector* credentials (FR-3). No connector holds a credential yet, so it is deferred to
  the first slice that has one.
- **One user, on loopback.** No multi-user, roles or sharing.
- **No TLS, and no cloud.** Spike I's TLS question is answered no from executed evidence
  (`D1`). A reverse proxy is the shape for the day the API leaves loopback, and HSTS is
  never sent until then. No managed identity; the second environment stays deferred.
- **No application database role** (`D6`). The API and the Migrator still connect as
  `postgres`, so `audit_event`'s immutability is a property of the application's code
  paths, not a database boundary (`G-7`). Trigger: real data, or anything other than this
  app reaching the database.
- **No dev-server CSP** (`D9`). The API's headers never reach a script tag under Vite, and
  by rule the dev page holds only invented data. Trigger: an XSS found in the app's own
  markup, or the dev page ever carrying real data.
- **No CORS to build.** The API registers no CORS and never has; that is the whole design
  (`C3-41`).
- **Slice 02's interim defence stays.** Content types that force a preflight, the
  `Sec-Fetch-Site` refusal, and `server.cors: false` on the dev server all remain beside the
  CSRF tokens this slice adds. None of them is replaced.

`security.md` is a frozen reference document whose banner says it "is corrected only when a
slice is commissioned against it". **This slice is commissioned against its "API",
"Frontend", "Audit" and "Credentials → Rules" sections**, and, because the boundary
paragraph above contradicts it otherwise, the "Out of scope" line of its "Threat model"
(`C3-4`). Where the build finds them wrong, they are corrected in the same change.

---

## Step 0 — the spikes (do these first)

Lettering continues from slice 02 (E–G). Spike H has run. The committee's own runs raised
most of Spike I and part of Spike J; what is left of each is below. Spike K is cut (`D9`).
The step that depends on a claim still at `INFERRED` does not start until its spike has
raised it to `EXECUTED` or replaced it.

### Spike H — what does it take to do WebAuthn and argon2id in .NET 10?

> **Claims under test.**
> 1. ASP.NET Core 10's Identity has built-in passkey support.
> 2. A maintained library (`Fido2NetLib` and others) is the alternative, if Identity's is
>    unfit.
> 3. .NET has no argon2id in the box, so a dependency is needed.
>
> [`INFERRED` for all three, recalled and not read.]

Slice 02 declined an OFX library on measured evidence (Spike E), and the same test applies
here: read the candidates, run each on registration and assertion with a virtual
authenticator, and decide on what the run shows, including taking no dependency.

**The result decides:** the library, or none; whether Identity's user model is taken whole
or only its passkey pieces; and the argon2id implementation and its parameters, which are
measured on this machine, not copied from a guide.

> **RAN: no WebAuthn library; Geralt for argon2id.** [`EXECUTED 2026-10-21`,
> `spikes/spike-h-auth/RESULTS.md`]
> - **Identity's in-box passkey API** (`SignInManager`/`UserManager`, schema
>   `Version3`) registered a passkey and signed in with it, end to end in Chromium with
>   a CDP virtual authenticator, on a throwaway PostgreSQL 18.3. An anonymous `/me` got
>   `401`, and `/me` signed in got `200`. **Fido2NetLib was not needed and not run.**
> - **On `http://127.0.0.1`, the browser refused both ceremonies:** "This is an invalid
>   domain". That observes Spike I's claim 2 early.
> - **Found:** the EF in-memory provider cannot read back a passkey's JSON-mapped data,
>   so tests need real Postgres. Cookie authentication redirects by default, and needs a
>   `401` instead.
> - **argon2id:** no type in .NET 10 (confirmed). Three candidates were compared.
>   **Geralt 4.4.0** was 108 ms at 64 MiB, t=3 here, and 139 ms on Linux under the
>   API's 512 MB limit. It accepts any valid parameters, and its `NeedsRehash` maps onto
>   Identity's `SuccessRehashNeeded`.
>   - Sodium.Core was within 5%, but refuses fewer than three passes.
>   - Konscious was about 3× slower, with no encoded string.
>
>   The two libsodium wrappers verify each other's hashes.
>
> **Step 1 inherits:** Identity passkeys; `401`, not redirects; tests on real Postgres.
> The Identity store lives in `PortfolioDbContext` (`IdentityUserContext<TUser>`, no
> roles), per the middle cut.
>
> **Not inherited (`D7`, 2026-10-24):** the Geralt hasher. The password fallback is
> deferred, so the platform takes no native dependency (`C3-46`). The measurement stays
> in `RESULTS.md` as the record for the day the fallback's trigger fires.

### Spike I — do passkeys and `Secure` cookies work on `http://localhost`?

> **Claims under test.**
> 1. WebAuthn requires a secure context, and `http://localhost` is one.
> 2. The relying-party id must be a domain, so `localhost` works and **`127.0.0.1` does
>    not**. Today `AllowedHosts` admits both.
> 3. A `Secure` cookie is accepted over `http://localhost` by Chromium and Firefox.
> 4. All of this holds under `aspire run` (the Vite proxy) and in the published stack (the
>    API itself, on port 8080).
>
> [`INFERRED` for all four.]

> **Mostly raised by the committee's runs, 2026-10-21.**
> - **Claims 1 and 2: `EXECUTED`** by Spike H and by the committee's browser probe in
>   Chromium 153 and Firefox 155: at `http://127.0.0.1` every `rp.id` throws
>   `SecurityError` before a request leaves the page. The server half is that the RP ID
>   is `ServerDomain ?? Request.Host.Host`, so step 2 pins it.
>   [`SOURCE@dotnet/aspnetcore@v10.0.13` `src/Identity/Core/src/PasskeyHandler.cs:51-55`;
>   committee BRW F1–F3]
> - **Claim 3: `EXECUTED`** in Chromium 153 and Firefox 155. `Secure`, `HttpOnly`,
>   `SameSite=Strict` and `__Host-` cookies are stored and sent back from
>   `http://localhost`, and a `Strict` cookie crosses `localhost` ports. **This was a
>   one-off run; the suite does not hold it** (`D14`). Safari is not a target browser
>   (`G-13`). [committee BRW F6, F7]
> - **Claim 4: the browser half is `EXECUTED`.** The Vite proxy forwards `Origin`
>   unchanged and rewrites only `Host`. [committee BRW F4]
>
> **Still owed, and run here:**
> - **`G-5`, the one load-bearing claim nobody has run:** register a passkey and sign in
>   through the Vite port, with `ValidateOrigin` set to the app's origins, logging
>   `Request.Headers.Origin` on the options endpoint.
> - **`G-12`:** the same ceremony against the published port 8080, not only Kestrel
>   direct.

**Answered:** the app is reached as `localhost` only. `127.0.0.1` and `[::1]` stay in
`AllowedHosts`, and the sign-in page warns the user before any ceremony (`D8`). The
published stack needs no TLS for this slice (`D1`).

### Spike J — CSRF tokens in a single-page app, beside slice 02's two layers

> **Claims under test.**
> 1. ASP.NET Core's antiforgery works for a single-page app calling minimal APIs, using a
>    token the page reads and sends in a header.
> 2. Its validation **and refusal** can cover every unsafe method, `DELETE` included,
>    without breaking the raw-body upload.
> 3. **`SameSite=Strict` does not stop a same-site attacker.** A page on another
>    `localhost` port is the same *site*, so the browser sends the session cookie to the
>    API. That is the case tokens exist for; the `Sec-Fetch-Site` layer already refuses it
>    once.
>
> [`INFERRED` for claim 1. Claim 2 as first written, "validation can cover", is **false
> as the framework ships**; see below. Claim 3 is `EXECUTED`.]

> **What the review established.**
> - **Claim 2 (`C3-1`, blocking).** `AntiforgeryMiddleware` considers only `POST`, `PUT`
>   and `PATCH`, and only on endpoints carrying antiforgery metadata, which minimal APIs
>   infer only for form-bound parameters. On failure it records the result and calls
>   `next`. Every platform write binds JSON or reads a raw body, so the framework's own
>   middleware would validate and then let the write run, and would never check a
>   `DELETE`. **The slice owns the refusal:** an endpoint filter on the `/api` group that
>   calls `IAntiforgery.ValidateRequestAsync` for every method but `GET`, `HEAD`,
>   `OPTIONS` and `TRACE`, and answers a Problem response on failure.
>   [`SOURCE@dotnet/aspnetcore@v10.0.13` `src/Antiforgery/src/AntiforgeryMiddleware.cs:25-51`,
>   `src/Http/Http.Extensions/src/RequestDelegateFactory.cs:397-402`; `DOCS@2026-10-21`
>   anti-request-forgery: "Endpoint code always runs, even if token validation fails"]
> - **Claim 3: `EXECUTED`** by slice 02's `CrossSiteBrowserTests` and the committee's
>   probe: every cookie, `Strict` included, arrives from another `localhost` port.
>   [committee BRW F7]
> - **The token binds to the signed-in user**, so a token fetched before sign-in fails
>   after it. [`SOURCE@dotnet/aspnetcore@v10.0.13`
>   `src/Antiforgery/src/Internal/DefaultAntiforgeryTokenGenerator.cs:57-66`; `C3-22`]
> - **The header is read first**, and the form only for form content types, so the
>   `text/csv` upload needs no special case. [`SOURCE@dotnet/aspnetcore@v10.0.13`
>   `src/Antiforgery/src/Internal/DefaultAntiforgeryTokenStore.cs:44-74`]

**The spike runs:** the filter against a JSON endpoint, the raw-body upload and a `DELETE`,
each with the header absent, and sees each refused. It also checks that a token issued
after sign-in validates with the filter placed after `UseAuthentication` (`G-20`), and
reads a `403` body from Playwright on a response the page itself cannot read (`G-19`).

**The result decides:** where the token comes from (an endpoint that requires a session, or
a cookie the page reads), and that it travels in `X-CSRF-TOKEN` on every unsafe request,
the upload included.

### Spike K — cut (`D9`)

Its claims were about a dev-server CSP, and the dev server gets none.

- **Claims 2 and 3: `EXECUTED`** by the committee's probe in Chromium 153 and Firefox 155.
  The built page runs with zero violations under `default-src 'self'; script-src 'self';
  style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none';
  base-uri 'none'; form-action 'self'; object-src 'none'`, with the charts rendered.
  [committee BRW F13, F15]
- **Claim 1** was true and incomplete: Vite also injects each imported `.css` file as a
  `<style>` element. It gates nothing once the dev server has no CSP. [committee BRW F14]

The published policy is built in step 3 and shown in step 7 (`D16`).

---

## The build (outline; refined after the spikes, as slice 02's was)

1. **Identity, data and enrolment.**
   - **Migration 5:** Identity's `Version3` store inside `PortfolioDbContext`
     (`IdentityUserContext<TUser>`, no roles), as one EF migration on Npgsql. Spike H did
     not run `Version3` through the Migrator, so this is where step 1 can balloon.
   - **`audit_event`:** append-only, by migration 4's trigger pattern, refusing `UPDATE`,
     `DELETE` and `TRUNCATE` as `ingest_run` does. Its `actor` is a value, not a foreign
     key, so `reset-owner` can clear the user (`C3-32`). The trigger binds the
     application's code paths; the connecting role can still remove it (`D6`, `G-7`).
   - **Enrolment (`C3-3`, `D2`).** The setup code is an Aspire secret parameter:
     user-secrets under `aspire run`, the git-ignored `.env.*` at publish.
     - It is at least 128 bits, from an alphabet without `0`/`O` or `1`/`l`/`I`, and
       printed grouped for reading. It travels only in a `POST` body, never in a query
       string.
     - It is accepted only while no user exists, compared with
       `CryptographicOperations.FixedTimeEquals`, and counted by step 3's limiter.
     - The order is: setup code → creation options (no user yet) → attestation → the
       user and the passkey created in one transaction → signed in. Nothing exists before
       the ceremony succeeds.
     - Once a user exists, the enrolment endpoints answer `409`.
     - Its hash is stored with the user, and `reset-owner` reopens enrolment only for a
       value that differs from it, so a code left in `.env.*` cannot be used twice.
     - Tests can set it (`UseSetting`; `WithEnvironment` in the test builder), and a test
       asserts the published compose file carries none.
     - The enrolment is an `audit_event` with time and connection address, shown on the
       first sign-in.
   - **The window is bounded and attested, not closed.** Any member of `docker-users` can
     read the parameter (`C3-4`). Three events reopen enrolment without anyone asking: a
     fresh volume, `down -v`, and moving the repository directory, because the compose
     project name hashes the AppHost path. The API then presents "no owner enrolled", not
     a sign-in page. [`SOURCE@dotnet/aspire` `DockerComposeEnvironmentResource.cs`;
     `EXECUTED` committee OPS, `docker volume ls`]
   - **The set-up views (`C3-53`):** setup code → username → passkey → done. Each step is
     a view whose `<h1>` names the step count and takes focus on arrival. A failed
     ceremony leaves the user on the same step with values intact.
2. **Authentication API.**
   - **Passkey options (`C3-10`, `C3-23`, `C3-34`).** `ServerDomain = "localhost"`, with a
     test that the options carry `rp.id == "localhost"` whatever the `Host` header says.
     `ValidateOrigin` accepts exactly the app's origins, the Vite port under `aspire run`
     and `http://localhost:8080` published, and rejects `crossOrigin`. User verification
     stays `required`, written down so a change to `preferred` is visible.
     `AuthenticatorTimeout` is 10 minutes. [`DOCS@2026-10-21` WebAuthn Level 3 §15.1]
   - **Cookies (`C3-6`, `C3-21`).** Every Identity cookie is `SameSite=Strict`,
     `SecurePolicy=Always` and `HttpOnly`, with a `__Host-` name. That includes
     `Identity.TwoFactorUserId`, which carries the passkey challenge. The antiforgery
     cookie is the one exception: `Strict` and `HttpOnly`, but not `Secure`, because
     antiforgery throws on a non-HTTPS request when it is. One test covers every
     `Set-Cookie`, asserted on the server-seen `Cookie` header, not on Playwright's
     `BrowserContext.cookies(url)` (`G-14`). [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Http/Http.Abstractions/src/CookieBuilder.cs:117`,
     `src/Antiforgery/src/Internal/DefaultAntiforgery.cs:256-262`; `EXECUTED` committee
     BRW F6]
   - **Sessions (`D4`, `D10`, `C3-5`, `C3-16`).** Identity's stateless cookie,
     `ExpireTimeSpan` 7 days with sliding expiration: WCAG 2.2.1's 20-hour exception. The
     sign-in page says "You stay signed in for 7 days after you last use the app, or
     until you sign out." There is no absolute cap; a stolen cookie in use is answered by
     `revoke-sessions`. `ValidationInterval` is one minute, and sign-out rotates the
     security stamp, so a copied cookie is refused within a minute of sign-out. That lag
     is what `D4` accepted. [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Identity/Core/src/SecurityStampValidator.cs:138-162`]
   - **No lockout (`C3-8`).** `Lockout.AllowedForNewUsers = false`. Passkey sign-in runs
     Identity's `PreSignInCheck`, so a lockout would let any local `curl` deny the only
     user. Test: after repeated failures, the passkey still signs in.
     [`SOURCE@dotnet/aspnetcore@v10.0.13` `src/Identity/Core/src/SignInManager.cs:659-673`]
   - **Authorization (`C3-12`).** `FallbackPolicy = RequireAuthenticatedUser()`, with
     `AllowAnonymous` on exactly the named routes, and `.DisableCookieRedirect()` on the
     `/api` group. [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Security/Authorization/Core/src/AuthorizationPolicy.cs:189-197`]
   - **Sign-out** is a `POST` under the token (`C3-36`).
   - **The replay model, written down (`C3-39`).** The challenge lives for 5 minutes in
     the data-protected `TwoFactorUserId` cookie, with no server-side nonce. A replayed
     exchange is caught only by the sign counter, which runs only when a counter is
     non-zero. `G-8` decides whether a per-credential nonce is needed.
   - **The key ring (`D3`, `C3-11`).** `PersistKeysToFileSystem` to a path on a named
     volume, plus `SetApplicationName("portfolio")`. The keys are plaintext on the host,
     and rely on disk encryption, as the envelope-encryption KEK does.
   - **Test sessions (`D11`, `C3-13`).** Integration's shared `SignedInClient`
     authenticates through a test scheme registered only by `ConfigureTestServices`. An
     architecture test asserts that nothing under `src/` registers it and that the
     published compose file carries no switch for it. The auth classes run the real
     ceremony, on one `AuthFixture : PostgresFixture`. The session requirement on the
     existing endpoints is switched on in step 4, not here.
3. **CSRF, CORS, the limiter, headers.**
   - **The antiforgery filter (`C3-1`)** on the `/api` group, per Spike J. It sits after
     `UseAuthentication` and `UseAuthorization` (`G-20`) and refuses with `403` and a
     `ProblemDetails.title` of its own. The token endpoint requires a session (`C3-22`).
   - **CORS: none registered** (`C3-41`). A test sends `Origin: http://attacker.localhost`
     and the Vite origin across DoD 2's enumeration, plus `OPTIONS` to each `POST` path,
     and asserts that no response carries `Access-Control-Allow-Origin`.
   - **The limiter (`C3-7`, `C3-19`, `C3-43`).** One fixed-window limiter covering the
     anonymous authentication endpoints (enrolment, passkey options, passkey sign-in). It
     is global, partitioned by policy name and never by the remote address.
     `RejectionStatusCode` is `429`. `OnRejected` writes a `ProblemDetails` titled "Too
     many sign-in attempts. Try again in about a minute." with `Retry-After`, and logs
     neither a partition key nor a body. The numbers are chosen at build and recorded
     here. [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Middleware/RateLimiting/src/RateLimiterOptions.cs:41` (the default rejection is
     `503`)]
   - **Headers (`C3-38`, `C3-40`)** come from one middleware placed first in
     `Program.cs`, so every status carries them:
     - `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`;
     - `X-Frame-Options: DENY`, with `AntiforgeryOptions.SuppressXFrameOptionsHeader`
       set so antiforgery adds no second value;
     - `Permissions-Policy` denying everything except
       `publickey-credentials-get=(self)` and `publickey-credentials-create=(self)`;
     - **no HSTS**, and its absence asserted (`D1`).

     The page served from `wwwroot` gets the published CSP, Spike K's string above.
     `frame-ancestors` works only as a header, which is why the API sets it.
     [`EXECUTED` committee BRW F16]
4. **Web.** **The error boundaries stay per section** (`edb9504`).
   - **Views (`C3-15`, `C3-37`, `C3-50`).** Sign-in, set-up, and the portfolio. Each sets
     `document.title`, and its `<h1>` takes focus when the view changes after a user
     action. The fields carry `autocomplete="username"`, and `one-time-code` on the setup
     code. The sign-in view shows the environment label before authentication, as
     `environments.md` requires.
   - **The passkey button (`C3-18`, `C3-52`, `D8`).**
     - Before the ceremony: a hint saying what the browser will ask for, and that it allows
       up to 10 minutes.
     - During: the button stays enabled but `aria-disabled`, re-entry is guarded by an
       `AbortController`, and a status says "Waiting for your passkey…".
     - After: each outcome has its own text and returns focus.
       - `NotAllowedError` covers both a cancel and a timeout, and the page does not
         pretend to tell them apart.
       - `SecurityError` says to open the app as `http://localhost`. When
         `location.hostname` is not `localhost`, the page says so before the ceremony.
       - Where passkeys are unsupported, the button is hidden and the page says this
         browser cannot sign in here.
   - **Expiry (`C3-17`, `C3-47`).**
     - One `401` handler sets a single signed-out state. Sections render no alert of their
       own for a `401`, and keep their data on screen.
     - `App` stays mounted, and sign-in opens as a `<dialog>` over it (`G-30`).
     - After signing in again, the page refetches the token first, then invalidates
       queries, returns focus, and announces "Signed in again."
     - A refused write is not replayed. Its form keeps the input and says why.
     - An antiforgery `400` means: refetch the token, retry once, then sign in.
     - The footer's health line does not say "unreachable" on expiry.
     - One E2E case tells "no cookie sent" from "ticket expired".
   - **Sign-out (`D15`).** The header shows "Signed in as <name>" and a `Sign out` button
     that `POST`s, then calls `location.assign('/')`. The sign-in view then says "You are
     signed out", from a one-shot flag.
   - **The token** is held in module memory, fetched after every sign-in, and discarded on
     sign-out and on a `401` (`C3-22`).
   - **Styling (`C3-51`).** The dialog uses the AA tokens. "Waiting…" is text, not only
     a spinner, since reduced motion removes the spinner. `aria-disabled` buttons are
     styled by colour, not opacity.
   - **The session requirement on the existing endpoints is switched on here**, with the
     E2E's signed-in helper (`D11`).
5. **Logging and audit.**
   - **Events:** enrolment, sign-in succeeded, sign-in failed, sign-out, sessions revoked,
     owner reset.
     - Each records actor, time, subject, environment and connection address. The address
       is the real one only after step 7's forwarded-headers fix (`C3-9`).
     - A failed sign-in records "unknown user" unless the name matches the one user.
     - The step 3 limiter bounds the rows (`C3-43`).
   - **DoD 7's test (`C3-26`).**
     - Capture: every category at `Trace`, with a known benign line asserted present so a
       silent provider cannot pass, and the OpenTelemetry in-memory exporter for traces
       (`G-22`).
     - Canaries: the setup code, a passkey assertion, the session cookie value and the
       antiforgery token. They are driven through enrolment, sign-in, a failed sign-in, a
       malformed assertion and a `500`, and injected into every field of every anonymous
       endpoint and into a query string. Each must be absent raw, URL-encoded and in
       base64.
     - Configuration: Production, with `EnableSensitiveDataLogging` asserted off (`G-24`)
       and the `Microsoft.AspNetCore` level pinned in the test.
     - Seen failing once, by logging a canary at `Debug`.
     - **The redaction filter is written only if this test goes red** (the middle cut).
   - **The contract test (`C3-27`)** runs over the committed OpenAPI document and asserts
     that no schema property has a credential-like name.
6. **The pre-commit hook (`C3-29`).** Installed by `scripts/install-hooks.sh`. The test in
   `tests/Architecture`:
   - builds a temporary repository, runs the installer, and asserts `core.hooksPath`;
   - stages a secret-shaped value, and asserts both a non-zero exit and the scanner's
     refusal text;
   - commits a clean file, and asserts exit 0. That clean twin is what tells "refused the
     secret" from "cannot run the scanner".

   The hook falls back to `python` where `python3` is absent, as on this machine. The CI
   scan is named in DoD 8 as the backstop, since `--no-verify` bypasses the hook by
   design.
7. **Tests and deploy.**
   - **The app model.** `.DisableForwardedHeaders()` on the `api` resource (`C3-9`), and the
     key-ring volume through `PublishAsDockerComposeService` (`D3`).
     [`SOURCE@dotnet/aspire` `src/Aspire.Hosting/ProjectResourceBuilderExtensions.cs`]
   - **Two commands.** `revoke-sessions` (rotate the stamp) and `reset-owner` (clear the
     credentials and reopen enrolment) run as `docker compose run --rm api <mode>`. Each
     is audited and tested.
   - **The runbook says:**
     - a redeploy now keeps sessions;
     - `down -v` destroys the key ring with the data, and reopens enrolment;
     - logs live in `docker logs` (100 MB, lost on recreate) (`G-27`).
   - **The published stack:**
     - passkey sign-in by a Playwright script against `docker compose`;
     - the headers, including HSTS's absence, by `curl`;
     - a forged `X-Forwarded-For` is not what `audit_event` records;
     - a session survives `docker compose restart api`;
     - the built-bundle E2E (`D16`, `G-29`);
     - every slice-01 and slice-02 property re-checked;
     - `G-11` (`127.0.0.1` publishes, probed from the LAN) and `G-26` (the Postgres
       password stable across deploys) are raised here.

## Definition of done

1. Spike H has run. Spike I's remainder (`G-5`, `G-12`) and Spike J run. Their claims are
   restated at `EXECUTED`, or replaced by what was found. Spike K is cut (`D9`).
2. **No endpoint answers without a session except an explicit allow-list of route names**
   (`C3-12`):
   - The list names the anonymous health route and the enrolment and passkey sign-in
     routes. Static files are anonymous by design.
   - It is asserted by request, over `EndpointDataSource`, in both Production and
     Development configuration. Every name on the list must exist.
   - Every unsafe-method endpoint carries the antiforgery filter, `DELETE` included.
   - A cookie from a signed-out session answers `401` on every endpoint, once the
     validation interval has passed.
3. Enrolment and sign-in work with a passkey, both under `aspire run` through the Vite proxy
   and in the published stack. The E2E uses a CDP virtual authenticator, in Chromium only,
   by tooling. The published half is run by hand and recorded with date and commit. A
   session survives `docker compose restart api`.
4. **A same-site attacker with a valid session in the browser cannot write** (`C3-2`, `D12`).
   - **Integration:** with a valid session, `Sec-Fetch-Site: same-origin` and an accepted
     content type, a write whose token is missing, foreign or another user's is refused,
     and nothing is written. Layer 2 alone, with a valid token but `same-site`, answers
     `403`.
   - **Browser:** a signed-in context, attacked from another `localhost` port.
     - Each refusal is identified by its `ProblemDetails.title`, not its status.
     - The cookie is proven sent; a `401` fails the test.
     - "Nothing written" is read from the signed-in page.
     - The cases include a `Blob`-typed `no-cors` `POST`, a `text/plain` cors `POST`
       (`C3-31`) and a passkey sign-in attempt (`C3-23`).
   - **Stated:** the token's job is same-origin script. The same-site story is layers 1
     and 2, which a browser reaches first.
5. The anonymous authentication endpoints are throttled. The trip is asserted (`429`,
   `Retry-After`, the title), never the reset. The page shows the title and a static "try
   again after" time.
6. **Headers and CSP (`C3-20`, `C3-28`, `C3-40`):**
   - Every status the API produces carries the security headers: `200`, `400`, `401`,
     `403`, `404`, `415` and `500`. HSTS is absent.
   - The published page's CSP has no `unsafe-inline` and no `unsafe-eval`. It is shown by
     the built-bundle E2E: zero "Refused to" console messages, with functional assertions
     ahead of axe.
   - axe reports 0 in both colour schemes on the sign-in view, the set-up view, the
     throttled state, the expiry dialog and the signed-in page.
   - Focus and the exact status text are asserted after passkey success, failure and
     pending, the throttle, expiry, signing in again, and sign-out.
7. **No secret reaches a log, a trace or an error response** (step 5's test). `audit_event`
   holds the six event kinds, each asserted by shape and by count, and no secret. It
   refuses `UPDATE`/`DELETE`/`TRUNCATE` from the application's code paths. **That is not a
   boundary against the connecting role**, which can remove the trigger (`D6`, `G-7`).
8. The pre-commit hook is installed by the documented command, refuses a staged
   secret-shaped value, and commits a clean one. The CI scan is the backstop.
9. Slice 01's and slice 02's DoD items still hold. The suites are green in CI, including the
   two time-zone jobs, which are required checks on `main` since 2026-10-20.
   `CrossSiteWriteTests` is re-pinned to the new refusal order, with the reason stated
   (`C3-13`). The E2E-backed items (3, 4's browser half, and 6's browser assertions) are
   evidenced in the assessment with date, commit and machine (`D13`).
10. Each obligation below is done, or recorded as not done with a reason.

## Obligations this slice carries

| From | Item | Here |
|---|---|---|
| Kyle, 2026-10-18 | No real balance before the security tier | **This slice is the prerequisite.** It does not admit data |
| The commission | The four additions | **Built**: steps 3 and 5. The login rate limit is a limiter on the anonymous authentication endpoints, since there is no password to throttle (`D7`); "secrets kept out of logs" is a test, with the filter written only if it fails |
| `D-030` Amendment 1 (now planning-repository history) | The pre-commit hook before the first real balance | **Built**: step 6 |
| Kyle, 2026-10-24 (`D7`) | The argon2id password fallback | **Deferred.** Trigger: an environment where a passkey is impractical, or the first lost authenticator |
| Kyle, 2026-10-24 (`D6`) | `security.md` "Database": the non-superuser application role | **Deferred.** Trigger: real data, or anything other than this app reaching the database |
| Kyle, 2026-10-24 (`D5`) | A backup command before a migration runs on a volume that matters | **Owed before the first real balance.** A dump then holds the passkey records and the audit log |
| Kyle, 2026-10-24 (`D17`) | `security.md` "Audit": data mutation, reports, projections, exports | **Carried.** Authentication events only here, over invented data. Trigger: real data |
| Committee `C3-48` | Coverage floors for security types | **Carried.** Trigger: the first pure security type outside a measured project |
| slice-02 assessment | Finer error boundaries, and a web unit-test runner | **Carried, not built.** Not this slice's technique; Kyle's call |
| slice-02 assessment | The import's plan-to-write race | **Carried.** Not this slice's technique |
| slice-02 assessment | The bundle (638 kB), the network split (`S-25a`) | **Carried** |
| Planning `D-043` | `C-389`, value-weighted coverage, discharged there | **Carried here as an open question**, Kyle's to rule, for the first slice holding real balances |

## The gate — slice 01's rule, unchanged

`LOG.md` carries on with both columns, one line per session, written as it goes.

> **The default at the end of slice 03 is STOP.** A fourth slice, or the first real balance,
> requires its own commission, **written and dated in this repository**, as this one is.
>
> The default is overridden only if the slice-03 assessment names **three techniques this
> slice taught that Kyle did not already have**. Each must come with the commit that
> demonstrates it, and must **not** be any of the thirteen slices 01 and 02 already claimed.
> **And** Kyle's hours must have stayed under **200** (`LOG.md`, `Kyle h`).
>
> **Explicitly NOT reasons to continue:** that the slice works, and that the next slice is
> obvious.

**Stated, not smoothed:** the agent builds, so the hours limb cannot trip. The gate rests on
the techniques limb and on Kyle's own judgement of what the slice taught.

## Then stop

Write `slice-03-assessment.md`. It answers:

- What did it cost, in both columns?
- Which of Spikes H, I and J were wrong (K was cut), and what in `security.md` was
  corrected as a result?
- Which of the attacks the tier exists for were shown refused in a real browser, and which
  only argued? Answer against the boundary in "What the slice is": each attacker class,
  the layer that refused it, and the test that showed it.
- Is the platform now fit to hold a real balance? **That is a question for the assessment
  to frame, not to answer.** Admitting one is its own commission.
- **Is a fourth slice wanted at all**, and if so, which technique is it for?

A fourth slice, or a real balance, begun without that assessment written has broken the
gate.
