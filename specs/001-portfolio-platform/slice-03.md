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

**Reviewed again, 2026-10-24** ([`../review/slice-03-r2.md`](../review/slice-03-r2.md)).
Three seats read this rewrite. Their findings are applied below, cited by seat (`SRC-n`,
`TST-n`, `SCO-n`), with Kyle's rulings `R1`–`R4`.

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
>   [`SOURCE@dotnet/aspnetcore@v10.0.13` `src/Identity/Core/src/PasskeyHandler.cs:677-678`;
>   committee BRW F1–F3]
> - **Claim 3: `EXECUTED`** in Chromium 153 and Firefox 155. `Secure`, `HttpOnly`,
>   `SameSite=Strict` and `__Host-` cookies are stored and sent back from
>   `http://localhost`, and a `Strict` cookie crosses `localhost` ports. **This was a
>   one-off run; the suite does not hold it** (`D14`). Safari is not a target browser
>   (`G-13`). [committee BRW F6, F7]
> - **Claim 4: the browser half is `EXECUTED`.** The Vite proxy forwards `Origin`
>   unchanged and rewrites only `Host`. [committee BRW F4]
>
> **`G-5` RAN: a passkey registers and signs in through the Vite proxy, and the origin
> check is live.** [`EXECUTED 2026-10-24`, `spikes/spike-i-proxy/RESULTS.md`]
> - The setup was step 2's configuration: `ServerDomain` pinned, `ValidateOrigin` against
>   configured origins, `__Host-`/`Strict`/`Secure` cookies, a persistent session, and the
>   10-minute challenge.
> - **Through the allowed Vite port:** registration `200`, sign-in `200`, `/me` `200`.
> - **Refused by `ValidateOrigin`:** a second Vite port's registration (`400`) and sign-in
>   (`401`), and sign-in on the API's own port (`401`).
> - The proxy forwards `Origin` unchanged and rewrites `Host`, which the pinned
>   `ServerDomain` makes harmless.
> - The session cookie arrived `__Host-`, `Secure`, `Strict`, `HttpOnly`, expiring in
>   7 days.
> - **Found:** a refused registration still leaves a passkey in the authenticator. With
>   Spike H's options endpoint it also left a user row with no passkey, which then masked a
>   later sign-in. That confirms `C3-3`'s rule that nothing exists before the ceremony
>   succeeds. Step 4's sign-in text treats "does not belong" as "not registered here".
> - **Not shown:** whether Aspire's Vite port is stable across runs (`SRC-12`); the design
>   does not depend on it.
>
> `G-12`, the same ceremony against the published port, runs in step 7, where the
> published stack first exists (round 2, `SCO-10`).

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

> **RAN: the slice's filter refuses; the framework's own middleware does not.**
> [`EXECUTED 2026-10-24`, `spikes/spike-j-csrf/RESULTS.md`]
> - **`C3-1` observed.** With only `UseAntiforgery()` and `RequireAntiforgeryToken`
>   metadata, a JSON `POST`, a `DELETE` and a raw `text/csv` upload with no token, or a
>   wrong one, all answered `200` and **wrote**.
> - **The slice's filter refused `403`, writing nothing,** for:
>   - a `POST`, `PUT` or `DELETE` with no token or a wrong token;
>   - the raw upload with no token;
>   - a token sent only in a form body (`SuppressReadingTokenFromFormBody`).
>
>   With the token in `X-CSRF-TOKEN`, each succeeded, the form-urlencoded upload
>   included.
> - **The exempt sign-in route (`R1`)** worked with no token. Sign-out without a token was
>   refused.
> - **The token binds to the user (`G-20`, `C3-22`).** A token issued after sign-in
>   validated through the filter, which runs after authentication. Alice's token was
>   refused for Bob, and Bob's token was refused once he had signed out.
> - **`G-19` is false, and so is `GT-6` for this case.** For a `no-cors` write from
>   another `localhost` port, Playwright reports the response's status (`403` or `200`),
>   but cannot read its body ("No data found") or its headers. It reports no `Cookie`,
>   `Origin` or `Sec-Fetch-*` on the request either.
> - **The server saw the `Strict` session cookie arrive and authenticate** from that port,
>   with `Sec-Fetch-Site: same-site`. DoD 4's browser half therefore reads "which layer
>   refused" and "the cookie arrived" from the **API's own record** of each request (`G-21`),
>   not from the browser.

**Already decided, so the spike confirms rather than chooses:**
- The token comes from an endpoint that requires a session (`C3-22`).
- It travels in `X-CSRF-TOKEN`, set as `AntiforgeryOptions.HeaderName`, since the default
  is `RequestVerificationToken`, on every unsafe request, the upload included.
- The allow-listed anonymous routes are exempt from the filter (round 2, `R1`).

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

**Merge order.** Each step merges with `main` green, as slices 01 and 02 did. Two switches
are deliberately held back until the sign-in page exists: the session requirement
(`FallbackPolicy`) and the antiforgery filter's enforcement. Both land in step 4 (`D11`;
round 2 `SCO-1`, `SCO-2`, `TST-1`). Steps 1–3 build behind them.

