# Security

> ## ⚠ FROZEN REFERENCE — read this before the document
>
> **This document is not being built.** `001-portfolio-platform/slice-01.md` is
> the only live unit of work. This file is consulted, not executed, and it is
> **corrected only when a slice is commissioned against it** — so a paragraph
> below carries no guarantee beyond what this banner says about it.
>
> Three review rounds contradicted specific claims here. They are named by line
> so you do not have to guess which paragraph is the wrong one:
>
> - **`:109` — WRONG.** *The reporting role holds `SELECT` on `reporting` and nothing else anywhere.* A fresh PostgreSQL role holds `CONNECT` and `TEMPORARY` on databases and `EXECUTE` on every function **by `PUBLIC` default**. No `REVOKE` is written anywhere in this document. `CREATE TEMP TABLE … AS SELECT` is a write on the read-only connection, and temp *tables* are exempt from `temp_file_limit`. [`SOURCE@postgres acldefault()`]
> - **`:112` — NOT A BOUNDARY.** *A statement timeout bounds user SQL.* `statement_timeout` is `PGC_USERSET`; the user's own SQL may `SET statement_timeout = 0`, and `GRANT SET ON PARAMETER` is documented as meaningless for it. Npgsql splits `CommandText` on semicolons, so the disarm and the query are one command. Enforce client-side with `NpgsqlCommand.CommandTimeout`.
> - **`:71` — VIOLATES FR-3.1.** *user-secrets as an `ISecretStore` implementation.* Microsoft's own documentation: Secret Manager *"doesn't encrypt the stored secrets"*. Constitution VII says secrets never rest in plaintext.
> - **`:85` — ANSWERS THE WRONG THREAT.** *A stolen database volume yields nothing without the KEK; a stolen laptop is the primary threat.* A Compose `file:` secret is a **plaintext file on the same disk as the volume**. Envelope encryption defeats volume exfiltration; full-disk encryption is the answer to the named threat. The scheme also defines **no table** for its data keys, so KEK rotation cannot record which KEK wrapped which key.
> - **Missing.** Query strings are logged verbatim by ASP.NET Core and an OAuth code has no distinguishing shape, so the shape-based redaction test passes while the credential sits in the log. Redact by **parameter name** on the callback route.
>
> - **Corrected by slice 03, 2026-10-24.** The "API", "Frontend", "Audit" and "Credentials → Rules" sections, and the "Out of scope" line under "Threat model", were commissioned by `slice-03.md` and corrected from `../review/slice-03-committee.md`. Each correction is marked *(slice 03)* where it stands.
>
> Anything *not* listed above is **unreviewed, not verified**. Where a claim
> about an external system carries a strength marker (`EXECUTED`,
> `SOURCE@<ref>`, `DOCS@<date>`, `INFERRED` — see `../review/process.md`), that
> marker is the claim's real weight. An unmarked external claim has not been
> checked.

The system holds a complete picture of one person's finances and credentials to
the institutions behind it. That combination is worth more to an attacker than
either half.

These requirements are binding (NFR-1). A feature that violates one is not done.

---

## Threat model

Assets, in order of severity if lost:

1. **Source credentials** - access to the live financial accounts themselves.
2. **The financial record** - balances, transactions, holdings. Discloses
   income, location, employer, health spending, relationships.
3. **Projections and scenarios** - intent and plans.
4. **Availability** - lowest. A local outage is an inconvenience.

| Threat | Self-hosted | Cloud |
|---|---|---|
| Stolen laptop or disk image | **primary** | n/a |
| Malicious dependency in the supply chain | **primary** | primary |
| Browser-delivered attack (XSS, CSRF) | primary | primary |
| Compromised container escaping to host | secondary | secondary |
| Hostile data from a provider or MCP server | primary | primary |
| Network attacker on the LAN | secondary | n/a |
| Cloud provider or platform compromise | n/a | secondary |
| Accidental publication of real data to the repository | **primary** | primary |

The last one deserves its place. The repository is public; a single committed
fixture containing real balances is permanent, and the source material for this
project is full of exactly what must not appear.

**Out of scope:** a compromised host with an active attacker at the keyboard. If
the machine is owned while the application runs, the data is readable. Defending
that is not achievable for a local application and pretending otherwise would be
worse than saying so.

