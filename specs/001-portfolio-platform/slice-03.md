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
| Login | **Passkey (WebAuthn), with an argon2id password as the fallback**, as `security.md` specifies both |
| Scope beyond `security.md`'s "API" section | **All four additions**: security headers and a strict CSP; an audit log of authentication events; rate limiting on login; secrets kept out of logs |
| Real data | **Invented data only.** This slice does not end by admitting a real balance |
| Who builds, and the gate | **The agent builds, under the same gate** as slices 01 and 02 |

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
application end to end**, so that the API on loopback stops trusting whatever on the machine
talks to it. Today any local process, any browser bug or any extension can read and write the
financial record. Slice 02's two layers stop cross-site **writes** only.

| Layer | What gets built |
|---|---|
| **Data** | Migration 5: the user and their credentials (passkey public keys; an argon2id password hash), sessions, and an append-only `audit_event`, immutable as `ingest_run` is |
| **API** | Enrolment, login (passkey; password fallback), logout. **Every existing endpoint requires a session.** CSRF tokens on every state-changing request; rate limiting on authentication; security headers and a strict CSP on every response |
| **Web** | A login page, first-run enrolment, logout, and session expiry handled without losing the page. Every write carries the CSRF token |
| **Logging** | A redaction filter: no password, passkey assertion, session id or token in logs, traces or error responses |
| **Repository** | The pre-commit hook **installed and tested**. Today `.githooks/pre-commit` exists, but `core.hooksPath` does not survive a clone, and this clone does not have it set |
| **Tests** | Unit, integration, E2E with a virtual authenticator, and the security properties as tests, not a checklist |
| **Deploy** | The published stack runs the same tier, on `http://localhost` with no TLS |

## What the slice is not

- **No real data.** Invented balances only, as in slices 01 and 02 (the commission).
- **No credential storage for data sources.** `security.md`'s envelope encryption protects
  *connector* credentials (FR-3). No connector holds a credential yet, so it is deferred to
  the first slice that has one.
- **One user, on loopback.** No multi-user, roles or sharing.
- **No cloud.** No TLS termination, no managed identity; the second environment stays
  deferred.
- **Slice 02's interim defence stays.** Content types that force a preflight, the
  `Sec-Fetch-Site` refusal, and `server.cors: false` on the dev server all remain beside the
  CSRF tokens this slice adds. None of them is replaced.

`security.md` is a frozen reference document whose banner says it "is corrected only when a
slice is commissioned against it". **This slice is commissioned against its "API",
"Frontend", "Audit" and "Credentials → Rules" sections.** Where the build finds them wrong,
they are corrected in the same change.

---

## Step 0 — the spikes (do these first)

Lettering continues from slice 02 (E–G). Each claim below is load-bearing and sits at
`INFERRED`. The step that depends on it does not start until its spike has raised it to
`EXECUTED` or replaced it.

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

**The result decides:** whether the app must be reached only as `localhost`; whether
`127.0.0.1` leaves `AllowedHosts` or redirects; and whether the published stack needs TLS
for this slice. If it does, that is a scope change put to Kyle, not made here.

### Spike J — CSRF tokens in a single-page app, beside slice 02's two layers