1. **Identity, data, and the setup-code rules.**
   - **Migration 5.** Identity's store inside `PortfolioDbContext`
     (`IdentityUserContext<TUser>`, no roles), as one EF migration on Npgsql. Every host
     must model Identity's schema `Version3`. The design-time factory and the Migrator
     have no `IdentityOptions`, and without it they model `Version1`, which has no
     passkey table (`SRC-4`). A test asserts that the migrated database has that table.
     - **Corrected at build:** round 2's fix, overriding the virtual `SchemaVersion`, does
       nothing. Identity's `OnModelCreating` reads the version only from `IdentityOptions`
       in the context's application services, and the method that applies it is
       `internal`. [`EXECUTED 2026-10-24`: a context with the override modelled no passkey
       table; `SOURCE@dotnet/aspnetcore@v10.0.13`
       `src/Identity/EntityFrameworkCore/src/IdentityUserContext.cs:135-180`]
     - **What does work:** every host registers the version
       (`PortfolioDbContext.AddIdentitySchema`), and a context built outside a host (the
       design-time factory, the test fixtures) gets a small provider of its own.
   - **`audit_event`.** Append-only, by migration 4's trigger pattern, refusing `UPDATE`,
     `DELETE` and `TRUNCATE` as `ingest_run` does; its test copies the `ingest_run` one.
     Its `actor` is a value, not a foreign key, so the owner can be reset (`C3-32`). The
     trigger binds the application's code paths; the connecting role can still remove it
     (`D6`, `G-7`).
   - **The setup code (`C3-3`, `D2`).** An Aspire secret parameter whose default the
     AppHost generates. It reaches user-secrets under `aspire run`, and the git-ignored
     `.env.<environment>` at **deploy**. `aspire publish` writes keys only.
     [`SOURCE@dotnet/aspire@v13.6.1`
     `src/Aspire.Hosting.Docker/DockerComposeEnvironmentResource.cs:548-578`]
     - At least 128 bits: **29 characters from Aspire's lower-case alphabet**, which
       already leaves out `i`, `l` and `o`, so no `0`/`O`/`1`/`l`/`I` can occur. That is
       about 131 bits, and the readability rule holds. **`GT-W` raised:** the generator
       draws with `RandomNumberGenerator.GetItems` [`SOURCE@dotnet/aspire@v13.6.1`
       `src/Shared/PasswordGenerator.cs`], and an app-model test checks a generated value
       against `^[a-hjkmnp-z]{29}$` [`EXECUTED 2026-10-24`].
     - It travels only in a `POST` body, never a query string. It is accepted only while
       no user exists, compared with `CryptographicOperations.FixedTimeEquals`, and
       counted by step 3's limiter.
     - **Single use survives a reset** (`SCO-4`, `TST-20`). The hash of every code that
       has completed an enrolment is kept in **`used_setup_code`**, keyed by the hash and
       append-only, outside Identity's rows. A code whose hash is there is refused. The
       spec first said "an `app_setting` row", but that table is held to one row by a
       CHECK; a table of its own makes "already spent" a primary-key fact. Test: enrol with code A, run
       `reset-owner`, and A is refused with nothing created; B succeeds; each step is
       audited.
     - **The window is bounded by single use, not by time.** Any member of
       `docker-users` can read the parameter (`C3-4`). Three events reopen enrolment
       without anyone asking:
       - a fresh volume;
       - `down -v`;
       - moving the repository directory, because `aspire deploy` names the compose
         project by a hash of the AppHost path.

       A `docker compose up` run by hand takes the directory name instead, and the
       runbook says which one it uses. [`SOURCE@dotnet/aspire@v13.6.1`
       `src/Aspire.Hosting.Docker/DockerComposeEnvironmentResource.cs:616-632`;
       `EXECUTED` committee OPS, `docker volume ls`]
     - Tests set it (`UseSetting`; a test-builder parameter). Asserting that the published
       compose file carries no literal is local-only, over the app model in publish mode
       (`TST-3`).
   - **The set-up views (`C3-53`):** setup code → username → passkey → done. Each step is
     a view whose `<h1>` names the step count and takes focus on arrival. A failed
     ceremony leaves the user on the same step with values intact. They are built in
     step 4.

   > **BUILT 2026-10-24 — the schema, the setup code's rules and its parameter.**
   > - **Migration 5 (`AuthAndAudit`):**
   >   - Identity's store at `Version3`: `identity_user`, `_claim`, `_login`, `_token` and
   >     `_passkey`. The tables are renamed to the platform's snake_case, since Identity sets
   >     `AspNetUsers` explicitly and the naming convention leaves an explicit name alone.
   >   - `audit_event`, with `audit_event_kind`'s labels in snake_case to match Npgsql's
   >     enum translator.
   >   - `used_setup_code`.
   >   - One `append_only()` trigger function refusing `UPDATE`, `DELETE` and `TRUNCATE` on
   >     both new tables.
   > - **`Domain.SetupCode`:** normalising (case, spaces, hyphens), fixed-time
   >   comparison, and SHA-256. It sits in `Domain`, so the coverage floor measures it.
   > - **The AppHost** generates the `setup-code` secret parameter, not persisted, and
   >   passes it to the Api as `Enrolment__SetupCode`. The Api reads it from step 2.
   >   [`EXECUTED 2026-10-24` `aspire publish` (CLI 13.6.4): the compose file reads
   >   `"${SETUP_CODE}"`, and `.env` carries the key with no value]
   > - **Tests:**
   >   - **Integration:**
   >     - the passkey table exists;
   >     - a passkey round-trips through its `jsonb` column under the naming convention
   >       (to the microsecond: the last tick of a `DateTimeOffset` is dropped);
   >     - both new tables refuse all three rewrites;
   >     - a code is recorded as spent once;
   >     - an audit kind round-trips.
   >   - **Unit:** `SetupCode`.
   >   - **App model:** the generated value and its wiring.
   > - **Every suite green:** Unit, Architecture and Integration (87), and all 15 local E2E
   >   cases against the stack migrated to 5 [`EXECUTED 2026-10-24`].
   > - **Owed elsewhere:** the full "enrol with A → `reset-owner` → A refused" test needs
   >   step 2's endpoints and step 7's command, so it lands in those steps.