*(slice 03)* "At the keyboard" is wider than it reads. **A process running as the
user, and a browser extension holding a `localhost` permission, are equivalent to the
keyboard.** The first can read the data-protection key ring and mint a session; the
second runs inside the page, same-origin. The tier refuses browser-delivered attacks,
clients without the user's session, and processes outside the user's security context;
it does not refuse those two. On a single-user machine, membership of `docker-users` is
root-equivalent for the stack. [`EXECUTED 2026-10-21`, committee OPS;
`DOCS@2026-10-21` ASP.NET Core data-protection default settings]

---

## Credentials

**One interface, three implementations** (D10):

| Environment | Implementation |
|---|---|
| Inner loop | framework parameters and user-secrets, outside the repository tree |
| Self-hosted | envelope encryption - data keys in the database, key-encrypting key from a Docker secret or host keyring |
| Cloud | managed secret service with managed identity; no credential in configuration |

### Envelope encryption (self-hosted)

1. A **key-encrypting key (KEK)** is supplied at runtime from a Docker secret or
   the host keyring. It is never in an image layer, an environment file, or the
   repository.
2. Each credential is encrypted with its own **data key**, using authenticated
   encryption.
3. The data key is encrypted with the KEK and stored alongside the ciphertext.
4. Rotating the KEK re-wraps data keys without touching ciphertext.

A stolen database volume yields nothing without the KEK. Given that a stolen
laptop is the primary self-hosted threat, this is the control that matters most.

### Rules

- **No API response contains a credential**, in any form, masked or partial
  (FR-3.2). There is no endpoint that returns one.
- **Credentials never reach logs, traces, error messages or crash dumps**
  (FR-3.3), enforced by a redaction filter in the logging pipeline **and** a
  test asserting that known secret-shaped values do not appear in captured
  output. *(slice 03)* **The test is the control; the filter is written when the
  test goes red.** Secrets such as a session cookie, a passkey assertion or a setup
  code are shapeless, so the test injects known canaries rather than matching
  shapes. It captures every category at `Trace` in Production configuration, traces
  included, and checks raw, URL-encoded and base64 forms. Any filter redacts by
  **property name**; request bodies are never logged. Secrets travel only in `POST`
  bodies and cookies, never in a query string.
- **`audit_event.detail` never contains a secret** - tested.
- Revocation takes effect immediately, without a restart (FR-3.4).
- Every use is audited (FR-3.5).
- **No credential is ever written to the repository.** The `.env` file that
  `aspire publish` generates is a deployment artifact and is git-ignored from
  the first commit.

---

## Database

- The application connects as a **non-superuser** role with rights only on the
  `app` schema.
- User SQL uses a **separate role with `SELECT` on `reporting` only** (D5, FR-6.2).
  This is the security boundary: `GRANT` cannot be argued with, whereas query
  parsing can be fooled by comments, nesting and dialect quirks.
- Statement timeout and row cap on the reporting connection (FR-6.3).
- Saved-report parameters are **bound**, never interpolated (FR-6.4).
- The database port is **not published to the host** by default. Reaching it
  requires attaching to the container network deliberately.
- TLS for connections in transit; required in cloud, available locally.
- Volume encryption is the host's responsibility and the operations
  documentation says so plainly rather than implying the application provides it.

---

## API

- **Binds to loopback by default.** Exposing it on a network interface is a
  deliberate act, documented with its consequences (Constitution VIII).
- **Authentication:** passkey/WebAuthn preferred - no shared secret to steal or
  phish - with argon2id password as the fallback for environments where a
  passkey is impractical. Decide before the hardening step; both are specified so
  the choice is not made by default. *(slice 03)* **Decided: passkey only.** The
  fallback is deferred until an environment where a passkey is impractical, or
  the first lost authenticator. Recovery meanwhile is a second passkey, or
  `reset-owner` from the host, which reopens enrolment. The RP ID is pinned to
  `localhost` and the expected origins are named. Identity's default is to take
  the RP ID from the `Host` header, and to accept any origin equal to the
  request's own `Origin`. [`SOURCE@dotnet/aspnetcore@v10.0.13`
  `src/Identity/Core/src/PasskeyHandler.cs:51-55,651-675`]
