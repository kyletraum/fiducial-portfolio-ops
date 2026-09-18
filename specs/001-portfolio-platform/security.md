# Security

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
  output.
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
  the choice is not made by default.
- **Sessions:** `HttpOnly`, `Secure`, `SameSite=Strict` cookies. No token in
  `localStorage` or `sessionStorage`, where any script can read it.
- **CSRF tokens** on every state-changing request.
- **CORS** restricted to the known frontend origin; no wildcard.
- **Rate limiting** on report execution, projection runs and authentication.
- Response DTOs are **explicit types**, never domain entities serialised
  directly - the mechanism by which a credential field most often leaks after
  someone adds it to an entity.
- Security headers: HSTS where TLS terminates, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: no-referrer`, a restrictive `Permissions-Policy`.

---

## Frontend

- **Strict CSP:** no `unsafe-inline`, no `unsafe-eval`, explicit script and
  style sources, `frame-ancestors 'none'`.
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
