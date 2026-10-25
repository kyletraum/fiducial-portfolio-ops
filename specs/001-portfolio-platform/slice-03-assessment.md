# Slice 03 — the assessment

**Status: AWAITING KYLE (§5 and §6).** Written 2026-10-25 against `main` at `9694f0b` (PR #85
merged). The default at the end of this slice is **STOP** (`slice-03.md`, "The gate").

**Who wrote which part.** Sections 1–4 and 7 are facts, assembled by the agent session that
did the build. Each can be checked against a commit, a test run or `LOG.md`, and none
carries a recommendation. Sections 5 and 6 are **Kyle's**: the gate asks what this taught
*Kyle*, which no one else can answer. The agent lays out candidates and evidence there and
decides nothing; every box is unticked.

---

## 1. Definition of done

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | Spikes H, I and J run; their claims restated at `EXECUTED` or replaced. K cut (`D9`) | **Met** | H `fe39229`, I `f0e3b3e`, J `d27dfca`; `G-12` raised on the published stack in step 7b. §3 has what each found |
| 2 | Only an explicit allow-list answers without a session; every unsafe endpoint off it carries the CSRF filter; a cookie copied before sign-out is refused after the validation interval | **Met** | `AllowListTests` (`fc8975d`), by request over `EndpointDataSource` in Production and Development, with a body table. Seen failing: making `ListAccounts` anonymous fails both environments. The copied-cookie test, `200` at +30 s and `401` at +61 s on a pinned clock (`565016f`) |
| 3 | Enrolment and sign-in with a passkey, in Integration, under `aspire run` through the Vite proxy, and in the published stack; a session survives `restart api` | **Met** | Integration through `SoftAuthenticator`, with its hostile variants refused (`565016f`). E2E through the Vite proxy (`fc8975d`). Published: `PublishedStackTests` and the checks by hand in step 7b (`9694f0b`). Chromium only, by tooling (`C3-49`) |
| 4 | A same-site attacker with a valid session cannot write | **Met** | **Integration:** the token alone, missing, foreign, another user's or after sign-out, on JSON, `PUT`, `DELETE` and raw bodies (`CsrfFilterTests`, `2b8f6ae`); layer 2 alone (`CrossSiteWriteTests`, unchanged). **Browser:** both attacker origins, against both targets, with which layer refused and whether the cookie arrived **read from the API's own record** (`4ee95d0`). A passkey from another port is refused by `ValidateOrigin` (Spike I; `SoftAuthenticator`'s foreign-origin variant). The token's place behind layer 2 is stated (`D12`) |
| 5 | The anonymous authentication endpoints are throttled, and the page shows it | **Met** | The trip test in an owned window (one hour, 3 permits), and the production numbers pinned (`2b8f6ae`). The page shows the title and a static "try again after HH:MM" (`9694f0b`) |
| 6 | Headers on every status; the published CSP strict and shown by the built bundle; axe 0 in both schemes on every new view; focus and text asserted | **Met; the CSP half by hand** | **Headers:** every status from `200` to `500`, with no HSTS (`2b8f6ae`, `fc8975d`). **CSP:** exact header and a positive control on the built bundle, run by hand against `aspire deploy` (`9694f0b`, `D16`). **axe:** both schemes on sign-in, throttled, signed-in, the expiry dialog and set-up (`9694f0b`). **Focus and status:** after each outcome (`fc8975d`) |
| 7 | No secret in a log, a trace or an error body; `audit_event` holds the six kinds and no secret; immutability stated as code-path only | **Met, with one note** | `SecretsStayOutTests` (`1cb41da`) went red once and was fixed (§3). **The six kinds:** each asserted as a per-action delta, the four ceremony kinds in `AuthApiTests` (`565016f`) and the two command kinds in `OwnerCommandsTests` (`4ee95d0`). **Immutability:** the trigger refuses all three rewrites (`56488f8`), and DoD 7 and `security.md` say it binds code paths, not the connecting role (`D6`). **The note:** a row's *shape* is asserted for one kind in Integration (`AuthSchemaTests`), and the published stack's rows show actor and address for the four ceremony kinds. There is no shape assertion per kind |
| 8 | The hook installed by the documented command; refuses a secret, commits a clean file, survives a failing `python3` | **Met** | `PreCommitHookTests` (`9364c2e`). Seen failing: all three cases against the old hook. Installed in this clone |
| 9 | Slice 01's and slice 02's DoD hold; CI green, time-zone jobs included; E2E-backed items evidenced | **Met, with one note** | CI was green at merge on every PR from #73 to #85 (#81 had one red run, a merge duplicate, fixed before it merged). **E2E** was green locally, 27 tests plus the by-hand published one, on 2026-10-24 at the tree `9694f0b` records (Windows 11, Docker Desktop, Playwright Chromium 1.63). The published run re-checked an account, a balance, the chart and an import. **The note:** `CrossSiteWriteTests` passed unchanged, because layer 2 runs before authentication, so its expected values did not move. No comment was added to it recording why, as the DoD's "re-pinned with the reason stated" asked |
| 10 | Each obligation done, or recorded as not done with a reason | **Met** | Below |

**DoD 10, obligation by obligation** (`slice-03.md`, "Obligations this slice carries"):

| From | Item | Outcome |
|---|---|---|
| Kyle, 2026-10-18 | No real balance before the security tier | **Held.** Invented data throughout. The tier is built; admitting a real balance is its own commission |
| The commission | The four additions | **Built.** Headers and CSP (`2b8f6ae`); the auth audit log (`565016f`, `4ee95d0`); the sign-in limiter (`2b8f6ae`); secrets kept out of logs (`1cb41da`) |
| `D-030` Amendment 1 | The pre-commit hook before the first real balance | **Built** (`9364c2e`), and installed in this clone |
| Kyle, 2026-10-24 (`D7`) | The argon2id password fallback | **Deferred.** Trigger: an environment where a passkey is impractical, or the first lost authenticator |
| Kyle, 2026-10-24 (`D6`) | The non-superuser application role | **Deferred.** Trigger: real data, or anything other than this app reaching the database |
| Kyle, 2026-10-24 (`D5`) | A backup command before a migration runs on a volume that matters | **Owed before the first real balance.** Migration 5 ran on the published volume, which holds invented data only |
| Kyle, 2026-10-24 (`D17`) | Data-mutation audit | **Carried.** Trigger: real data |
| The review | The deferrals with triggers (`C3-44`, `C3-50`, `C3-39`, `C3-48`, `D4`, `D8`, `D14`) | **Carried**, as listed in `slice-03.md` |
| slice-02 assessment | Finer error boundaries; a web unit-test runner | **Carried, not built** |
| slice-02 assessment | The import's plan-to-write race | **Carried** |
| slice-02 assessment | The bundle; the network split (`S-25a`) | **Carried.** The bundle has grown from 638 kB to **647 kB** (192 kB gzipped) |
| Planning `D-043` | `C-389`, value-weighted coverage | **Open question**, untouched by this slice |

---

## 2. What it cost

| | Hours | How it was measured |
|---|---|---|
| **Kyle** | **0** logged | `LOG.md`, the `Kyle h` column: every slice-03 row is `—`. Kyle answered 25 decisions (D1–D17, three proposals, R1–R4, the query-string ruling) and reviewed and merged 16 PRs; none of it was logged as hours |
| **Agent**, as logged | **13.9** | `LOG.md`, the `Agent h` column: 14 rows. Every row says "estimated": no timer ran |
| **Agent**, as measured | **≈ 10** for the review dispositions through step 7b | Wall-clock on 2026-10-24, from PR #69 merging (11:51 UTC) to PR #85 opening (21:50 UTC). It includes the waits on Kyle's answers and merges. The rows inside that span total 12.6 |

**The logged agent figure is overstated again**, as in slices 01 and 02: the estimates total
more than the span they fall inside.

**Work outside both columns:**
- **The committee review** (PR #69, 2026-10-21): eight seats and a broker, with no `LOG.md`
  row;
- **round 2's three seats**, run as subagents inside the 2026-10-24 span, and so inside the
  measured figure.

**Against the 200-hour threshold:** **not tripped, and that tells us nothing.** `slice-03.md`
said so in advance: with the agent building, this limb cannot trip. **The gate rests on the
techniques limb, §5.**

---

## 3. The spikes' claims, what `security.md` was corrected for, and what the build found

| Spike | Claim | Result |
|---|---|---|
| H | Identity has in-box passkeys; a library is the alternative; .NET has no argon2id | **Held, all three.** No WebAuthn library was needed. Geralt was chosen for argon2id, then not taken when `D7` deferred the password |
| I | `http://localhost` is a secure context; `127.0.0.1` cannot be the RP ID; `Secure` cookies work on `http://localhost`; all of it through the Vite proxy and the published port | **Held, all four.** Claims 1–3 were executed by the committee's browser probe in Chromium and Firefox. Claim 4: through the proxy (`G-5`, Spike I) and on port 8080 (`G-12`, step 7b). **Found:** a refused registration still leaves a passkey on the device, and Spike H's code left a user row too |
| J | Antiforgery works for an SPA; **its validation can cover every unsafe method**; `SameSite=Strict` does not stop a same-site page | **Claim 2 was false as the framework ships.** `UseAntiforgery()` with `RequireAntiforgeryToken` validated and **let every write run**: JSON, `DELETE`, raw upload, and a real same-site attack. That was round 1's blocking finding, now observed. The slice owns the refusal in its own filter. Claims 1 and 3 held. **`G-19` was false:** Playwright cannot read an opaque response's body or headers, nor the cookie sent on it, so DoD 4 reads the API's record |
| K | A strict CSP survives the dev server and the chart | **Cut** (`D9`). Claims 2 and 3 were executed by the committee; claim 1 was incomplete (Vite injects styles too). The published policy was built in step 3 and held in step 7b |

**What `security.md` was corrected for** (each correction is marked *(slice 03)* in the
document):
- **Threat model:** a process running as the user, and an extension with `localhost`
  permission, are equivalent to the keyboard, and out of scope.
- **API:**
  - passkey only, with the RP ID and origins pinned;
  - every Identity cookie `Strict`, `Secure` and `HttpOnly`, but not the antiforgery
    cookie, and why;
  - stateless sessions, with sign-out effective within the validation interval;
  - the application refuses CSRF, not the framework;
  - **no CORS at all**, where the document had said "restricted to the frontend origin";
  - one sign-in limiter, never keyed by address, and lockout off;
  - no HSTS on `localhost`, ever;
  - a `Permissions-Policy` that allows the passkey features;
  - forwarded headers off;
  - the key ring on a volume.
- **Frontend:** the strict CSP is the published page's, set as a header; the dev server sets
  none, with the reason.
- **Audit:** authentication events only so far, and immutability as a code-path statement.
- **Credentials → Rules:** the secrets test is the control. It went red once, on query
  strings.

### Defects the build found that no review did

Each was found by running something, not by reading. Two of them corrected the reviews
themselves.

| Defect | Found by | Commit |
|---|---|---|
| **Round 2's fix for the passkey table did nothing.** Overriding `SchemaVersion` compiles, but Identity reads the version only from `IdentityOptions` in the context's services | A probe context, then the generated migration | `56488f8` |
| **Multi-word enum labels would not match Npgsql's translator** (`signinsucceeded` against `sign_in_succeeded`) | Reading the generated migration | `56488f8` |
| **The committee cited FR-3.4 for sessions**; it is about a connection's credentials | Reading `spec.md` before editing `security.md` | the C3-5 row in `9e67ba6` |
| **ASP.NET Core's request lines log the full query string**, so a secret pasted into a URL is written down | `SecretsStayOutTests`, at `Trace` | `1cb41da` (off below Warning in code, by Kyle's ruling) |
| **Auth refusals copied Identity's failure message**, which can echo client input, into the error body | Reading the canary test's design against the endpoints | `1cb41da` |
| **"Signed in again." was never announced**: its live region unmounted with the dialog | The expiry E2E | `fc8975d` |
| **Watching the API's log by name followed nothing**: a project's resource id carries a replica suffix | The DoD 4 E2E | `4ee95d0` |
| **The cross-site attack's `no-cors` fetch sent no cookie**, because `fetch` defaults to `same-origin` credentials. The test had not been attacking with the session | Reading the refusal lines | `4ee95d0` |
| **`aspire deploy` keeps its setup code**, so the runbook's "redeploy for a fresh code" was wrong | Three deploys, comparing hashes | `9694f0b` (runbook corrected) |
| **RouteHandlerAnalyzer crashes** (`AD0001`) on a method group from another assembly | The build of a test | `2b8f6ae` |

**The session's own slips, each corrected:**
- a merge from `main` into a stacked branch kept a duplicate method (CS0111 on #81), because
  its base had been squash-merged; the lesson is rebasing a stacked branch onto `main` when
  its base merges;
- three test-harness bugs:
  - a `DelegatingHandler` reused across clients;
  - a span list enumerated while the exporter wrote to it;
  - a stranger passkey's random-byte user handle, which made Identity fail a different way;
- a flaky full-suite E2E under the load of every class starting its own stack. The default
  assertion timeout is now 15 s;
- a throwaway setup code, never applied, printed into a temporary log by a mistyped shell
  variable. The log was deleted.

---

## 4. The attacks the tier exists for: shown, or argued

Against the boundary `slice-03.md` states: the tier refuses browser-delivered attacks,
clients without the session, and processes outside the user's security context. It does not
refuse a process running as the user, or an extension with `localhost` permission.

| Attacker | Refused by | Shown how |
|---|---|---|
| **A page on another site** (`attacker.localhost`) writing | Layer 2 (`Sec-Fetch-Site: cross-site`); the `Strict` cookie is not sent | **In a real browser**, both targets, the layer and the cookie's absence read from the API's record (`4ee95d0`) |
| **A page on another `localhost` port, with the session**, writing | Layer 2 (`same-site`); behind it, the CSRF token | **In a real browser**, the cookie **arrived**, and the write was still refused (`4ee95d0`). The token alone is shown in Integration (`2b8f6ae`). The framework's own antiforgery alone **let this attack write**, in a browser (Spike J) |
| **Either page, reading** | The browser's same-origin policy; no CORS is granted | **In a real browser**, since slice 02, still green (`4ee95d0`) |
| **A passkey ceremony from another port** | `ValidateOrigin` | **In a real browser** (Spike I), and in Integration with a foreign origin and `crossOrigin` (`565016f`) |
| **Script injected into the published page** | The CSP | **In a real browser**, on the built bundle: an inline script was refused (step 7b). No XSS in the app's own markup was attempted; React escapes text, which is argued, not shown |
| **Framing the page** | `frame-ancestors 'none'`, `X-Frame-Options: DENY` | **Headers asserted** on every status; framing itself was executed by the committee's probe (BRW F16), not re-run here |
| **A client with no session** (`curl`, any local process) | The fallback policy | **By request** over every endpoint (`fc8975d`) |
| **A stolen session cookie** | Sign-out rotates the stamp; `revoke-sessions` | **In Integration** (a copy refused after +61 s, `565016f`; after the command, `4ee95d0`) and **on the published stack** (step 7b). A copy stays usable for up to the validation interval: the lag `D4` accepted |
| **A replayed or cloned passkey assertion** | The sign counter; a consumed challenge | **In Integration**: a replay with its challenge cookie, a counter that did not advance (`565016f`). Platform passkeys that report a counter of 0 are not covered (`G-8`, open) |
| **Guessing the setup code** | 131 bits; single use, surviving `reset-owner`; the limiter | **In Integration** (`56488f8`, `565016f`, `4ee95d0`). The guessing itself is **argued** from the code's size |
| **A caller forging its address** (`X-Forwarded-For`) | Forwarded headers off | **On the published stack**: audited at the real address (step 7b) |
| **A secret written to a log, trace or error body** | Nothing writes one; the query-string lines are off | **The canary test**, over every encoding (`1cb41da`) |
| **Something on the LAN** | The `127.0.0.1` publish | **Partly:** refused on this machine's own LAN address (step 7b). **A probe from a second host is owed** (`G-11`) |
| **A process running as the user**, or **an extension with `localhost` permission** | **Nothing**: out of scope, by the stated boundary | Argued, and written into `security.md` |
| **The database's own connecting role** removing the audit trigger | **Nothing yet**: `D6` deferred the app role | Argued; `G-7` open |
| **A stolen disk** | Disk encryption, which is the host's | Argued. The key ring is plaintext on the volume, as `security.md` says |

**Is the platform now fit to hold a real balance?** That is the assessment's question to
frame, not answer (`slice-03.md`, "Then stop"). The facts that frame it:
- **The tier `slice-03.md` commissioned is built and tested.** The tests are above, and §1.
- **What the rulings deferred to "real data" is still owed.** That is the backup command
  (`D5`), the application role that would make the audit trail a database boundary (`D6`),
  and the data-mutation audit (`D17`).
- **`security.md`'s "Database" section was not commissioned** by this slice. Neither were
  its envelope encryption for connector credentials, which no connector needs yet, or its
  reporting role.
- **The LAN probe from a second host** is owed.
- **Admitting a real balance is its own commission**, written and dated in this repository
  (Kyle, 2026-10-18).

---

## 5. Techniques — **Kyle to complete**

The gate's words (`slice-03.md`): *"three techniques this slice taught that Kyle did not
already have. Each must come with the commit that demonstrates it, and must **not** be any
of the thirteen slices 01 and 02 already claimed."*

**The fact that frames this section:** Kyle logged no hours, and an agent session did the
build. What follows are techniques the build **demonstrates**, each with its commit. Whether
any is now **Kyle's**, understood well enough to use without the agent, only Kyle can answer.
**A technique merely present in the repository does not satisfy this limb.**

Tick the ones that are genuinely yours; strike the rest. **Fewer than three means STOP.**

- [ ] **Passkeys end to end on a framework's in-box handler, with no library.** The RP ID
      and origins are pinned, enrolment creates nothing before the ceremony succeeds, and
      a software authenticator with hostile variants drives it in CI.
      `565016f`, `f0e3b3e`, `56488f8`.
- [ ] **"The framework validates; the application refuses."** CSRF in a single-page app
      with an app-owned filter and a token bound to the user. It is proven by a control in
      which the framework alone let the attack write.
      `d27dfca`, `2b8f6ae`, `fc8975d`.
- [ ] **Default-deny authorization, asserted by request over every endpoint.** An
      allow-list, a body table for unsafe endpoints, and the test seen failing.
      `fc8975d`.
- [ ] **What revocation means for a stateless session.** Security-stamp rotation, with the
      validation interval as the revocation lag, a persistent cookie that the framework
      would not have issued, and a host command for a stolen cookie.
      `565016f`, `4ee95d0`.
- [ ] **Leak-testing by canary rather than by shape.** Secrets driven through every flow,
      scanned in every encoding across logs, spans, bodies and audit rows, with a guard
      against a silent capture and a committed failing twin. It found a real path.
      `1cb41da`.
- [ ] **Reading from the server what the browser hides.** A refusal line per layer, read in
      the E2E through Aspire's resource logger, because Playwright cannot see an opaque
      response or the cookie on it.
      `4ee95d0`, `d27dfca`.
- [ ] **An accessible asynchronous sign-in.** Focus where the browser hands it back, a
      dialog over a preserved page on expiry, one message rather than an alert storm, and
      each ceremony outcome named.
      `fc8975d`, `9694f0b`.
- [ ] **"The published artifact is not the thing you ran."** Forwarded headers on by
      default, a volume's ownership decided by its mount point, and a deploy that keeps its
      generated secrets. Each was found by reading the generated file or deploying.
      `4ee95d0`, `9694f0b`.

*Overlaps to judge:*
- **The CSRF and server-record candidates sit close to slice 02's "defence in depth,
  tested from a real second origin".**
- **The canary candidate sits close to slice 02's "proving a defence by breaking it".**

Kyle should decide whether each is new or the same.

Kyle's own, if different: ________________________________________________

---

## 6. The decision — **Kyle to complete**

`slice-03.md`: *"Explicitly NOT reasons to continue: that the slice works, and that the next
slice is obvious."*

- [ ] **STOP.** The default.
- [ ] **A fourth slice**, which requires **its own commission, written and dated in this
      repository**. If chosen: which technique is it for?
      ________________________________________________
- [ ] **Admitting the first real balance**, which also requires its own commission. It is
      bound by what §4 lists as owed (`D5`; and `D6` and `D17`, whose triggers are real
      data), and by `C-389`, Kyle's to rule.

**Kyle's hours stayed under 200** (0 logged), so the gate rests on §5 and on Kyle's own
judgement of what the slice taught.

---

## 7. Owed elsewhere

**Nothing to the planning repository.** `D-043` decoupled the projects; the slice was
commissioned, reviewed, ruled and built here, and this assessment is its record.

**In this repository**, for whatever comes next, with nothing scheduled:
- **`G-11`'s second-host probe.**
- **`G-8`:** which platform passkeys report a counter of 0.
- **`G-7`:** whether the app's role can remove the audit trigger, which `D6` defers.
- **A comment in `CrossSiteWriteTests`** recording why its expected values did not move
  (DoD 9's note).
- **A shape assertion per audit kind** (DoD 7's note).