- **Sessions:** `HttpOnly`, `Secure`, `SameSite=Strict` cookies. No token in
  `localStorage` or `sessionStorage`, where any script can read it.
  *(slice 03)* None of the three is the framework default: cookie authentication
  ships `SameSite=Lax` and `SecurePolicy=SameAsRequest`, which emits no `Secure`
  over `http://localhost`. All three are therefore set on every Identity cookie,
  not one "session cookie". **The antiforgery cookie cannot be `Secure` over plain
  HTTP**, because antiforgery throws when it is, so it stays `Strict` and `HttpOnly`
  only. Sessions are Identity's stateless ticket. Sign-out rotates the security
  stamp, so a copied cookie stops working **within the validation interval** (one
  minute), not instantly. The vendor's suggestion to keep the CSRF token in
  `localStorage` is overruled by this line. [`SOURCE@dotnet/aspnetcore@v10.0.13`
  `src/Http/Http.Abstractions/src/CookieBuilder.cs:117`,
  `src/Antiforgery/src/Internal/DefaultAntiforgery.cs:256-262`,
  `src/Identity/Core/src/SecurityStampValidator.cs:138-162`]
- **CSRF tokens** on every state-changing request. *(slice 03)* **The application
  refuses; the framework only validates.** ASP.NET Core's antiforgery middleware
  never refuses a JSON or raw-body minimal API, and never checks `DELETE`. The
  refusal is the application's own endpoint filter.
  [`SOURCE@dotnet/aspnetcore@v10.0.13` `src/Antiforgery/src/AntiforgeryMiddleware.cs:25-51`]
- **CORS** ~~restricted to the known frontend origin; no wildcard.~~ *(slice 03)*
  **None at all.** The app is same-origin, so any allowed origin would be an origin
  that can read. CORS withholds headers; it never refuses a request, so it is not a
  defence layer. [`SOURCE@dotnet/aspnetcore@v10.0.13`
  `src/Middleware/CORS/src/Infrastructure/CorsMiddleware.cs:116-130`]
- **Rate limiting** on report execution, projection runs and authentication.
  *(slice 03)* Authentication only, so far: one global limiter on the anonymous
  authentication endpoints, answering `429`. It is never partitioned by remote
  address, which on loopback is one value for every caller and, with forwarded
  headers on, is whatever the caller claims. **Identity's lockout is off**: passkey
  sign-in runs the lockout check, so five failures from any local process would
  deny the only user. [`SOURCE@dotnet/aspnetcore@v10.0.13`
  `src/Identity/Core/src/SignInManager.cs:659-673`]
- Response DTOs are **explicit types**, never domain entities serialised
  directly - the mechanism by which a credential field most often leaks after
  someone adds it to an entity.