> **Claims under test.**
> 1. ASP.NET Core's antiforgery works for a single-page app calling minimal APIs, using a
>    token the page reads and sends in a header.
> 2. Its validation can cover every unsafe method without breaking the raw-body upload.
> 3. **`SameSite=Strict` does not stop a same-site attacker.** A page on another
>    `localhost` port is the same *site*, so the browser sends the session cookie to the
>    API. That is the case tokens exist for; the `Sec-Fetch-Site` layer already refuses it
>    once.
>
> [`INFERRED` for all three; the third is reasoned from how browsers define a site, and
> slice 02's browser test showed `localhost`-to-`localhost` requests as same-site.]

**The result decides:** where the token comes from (an endpoint, or a cookie the page reads),
how the upload carries it, and the attack the E2E must show refused **with a valid session
present**, which slice 02's browser test could not, having no sessions.

### Spike K — does a strict CSP survive the dev server and the chart?

> **Claims under test.**
> 1. Vite's dev server injects an **inline** module script (React Fast Refresh's preamble;
>    slice 02's debug dump of the page showed it), so `script-src` without `unsafe-inline`
>    breaks development unless it is given a nonce or a hash.
> 2. Recharts sets styles through the DOM rather than in markup, which a `style-src` without
>    `unsafe-inline` allows.
> 3. The published page, served by the API from `wwwroot`, needs no inline script at all.
>
> [`INFERRED` for all three.]

**The result decides:** one policy, or a stricter published policy and a dev policy that
differs only where the dev server forces it; where each is set (the API's headers; Vite's
`server.headers`); and the E2E assertion on the published policy.

---

## The build (outline; refined after the spikes, as slice 02's was)

1. **Identity and data.** Migration 5: the user, passkey credentials, the password hash,
   sessions, and `audit_event`, append-only and refusing `UPDATE`, `DELETE` and `TRUNCATE`
   as `ingest_run` does. **First-run enrolment is an open design question for step 1.** On
   loopback, with nobody signed in, who may create the one user? A one-time setup code shown
   in the AppHost's console is the candidate. It must not leave a window in which any local
   process can claim the account.
2. **Authentication API.** Enrolment, passkey login, password fallback, logout, session
   lifetime and renewal. Every existing endpoint gains a session requirement: an
   architecture-style test asserts that no endpoint is anonymous except health, enrolment
   and login.
3. **CSRF, CORS, rate limits, headers.** Antiforgery per Spike J on every unsafe method. CORS
   grants nothing: the app is same-origin, so no origin is allowed, and a test asserts that
   no response carries `Access-Control-Allow-Origin`. Rate limiting on authentication. The
   security headers and CSP per Spike K.
4. **Web.** Login and enrolment pages, logout, an expired session sending the viewer to log
   in without losing the page, and the CSRF token on every write. **The error boundaries
   stay per section** (`edb9504`).
5. **Logging and audit.** The redaction filter, and authentication events written to
   `audit_event`, with the actor, time, subject and environment, and never a secret.
6. **The pre-commit hook.** Installed by `scripts/install-hooks.sh`, with a test that a
   staged secret-shaped value is refused. The README already names the installer; this step
   makes not installing it visible.
7. **Tests and deploy.** As below, then the published stack: login over `http://localhost`,
   the headers present, and every slice-01 and slice-02 property still holding.

## Definition of done

1. Spikes H, I, J and K run; their claims restated at `EXECUTED`, or replaced by what was
   found.
2. **No endpoint but health, enrolment and login answers without a session.** This is
   asserted over every mapped endpoint, so a new one cannot be added anonymous by accident.
3. Enrolment and login work with a passkey **and** with the password fallback, under
   `aspire run` and in the published stack. The E2E uses a virtual authenticator.
4. **A same-site attacker with a valid session in the browser cannot write.** The browser
   test, from another `localhost` port, shows the CSRF token refusing the write. It also
   shows each of slice 02's layers still refusing on its own.
5. Repeated failed logins are throttled, and the throttle is shown.
6. Every response carries the security headers. The published page's CSP has no
   `unsafe-inline` and no `unsafe-eval`, and the app works under it (axe still 0).
7. **No secret reaches a log, a trace or an error response.** Secret-shaped values are
   injected and the captured output is asserted clean. `audit_event` holds the
   authentication events and no secret, and refuses `UPDATE`/`DELETE`/`TRUNCATE`.
8. The pre-commit hook is installed by the documented command, and refuses a staged
   secret-shaped value.
9. Slice 01's and slice 02's DoD items still hold. The suites are green in CI, including the
   two time-zone jobs, which are required checks on `main` since 2026-10-20.
10. Each obligation below is done, or recorded as not done with a reason.

## Obligations this slice carries

| From | Item | Here |
|---|---|---|
| Kyle, 2026-10-18 | No real balance before the security tier | **This slice is the prerequisite.** It does not admit data |
| The commission | The four additions | **Built**: steps 3 and 5 |
| `D-030` Amendment 1 (now planning-repository history) | The pre-commit hook before the first real balance | **Built**: step 6 |
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
- Which of Spikes H, I, J and K were wrong, and what in `security.md` was corrected as a
  result?
- Which of the attacks the tier exists for were shown refused in a real browser, and which
  only argued?
- Is the platform now fit to hold a real balance? **That is a question for the assessment
  to frame, not to answer.** Admitting one is its own commission.
- **Is a fourth slice wanted at all**, and if so, which technique is it for?

A fourth slice, or a real balance, begun without that assessment written has broken the
gate.
