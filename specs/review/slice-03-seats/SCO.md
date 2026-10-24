# Seat: Scope realist

## Method

Read, in this order and nothing under `specs/review/` or the committee folder:
`specs/001-portfolio-platform/slice-03.md` (the subject), `slice-02.md` in full (the finished
precedent, especially "Cross-site writes" and steps 3 and 5), `slice-02-assessment.md`,
`slice-01.md` (the banner's middle cut, the DoD, the gate), `slice-01-assessment.md` §1–2,
`security.md` (what the slice is commissioned against), `LOG.md` (all three tables),
`spikes/spike-h-auth/RESULTS.md` and `SpikeH.Web/Program.cs` (113 lines: what Identity's
passkey path costs in code), `README.md` "Before you commit anything", `scripts/hygiene.py`,
`scripts/install-hooks.sh`, `.githooks/pre-commit`, `.github/workflows/ci.yml`. Then the
code the slice must change: `src/Api/Program.cs`, `Endpoints.cs`, `ImportEndpoints.cs`,
`CrossSiteWrites.cs`, `src/AppHost/AppHost.cs`, `src/ServiceDefaults/Extensions.cs`,
`web/vite.config.ts`, `web/src/App.tsx`, `web/src/api/queries.ts`, `web/package.json`. Then
the tests the session requirement will touch: every file under `tests/`, with a grep for which
classes go through `WebApplicationFactory` or the browser. `git log --date=iso` for the
slice-02 step timings. No app was run; no spike was re-run.

Claim-strength markers appear only on claims about frameworks or browsers. Everything else is
read from this repository and carries none.

Test inventory used below (counted from `[Fact]`/`[Theory]` and the assessments' totals):
unit 119, integration 77 (5 classes go through the HTTP host: `ImportApiTests` 12,
`CrossSiteWriteTests` 4 theories ≈ 16 cases, `BalanceIdempotencyTests` 2, `ClockTests` 2,
`HostFilteringTests` 7 cases on the health route), E2E 14 in 7 classes (6 drive the browser;
`CrossSiteBrowserTests` also uses a raw `HttpClient` against the API), architecture 52,
contract 1.

## Findings

Defects only: a contradiction, an item that cannot be done as written, a DoD that cannot be
met. Size opinions are in Axis 2.

| ID | Severity | Finding | Rests on | What to do | Where |
|---|---|---|---|---|---|
| SCO-1 | MUST FIX | **DoD 4 cannot be met in a browser as written.** It asks the browser test to show "the CSRF token refusing the write" from another `localhost` port, with slice 02's two layers kept ("none of them is replaced"). But with those layers in place no same-site write ever reaches an antiforgery check: a `fetch` carrying JSON or `text/csv` is preflighted and never sent (slice 02 DoD 11 showed exactly this); a form post or a `no-cors` fetch arrives as a form content type and is `415` by routing, or `403` by `UseCrossSiteWriteRefusal`, which runs before routing. Whichever layer is first in the pipeline is the only one a browser can ever see refuse. "Shows each of slice 02's layers still refusing on its own" in the same browser run has the same problem in reverse | `src/Api/Program.cs` (middleware order), `CrossSiteWrites.cs`, `tests/E2E/CrossSiteBrowserTests.cs` outcomes (`csv`/`json` blocked, `form`/`nocors` 403), slice-03 Spike J claim 3 | Reword DoD 4: the token's refusal **with a valid session present** is shown in integration (session cookie on the client, an accepted content type, no `Sec-Fetch-Site`, no token → refused, nothing written), where `CrossSiteWriteTests` already shows the other two on their own. The browser test shows the three **together** from another port with a signed-in session, and records which layer answered. If a per-layer browser demonstration is wanted, say so and add a test-only switch that turns the other layers off, with its cost | `slice-03.md` DoD 4, Spike J "The result decides" |
| SCO-2 | SHOULD FIX | **Step 1's enrolment candidate contradicts DoD 7 and does not exist in the published stack.** "A one-time setup code shown in the AppHost's console" is a secret-shaped value written to a log, which DoD 7 asserts never happens; the no-secret test either fails on it or carves out an exception nobody wrote down. And DoD 3 requires enrolment "in the published stack", which has no AppHost and no dashboard (`AppHost.cs`: `WithDashboard(false)`; the stack is `docker compose`), so there is no AppHost console to read it from | `slice-03.md` step 1, DoD 3, DoD 7; `src/AppHost/AppHost.cs`; `README.md` (the generated `.env` already carries the database password) | Name the design now rather than leaving it open: the setup code is an Aspire **secret parameter** (`builder.AddParameter(..., secret: true)`: user-secrets under `aspire run`, the git-ignored `.env` at publish, the channel the database password already travels), accepted once by the enrolment endpoint while no user exists, never logged. If a printed code is kept, it is printed by the **API** process (readable as `docker compose logs api`) and DoD 7 names the exception explicitly | `slice-03.md` step 1 and DoD 7 |
| SCO-3 | SHOULD FIX | **The build order leaves the suites red for two steps.** Step 2 makes every existing endpoint require a session; the login page is step 4. Between them, under `aspire run`, the page cannot load data; 4 integration classes that go through the HTTP host (≈32 cases) and all 6 browser-driven E2E classes fail unless each step ships its own test helper. Slices 01 and 02 merged every step green (PRs #54–#60) and DoD 9 requires the old DoD items to keep holding | Test inventory above; `tests/Integration/ImportApiTests.cs` `ApiTests` base (one `Client` for 4 classes); `tests/E2E/AppFixture.cs` (one app per class, fresh database, so enrolment is per class) | State in step 2 that it ships the integration session helper (enrol + log in inside `ApiTests`, cookie and token on `Client`) in the same change, and that the session requirement on the **existing** endpoints is switched on in step 4 with the login page and the E2E helper, or that the E2E is accepted red locally until step 4. Either is fine; the document should pick one | `slice-03.md` "The build" 2 and 4 |
| SCO-4 | NOTE | **DoD 2's "health, enrolment and login" is three words for six to eight routes.** Passkey login is two round trips (request options, assertion) and enrolment two (creation options, attestation), the password login a fifth, plus health, plus a CSRF-token route if Spike J picks an endpoint rather than a cookie, plus `/openapi`, `/health` and `/alive` in Development. The login page itself comes from `UseFileServer`, which is not an endpoint at all | `spikes/spike-h-auth/SpikeH.Web/Program.cs` (four passkey routes); `src/Api/Program.cs`; `src/ServiceDefaults/Extensions.cs` `MapDefaultEndpoints` | Write DoD 2 as "an explicit allow-list of route names, asserted over `EndpointDataSource`", and say static files are outside it. Otherwise the architecture-style test cannot be written to the DoD's words | `slice-03.md` DoD 2, step 2 |
| SCO-5 | NOTE | **Spike I names Firefox; nothing in the repository can run it.** `AppFixture` installs and launches Chromium only. Claim 3 can be raised to `EXECUTED` only by a manual run that no test repeats | `tests/E2E/AppFixture.cs`; Spike I claim 3 | Drop Firefox from the claim, or record it as a one-off manual `EXECUTED` with the date and say the suite does not hold it | `slice-03.md` Spike I |
| SCO-6 | NOTE | **Spike I's "whether `127.0.0.1` leaves `AllowedHosts`" would change a slice-01 property DoD 9 says must still hold.** `HostFilteringTests` asserts `127.0.0.1` and `[::1]` are served (slice-01 SEC-7) | `tests/Integration/HostFilteringTests.cs` | Keep `AllowedHosts` as it is. The browser already refuses the ceremony on an IP origin (Spike H), so the login page says "open as `localhost`" and nothing redirects. If the host list does change, DoD 9 must name the exception | `slice-03.md` Spike I "The result decides" |
| SCO-7 | NOTE | **DoD 5 does not say what a throttle partitions on, and on loopback the usual answer is empty.** Every caller is `127.0.0.1`; under `aspire run` the API sees the Vite proxy. A client-address limiter would pass a test and distinguish nothing | `src/AppHost/AppHost.cs` (loopback publish); `web/vite.config.ts` (proxy) | Say "per account" (Identity's lockout, in the box) or "global", and show that one. See Axis 2 for which | `slice-03.md` DoD 5, step 3 |

## Gates

Which `INFERRED` claims still gate a step, after Spike H's run and slice 02's findings:

- **Spike H:** done. Nothing gates.
- **Spike I:** claims 1 and 2 were **observed by Spike H** (`isSecureContext` true on both origins; the IP origin refused with `SecurityError`). Restate them at `EXECUTED` from H's run; do not re-run. What still gates step 2: claim 3 for **Chromium** (a `Secure` cookie over `http://localhost`) and claim 4's **Vite-proxy** half (the ceremony and the cookie through `changeOrigin: true`, where the API sees its own `Host` but the browser's `Origin`; Identity validates the origin of the client data [`INFERRED`, `IdentityPasskeyOptions` names origin validation per H's reading of the reference]). The published-stack half of claim 4 is same-origin on one port and is step 7's check, not a spike. Firefox: SCO-5. Size: about an hour.
- **Spike J:** claim 3 is **already `EXECUTED` by slice 02's browser test** (`localhost`-to-`localhost` arrived marked `same-site`; the Sec-Fetch-Site layer refused it). Restate it; do not re-run. Claims 1 and 2 gate step 3 in one respect only: **where the token comes from** and that validation runs on the raw-body upload without a form reader [`INFERRED`: ASP.NET Core's antiforgery validates a header token against its cookie, and minimal APIs opt an endpoint in with metadata; the raw body is never read by it]. Size: about an hour, and SCO-1 decides what its E2E can show.
- **Spike K:** claim 1 (Vite's inline Fast Refresh preamble) gates nothing **if there is no dev-server CSP** (Axis 2, item 6). Claims 2 and 3 are the published policy: build it, run the bundle under it, run axe. That is step 3 and step 7 work, about an hour, not a separate spike.

The one claim that is load-bearing and nobody has run: **the passkey ceremony through the Vite dev proxy with Identity's origin validation**. If it fails, the fix is configuration on `IdentityPasskeyOptions`, not a design change; but it is the place a day can go.

## Axis 2 — scope, buildability, teaching value

No severities here. These are opinions about size, with the reason each time.

### Item by item (scope and DoD)

The question for each: does a single-user loopback app with invented data need it **in this
slice** to satisfy the ruling and carry the technique, what is the smallest version, and what
does slice 02 already cover.

| Item (scope / DoD) | Needed here? | Smallest version that carries the technique | Already covered by slice 02? |
|---|---|---|---|
| Migration 5: user, passkey credentials, sessions | Yes: it is the technique | Identity's `Version3` store inside `PortfolioDbContext` (`IdentityUserContext<TUser>`, no roles). One EF migration on Npgsql, the thing Spike H did not run. No separate `sessions` table: the session is the cookie (ASP.NET's data-protected ticket); a table only matters for revocation of other sessions, which one user on one machine has none of | No |
| Password hash column, Geralt hasher | Only because the ruling says so | A custom `IPasswordHasher<TUser>` of about 30 lines, parameters as measured | No. See "The password fallback" |
| `audit_event`, append-only | Cheap, and `security.md` "Audit" is in the commission | One table, one trigger **copied from migration 4's `ingest_run` guard**, five event kinds (enrol, login ok, login failed, logout, lockout). The test that it refuses `UPDATE`/`DELETE`/`TRUNCATE` is a copy of the `ingest_run` one | The pattern, yes; the table, no |
| Enrolment, login, logout endpoints | Yes | Six routes (SCO-4). Spike H's 113-line program is most of it | No |
| **Every existing endpoint requires a session** | Yes: without it the slice secures nothing | `RequireAuthorization()` on the `/api/v1` group with the allow-list carved out, cookie events answering `401`/`403` (Spike H). The architecture-style test over `EndpointDataSource` is ~20 lines | No. SCO-3 for the order |
| CSRF tokens on every unsafe method | Ruled. Note what it adds: a **third** refusal of the same request slice 02 refuses twice | Cookie-plus-header double submit with ASP.NET antiforgery; the `openapi-fetch` client gets one `onRequest` middleware that copies the header; the raw-body upload already uses `fetch` and adds one header. ~40 lines and two tests | **Largely.** Layer 1 (content types) and layer 2 (`Sec-Fetch-Site`) already refuse every cross-site and same-site write, each tested alone and both in a browser. The token's one independent contribution is against a browser that omits `Sec-Fetch-Site` (none of the three engines does today [`INFERRED`]) |
| "CORS grants nothing" | There is nothing to build: the API has no CORS middleware and never answered a preflight, which is why slice 02's preflighted writes were never sent | **One test**: no response carries `Access-Control-Allow-Origin`. That is the whole item. Correct `security.md`'s "restricted to the known frontend origin" line in the same change | **Yes**, including the dev server (`server.cors: false`, found by the browser test) |
| Rate limiting on authentication | Only the password path can be guessed online; a passkey assertion cannot be brute-forced [`INFERRED` from the ceremony being a signature over a server challenge]. On loopback there is no address to partition on (SCO-7) | Identity's **lockout** (`MaxFailedAccessAttempts`, `DefaultLockoutTimeSpan`, in the box, per account) and one **concurrency** limiter of 1 on the password route, because each argon2id hash holds 64 MiB inside a 512 MB container (Spike H §3). Together they are the throttle, and DoD 5's "shown" is one integration test of six wrong passwords | No |
| Security headers | Yes, cheap | One middleware, ~15 lines: `nosniff`, `Referrer-Policy`, `Permissions-Policy`, `frame-ancestors 'none'`, the CSP. No HSTS: no TLS | No |
| Strict CSP, **published** page | Yes: it is the one frontend security property in the commission that can be tested | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'`, set by the API (which serves `wwwroot`). Shown by the existing axe call and the E2E against the built bundle | No |
| Strict CSP, **dev** server | No. The dev page is served by Vite, the API's headers never reach a script tag there, and the dev page holds invented data by rule | None. Document that the dev server sets no CSP and why. Spike K claim 1 then gates nothing | n/a |
| Web: login page, enrolment page, logout | Yes | One form with a "Use a passkey" button (and, while the ruling stands, a password field); enrolment is the same form in setup mode when `/me` says no user exists. Logout is a button | No |
| Web: "session expiry handled without losing the page" | Overspecified for this app | There is one page and no router (`App.tsx`). "Losing the page" can only mean losing unsent form input. Smallest: a `401` from any query sets a `signedOut` flag, the login form renders **in place** above the sections, and TanStack Query refetches on success. Sliding expiry is Identity's default; renewal UX is nothing | n/a |
| Web: CSRF token on every write | Ruled; one middleware on the client | See CSRF row | n/a |
| Redaction filter | **The test, yes; the filter only if the test fails.** ASP.NET Core does not log request bodies; the known leak shapes are query strings and exception messages (`security.md` banner, "Missing") | Write the DoD 7 test first: a marker password and a marker session cookie value injected through enrolment, login, a wrong password, a thrown exception and a `413`; capture logs, traces and error bodies; assert clean. If green, there is no filter to write and the technique is "proved by trying to break it" (slice 02's seventh technique). If red, the filter is sized to what leaked | No |
| Pre-commit hook installed and tested | Yes: an obligation with its trigger met ("before the first real balance"; this is the prerequisite slice) | `install-hooks.sh` exists. The test is a CI step: `git init` a temp repo, stage a file holding a fake `AKIA…` key, run `.githooks/pre-commit`, assert exit 1. ~15 lines of shell. Slice 02 did the equivalent for range mode "on a throwaway repo" and did not keep it | Half: the scanner's `--staged` mode exists; nothing exercises it |
| Tests: E2E with a virtual authenticator | Yes | Spike H's CDP authenticator, lifted into `AppFixture` | No |
| Deploy: the published stack, login over `http://localhost` | Yes | A Playwright script against `docker compose` for the passkey (there is no curl for a passkey), the headers with curl, the slice-01/02 regression as slice 02 did it | Partly: slice 02's step-7 checks are repeated, not rebuilt |
| DoD 1 (spikes) | See "The spikes" | I and J shrink to one hour each; K folds into steps 3 and 7 | |
| DoD 3 "with a passkey **and** the password fallback, under `aspire run` and in the published stack" | 2 × 2 = 4 demonstrations, two of them of a path this app has no environment for | Passkey under the test builder (the E2E) and in the published stack (the script). The password path, while ruled in, in integration only | |
| DoD 6 "every response carries the security headers" | Yes for the API's responses; the dev server's are Vite's | Assert on API responses, including `wwwroot` files in Production. Say the dev server is out of it | |
| DoD 9 (old DoD items hold) | Yes | Unchanged. SCO-3 and SCO-6 are the two places it is at risk | |
| DoD 10 (obligations) | Paperwork, and correct paperwork | Unchanged | |

### The password fallback

**What it costs.** Counting only what exists because of it:

- a native dependency (Geralt wraps libsodium; it loaded on Windows and in the Linux container,
  so the risk is small, but it is the slice's only new runtime dependency);
- a custom `IPasswordHasher<TUser>`, its parameters, and the `NeedsRehash` wiring;
- a password at enrolment: a second field, a strength rule, a confirmation, and its own
  `422`s;
- a second login form and a second login route;
- **the throttle exists for it.** A passkey login has nothing to guess; Identity's lockout
  and the concurrency limiter are there because a password can be tried;
- the 64 MiB per hash inside the 512 MB limit, which is why the concurrency limiter is not
  optional;
- the redaction test's main subject. Without a password the only secrets in flight are the
  session cookie and the setup code;
- the audit events for failed attempts and lockouts;
- DoD 3's second column: the password path shown under both stacks;
- and the one that is not hours: **a secret at rest.** With a password hash in `app_user`, a
  stolen disk (the threat model's **primary** self-hosted threat) holds something crackable
  offline. argon2id at 64 MiB slows that; it does not make a weak password strong. With a
  passkey only, the private key is in the platform authenticator and the database holds a
  public key. The fallback weakens the posture against the threat `security.md` ranks first.

Roughly **2–3 of the slice's measured hours**, and the only item whose failure modes are all
its own.

**What a passkey-only app on one machine loses.** Recovery when the platform credential is
gone: an OS reinstall, a profile loss, a cleared TPM. `security.md` specifies the password
"for environments where a passkey is impractical"; this environment is one machine with a
platform authenticator and a browser that just registered one (Spike H). The loss is real but
has two cheaper answers:

1. **A second passkey**, which Identity's `UserManager` already supports (`AddOrUpdatePasskeyAsync`
   per credential; Spike H §1): a hardware key as the backup. Same technique, no new code
   path, no secret at rest.
2. **Host-side re-enrolment**: the same setup parameter that enrols the first user (SCO-2)
   re-arms enrolment when the operator sets it again. Anyone who can do that can also read
   the `.env` with the database password, which the threat model already calls game over
   ("a compromised host with an active attacker at the keyboard" is out of scope).

**The ruling stands**, so this seat does not strike it. It names it as the one item worth
putting back to Kyle with the figure above: *defer the password fallback to the first
environment where a passkey is impractical, and take a second passkey as the recovery path.*
If Kyle keeps it, the smallest version is the one in the table: hasher, lockout, concurrency
1, integration-tested, and not demonstrated in the browser under both stacks.

### First-run enrolment

The open question in step 1, answered as small as it goes:

- **Who may create the one user?** Whoever holds the setup secret. It is an Aspire parameter
  marked secret (SCO-2): user-secrets under `aspire run`, the git-ignored `.env` at publish.
  Both channels already exist and already carry the database password, so nothing new is
  trusted.
- **The window.** Enrolment is accepted only while `app_user` is empty **and** the request
  carries the secret. A local process that races the first start has nothing to send. There
  is no console to read and no log line to redact.
- **After enrolment** the route answers `409` forever, until the operator re-arms it (the
  recovery path above). The architecture test's allow-list carries it as anonymous, which is
  honest: it is anonymous, and gated by a secret rather than a session.
- **The "setup code in the AppHost console" idea** is more than the slice needs and less than
  it can use: it needs a startup check, a generator, a log line the redaction test must
  exempt, and it is unavailable in the published stack. Cut in favour of the parameter.

About twenty lines of API and one Aspire parameter. The E2E's `AppFixture` sets the
parameter on the test builder and enrols once per class.

### The spikes

| Spike | Already answered | Still a gate | Size |
|---|---|---|---|
| H | All three claims, run | Nothing. The open store-shape question is a step-1 decision, and `IdentityUserContext<TUser>` is the smaller one | 0 (0.9 h logged) |
| I | Claims 1 and 2 by H's run. Claim 4's published half is same-origin and belongs to step 7 | Claim 3 in Chromium; claim 4 through the Vite proxy (the one unrun load-bearing claim) | ~1 h |
| J | Claim 3 by slice 02's browser test | Where the token comes from, and that it validates on the raw-body upload. SCO-1 decides what the E2E can show | ~1 h |
| K | Nothing yet, but claim 1 gates nothing without a dev CSP | Claims 2 and 3, which are the published policy itself: build it, run under it. Not a spike | folded into steps 3 and 7 |

The spikes in slice 02 cost 1.7 h logged for three. Here: about 2 h for I and J, if K is
folded.

### Cost, step by step

Slice 02: 9.4 h logged, ~5.2 h measured, 13 rows. Slice 03's commission and Spike H have
already logged 1.3 h. The estimate below is in **logged** agent hours at slice 02's ratio
(measured is roughly 55% of logged), for the scope **as written**, then for the middle cut.

| Step | As written | Where it balloons | Middle cut |
|---|---|---|---|
| 0 Spikes I, J, K | 2.5 | Identity's origin validation through the proxy | 2.0 |
| 1 Identity, migration 5, `audit_event`, hasher, enrolment | 3.0 | Identity's model under `UseSnakeCaseNamingConvention` and the Designer snapshot; the Migrator applying `Version3` on Npgsql, which Spike H did not run (`EnsureCreated` only) | 2.0 (no hasher, no password column, parameter enrolment) |
| 2 Auth API, **every endpoint requires a session**, the allow-list test | 3.0 | **The test rework**: `ApiTests` for 4 integration classes, `AppFixture` for 6 browser classes plus the raw `HttpClient` in `CrossSiteBrowserTests`, and `ClockTests` which builds its own factory. This is the step most likely to double | 2.5 |
| 3 CSRF, the CORS test, throttle, headers, published CSP | 2.0 | SCO-1, if a per-layer browser demonstration is attempted | 1.5 (no limiter beyond lockout; one CORS test) |
| 4 Web: login, enrolment, logout, expiry, token | 3.0 | "Without losing the page" if read as state preservation; the password form | 2.0 |
| 5 Redaction and audit | 1.5 | A filter written before the test shows a leak | 1.0 (test first) |
| 6 Hook | 0.5 | | 0.5 |
| 7 Published stack, both paths, regression | 2.0 | A Playwright script against compose for the passkey; the password path again | 1.5 |
| Assessment | 0.5 | | 0.5 |
| **Total, this session onward** | **~18 h logged, ~10 h measured** | | **~13.5 h logged, ~7.5 h measured** |

Either way the slice is about **1.5–2× slice 02**, which is what "touch every existing test
and every existing endpoint" costs. The two places it most likely balloons: **step 2's test
rework** (every suite learns to log in) and **step 4's web** (two credential paths, enrolment
mode, expiry). Both shrink with the cut.

### The middle cut I would propose

**KEEP — because it is the technique:**

- Passkey enrolment and login on Identity's in-box support, no WebAuthn library (Spike H).
- The session cookie: `HttpOnly`, `Secure`, `SameSite=Strict`, `401`/`403` and never a
  redirect.
- **Every existing endpoint requires a session**, with the allow-list test over
  `EndpointDataSource` (SCO-4), switched on with the login page (SCO-3).
- First-run enrolment by a secret parameter, no window (SCO-2).
- CSRF token on every unsafe method (ruled), shown in integration with a session present;
  the three layers together in the browser (SCO-1).
- Security headers on every API response and a strict CSP on the **published** page, shown
  by axe and the E2E against the built bundle.
- `audit_event`, append-only by the migration-4 trigger pattern, five event kinds.
- The DoD 7 test: marker secrets injected, logs, traces and error bodies asserted clean.
- The pre-commit hook installed by the documented command and a CI step that shows it
  refusing a staged secret-shaped value.
- The published stack: passkey login over `http://localhost`, headers present, slice-01 and
  slice-02 properties re-checked.
- Spikes I (proxy and Chromium cookie) and J (token source), and the `EXECUTED` restatements
  of what H and slice 02 already showed.

**DEFER — each with its trigger:**

- **The argon2id password fallback** (Kyle's to re-rule; this is the one this seat would most
  want deferred). Trigger: an environment where a passkey is impractical, in `security.md`'s
  own words, or the first lost-authenticator event. Until then, a second passkey is the
  recovery credential and host-side re-enrolment the last resort. Keep Spike H's argon2id
  measurement as the record for when the trigger fires.
- **Rate limiting beyond Identity's lockout.** Trigger: the password path exists (then a
  concurrency limiter of 1, for the 64 MiB), or the API is ever bound off loopback (then a
  partition key exists).
- **The redaction filter**, as code. Trigger: the DoD 7 test goes red. The test stays.
- **A dev-server CSP.** Trigger: an XSS found in the app's own markup, or the dev page ever
  carrying real data (never, by rule). Spike K claim 1 goes with it.
- **Session "renewal" and "without losing the page" as state preservation.** Trigger: a
  router or a second page exists. Today: the login form in place and a refetch.
- **A `sessions` table.** Trigger: more than one session to revoke. Today the cookie is the
  session.
- **Firefox in Spike I.** Trigger: Kyle uses Firefox for this app.
- **A `127.0.0.1` → `localhost` redirect.** Trigger: someone actually opens it that way and
  the login page's one-line hint is not enough.
- **Envelope encryption, roles, multi-user, TLS**: already deferred by the slice; agreed.

**CUT — with the reason:**

- **"CORS" as a build item.** Nothing exists to restrict; the API has no CORS middleware and
  never answers a preflight, and slice 02 closed the dev server's. One assertion that no
  response carries `Access-Control-Allow-Origin` is the whole of it, plus the `security.md`
  correction.
- **Re-running Spike I claims 1–2 and Spike J claim 3.** Already `EXECUTED` by Spike H and by
  slice 02's browser test. Restate with the dates; do not spend an hour observing them again.
- **Spike K as a spike.** Its two live claims are the published policy; build it in step 3
  and show it in step 7.
- **The AppHost-console setup code.** Replaced by the parameter (SCO-2); it contradicts
  DoD 7 and has no console in the published stack.
- **DoD 4's "each of slice 02's layers still refusing on its own" in the browser.** They are
  already shown alone in `CrossSiteWriteTests` and cannot be shown alone in a browser that
  keeps all three (SCO-1). The browser shows them together.
- **DoD 3's four-way matrix.** The passkey under both stacks is the demonstration; the
  password path (while it exists) in integration only.
- **Pre-step-1 paperwork beyond the corrections to `security.md`'s four named sections**: the
  same cut slice 01 made, for the same reason (the documents are frozen anyway).

What remains is one technique carried through every layer once, as the slice's own banner
asks: *the API on loopback stops trusting whatever on the machine talks to it*, shown in a
real browser with a virtual authenticator, in the published stack, with every old property
still green.
