# Slice 03, round 2 — scope realist (SCO)

**Seat:** scope realist. **Date:** 2026-10-24. **Under review:** PR #74,
`git diff slice-03-dispositions -- specs/001-portfolio-platform/ README.md` on
`slice-03-spec-edits` (`0b20339`).

**Read:** `specs/001-portfolio-platform/slice-03.md` (whole, as rewritten);
`specs/001-portfolio-platform/security.md` (the diff); `README.md` (the diff);
`specs/review/slice-03-committee.md` "Rulings and dispositions — 2026-10-24" (rulings, "What
the rulings leave open", the by-finding table, gates under the rulings), "Axis 2" and "The
scope realist's middle cut", and the "What to do" text of C3-2, C3-3, C3-20, C3-26, C3-32,
C3-34 where a disposition's carriage needed checking; `LOG.md`; `slice-02-assessment.md` §2
and §5; `src/Api/Program.cs`; `.githooks/pre-commit`; `git log main` (step-per-PR pattern).

Line numbers are `slice-03.md` at `0b20339` unless another file is named.

---

## Findings

### SCO-1 · MUST FIX (step 2) · `FallbackPolicy` switches the session requirement on in step 2

**Location:** `slice-03.md:327-330` against `:343-344`, `:409-410` and `D11`.

**The defect.** Step 2 sets `FallbackPolicy = RequireAuthenticatedUser()`. A fallback policy
applies to every endpoint without authorization metadata, which is every existing
`/api/v1` endpoint (`src/Api/Endpoints.cs:21`). So the session requirement on the existing
endpoints goes on in step 2, while the same step (`:343`) and step 4 (`:409`) say it goes on
in step 4, as `D11` ruled. Merged as written, step 2 leaves every browser write and the
existing E2E red until step 4 ships a sign-in page. [`INFERRED`, from the fallback policy's
documented meaning; the design text, not the framework, is the defect.]

**What to do.** Step 2 registers authentication, the cookie, `AllowAnonymous` on the named
routes and `.DisableCookieRedirect()`; the `FallbackPolicy` line moves to step 4's "switched
on here" bullet. DoD 2's allow-list test lands with it in step 4.

### SCO-2 · MUST FIX (step 3) · the antiforgery filter breaks every browser write from step 3 to step 4

**Location:** `:346-348` against `:404-405` and `:409-410`.

**The defect.** Step 3 puts the refusing filter on the `/api` group, and the token endpoint
requires a session. Until step 4 the page has no sign-in and no token client, so from step 3's
merge every write from the browser (balance form, link form, upload) is refused, and the
existing E2E goes red. This is the same ordering problem `D11` solved for the session
requirement. It just was not applied to the token.

**What to do.** One sentence. Either the filter's enforcement is switched on in step 4 beside
the session requirement (step 3 ships it, tested in integration through `SignedInClient`), or
step 3 ships the web's token middleware and the E2E signed-in helper, which pulls step 4's
sign-in forward. The first is smaller.

### SCO-3 · MUST FIX (step 3, DoD 2) · the anonymous authentication endpoints cannot carry a token

**Location:** `:346-348`, `:475`; Spike J `:242-244`.

**The defect.** The filter refuses every unsafe method on `/api`, and DoD 2 says "every
unsafe-method endpoint carries the antiforgery filter". The enrolment and passkey sign-in
`POST`s are unsafe and anonymous. The token endpoint "requires a session" (`C3-22`), and the
token binds to the principal (`:230-232`). So an anonymous client can never hold a token that
validates, and the slice's own filter refuses enrolment and sign-in. Spike J then says "the
result decides where the token comes from" (`:242`), which `C3-22`'s FIX has already
decided.

**What to do.** Name the rule. The smaller choice: the allow-listed anonymous endpoints are
exempt from the filter, and are covered instead by layers 1 and 2, `ValidateOrigin` and the
limiter. DoD 2 then reads "every unsafe-method endpoint not on the allow-list carries the
filter", and the test asserts both halves. The other choice: an anonymous token before
sign-in, refetched after, which `C3-22` already requires. Then strike "the result decides
where the token comes from" from Spike J.

### SCO-4 · MUST FIX (step 1, step 7) · `reset-owner` and the single-use setup code contradict each other

**Location:** `:270` ("so `reset-owner` can clear the user"), `:282-284` ("Its hash is stored
with the user"), `:448` ("clear the credentials and reopen enrolment"); `D2`; "What the
rulings leave open" (3).

**The defect.** `D2` accepts the code "only while no user exists". If `reset-owner` deletes
the user (`:270`), the stored hash of the last code goes with the row, and nothing remains to
refuse the stale value left in `.env.*`. The rulings' own consequence (3) then fails. If it
keeps the user and clears only the credentials (`:448`), a user still exists and enrolment
never reopens. The two readings are in the same document.

**What to do.** Pick one sentence for step 1. `reset-owner` deletes the user and its
passkeys. The consumed code's hash survives the reset outside the user row. The smallest place
for it is the enrolment `audit_event` (append-only, already written, and a hash of a 128-bit
code is not a secret); enrolment refuses a code whose hash appears in any enrolment event.
Step 7's command wording follows.

### SCO-5 · MUST FIX (step 4) · the filter answers `403`, the page retries on `400`

**Location:** `:347` ("refuses with `403`") against `:398` ("An antiforgery `400` means:
refetch the token, retry once").

**The defect.** As written, the retry branch can never fire. A `403` is also slice 02's
`Sec-Fetch-Site` refusal, so retrying on the status would retry a cross-site refusal too.
(`C3-17`'s row says `400`; the step 3 design chose `403`.)

**What to do.** The page keys the retry on the filter's `ProblemDetails.title`, not on the
status, consistent with DoD 4's "identified by its title, not its status". Choose one status
and use it in both places.

### SCO-6 · SHOULD FIX (step 5, step 7) · two audit kinds are asserted before their commands exist

**Location:** `:412-413`, DoD 7 `:508-509`, against `:448-450`.

**The defect.** Step 5 lists six event kinds, and DoD 7 asserts each "by shape and by count".
"Sessions revoked" and "owner reset" are raised only by step 7's commands, so step 5 cannot
merge with its count test green.

**What to do.** Step 5 builds four kinds. Step 7 adds two, with their commands, and extends
the count test.

### SCO-7 · SHOULD FIX (steps 1, 2) · enrolment runs a ceremony that step 2 configures

**Location:** `:272-288`, `:295-297` against `:299-304`.

**The defect.** Step 1 builds the enrolment ceremony (creation options, attestation, signed
in). The options it must run under arrive in step 2: `ServerDomain`, `ValidateOrigin`, UV,
`AuthenticatorTimeout` and the `__Host-` cookie that carries the challenge. Step 1 would ship
on Identity's defaults, which take the RP ID from `Host` (`security.md` diff,
`PasskeyHandler.cs:51-55`). The set-up *views* are listed in step 1, but they are web work,
and step 4 builds them (`:374`); `C3-53` asked for a design note.

**What to do.** Step 1 is migration 5, `audit_event`, and the setup-code parameter with its
rules. The enrolment endpoints move to step 2 beside sign-in. The set-up views bullet says
"designed here, built in step 4".

### SCO-8 · SHOULD FIX (step 2) · "the auth classes run the real ceremony" in integration is unbuilt and unpriced

**Location:** `:339-344`; DoD 2 `:476-477`; `C3-8`'s test `:325`.

**The defect.** `AuthFixture : PostgresFixture` is the integration suite, which has no
browser. A real passkey ceremony there needs a software authenticator in C#: an ES256 key, CBOR
`authData` and attestation object (`fmt: none`), and `clientDataJSON` over the server's
challenge and origin. That is the largest new test artefact D7 created, and nothing names it.
[`INFERRED`] DoD 2's "a signed-out cookie answers `401` once the validation interval has
passed" also needs a pinned clock or a zero interval in tests, or the suite waits a minute.
`C3-13`'s "auth tests in `tests/Integration`" is not stated either.

**What to do.** Add one bullet to step 2: a test-only authenticator under `tests/`, covered by
the same architecture test as the test scheme; the security-stamp check driven by the pinned
`TimeProvider` slice 02 already injects (`Program.cs:16`) or by an interval of zero in the
test host; and the auth classes in `tests/Integration`.

### SCO-9 · SHOULD FIX (step 3) · one global window is shared by the whole suite

**Location:** `:352-357`; DoD 7 sweep `:422-425`.

**The defect.** The limiter is global and never keyed by address, as ruled. The shared
`AuthFixture` runs every auth class, and DoD 7's sweep "injects every field of every anonymous
endpoint". Both draw from one window, so unrelated tests will trip `429` in an order that
depends on scheduling.

**What to do.** Make the limits configuration. Tests run them high, and the limiter's own class
gets its own factory with the real numbers.

### SCO-10 · SHOULD FIX (step 0) · `G-12` cannot run in step 0

**Location:** `:193-194`; the committee's `G-12` row ("Spike I / step 7").

**The defect.** "The same ceremony against the published port 8080" needs a published stack
with Identity in it, which exists only after step 7. Spike H's throwaway program would have to
be containerised to answer it in step 0. That is extra work for a gate the row allows at
step 7.

**What to do.** Raise `G-12` in step 7, where DoD 3's published half already runs the
ceremony on 8080. Step 0 keeps `G-5`, `G-19` and `G-20`.

### SCO-11 · SHOULD FIX (obligations table, DoD 10) · DEFER halves with triggers are not carried

**Location:** `:521-536`.

**The defect.** DoD 10 walks "each obligation below", and the table leaves out several
deferred items whose triggers the rulings recorded:
- `C3-44`: the authentication method in audit; trigger D7's.
- `C3-50`'s compose healthcheck: trigger off-loopback or an observed restart loop.
- `C3-39`'s per-credential nonce: trigger `G-8`. The step 2 text names it; the table does not.
- `D4`'s `sessions` table: trigger more than one session to revoke.
- `D8`'s `127.0.0.1` → `localhost` redirect.
- Firefox E2E (`D14`).

The assessment can lose them.

**What to do.** Add one row each, or one row "Deferred by the review" that lists them with
their triggers.

### SCO-12 · SHOULD FIX (step 4, DoD 6) · three FIX clauses dropped

**Location:** step 4 `:379-410`; DoD 6 `:498-507`.

**The defect.**
- `C3-34`'s "no ceremony on page load" is absent.
- `C3-36`'s "the Identity and antiforgery cookies cleared" on sign-out is absent.
- `C3-20`'s "separate CSP and axe contexts if `G-29` needs `BypassCSP`" is not in DoD 6. Step 7
  only names `G-29`.

**What to do.** One clause each, at step 4 and DoD 6.

### SCO-13 · SHOULD FIX (steps 1, 4; DoD 2) · the page's pre-authentication state has no route

**Location:** `:291-292` ("presents 'no owner enrolled'"), `:377-378` (environment label
"before authentication"), DoD 2 `:471-472`.

**The defect.** Before signing in, the page must choose between set-up and sign-in, and must
show the environment label. Neither has an anonymous route on DoD 2's allow-list, so either the
allow-list test fails or the page infers both from somewhere unstated.

**What to do.** Name it: one anonymous `GET` (enrolled or not, plus the environment), put on
the allow-list. Or state that the page infers set-up from the enrolment options' `409` and that
the label is build-time configuration.

### SCO-14 · SHOULD FIX (step 1) · the setup code's producer and its "bound" are unnamed

**Location:** `:272-276`, `:289`.

**The defect.** The code is "printed grouped for reading", but under `D2` nothing prints. It is
a parameter, and the text never says who generates a ≥128-bit value in that alphabet, or where
`reset-owner`'s "fresh value" comes from. "Bounded" came from the console option's TTL, which
went with `D2`. As written, the window is bounded only by enrolment.

**What to do.** One sentence each. Name the generator: a documented one-liner, or the AppHost's
generated parameter default, checked by a gate if relied on. Then say "bounded by single use,
not by time".

### SCO-15 · NOTE (step 5) · password residue in the failed-sign-in audit

**Location:** `:416`.

**The defect.** "Records 'unknown user' unless the name matches the one user" comes from the
password path (C3-26's RED-9 text). A passkey assertion carries a credential id and a user
handle, not a typed name.

**What to do.** "Records the credential id as unknown, or the user, if it resolves."

### SCO-16 · NOTE (DoD 4) · "the token's job is same-origin script" is the wrong attacker

**Location:** `:493-494`.

**The defect.** Same-origin script can call the token endpoint itself. Under `D12` the token
is defence in depth behind layer 2, and the sentence should name who reaches it: a client or
browser that sends no `Sec-Fetch-Site`, or layer 2 removed by a later change. This is the
security seat's call, and it is noted here because the assessment's attacker-class taxonomy
will quote it.

**What to do.** Reword to `D12`'s own sentence.

### SCO-17 · SHOULD FIX (step 4, DoD 2) · "static files are anonymous by design" is not yet true by design

**Location:** DoD 2 `:472`; `src/Api/Program.cs:50` (`app.UseFileServer()` is the last
middleware).

**The defect.** `WebApplication` inserts authentication and authorization early in the
pipeline when they are registered. Static files served by middleware placed after
authorization are then subject to the fallback policy, so the published `index.html` and the
bundle would answer `401`: the sign-in page could not load. [`INFERRED`, recalled from the
ASP.NET Core static-files documentation's "Static file authorization" section, not re-read
this session. It is load-bearing for DoD 2 and DoD 3's published half, so it is a gate.]

**What to do.** In step 4, where the fallback policy goes on (SCO-1), say where
`UseFileServer` sits relative to `UseAuthentication`/`UseAuthorization` (explicitly before),
and add an anonymous `GET /` and bundle-asset `200` in Production to DoD 2's test.

---

## Fidelity table

`OK` means the disposition is carried at the step it names, or one step from it with no
effect. `MISSING` / `PARTIAL` / `CREPT` mean what they say. CUT and DEFER rows are checked for
not reappearing as build work.

| Finding | Disposition | Where it landed | Verdict |
|---|---|---|---|
| C3-1 | FIX | Spike J `:237-240`; step 3 `:346-348`; DoD 2 `:475` | OK, but see SCO-2 and SCO-3 |
| C3-2 | FIX | DoD 4 `:482-494` | OK (wording: SCO-16) |
| C3-3 | FIX | step 1 `:272-294` | PARTIAL: SCO-4, SCO-14 |
| C3-4 | FIX | `:51-64`; `security.md` "Threat model" | OK |
| C3-5 | FIX | step 2 `:315-322`; DoD 2 `:476`; step 7 `:448`; `security.md` "Sessions" | OK |
| C3-6 | FIX | step 2 `:305-314` | OK |
| C3-7 | FIX / CUT half | step 3 `:352-360`; DoD 5; no concurrency limiter anywhere | OK (SCO-9) |
| C3-8 | FIX | step 2 `:323-326` | OK |
| C3-9 | FIX | step 7 `:445`, `:458` | OK |
| C3-10 | FIX | step 2 `:299-304`; DoD 3 `:478` | OK |
| C3-11 | FIX | step 2 `:336-338`; step 7 `:446`, `:459`; DoD 3; `security.md` | OK |
| C3-12 | FIX | step 2 `:327-330`; DoD 2 `:469-477` | Carried, but in the wrong step: SCO-1 |
| C3-13 | FIX | step 2 `:339-344`; DoD 9 `:516-517` | PARTIAL: `tests/Integration` unstated (SCO-8) |
| C3-14 | CUT | `:92-94`; Spike K `:246-258` | OK, not built |
| C3-15 | FIX | step 4 `:376-377` | OK |
| C3-16 | FIX | step 2 `:315-319` | OK |
| C3-17 | FIX | step 4 `:391-400` | Carried; status wrong: SCO-5 |
| C3-18 | FIX | step 4 `:379-390` | OK |
| C3-19 | FIX | step 3 `:355-356`; DoD 5 | OK |
| C3-20 | FIX | DoD 6 `:504-507` | PARTIAL: separate contexts (SCO-12) |
| C3-21 | FIX | step 2 `:307-309`; `security.md` | OK |
| C3-22 | FIX | step 3 `:348`; step 4 `:404-405` | OK; `HeaderName` "set explicitly" is only implied by `X-CSRF-TOKEN` (`:243`); conflicts with Spike J `:242` (SCO-3) |
| C3-23 | FIX | step 2 `:301-302`; DoD 4 `:492` | OK |
| C3-24 | DEFER / FIX wording | `:88-91`, `:270-271`, DoD 7 `:510-511`; `security.md` "Audit" | OK, no role built |
| C3-25 | CUT | absent | OK |
| C3-26 | FIX / DEFER filter | step 5 `:418-430`; `security.md` "Rules" | OK; filter not built |
| C3-27 | FIX / DEFER audit | step 5 `:431-432`; obligations `:531` | OK |
| C3-28 | FIX | step 7 `:460`; DoD 6 `:501-503` | OK |
| C3-29 | FIX | step 6 `:433-443`; DoD 8 | OK |
| C3-30 | FIX | DoD 3 `:480`; DoD 9 `:517-518` | OK |
| C3-31 | FIX | DoD 4 `:491-492` | OK |
| C3-32 | FIX / CUT half | step 1 `:269-270`; step 7 `:448-450` | Contradictory: SCO-4 |
| C3-33 | CUT | obligations `:530` (folded into D5) | OK |
| C3-34 | FIX | step 2 `:304` | PARTIAL: "no ceremony on page load" MISSING (SCO-12) |
| C3-35 | CUT | absent | OK |
| C3-36 | FIX | step 2 `:331`; step 4 `:401-403` | PARTIAL: cookies cleared MISSING (SCO-12) |
| C3-37 | FIX | step 4 `:374-376` | OK |
| C3-38 | FIX | step 3 `:366-367`; `security.md` | OK |
| C3-39 | FIX / DEFER nonce | step 2 `:307`, `:332-335` | OK; DEFER not in obligations (SCO-11) |
| C3-40 | FIX | step 3 `:361-368`; DoD 6 `:499-500` | OK |
| C3-41 | FIX / CUT build item | step 3 `:349-351`; `:95-96`; `security.md` "CORS" | OK; no CORS built |
| C3-42 | FIX (no work) | via C3-6, C3-36 | OK |
| C3-43 | FIX | step 5 `:417` | OK |
| C3-44 | DEFER | absent | Not built, but trigger not carried (SCO-11) |
| C3-45 | CUT | Spike H `:158-160` | OK |
| C3-46 | CUT | Spike H `:158-160` | OK |
| C3-47 | FIX | step 4 `:400` | OK |
| C3-48 | DEFER | obligations `:532` | OK |
| C3-49 | FIX | Spike I `:181-185`; DoD 3 `:479-480` | OK |
| C3-50 | FIX / DEFER healthcheck | step 4 `:377-378` | Label OK; healthcheck trigger not carried (SCO-11) |
| C3-51 | FIX | step 4 `:406-408` | OK |
| C3-52 | FIX | step 4 `:389-390` | OK |
| C3-53 | FIX | step 1 `:295-297` | OK as a design note; build step unclear (SCO-7) |

**Rulings.** D1, D2, D3, D4, D5, D6, D7, D9, D10, D13, D14, D15, D16 and D17 are carried, and
`security.md` agrees with each. **D11** is contradicted by `FallbackPolicy` in step 2
(SCO-1). **D12** is carried in structure; the wording is SCO-16. **D8**'s deferred redirect is
not recorded (SCO-11). "Leave open" (1) is carried. (2) is carried, but its integration
ceremony is unpriced (SCO-8). (3) is carried, but contradicts itself (SCO-4).

**Nothing CUT or DEFERRED reappears as build work.** There is no hasher, password route,
concurrency limiter, dev CSP, `sessions` table, app role, CORS policy, redaction filter,
healthcheck or redirect.

---

## Creep: what the rewrite added beyond the middle cut and the rulings

Every addition traces to a FIX row, a gate owed at its step, or a ruling's consequence. **I
found no item the editor invented.** These are the ones that grew past the smallest version:

| Added | Source | Smallest version? |
|---|---|---|
| Sixth and fifth audit kinds (sessions revoked, owner reset) replacing "lockout" | C3-5, C3-32 "audited"; C3-8 removes lockout | Yes, but in the wrong step (SCO-6) |
| `revoke-sessions` and `reset-owner` as `docker compose run --rm api <mode>` | C3-5, C3-32, leave-open (3) | Yes, if they are one CLI branch with two verbs |
| Test scheme plus an architecture test | Leave-open (2) | Yes |
| Real ceremony in integration (implied software authenticator) | Leave-open (2) | Unnamed (SCO-8) |
| Enrolment "shown on the first sign-in" | C3-3 What-to-do (OPS-4), "attested" | Smallest is one line from the sign-in response, with no audit read endpoint. Say so |
| "Signed in as <name>", footer health line, `AbortController` guard | C3-36, C3-17, C3-18 What-to-do | Yes |
| Sign-in `<dialog>` over the preserved page | C3-17, D15 (overrides the middle cut's DEFER of "without losing the page") | Ruled. It is the largest single cost moved, below |
| CORS test across DoD 2's enumeration plus `OPTIONS` | C3-41 | Yes; it reuses the enumeration |
| `G-12` in step 0 | Gate row allows step 7 | No (SCO-10) |
| Hook test builds a temporary repository and asserts `core.hooksPath` | C3-29 | Yes |
| Setup code ≥128 bits, restricted alphabet, grouped | C3-3 What-to-do | Yes, once the producer is named (SCO-14) |

---

## Cost

Logged agent hours at slice 02's ratio (measured ≈ 55 % of logged: 5.2 / 9.4,
`slice-02-assessment.md:68-69`), counted from this session onward. The 0.9 h of Spike H and
the 2.9 h of commission and review already in `LOG.md` are excluded, as the middle cut
excluded them.

| Step | Middle cut | Rewrite | Why it moved |
|---|---|---|---|
| 0 · spikes | 2.0 | **1.5** | Spike I claims 1–3 and claim 4's browser half are EXECUTED, and K is cut. Left: G-5 (still "the place a day can go"), Spike J's filter on three endpoint kinds, G-19 and G-20. Add 0.25 if G-12 stays here (SCO-10) |
| 1 · data and enrolment | 2.0 | **2.5** | −0.3 for no hasher. +0.5 for the setup-code rules (fixed time, single use surviving reset, `409`, one transaction, compose-file test). +0.3 for enrolment tests needing the software authenticator. Migration 5 risk unchanged |
| 2 · auth API | 2.5 | **2.75** | −0.4 for no password route or password helper. −0.5 for the browser-suite rework moving to step 4 (D11). +0.3 test scheme and architecture test. +0.5 software authenticator (SCO-8). +0.2 lockout-off and signed-out-cookie tests with a clock. +0.2 Set-Cookie test across every Identity cookie. +0.25 `rp.id`/origin pinning tests |
| 3 · CSRF, limiter, headers | 1.5 | **2.0** | −0.3 for no concurrency limiter or G-1. +0.2 global limiter with ProblemDetails and test isolation (SCO-9). +0.3 the C3-1 filter and its exemption rule (SCO-3). +0.3 CORS, HSTS-absent, Permissions-Policy and all-status tests |
| 4 · web | 2.0 | **3.5** | −0.3 for no password field. +1.0 for the dialog over the preserved page with token refetch, retry once and no replay (C3-17/D15). +0.5 for the six browser classes and `CrossSiteBrowserTests` re-done here (D11). +0.3 set-up views, per-outcome texts, environment label, unsupported browser, `127.0.0.1` warning. **This is where the slice can double**, as step 2 could in the middle cut |
| 5 · logging and audit | 1.0 | **1.5** | C3-26's full recipe (OTel exporter, three encodings, every anonymous field, query string, malformed assertion, `500`, seen failing) and the contract test |
| 6 · hook | 0.5 | **0.75** | A temporary-repo test with a clean twin, the `python` fallback, and the scanner present in the Architecture job |
| 7 · deploy | 1.5 | **2.5** | +0.5 for two commands, audited and tested, with the fresh-value rule. +0.2 forwarded headers and a forged-XFF check. +0.2 key-ring volume and restart survival. +0.1 runbook, G-11, G-26 |
| Assessment | 0.5 | **0.75** | An evidence line per E2E-backed item (D13), and the attacker-class taxonomy |
| **Total** | **13.5 logged / 7.5 measured** | **≈ 17.75 logged / ≈ 9.8 measured** | Back to the "as written" 18 h |

**What D7 actually saved and cost.** Removing the fallback saved about **1.5 h logged**:
- the hasher, 0.3;
- the password route and the password test helper, 0.4;
- the concurrency limiter and the G-1 measurement, 0.4;
- the password UI, 0.3;
- the Geralt dependency, 0.1.

Its consequences added about **1.9 h**:
- the software authenticator for real ceremonies in integration, 0.5 (the largest, and
  unnamed);
- `reset-owner` with a code that stays single-use across a reset, 0.4;
- the test-only scheme and its architecture test, 0.3;
- `revoke-sessions`, 0.2;
- the global limiter and its test isolation, 0.2, replacing the concurrency limiter;
- the setup code re-armed as recovery, 0.3.

**Net, D7 is about +0.4 h: cost-neutral.** It bought simplicity of surface (no hash, no
native dependency, no password policy), not hours. The growth from 13.5 to ~17.75 is the 44
FIXes themselves, about 2.5 h. The biggest of those is C3-17/D15 (≈1.0), and then the C3-26,
C3-20 and C3-1 tests. The middle cut's figure was struck before those FIXes existed, so it is
no longer the number to hold the slice to.

**Buildability order, in summary.** Steps 1–7 can each merge with `main` green only after
SCO-1 (fallback policy to step 4), SCO-2 (filter enforcement to step 4), SCO-6 (two audit
kinds to step 7) and SCO-7 (enrolment ceremony after the passkey options). With those, steps 3
and 4 together become the "switch on" pair, and step 4 carries the largest risk. Slice 02
merged a PR per step (`git log`: `#54`–`#60`). Nothing in the rewrite needs that pattern
broken, once these four are fixed.

---

## Axis 2 — scope, cost, teaching value (no severities)

| Item | Judgement |
|---|---|
| A software authenticator in `tests/` so integration runs the real passkey ceremony | **KEEP.** With no E2E in CI (D13), it is the only way CI holds the passkey path, the lockout-off property and the signed-out-cookie `401`. Name it (SCO-8) |
| `revoke-sessions` beside `reset-owner` | **KEEP.** It is a FIX, and the stolen-cookie answer that doesn't cost the passkey. Smallest form: one `args` branch with two verbs and one test class |
| Enrolment "shown on the first sign-in" | **KEEP**, in its smallest form: the sign-in response carries the enrolment time and address once. **CUT** any audit-reading endpoint or view; that is D17's territory |
| The `<dialog>` over the preserved page (C3-17/D15) | **KEEP**; it is ruled. It is the step 4 cost to watch. If step 4 passes 4 h logged, the trigger to stop and re-cut is the replay-and-retry logic, not the dialog |
| `G-12` in step 0 | **CUT** from step 0. Run it in step 7 with DoD 3's published half |
| `G-11` (LAN probe from a second host) | **KEEP**, as a hand step in step 7 |
| Six audit kinds by shape and by count | **KEEP.** Four in step 5, two in step 7 |
| The CORS test over the enumeration | **KEEP.** About five lines on DoD 2's enumeration |
| The hook test in `tests/Architecture` | **KEEP.** It is the only non-CI proof that the hook refuses |
| An anonymous auth-state route | **KEEP** if SCO-13 resolves that way. It is the smallest honest version, against inference from a `409` |
| An estimate in the commission | **DEFER.** "Estimate: none, as in slice 02" (`:39`) stands. The assessment should compare against ≈17.75 logged here and 13.5 from the middle cut, and say which FIXes moved it. Trigger: the assessment |

**The gate (techniques limb).** The slice as rewritten still offers **at least three
techniques Kyle did not have**, none of them slice 01's or slice 02's thirteen
(`slice-02-assessment.md:170-190`):

1. **Passkeys end to end on Identity's in-box handler**: RP ID and origin pinned by
   configuration because the default takes them from the request, UV required, and a virtual
   authenticator in the browser plus a software one in CI.
2. **"The framework validates; the application refuses"**: antiforgery on minimal APIs, a token
   bound to the principal and refetched on sign-in, and the refusing layer named by
   `ProblemDetails.title` with the cookie proven sent.
3. **Default-deny authorization as a behavioural property**: `FallbackPolicy` plus
   `AllowAnonymous`, tested by request over `EndpointDataSource`, which a new anonymous
   endpoint fails.
4. Weaker candidates: stateless revocation by security stamp and the lag it accepts; publish
   defaults as defects (forwarded headers, the key ring); focus management under asynchronous
   authentication.

**What D7, D4 and D6 cost the limb.** D7 removed the hash budgeted against a container limit,
which overlapped slice 02's "measuring before limiting" anyway. D4 removed `ITicketStore`
sessions. D6 removed grant-based immutability, the strongest unclaimed candidate. Two
candidates are close to slice 02's ticks, and Kyle should be asked about them, not have them
counted for him: the canary secret test (near "proving a defence by breaking it") and
"declining a library" (Spike H; near slice 02's fourth). The limb is still reachable. It is no
longer generous.