2. **Authentication API.** Registered here, enforced in step 4.
   - **Passkey options (`C3-10`, `C3-23`, `C3-34`).**
     - `ServerDomain = "localhost"`, with a test that the options carry
       `rp.id == "localhost"` whatever the `Host` header says.
     - `ValidateOrigin` compares against **origins from configuration**: the AppHost
       passes the web endpoint's URL under `aspire run`, and the published origin from the
       same parameter as `API_PORT`. Aspire allocates the Vite port, and the published
       port is a `.env` value (`SRC-12`, `TST-8`). The override replaces Identity's
       default check, so it must also reject `crossOrigin` itself.
       [`SOURCE@dotnet/aspnetcore@v10.0.13` `src/Identity/Core/src/PasskeyHandler.cs:651-675`]
     - User verification stays `required`, written down so a change to `preferred` is
       visible.
     - `AuthenticatorTimeout` **and the `TwoFactorUserId` challenge cookie are both 10
       minutes** (`R3`). Identity's default for the cookie is 5 minutes, and a ceremony
       that outlives it throws. [`DOCS@2026-10-24` WebAuthn Level 3 §15.1;
       `SOURCE@dotnet/aspnetcore@v10.0.13`
       `src/Identity/Core/src/IdentityCookiesBuilderExtensions.cs:98-108`]
   - **The enrolment and sign-in endpoints** (`SCO-7`: here, after the options they need).
     - Enrolment creates the user and stores the passkey inside one explicit
       `Database.BeginTransactionAsync`, with the no-user check inside it, so two
       concurrent enrolments cannot both pass (`SRC-18`). Once a user exists, the
       enrolment endpoints answer `409`.
     - Both finish endpoints catch the "no ceremony underway" case: an expired, missing or
       replayed challenge cookie. They answer `400` with a `ProblemDetails.title` of their
       own, not the framework's `500` (`SRC-3`). [`SOURCE@dotnet/aspnetcore@v10.0.13`
       `src/Identity/Core/src/SignInManager.cs:555-563,706-724`]
     - One anonymous `GET` reports whether an owner is enrolled, and the environment
       label, so the page can choose set-up or sign-in before authentication (`SCO-13`).
   - **Cookies (`C3-6`, `C3-21`).** Every Identity cookie is `SameSite=Strict`,
     `SecurePolicy=Always` and `HttpOnly`, with a `__Host-` name. That includes
     `Identity.TwoFactorUserId`, which carries the passkey challenge.
     - The antiforgery cookie is the one exception: `Strict` and `HttpOnly`, but neither
       `Secure` nor `__Host-`, because antiforgery throws on a non-HTTPS request when it
       is `Secure`.
     - One test covers every `Set-Cookie`, the antiforgery cookie's lack of a prefix
       included (`TST-24`). [`SOURCE@dotnet/aspnetcore@v10.0.13`
       `src/Http/Http.Abstractions/src/CookieBuilder.cs:117`,
       `src/Antiforgery/src/Internal/DefaultAntiforgery.cs:256-262`; `EXECUTED`
       committee BRW F6]
   - **Sessions (`D4`, `D10`, `C3-5`, `C3-16`).** Identity's stateless cookie,
     `ExpireTimeSpan` 7 days.
     - **Persistent:** passkey sign-in hard-codes `isPersistent: false`, which gives a
       browser-session cookie, so the application cookie's `OnSigningIn` sets
       `IsPersistent = true`, for enrolment's sign-in too (`SRC-2`). A test asserts that
       `Set-Cookie` carries `Expires` about 7 days out.
       [`SOURCE@dotnet/aspnetcore@v10.0.13`
       `src/Identity/Core/src/SignInManager.cs:642-683`,
       `src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs:328-334`]
     - `ValidationInterval` is one minute, read from configuration so the E2E can set it
       to zero. Every successful stamp check re-bases the ticket, so the session slides to
       within a minute of the last request that carries the cookie (`SRC-16`). The
       sign-in page says "You stay signed in for 7 days after you last use the app, or
       until you sign out."
     - **Sign-out calls `UpdateSecurityStampAsync`**, because `SignOutAsync` alone does
       not rotate it. That signs out every browser, and a copied cookie is refused within
       a minute (`SRC-16`), the lag `D4` accepted.
     - There is no absolute cap; a stolen cookie in use is answered by `revoke-sessions`.
       [`SOURCE@dotnet/aspnetcore@v10.0.13`
       `src/Identity/Core/src/SecurityStampValidator.cs:136-162`]
   - **Lockout off (`C3-8`, restated by `SRC-11`).** `Lockout.AllowedForNewUsers = false`.
     Today no passkey failure is counted: only the password and two-factor paths call
     `AccessFailedAsync`. The setting exists so the deferred password path cannot lock
     the passkey. The test is the twin: with lockout set on the user, the passkey still
     signs in. [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Identity/Core/src/SignInManager.cs:497-499,659-673`]
   - **Authorization, prepared, not yet switched on.** Each anonymous route carries
     `AllowAnonymous`. The application cookie's `OnRedirectToLogin` and
     `OnRedirectToAccessDenied` answer a bare `401` and `403` everywhere, since
     `.DisableCookieRedirect()` still sets `Location` (`SRC-15`).
   - **Sign-out** is a `POST` under the token (`C3-36`). It clears the Identity cookies and
     the antiforgery cookie (round 1's clause, restored: `SCO-12`).
   - **The replay model, written down (`C3-39`).** The challenge lives for 10 minutes
     (`R3`) in the data-protected `TwoFactorUserId` cookie, with no server-side nonce. A
     replayed exchange is caught only by the sign counter, which runs only when a counter
     is non-zero. `G-8` decides whether a per-credential nonce is needed. Whoever captured
     the exchange also holds the session cookie that followed.
   - **The key ring (`D3`, `C3-11`).** `PersistKeysToFileSystem` to a path from
     configuration, plus `SetApplicationName("portfolio")`. Under `aspire run` the path is
     the user profile; in the container it is a named volume (step 7). The keys are
     plaintext on the host, and rely on disk encryption, as the envelope-encryption KEK
     does.
   - **Test harness (`R2`, `TST-2`, `TST-6`, `TST-7`, `SRC-9`, `SRC-10`).**
     - **`SoftAuthenticator`** in `tests/Integration`. It generates an ES256 key; builds
       `clientDataJSON` with the configured origin; builds `authenticatorData` with flags
       and counter; CBOR-encodes a `none` attestation; and signs assertions. It has
       hostile variants: a foreign origin, `crossOrigin`, UV off, a replayed counter, and
       malformed CBOR. **Gate (`GT-3`):** Identity accepts its attestation. The first
       registration raises it.
     - **`AuthFixture : PostgresFixture`**, with the auth classes in `tests/Integration`.
       Its client uses `https://localhost`, because .NET's cookie container never sends a
       `Secure` cookie back over `http://localhost`. `IsHttps` is therefore true in these
       tests and false in production; the E2E covers the plain-HTTP path. Replayed cookies
       are sent by hand. [`EXECUTED 2026-10-24` round 2 TST and SRC probes]
     - **`SignedInClient`**, for every other HTTP test class. It authenticates through a
       test scheme that accepts only requests carrying `X-Test-User`, and answers
       `NoResult` otherwise. A policy scheme selects it, so it is consulted at all (`SRC-10`).
       Its principal's `NameIdentifier` is the real user's id, because antiforgery binds
       the token to that claim. The four HTTP classes (`ImportApiTests`,
       `CrossSiteWriteTests`, `ClockTests`, `BalanceIdempotencyTests`) move onto it here.
     - **The guard against the test scheme leaking** (`TST-3`):
       - On the unmodified Production and Development factories, the registered schemes
         and the default schemes are exactly Identity's.
       - An architecture test asserts that no `src/*.csproj` references `tests/`.

   > **BUILT 2026-10-24: registered, not yet enforced.**
   > - **The routes**, under `/api/v1/auth`:
   >   - `GET /state` (anonymous): enrolled or not, and the environment;
   >   - `POST /enrolment/options` and `POST /enrolment`;
   >   - `POST /sign-in/options` and `POST /sign-in`;
   >   - `POST /sign-out` and `GET /session`, which require a session.
   >
   >   The anonymous ones carry `AllowAnonymous`. `GET /session` was added for step 4's
   >   "Signed in as <name>".
   > - **Titles are contract.** Every refusal title is a constant in `ProblemTitles`, which
   >   the page will key on.
   > - **Sign-in departs from the text above.** It runs `PerformPasskeyAssertionAsync`,
   >   then stores the new sign count, then `SignInAsync(…, isPersistent: true)`.
   >   `PasskeySignInAsync` returns a bare `Failed`, and the page needs "this passkey isn't
   >   registered with this app" (Spike I §3) apart from other refusals. This is round 2's
   >   own alternative (`SRC-2`). Two consequences:
   >   - Identity's lockout check is never run on the passkey path, so the lockout twin
   >     holds by construction, and its test documents it.
   >   - `OnSigningIn` still makes any sign-in persistent.
   > - **Enrolment:**
   >   - the options are made for a user who does not exist yet;
   >   - the finish creates the user and stores the passkey in one transaction, under
   >     `pg_advisory_xact_lock`, re-checking both "no owner" and "code not spent";
   >   - a refused registration leaves no user and the code unspent.
   > - **Not yet:** clearing the antiforgery cookie on sign-out, which step 3 introduces.
   > - **The harness:**
   >   - `SoftAuthenticator` (ES256, a `none` attestation, CTAP2-canonical CBOR, DER
   >     signatures), built from Identity's own parsers. **`GT-3` raised:** Identity accepted
   >     its attestation and assertions.
   >   - `AuthFixture` on `https://localhost`.
   >   - `TestAuth`: the header-only scheme behind a policy scheme.
   >   - The guards: the app's scheme set and defaults asserted in Production and
   >     Development, and an architecture test that no app project references a test
   >     project.
   >   - The four HTTP classes now run signed in.
   > - **Shown** [`EXECUTED 2026-10-24`, `tests/Integration/AuthApiTests.cs`, 24 cases]:
   >   - **Enrolment:**
   >     - it creates the owner with a passkey, spends the code, audits it and signs in;
   >     - a wrong, empty or unconfigured code is refused (`403`);
   >     - once an owner exists, enrolment answers `409`;
   >     - a spent code is refused after the owner is reset, and a fresh one enrols;
   >     - a foreign origin, `crossOrigin`, UV off and malformed CBOR are each refused
   >       with nothing created.
   >   - **The RP ID** is `localhost` for any `Host`.
   >   - **No ceremony underway** answers `400`, not `500`.
   >   - **Sign-in:**
   >     - a fresh browser signs in;
   >     - an unknown passkey is told "isn't registered with this app";
   >     - a foreign origin, `crossOrigin`, UV off, a non-advancing counter and truncated
   >       data are each refused;
   >     - a replay with its challenge cookie is caught by the counter;
   >     - a lockout set on the user does not stop the passkey.
   >   - **Cookies and sessions:**
   >     - every cookie is `__Host-`, `Secure`, `HttpOnly`, `Strict`, `Path=/`, with no
   >       `Domain`;
   >     - the session expires about 7 days out;
   >     - a cookie copied before sign-out still answers `200` at +30 s and `401` at +61 s.
3. **CSRF, CORS, the limiter, headers.**
   - **The pipeline, in order** (`SRC-1`, `SRC-8`, `TST-5`, `TST-9`, `TST-10`):
     1. the headers middleware, writing in `Response.OnStarting`, so a `500` keeps them;
     2. `UseExceptionHandler`, `UseStatusCodePages`;
     3. the cross-site refusal;
     4. `UseFileServer`;
     5. **explicit** `UseAuthentication()`, `UseAuthorization()`;
     6. `UseRateLimiter()`;
     7. the endpoints, with the antiforgery filter on `/api`.

     Calling `UseAuthentication` and `UseAuthorization` explicitly matters.
     `WebApplication` otherwise inserts them ahead of every user middleware, which takes
     the headers off a `401`. A fallback policy also covers a static file served after
     them, so the SPA would answer `401`.

     Host filtering wraps the whole pipeline, so its `400` carries no security headers.
     That is accepted, because it refuses before any application code runs.

     An Integration test on a temporary web root checks the order: anonymous `GET /` and
     an asset answer `200`; `GET /` carries the exact published CSP; anonymous `/api`
     answers `401`.

     [`EXECUTED 2026-10-24` round 2 SRC probes A, B and C; `SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/DefaultBuilder/src/WebApplicationBuilder.cs:434-453`,
     `src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs:97-150`,
     `src/Middleware/Diagnostics/src/ExceptionHandler/ExceptionHandlerMiddlewareImpl.cs:291-293`]
   - **The antiforgery filter (`C3-1`)** is built and tested here, per Spike J. It
     refuses with `403` and a `ProblemDetails.title` of its own.
     - **The allow-listed anonymous routes are exempt** (`R1`). They are covered instead
       by layers 1 and 2, `ValidateOrigin`, the limiter and the `Strict` challenge cookie.
     - The token endpoint requires a session (`C3-22`). `SignedInClient` gains a handler
       that fetches the token and sends `X-CSRF-TOKEN` on unsafe methods.
     - **The filter's enforcement on the existing endpoints is switched on in step 4**,
       with the session requirement.
   - **CORS: none registered** (`C3-41`). A test sends `Origin: http://attacker.localhost`
     and the Vite origin across DoD 2's enumeration, plus `OPTIONS` to each `POST` path,
     and asserts that no response carries `Access-Control-Allow-Origin`.
   - **The limiter (`C3-7`, `C3-19`, `C3-43`; `SRC-14`, `SCO-9`, `TST-12`, `TST-13`).**
     - `AddFixedWindowLimiter("auth", …)` plus `RequireRateLimiting("auth")` on the
       anonymous authentication endpoints, not `GlobalLimiter`. It is one limiter for
       every caller, never keyed by address.
     - Its permit limit and window are bound from configuration, and the production
       numbers are recorded here at build.
     - `RejectionStatusCode` is `429`. `OnRejected` writes a `ProblemDetails` titled "Too
       many sign-in attempts. Try again in about a minute.", with `Retry-After` from the
       lease, which is the window length. It logs neither a partition key nor a body.
     - Tests that sign in more than once, and DoD 7's canary sweep, run with the limit
       raised and assert that no response was `429`.

     [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Middleware/RateLimiting/src/RateLimiterOptionsExtensions.cs:56-70`,
     `RateLimiterOptions.cs:41`; `SOURCE@dotnet/runtime@v10.0.13`
     `src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs:209-216`]
   - **Headers (`C3-38`, `C3-40`):**
     - `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`;
     - `X-Frame-Options: DENY`, with `AntiforgeryOptions.SuppressXFrameOptionsHeader` set
       so antiforgery adds no second value;
     - `Permissions-Policy` denying everything except
       `publickey-credentials-get=(self)` and `publickey-credentials-create=(self)`;
     - **no HSTS**, and its absence asserted (`D1`).

     The page served from `wwwroot` gets the published CSP, Spike K's string above.
     `frame-ancestors` works only as a header, which is why the API sets it.
     [`EXECUTED` committee BRW F16]

   > **BUILT 2026-10-24.**
   > - **The pipeline** is the order above, written once in `Program.cs` with the reason for
   >   each place.
   > - **`SecurityHeaders`:**
   >   - written in `OnStarting`;
   >   - the CSP goes on every response, not only the page's. A JSON body ignores it, and
   >     one rule is simpler than two;
   >   - the `Permissions-Policy` denies sixteen features and allows the two passkey ones
   >     for `self`.
   > - **`CsrfTokens`:**
   >   - the filter, `RequireCsrfToken()`;
   >   - the `Exempt` marker on the four anonymous ceremony routes (`R1`);
   >   - `GET /api/v1/auth/csrf-token`, which requires a session;
   >   - the cookie is `portfolio-csrf`: `Strict` and `HttpOnly`, but neither `Secure` nor
   >     `__Host-`.
   > - **What the filter guards now:** sign-out only. That is the one unsafe route that
   >   already requires a session, and sign-out also clears the token's cookie. The
   >   `/api/v1` group gets it in step 4 (`D11`). The test client already sends a token on
   >   every unsafe request, ready for that.
   > - **`SignInRateLimit`:**
   >   - **the recorded production numbers: 10 requests a minute** on the four ceremony
   >     routes, which is five attempts. Bound from `RateLimits:SignIn`;
   >   - `429` with `Retry-After` and the title;
   >   - the state check is not limited.
   > - **CORS:** none registered.
   > - **Shown** [`EXECUTED 2026-10-24`]:
   >   - **`SecurityHeadersTests`:**
   >     - every status from `200` to `500` carries the headers, with no HSTS;
   >     - the page and an asset are served `200` before authentication, under the exact
   >       CSP;
   >     - a `401` carries no `Location`.
   >   - **`CsrfFilterTests`**, on a minimal app:
   >     - JSON `POST`, `PUT`, `DELETE` and a raw `text/csv` body are each refused with no
   >       token or a wrong one, writing nothing, and accepted with the token;
   >     - a token in the form body alone is not read;
   >     - another user's token is refused, and so is a signed-out one;
   >     - an exempt route and a `GET` need none.
   >   - **`SignInRateLimitTests`:**
   >     - the window is shared by the ceremony routes;
   >     - the 4th request in a 1-hour, 3-permit window answers `429`, with
   >       `Retry-After: 3600` and the title;
   >     - the production numbers are pinned.
   >   - **`CorsTests`:** every mapped endpoint, with two origins, and a preflight to every
   >     `POST`. None answers `Access-Control-Allow-Origin`.
   > - **Found:** ASP.NET Core 10.0.13's `RouteHandlerAnalyzer` throws (`AD0001`) on a
   >   method group from another assembly passed to `MapGet` in the test project. The test
   >   uses a lambda instead.
4. **Web, and the switch.** **The error boundaries stay per section** (`edb9504`).
   - **The switch (`D11`).** `FallbackPolicy = RequireAuthenticatedUser()` and the
     antiforgery filter's enforcement go on together with the sign-in page and the E2E's
     signed-in helper, in one change. DoD 2's allow-list test lands with them.
     [`SOURCE@dotnet/aspnetcore@v10.0.13`
     `src/Security/Authorization/Core/src/AuthorizationPolicy.cs:189-197`]
   - **The E2E signed-in helper (`TST-25`)** enrols once per `AppFixture`, with the setup
     code as a test-builder parameter. It signs in once, and opens each context from the
     saved `StorageState`. **Gate (`GT-6`):** restored `Secure` cookies are sent to
     `http://localhost`. The E2E's bare `HttpClient` helpers become `fetch` calls inside
     the signed-in page, which carry the cookie and the token (`TST-6`).
   - **Views (`C3-15`, `C3-37`, `C3-50`).** Sign-in, set-up, and the portfolio, chosen
     from step 2's anonymous state `GET`. Each sets `document.title`, and its `<h1>`
     takes focus when the view changes after a user action. The fields carry
     `autocomplete="username"`, and `one-time-code` on the setup code. The sign-in view
     shows the environment label before authentication, as `environments.md` requires.
   - **The passkey button (`C3-18`, `C3-34`, `C3-52`, `D8`).**
     - It never starts a ceremony on page load or on field focus (`SCO-12`).
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
       - A sign-in refused because the presented passkey is unknown here says so: "This
         passkey isn't registered with this app." A refused enrolment can leave such a
         passkey on the device. [`EXECUTED 2026-10-24` Spike I, §3]
     - **The E2E's producers (`TST-15`):**
       - pending: a route that holds the sign-in `POST`;
       - `NotAllowedError`, `SecurityError` and unsupported: init scripts that replace
         `navigator.credentials.get` or remove `PublicKeyCredential` (`GT-9`, `GT-12`);
       - success: one real ceremony.
   - **Expiry (`C3-17`, `C3-47`).**
     - One `401` handler sets a single signed-out state. Sections render no alert of their
       own for a `401`, and keep their data on screen.
     - `App` stays mounted, and sign-in opens as a `<dialog>` over it (`G-30`).
     - After signing in again, the page refetches the token first, then invalidates
       queries, returns focus, and announces "Signed in again."
     - A refused write is not replayed. Its form keeps the input and says why.
     - **A `403` whose title is the antiforgery filter's** means: refetch the token, retry
       once, then sign in. The page keys on the title, never the status, since layer 2
       also answers `403` (`SRC-6`, `SCO-5`).
     - The footer's health line does not say "unreachable" on expiry.
     - E2E: with `ValidationInterval` zero, copy context A's cookies to B, sign out in A,
       and B's next request is a `401` with the cookie sent: "ticket expired". Clearing
       B's cookies gives "no cookie sent". The two must be told apart (`TST-15`).
   - **Sign-out (`D15`).** The header shows "Signed in as <name>" and a `Sign out` button
     that `POST`s, then calls `location.assign('/')`. The sign-in view then says "You are
     signed out", from a one-shot flag.
   - **The token** is held in module memory, fetched after every sign-in, and discarded on
     sign-out and on a `401` (`C3-22`). That is a client convention: the token binds to
     the user, not the session (`SRC-18`).
   - **Styling (`C3-51`).** The dialog uses the AA tokens. "Waiting…" is text, not only
     a spinner, since reduced motion removes the spinner. `aria-disabled` buttons are
     styled by colour, not opacity.

   > **BUILT 2026-10-24: the switch is on.**
   > - **Server:**
   >   - `FallbackPolicy = RequireAuthenticatedUser()`;
   >   - the CSRF filter on the whole `/api/v1` group, with the ceremonies exempt;
   >   - `GetHealth` anonymous, and the Development `/health` and `/alive` too, since the
   >     AppHost probes them;
   >   - `/openapi` behind the session (`R4`).
   > - **DoD 2 (`AllowListTests`):**
   >   - Production and Development, unmodified, enumerated over `EndpointDataSource`:
   >     only the allow-list answers without a session, with no `Location` header, and
   >     every name on the list exists;
   >   - a table of bodies shows every unsafe endpoint off the list refused for its
   >     token, and none on the list;
   >   - **seen failing:** making `ListAccounts` anonymous fails both environments.
   > - **Web:**
   >   - `apiFetch` behind the typed client and the upload: the token on unsafe requests;
   >     one retry on the filter's title; and a `401` outside `/auth/` sets the one
   >     signed-out state;
   >   - `AuthGate` with the set-up view (three steps), the sign-in view, the expiry
   >     `<dialog>`, and the header's account line;
   >   - one `PasskeyButton` for every outcome;
   >   - sections render no alert of their own for a `401`.
   > - **E2E:**
   >   - **The fixture** sets the setup code as the AppHost parameter, enrols once, and
   >     keeps the browser state and the passkey. It sets the validation interval to zero
   >     and raises the limiter.
   >   - **Copies of the passkey** get counters 1,000 apart, or a second copy would be
   >     refused as a clone.
   >   - **The existing tests** run from the stored state, and their bare `HttpClient`
   >     calls became `fetch` from a signed-in page. **`GT-6`'s second half is raised**:
   >     restored `Secure` cookies are sent to `http://localhost`, or none of them would
   >     pass.
   >   - **`AuthBrowserTests`** (9) and **`SetUpBrowserTests`** (1), on a fixture with no
   >     owner. [`EXECUTED 2026-10-24`, all 25 E2E cases green]
   >     - **Sign-in:** title, environment, lifetime and time allowed shown, then focus on
   >       the portfolio's `h1`.
   >     - **Pending:** while a route holds the sign-in `POST`, "Waiting…" is shown and
   >       the button is `aria-disabled` and keeps focus.
   >     - **Failures:** `NotAllowedError` and `SecurityError` each get their own text,
   >       with focus back on the button.
   >     - **Unsupported:** with no `PublicKeyCredential`, the page says so and shows no
   >       button.
   >     - **An unknown passkey** is named as such.
   >     - **Sign-out:** "You are signed out", with focus on `Sign in`.
   >     - **Expiry**, both as "ticket expired" and as "no cookie sent":
   >       - the dialog opens over the page, with no alert storm;
   >       - the typed input is kept, with "signed out before this was saved";
   >       - signing in again closes it and announces "Signed in again.";
   >       - the same write then succeeds.
   >     - **Set-up:**
   >       - a wrong code is refused at the passkey step;
   >       - going back, the code pasted grouped and in capitals enrols;
   >       - each step's heading takes focus;
   >       - a fresh browser is then offered sign-in.
   > - **Found:**
   >   - **The announcement had to move.** "Signed in again." first lived in the dialog,
   >     which unmounts the moment the session is back, so it was never announced. It now
   >     sits on the gate.
   >   - **The stranger passkey's user handle.** A test passkey's handle has to be UTF-8,
   >     as Identity's are; random bytes make Identity fail the lookup another way.
   > - **Moved to step 7:**
   >   - **DoD 4's browser half**, which reads which layer refused, and whether the cookie
   >     arrived, from the API's own log (`G-21`);
   >   - **DoD 6's checks:** axe in both colour schemes over these views, and the CSP
   >     against the built bundle.
   >
   >   Both sit with the published-stack work, where the built bundle exists.
5. **Logging and audit.**
   - **Events:** enrolment, sign-in succeeded, sign-in failed, sign-out. Each records
     actor, time, subject, environment and connection address. The address is the real
     one only after step 7's forwarded-headers fix (`C3-9`). The step 3 limiter bounds the
     rows (`C3-43`). The two command events, sessions revoked and owner reset, are
     asserted in step 7, where the commands are built (`SCO-6`).
   - **DoD 7's test (`C3-26`, `TST-14`).**
     - **Capture:** every category at `Trace`, with the `Microsoft.AspNetCore` and
       `Microsoft.EntityFrameworkCore` levels pinned through settings. What is captured:
       - each entry's formatted message, every structured state value, every scope value
         and the full exception chain;
       - every Activity's tags, events and status, through the OpenTelemetry in-memory
         exporter (`G-22`);
       - every non-2xx body.
     - **Non-empty guard:** at least one `Microsoft.AspNetCore.Hosting` line and one span
       for `GET /api/v1/system/health` must be present.
     - **Canaries:** the setup code, a passkey assertion, the session cookie value and the
       antiforgery token. They are driven through enrolment, sign-in, a failed sign-in, a
       malformed assertion (from `SoftAuthenticator`) and a `500` (from DoD 6's producer).
       They are injected into every field of every anonymous endpoint and into a query
       string, with the limiter raised, and each must reach its handler.
     - **Matching:** raw, URL-encoded, and base64 and base64url at each of the three byte
       alignments.
     - **Configuration:** Production, with `EnableSensitiveDataLogging` asserted off
       (`G-24`).
     - **Seen failing:** a committed test logs one canary at `Debug` and asserts the scan
       finds it.
     - **The redaction filter is written only if this test goes red** (the middle cut).
   - **The contract test (`C3-27`, `TST-19`)** sits in `tests/Architecture`, which is
     required in CI, and reads the committed OpenAPI document. It scans **response**
     schemas for credential-like property names, and asserts that the authentication
     operations are present, so the scan is not vacuous.

   > **BUILT 2026-10-24.**
   > - **The audit events** were written in step 2. Their rows are scanned here for secrets
   >   too.
   > - **`SecretsStayOutTests` (DoD 7):**
   >   - **The secrets:** the setup code, a passkey assertion's signature and client data,
   >     the session cookie and the CSRF token. They are driven through enrolment, sign-in,
   >     a failed and a malformed sign-in, sign-out and a `500` that carries the cookie.
   >   - **The canary** goes into every field of every anonymous endpoint and into a query
   >     string, and each request reached its handler, with no `429`.
   >   - **What is scanned,** at `Trace` in Production:
   >     - every log entry's message, structured values, scopes and exception chain;
   >     - every span's tags, events and status, from the OpenTelemetry in-memory exporter;
   >     - every non-2xx body;
   >     - every `audit_event` row.
   >   - **The forms looked for:** raw, URL-encoded, and base64 and base64url at all three
   >     byte alignments.
   >   - **The guards:** an ASP.NET Core `Trace` line and the health span must be present,
   >     and `EnableSensitiveDataLogging` must be off (`G-22` and `G-24` raised).
   >   - **Seen failing:** a committed twin logs a secret at `Debug` plain, inside a base64
   >     blob, and URL-encoded in a scope, and the scan finds all three.
   > - **It went red once.** That was its job, and it decided the "filter only if red"
   >   rule. ASP.NET Core's `Microsoft.AspNetCore.Hosting.Diagnostics` "Request starting"
   >   and "Request finished" lines carry the full query string, so a secret pasted into a
   >   URL would be logged. [`EXECUTED 2026-10-24`: 12 lines]
   >   - **Kyle's ruling:** that category is off below `Warning` in code, whatever
   >     configuration says. A code filter comes after configuration's and wins at the same
   >     category, and the test sets that category to `Trace` to prove it.
   >   - Each request is still a span, whose `url.query` OpenTelemetry already redacted.
   >   - Nothing else was found: no log, span, body or audit row held any secret.
   > - **Also changed:** the auth refusals no longer copy Identity's failure message into
   >   `detail`. One of those messages echoes what the client sent ("got '<type>'").
   > - **`NoCredentialInResponsesTests`**, in `tests/Architecture`:
   >   - it reads the committed document and follows `$ref`s from every response;
   >   - it requires the auth operations to be present, and at least 30 properties to be
   >     scanned;
   >   - the one exception, `CsrfTokenResponse.token`, is named with its reason;
   >   - its twin shows a nested `passwordHash` is flagged.
   > - **Not written:** the redaction filter. Nothing but the query-string lines needed
   >   one.
6. **The pre-commit hook (`C3-29`, `TST-16`).** Installed by `scripts/install-hooks.sh`.
   - **The interpreter is chosen by running it, not by finding it.** On this machine
     `python3` is the Microsoft Store stub: it exists and exits 49, so today's hook refuses
     even a clean commit. [`EXECUTED 2026-10-24` round 2 TST]
   - **The test in `tests/Architecture`:**
     - builds a temporary repository with the hook, scanner and installer copied in;
     - isolates git config (`GIT_CONFIG_GLOBAL` empty, `GIT_CONFIG_NOSYSTEM=1`) and sets
       a local identity;
     - builds the secret-shaped value at runtime, so the test's own source does not trip
       the scanner.
   - **Three cases:**
     - the secret is refused: a non-zero exit and the scanner's refusal text;
     - a clean file commits with exit 0;
     - with a `python3` shim that exits 49 first on `PATH`, a clean file still commits.

   The CI scan is named in DoD 8 as the backstop, since `--no-verify` bypasses the hook by
   design.

   > **BUILT 2026-10-24.**
   > - **The hook** tries `python3`, then `python`, each run with `-c ''` first, and
   >   otherwise exits 2 naming what it tried.
   > - **`.gitattributes`** now pins `.githooks/*` to LF, as `*.sh` already was. The hook
   >   has no extension, so the old rule missed it.
   > - **`PreCommitHookTests`**, in `tests/Architecture` (required in CI):
   >   - each case gets a fresh repository with git's global and system config shut out
   >     and a local identity;
   >   - the hook, scanner and installer are copied in, and installed by
   >     `sh scripts/install-hooks.sh`, then `core.hooksPath` is asserted;
   >   - the secret is built at run time;
   >   - the three cases:
   >     1. **refused**, with the scanner's text;
   >     2. **clean**, with exit 0;
   >     3. a `python3` that exits 49 first on `PATH`, beside a `python` wrapping a real
   >        interpreter, so CI's Linux runs the fallback too. A clean commit still goes
   >        through.
   > - **Seen failing:** with the old hook put back, all three fail on this machine. The
   >   refusal case fails because the scanner never ran, which is the false green the clean
   >   twin exists for. [`EXECUTED 2026-10-24`]
   > - **This clone** now has the hook, installed by the documented command, and the commit
   >   that built this step went through it.
7. **Tests and deploy.**
   - **The app model.** `.DisableForwardedHeaders()` on the `api` resource (`C3-9`).
     [`SOURCE@dotnet/aspire@v13.6.1`
     `src/Aspire.Hosting/ProjectResourceBuilderExtensions.cs:633-643,960-964`] The key
     ring goes on a named volume, declared through a `ContainerMountAnnotation` (or
     `ConfigureComposeFile`). A volume added only in `PublishAsDockerComposeService` is
     never declared, and `docker compose config` rejects it. It is mounted under
     `/home/app`, which the image's `app` user (UID 1654) owns; a fresh volume at a new
     path is root-owned and unwritable (`SRC-7`). [`EXECUTED 2026-10-24` round 2 SRC:
     `docker compose config`; `docker run` against `api:latest`;
     `SOURCE@dotnet/aspire@v13.6.1`
     `src/Aspire.Hosting.Docker/DockerComposePublishingContext.cs:100-127`]
   - **Two commands (`TST-22`).** `revoke-sessions` (rotate the stamp) and `reset-owner`
     (clear the credentials and reopen enrolment) run as `docker compose run --rm api
     <mode>`.
     - Their bodies are a service. Integration invokes it through `factory.Services`, and
       one process-level test runs the built API with a mode.
     - Each writes its audit event, asserted as a per-action delta.
   - **The runbook says:**
     - a redeploy now keeps sessions;
     - `down -v` destroys the key ring with the data, and reopens enrolment;
     - logs live in `docker logs` (100 MB, lost on recreate) (`G-27`);
     - which compose project name it uses (step 1).
   - **The published stack:**
     - passkey sign-in by a Playwright script against `docker compose`, which raises
       `G-12`;
     - the headers, including HSTS's absence, by `curl`;
     - a forged `X-Forwarded-For` is not what `audit_event` records;
     - a session survives `docker compose restart api`;
     - the built-bundle E2E (`D16`, `G-29`);
     - every slice-01 and slice-02 property re-checked;
     - `G-11` (`127.0.0.1` publishes, probed from the LAN) and `G-26` (the Postgres
       password stable across deploys) are raised here.

   > **BUILT 2026-10-24, part a: the app model, the commands, the runbook, DoD 4's browser half.**
   > - **Forwarded headers:** `.DisableForwardedHeaders()` on `api`. The published compose
   >   file no longer carries `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.
   > - **The key ring:**
   >   - in publish mode, a `ContainerMountAnnotation` mounts the named volume
   >     `portfolio-dev-keys` **at `/home/app`**, and `DataProtection__KeyPath` is
   >     `/home/app/keys`;
   >   - the compose file declares the volume at the top level, and `docker compose config`
   >     accepts it.
   >
   >   [`EXECUTED 2026-10-24`: `aspire publish`; `docker compose config`; `docker run`
   >   against `api:latest`, where a fresh volume at `/home/app` is owned by UID 1654 and the
   >   key directory is writable]
   > - **The commands:**
   >   - `OwnerCommands` is a service with `revoke-sessions` (rotate every stamp) and
   >     `reset-owner` (delete the owner and their passkeys);
   >   - each writes its audit event, so all six audit kinds are now asserted;
   >   - `Program.cs` runs one and exits, before `app.Run()`.
   >
   >   `OwnerCommandsTests` show it: a copied cookie is refused after `revoke-sessions`;
   >   after `reset-owner`, enrolment is open, the old code is refused and a fresh one
   >   enrols; with no owner, nothing happens; and the built `Api.dll` runs a command and
   >   exits without serving.
   > - **DoD 4's browser half** (moved here from step 4):
   >   - each refusing layer writes one line, `Refused by <layer>: <method> <path>
   >     Sec-Fetch-Site=<v> session-cookie=<arrived|absent>`, with no query string and no
   >     cookie value (`RefusalLogTests`);
   >   - the E2E fixture follows the API's console through `ResourceLoggerService`.
   >     **`G-21` raised:** watch by `IResource`, since watching by name follows nothing
   >     when the id carries a replica suffix;
   >   - `CrossSiteBrowserTests` assert `cross-site` with the cookie **arrived** from
   >     another `localhost` port, and **absent** from `attacker.localhost`, through both
   >     the API and the Vite proxy.
   >
   >   **Found:** the attack page's `no-cors` fetch sent no cookie at all, because
   >   `fetch` defaults to `credentials: 'same-origin'`. It now asks for `include`, so the
   >   attack refused is the one that carries the session.
   > - **The runbook** is in the README, under "Signing in" and "Recovering". It covers:
   >   - where the setup code is;
   >   - what survives a restart, and what `down -v` destroys;
   >   - the project name;
   >   - both commands;
   >   - that a fresh code is needed after a reset;
   >   - the logs.
   > - **Part b** is the published-stack run and DoD 6's checks.

   > **BUILT 2026-10-24, part b: DoD 6, DoD 5's page half, and the published stack.**
   > - **DoD 6, axe** in both colour schemes, in the dev E2E:
   >   - the sign-in view, the throttled state, the signed-in page and the expiry dialog
   >     (`AuthBrowserTests`);
   >   - the set-up view (`SetUpBrowserTests`, while no owner exists yet).
   >
   >   All clean.
   > - **DoD 5's page half:** a `429` now reads "… You can try again after HH:MM.", worked
   >   out once from `Retry-After`, never a countdown.
   > - **The E2E's default assertion timeout is now 15 seconds.** Two load flakes needed it,
   >   each passing alone.
   > - **`PublishedStackTests`** runs by hand against a deployed stack, and is skipped
   >   otherwise (`D13`, `D16`).
   >
   > **The published run** [`EXECUTED 2026-10-24`, of the tree this commit records (the images were built from it),
   > Windows, Docker Desktop, Chromium 1.63]:
   > - **The deploy** applied migration 5 to the existing volume
   >   `aspire-compose-075c33a6_portfolio-dev-pgdata` (invented data), and created
   >   `aspire-compose-075c33a6_portfolio-dev-keys`.
   > - **The built bundle under its CSP** (`PublishedStackTests`, in a context with no
   >   `BypassCSP`):
   >   - the document's CSP exactly equals the published string, the rest of the set is
   >     present, and there is no HSTS;
   >   - a deliberate inline script registered exactly one violation, the positive control;
   >   - set-up through the page, an account, a balance and the chart, with no further
   >     violations;
   >   - axe clean in light and dark. **`G-29` raised:** no `BypassCSP` was needed;
   >   - sign-out, then a passkey sign-in on port 8080. **`G-12` raised.**
   > - **By hand:**
   >   - `curl` shows the headers on `/`, with no `Strict-Transport-Security`;
   >   - a slice-02 import through the session and token answered `201`;
   >   - **the session survived `docker compose restart api`** (`200` before and after), so
   >     the key ring is on its volume (DoD 3);
   >   - a refused sign-in carrying `X-Forwarded-For: 203.0.113.9` was audited at the real
   >     address, `::ffff:172.18.0.1`, the bridge gateway (`C3-9`). **`G-3` raised:** every
   >     request from the host arrives from that one address;
   >   - `docker compose … run --rm api revoke-sessions` printed its line, exited 0 and left
   >     no container, and the saved session was then refused.
   > - **Gates:**
   >   - **`G-26` raised.** Over three deploys, `POSTGRES_PASSWORD` was unchanged and the
   >     Migrator connected to the volume it initialised.
   >   - **`G-27` raised.** The API's log driver is `local`.
   >   - **`G-11` partly raised.** Port 8080 refuses on this machine's LAN address
   >     (`192.168.87.248`) and answers on `127.0.0.1`. **A probe from a second LAN host is
   >     still owed.**
   > - **Found:**
   >   - **The deploy keeps its setup code.** `aspire deploy` holds generated parameters in
   >     its deployment state, `~/.aspire/deployments/<hash>/`. The current state is in
   >     `production.json.migration`; `production.json` is a legacy fallback. So every
   >     deploy writes the same code, and neither deleting it from `production.json` and
   >     `.env.Production` nor passing `Parameters__setup-code` changed it.
   >   - **The route that works,** now in the runbook: put a new `SETUP_CODE` in
   >     `.env.<Environment>`, then `docker compose … up -d api`. The new code reached the
   >     container. Never use `--clear-cache` on a stack with data: it regenerates the
   >     database password too.
   > - **Not run on the published stack:** `reset-owner`. It is shown in-process and as a
   >   process in Integration, and on the published stack it would leave no owner and need
   >   a fresh code.


1. Spike H has run. Spike I's remainder (`G-5`) and Spike J run; `G-12` runs in step 7.
   Their claims are restated at `EXECUTED`, or replaced by what was found. Spike K is cut
   (`D9`).
2. **No endpoint answers without a session except an explicit allow-list** (`C3-12`, `TST-4`).
   - There are two lists, one per environment, keyed by endpoint name, or by route pattern
     where an endpoint has no name.
     - **Production:** the health route, the anonymous state `GET`, the enrolment routes
       and the passkey sign-in routes.
     - **Development:** adds `/health` and `/alive`. `/openapi` stays behind the session
       (`R4`).
   - Static files are anonymous because they are served before authorization (step 3's
     order), as the temporary-web-root test shows.
   - The lists are asserted by request over `EndpointDataSource`, on the **unmodified**
     Production and Development factories, with no test scheme (`TST-2`). An anonymous
     request answers `401` with no `Location` header. Every name on each list must exist.
   - Every unsafe-method endpoint **off** the allow-list carries the antiforgery filter.
     Every one **on** it does not (`R1`). This is asserted from a table of method, sample
     URL, content type and valid body per endpoint, and an unsafe endpoint with no row
     fails the test (`TST-17`). A `DELETE` is shown refused by a filter-level test on a
     test-only route, since the API has none.
   - A cookie copied before sign-out still answers `200` at +30 seconds and `401` at +61
     seconds, on a pinned clock (`TST-18`).
3. Enrolment and sign-in work with a passkey:
   - in Integration, through `SoftAuthenticator`, with its hostile variants refused;
   - under `aspire run` through the Vite proxy, by the E2E with a CDP virtual
     authenticator, in Chromium only by tooling;
   - in the published stack, run by hand and recorded with date and commit.

   A session survives `docker compose restart api`.
4. **A same-site attacker with a valid session in the browser cannot write** (`C3-2`, `D12`).
   - **Integration:** with a valid session, `Sec-Fetch-Site: same-origin` and an accepted
     content type, a write whose token is missing, foreign or another user's is refused,
     and nothing is written. Layer 2 alone, with a valid token but `same-site`, answers
     `403`.
   - **Browser:** a signed-in context, attacked from another `localhost` port.
     - **Each refusal's layer, and whether the session cookie arrived, are read from the
       API's own record of the request**, not from the browser. Every refusal logs its
       layer and whether the request was authenticated, and the E2E reads those lines
       through the test builder's resource log (`G-21`, now load-bearing here).
       - The reason: Playwright sees only the status of a response the attacking page
         cannot read. It cannot read the body or the headers, and it reports no `Cookie`
         on the request. [`EXECUTED 2026-10-24` Spike J]
       - Playwright's status is still asserted alongside.
     - "Nothing written" is read from the signed-in page.
     - The cases include a `Blob`-typed `no-cors` `POST` and a `text/plain` cors `POST`
       (`C3-31`).
     - **For each same-site case the cookie is proven to have arrived**, and a `401` there
       fails the test. For the cross-site (`attacker.localhost`) cases the `Strict` cookie
       is proven **not** to have arrived (`TST-9`).
   - **Not in the browser:** a passkey sign-in from another port is refused by layer 2
     before the origin check is reached. `ValidateOrigin` is shown in Integration by
     `SoftAuthenticator`'s foreign-origin variant (`C3-23`, `TST-9`).
   - **Stated** (`D12`): the token is defence in depth behind layer 2. What it refuses on
     its own is a client that sends no `Sec-Fetch-Site`, or a write after a later change
     removes layer 2 (`SCO-16`).
5. The anonymous authentication endpoints are throttled.
   - The trip test owns its window (an hour, 3 permits) on its own factory. It asserts
     `429`, a numeric `Retry-After` and the title, and that the refused request reached no
     handler. It never asserts the reset.
   - A second test pins the production numbers from configuration (`TST-12`).
   - The page shows the title and a static "try again after" time.
6. **Headers and CSP (`C3-20`, `C3-28`, `C3-40`):**
   - Every status the API produces carries the security headers, with HSTS absent. One
     theory runs on the Production factory, over a producer per status (`TST-10`):
     - `200`: the health route;
     - `400`: malformed JSON;
     - `401`: anonymous, on the unmodified factory;
     - `403`: same-site, and token-less;
     - `404`: an unknown account;
     - `415`: a `text/plain` upload;
     - `500`: a closed-port connection string.

     A host-filter `400` is the stated exception (step 3).
   - The published page's CSP has no `unsafe-inline` and no `unsafe-eval`. It is shown by
     the built-bundle E2E, in a context **without** `BypassCSP`:
     - the document's `Content-Security-Policy` equals the published string exactly;
     - **a deliberate inline script registers one violation first**, a positive control;
     - the functional path then runs with zero further violations (`TST-11`).
   - axe runs in a **separate** context, if `G-29` needs `BypassCSP` (`SCO-12`). It reports
     0 in both colour schemes on the sign-in view, the set-up view, the throttled state,
     the expiry dialog and the signed-in page.
   - Focus and the exact status text are asserted after passkey success, failure and
     pending, the throttle, expiry, signing in again, and sign-out.
7. **No secret reaches a log, a trace or an error response** (step 5's test). `audit_event`
   holds the six event kinds, each asserted by shape and as a per-action delta, and no
   secret. It refuses `UPDATE`/`DELETE`/`TRUNCATE` from the application's code paths.
   **That is not a boundary against the connecting role**, which can remove the trigger
   (`D6`, `G-7`).
8. The pre-commit hook is installed by the documented command, refuses a staged
   secret-shaped value, commits a clean one, and still runs where `python3` is a stub. The
   CI scan is the backstop.
9. Slice 01's and slice 02's DoD items still hold. The suites are green in CI, including the
   two time-zone jobs, which are required checks on `main` since 2026-10-20.
   - The 36 Integration cases that need a session run on `SignedInClient`, and the 13 E2E
     cases on the signed-in helper (`TST-1`).
   - `CrossSiteWriteTests` is re-pinned to step 3's pipeline order, with the reason stated
     (`C3-13`).
   - The E2E-backed items (3's browser half, 4's browser half, and 6's browser assertions)
     are evidenced in the assessment with date, commit and machine (`D13`).
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
| The review, 2026-10-24 | Deferred by the dispositions | **Carried, each with its trigger** (round 2, `SCO-11`). See the list below |
| slice-02 assessment | Finer error boundaries, and a web unit-test runner | **Carried, not built.** Not this slice's technique; Kyle's call |
| slice-02 assessment | The import's plan-to-write race | **Carried.** Not this slice's technique |
| slice-02 assessment | The bundle (638 kB), the network split (`S-25a`) | **Carried** |
| Planning `D-043` | `C-389`, value-weighted coverage, discharged there | **Carried here as an open question**, Kyle's to rule, for the first slice holding real balances |

The items deferred by the review, each with the trigger that reopens it:

| Item | Trigger |
|---|---|
| The authentication method in audit (`C3-44`) | `D7`'s trigger |
| A compose healthcheck (`C3-50`) | The API leaves loopback, or a restart loop is observed |
| A per-credential nonce (`C3-39`) | `G-8` shows that platform passkeys report `signCount = 0` |
| Coverage floors for security types (`C3-48`) | The first pure security type outside a measured project |
| A `sessions` table (`D4`) | More than one session to revoke |
| A `127.0.0.1` → `localhost` redirect (`D8`) | The pre-ceremony warning is ever missed in use |
| Firefox in the E2E (`D14`) | Playwright gains a virtual authenticator for Firefox |

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