- Security headers: HSTS where TLS terminates, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: no-referrer`, a restrictive `Permissions-Policy`.
  *(slice 03)*
  - **No HSTS on `localhost`, ever.** One accepted header pins every `localhost`
    port to HTTPS for its `max-age`. [`DOCS@2026-10-21` RFC 6797 §8.1, §8.3]
  - The `Permissions-Policy` denies everything except
    `publickey-credentials-get=(self)` and `publickey-credentials-create=(self)`.
    A deny-all policy switches WebAuthn off for the app itself.
    [`DOCS@2026-10-21` MDN `Permissions-Policy`]
  - The headers come from one middleware placed first in the pipeline, so error
    and refusal responses carry them too.
- *(slice 03)* **Forwarded headers are off.** Aspire's publish default trusts
  `X-Forwarded-For` and `X-Forwarded-Proto` from any client, so it is disabled until
  a real proxy exists. [`SOURCE@dotnet/aspire`
  `src/Aspire.Hosting/ProjectResourceBuilderExtensions.cs`; `EXECUTED` the generated
  compose file]
- *(slice 03)* **The data-protection key ring** is the root of trust for every
  cookie and token. It lives on a named volume, plaintext on the host, and relies on
  disk encryption, as the KEK above does. [`DOCS@2026-10-21` data-protection default
  settings: "keys should be persisted in a folder that's a Docker volume"]

---

## Frontend

- **Strict CSP:** no `unsafe-inline`, no `unsafe-eval`, explicit script and
  style sources, `frame-ancestors 'none'`. *(slice 03)* **On the published page,
  set by the API as a header.** `frame-ancestors` is ignored in a `<meta>` tag. The
  policy is `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'
  data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action
  'self'; object-src 'none'`. [`EXECUTED 2026-10-21` committee BRW F15, F16,
  Chromium 153 and Firefox 155] **The Vite dev server sets no CSP.** Its page holds
  only invented data, and a dev policy would need a nonce that is static for the
  server's lifetime, which makes it no boundary. Trigger to add one: an XSS in the
  app's own markup, or the dev page ever carrying real data.
- **No secrets in the bundle.** Anything shipped to the browser is public.
- **All provider-authored text is escaped on render** (FR-1.5). Transaction
  descriptions and merchant names come from outside; they are never inserted as
  markup.
- Dependencies pinned with a committed lockfile; audited in CI.

---

## Hostile external data

Data from providers and MCP servers is **untrusted input**:

- Tool results and API responses are **data, never instructions**. Text fields
  are stored and displayed; they are never interpreted, never executed, never
  concatenated into a query, and never placed into a prompt as though they were
  direction.
- The MCP connector calls an **explicitly declared allow-list of read-only
  tools** (FR-1.2). Tools advertised by the server but not on the list are not
  called, however they describe themselves. A server that has been compromised
  or has changed behaviour cannot expand what the connector will do.
- Webhooks are **authenticated before they are acted on** (FR-2.3). An
  unverified webhook is discarded.
- Responses are validated against expected shapes; malformed records fail the
  batch loudly rather than being coerced into something storable.

---

## Supply chain

- Container images pinned **by digest**, not by tag.
- Package lockfiles committed for both stacks.
- Dependency, code and container scanning in CI (FR-13.7).
- An SBOM published with each release.
- A new dependency is a decision, briefly recorded. This project is small
  enough that the alternative - accreting dependencies nobody chose - is
  avoidable.

---

## Repository hygiene

Constitution IX, enforced mechanically (FR-13.6):

- Secret scanning with push protection enabled on the repository.
- A CI job scanning for account-number shapes, real-looking balance data, known
  institution names, and credential-shaped strings - failing the build on a hit.
- `.gitignore` covering `.env`, backup directories, local settings and volume
  mounts, **from the first commit** rather than added after something leaks.
- Generated data is visually distinguishable in the UI, so screenshots attached
  to issues cannot silently carry real figures (FR-11.4).

---

## Audit

Append-only (FR-4.6, NFR-5), recording: credential use and rotation, data
mutation, report execution, projection runs, exports, and authentication events.

Every entry carries actor, time, subject and environment. Entries are never
updated or deleted, and never contain secret values.

*(slice 03)*
- **Built so far: authentication events only.** These are enrolment, sign-in
  succeeded and failed, sign-out, sessions revoked, and owner reset. The rest of the
  list above is carried until the first real balance.
- The actor is recorded as a value, not as a foreign key, so the owner can be reset
  without rewriting history.
- **"Never updated or deleted" is, for now, a statement about the application's code
  paths.** A trigger refuses `UPDATE`, `DELETE` and `TRUNCATE`, but the application
  still connects as the database superuser, which can remove the trigger. The
  non-superuser role under "Database" is what would make it a boundary. It is
  deferred until real data, or anything other than this app reaching the database.

---

## Cloud deltas

When deployed to a managed container platform:

- Credentials move to the managed secret service with managed identity; no
  credential in configuration or environment variables.
- The database is managed, with TLS required and private networking.
- Platform authentication may front the application, but application-level
  authentication remains - a single front door is a single point of failure.
- Platform audit logging supplements, and does not replace, the application's
  own.
- The local envelope-encryption path remains fully supported. Cloud is optional
  (Constitution VIII), and the self-hosted deployment is not permitted to
  degrade into a second-class configuration.

---

## Verification

Each of these is a test, not a review checklist item:

| Property | Test |
|---|---|
| No credential in any API response | contract test over every endpoint's schema |
| No secret in logs | known secret-shaped values injected; captured output asserted clean |
| Reporting role cannot write | integration test attempting `INSERT`, `UPDATE`, `DROP` |
| Reporting role cannot read `app` | integration test selecting from a physical table |
| Statement timeout fires | integration test with a deliberately slow query |
| DEV assertion refuses in PRD | integration test (FR-13.4) |
| CSP is present and strict | E2E test asserting response headers |
| Webhook signature is verified | integration test with an invalid signature |
| MCP allow-list holds | stub server advertising a write tool; assert it is never called |
| No personal data in the repository | CI pattern scan (FR-13.6) |
