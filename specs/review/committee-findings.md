# Committee Findings: Portfolio Platform Specification Set

**Reviewed:** `specs/` at commit `bc0f689` (13 documents, 112,966 bytes)
**Date:** 2026-09-18
**Committee:** 14 reviewers + broker
**Raw findings:** 118 · **After dedup and verification:** 84

---

## What this is

A structured adversarial review of the specification set on branch
`claude/jolly-allen-87d888`. Fourteen specialist reviewers read the specs
independently against their own discipline; this document is the brokered
result — deduplicated, severity-resolved, and ordered so the decisions that
gate other decisions come first.

**Every finding below was verified against the spec text before inclusion.**
Findings that could not be anchored in a quotable passage were dropped. Where
two seats found the same defect from different directions, that convergence is
noted — it is the strongest signal in the set.

### The committee

| Seat | Focus |
|---|---|
| Software engineer | architecture, buildability, interface design |
| QA engineer | testability and completeness of requirements |
| Test automation engineer | whether the test strategy can actually be built |
| Spec-driven development | spec-kit compatibility, requirement discipline |
| Security specialist | control correctness and sufficiency |
| Red team | adversarial — attack paths through the design |
| Retirement domain specialist | can it compute correct financial answers |
| .NET / Aspire platform | are the platform claims true and idiomatic |
| Data & schema architect | schema correctness, temporal modelling |
| Privacy & compliance | provider terms, retention, publication |
| DevOps / SRE | operability, backup, upgrade, failure modes |
| Scope realist | what to cut — the only seat arguing subtraction |
| UX & accessibility | can a person use it, can everyone use it |
| Technical writer | contradictions and broken references across documents |

### Severity

| | Meaning |
|---|---|
| **Must Fix** | Ships broken, unsafe, or self-contradictory. Blocks implementation, or produces a wrong financial number. |
| **Should Fix** | A real defect that will cost real time or create real risk. |
| **Consider** | A judgement call that deserves an explicit decision. |
| **Informational** | Worth knowing. No action required. |

---

## DECIDE FIRST — the scope question gates everything else

Every other finding should be read against this one, because roughly half the
defective surface is in work that arguably should not be in a first release.

### S-0 · Full specified scope is 700–1,000 solo part-time hours, with nothing usable until year one
**Severity: Must Fix · Seat: Scope realist**

The build order in `plan.md` reaches the first user-visible screen at **step 6
of 9**. Steps 1–5 produce nothing beyond "API health endpoint, React shell" —
an estimated 285–410 hours, or 9–16 months at 6–8 hours a week, before a chart
of your own money exists.

The coupling is deliberate and hard-wired by FR-5.5: *"Dashboards MUST be
composed from the same read-only view schema the reporting feature uses"* — so
the first chart sits behind the `reporting` schema, the restricted role, saved
reports and the query endpoint. Compounding it, there is no manual data-entry
path anywhere in the spec (it is an open question), so until a connector works
there is no data at all.

**This is the single most likely cause of the project being abandoned.** The
motivational payback is "I can see my net worth"; the plan defers it past the
point where most spare-time projects die.

**Recommended fix — invert to a vertical slice.** Step 1 becomes: Aspire
skeleton + Postgres + `institution`/`account`/`account_balance` + a manual
balance-entry endpoint + one net-worth chart. That is a usable application in
~40–60 hours, and it resolves the manual-accounts open question as *yes*. Then
the generator, then the MCP connector, then the reporting schema — and when the
reporting schema arrives, re-point the existing dashboard at the views. Restate
FR-5.5 as an end-state invariant with a test, not a precondition.

**Recommended v1 cut** (~150–200 hours):

- **In:** storage model with provenance, idempotency, soft delete (FR-4.1–4.3,
  4.5–4.7) · named volume + forward-only migrations (FR-9.1, 9.3) · DEV
  assertion and environment visibility (FR-10.3–10.5) · the deterministic
  generator (FR-11.1, 11.2, 11.4) · the MCP connector behind the general
  connector interface (FR-1) · **manual account/balance entry** · net worth and
  account drill-down (FR-5.1 partial, 5.2, 5.3) · ad-hoc SQL against the
  restricted role (FR-6.1–6.3) · the projection engine with **two** models
  (FR-8.1–8.6, 8.8) · OpenAPI + generated client + drift check (FR-12.2, 12.3,
  12.5) · the Aspire boundary architecture test · envelope encryption, log
  redaction, DTO discipline, provider-text escaping, CSP, `.gitignore`.
- **Wait:** FR-2 entire (Plaid: token exchange, cursor sync, webhooks, re-auth,
  sandbox) · Azure Container Apps · the Release and Deploy pipelines beyond
  tag→build→push · SBOM, signing, attestation, multi-arch · allocation and
  cash-flow dashboards · saved/parameterised/exportable reports (FR-6.4–6.6) ·
  scenario comparison and the remaining four models · backup/restore *until PRD
  holds real data* · simultaneous environments (FR-10.1) · WebAuthn, rate
  limiting, HSTS · E2E as a required check · the ~990-test target · coverage
  floors outside `Domain`/`Projections`.

Cutting FR-2 alone removes 80–120 hours and retires the provider sandbox, the
webhook path, the duplicate-account open question, and a large share of the
security findings below.

---

---

## DISPOSITION — what we are doing about each of the 89

Added 2026-09-18 under `D-029`. **A finding with no disposition is not a finding;
it is an observation.** Severity says how bad this is if it ships; disposition says
what we are doing about it. They are different axes, and a Must Fix can legitimately
be CUT — the defect is real and the surface is not being built.

Every finding below is dispositioned against **S-0's recommended v1 cut**, not
against the full specified scope.

| | Count | Means |
|---|---|---|
| **FIX** | 61 | In v1 scope, will be corrected. |
| **DEFER** | 14 | Not now — each carries the **trigger** that reopens it. A DEFER with no trigger is a FIX nobody scheduled. |
| **CUT** | 13 | Dissolves because the surface is being removed. |
| **ACCEPT** | 1 | Real, and being lived with, for a stated reason. |

**On the 61 FIX.** That is a high number and it should not be read as 61 units of
work. Roughly half are one-line document corrections — a wrong citation, a
contradictory sentence, a count that disagrees with itself — several of which are
fixed by the same edit. The genuinely substantial ones are M-1, M-6, M-8, M-10,
M-12, M-13, M-16 and M-24. **If this list still looks too long, the lever is the
scope decision, not the dispositions:** cutting more surface moves items from FIX to
CUT, and no amount of re-triage will.

**Where a finding splits**, the disposition is the one that governs the v1 decision
and the note names the other half — M-8 and M-20 are the two that genuinely split.

| Finding | Disposition | Reasoning / trigger |
|---|---|---|
| `M-1` | **FIX** | v1 - net worth is the v1 dashboard; a defect in a founding principle propagates. |
| `M-2` | **FIX** | v1 - the DEV assertion is in v1 and this is the least recoverable failure in the set. |
| `M-3` | **CUT** | Feature cut: FR-10.1 simultaneous environments defers. The false claim in environments.md is corrected in the same edit - a cut surface still may not be described wrongly. |
| `M-4` | **FIX** | v1 - the SPA must reach the API in v1. PublishAsStaticWebsite/YARP same-origin is the v1 answer and also settles S-40. |
| `M-5` | **FIX** | v1 - migrations ship in v1; a separate Migrator project is the fix. |
| `M-6` | **FIX** | v1 - ad-hoc SQL on the restricted role is the marquee v1 feature and D5 is the reason it is safe. |
| `M-7` | **FIX** | v1 - the MCP connector ships in v1, so these columns exist and are published. |
| `M-8` | **FIX** | v1 for the resolver split only - it is what makes Constitution II structural rather than conventional. The ITimeSteppedModel tier DEFERS, trigger: the first model needing multi-year iteration. |
| `M-9` | **FIX** | v1 - storage is v1 and the restatement fixture fails at build step 2 without it. |
| `M-10` | **FIX** | v1 - the migration path from the YAML files guarantees the double-count on day one. |
| `M-11` | **CUT** | Declare v1 single-currency, drop the generator's second-currency state, CHECK one reporting currency. The fx_rate table returns with the first foreign account. |
| `M-12` | **FIX** | v1 - the MCP connector is v1 and absence is its only removal signal; this one destroys real history. |
| `M-13` | **FIX** | v1 - name argon2id + session cookie now (an evening, per C-13). Distinct hosts and __Host- cookies land with it. |
| `M-14` | **FIX** | v1 - the MCP connector uses OAuth 2.1 PKCE in v1, so the callback exists in v1. |
| `M-15` | **DEFER** | Trigger: the export endpoint ships (FR-9.4, not in v1). |
| `M-16` | **FIX** | v1 - envelope encryption is explicitly in the v1 cut, and AAD is what makes it sound. |
| `M-17` | **DEFER** | Trigger: PRD first holds real data - the same trigger as FR-9.2. The one-line 'the KEK is part of the recovery set' statement lands with the v1 encryption work regardless. |
| `M-18` | **DEFER** | Trigger: PRD first holds real data. Before that there is nothing to lose. |
| `M-19` | **FIX** | v1 - migrations ship in v1 and the fix is mostly telling the truth in the document. |
| `M-20` | **FIX** | Constitution I half FIXED this crossing (Amendment 2). The schedule table and the incomplete-set state DEFER, trigger: the first bracket-aware model. |
| `M-21` | **DEFER** | Trigger: the first tax-aware model. Four of six models defer, and these are their inputs. |
| `M-22` | **DEFER** | Trigger: the first tax-aware model. |
| `M-23` | **FIX** | v1 - two models ship in v1, the field is cheap, and it is the false-confidence guard. A regression from the source system. |
| `M-24` | **FIX** | v1 - the UI ships in v1 and retrofitting semantic tables and chart alternatives after the component library is chosen is the expensive path. |
| `M-25` | **FIX** | v1 - environment visibility (FR-10.4) is in the v1 cut and a text label costs nothing. |
| `M-26` | **FIX** | v1 - the repo is public from the first commit, so the licence and the disclaimer are first-commit items. |
| `M-27` | **FIX** | The fork-PR and permissions half is v1 (public repo). The sandbox-credential half CUTs with FR-2. |
| `S-1` | **FIX** | v1 - storage. |
| `S-2` | **DEFER** | Trigger: conditional money becomes reachable (needs a second source; FR-5.4 defers). |
| `S-3` | **FIX** | v1 - MCP connector failure paths. |
| `S-4` | **DEFER** | Trigger: the dataset reaches the NFR-2 profile. Unmeasurable at v1 scale. |
| `S-5` | **FIX** | v1 - atomic ids are what make tasks derivable from the spec. |
| `S-6` | **FIX** | v1 - the hygiene job is the sole enforcement of Constitution IX on a public repo. |
| `S-7` | **FIX** | v1 - document fix, cheap. |
| `S-8` | **DEFER** | Trigger: scenario comparison ships (FR-8.7 defers). |
| `S-9` | **FIX** | v1 - integration tests against real Postgres are in the v1 cut. |
| `S-10` | **FIX** | v1 - same layer. |
| `S-11` | **CUT** | S-0 drops coverage floors outside Domain/Projections. |
| `S-12` | **CUT** | S-0 moves E2E off the required path. |
| `S-13` | **CUT** | CUTs with FR-2. |
| `S-14` | **FIX** | v1 - the drift check is in the v1 cut. |
| `S-15` | **CUT** | The zero-flake SLA is a team policy; S-0 drops it. |
| `S-16` | **FIX** | v1 - the generator is in the v1 cut and every test sources from it. |
| `S-17` | **FIX** | v1 - storage; the constraint as written cannot be created. |
| `S-18` | **CUT** | CUTs with the allocation dashboard. The PATCH endpoint naming a non-existent column is corrected in the same edit. |
| `S-19` | **FIX** | v1 - a triggered sync exists in v1 and has no mechanism. |
| `S-20` | **FIX** | v1 - the architecture test is one test and the whole point of D2. |
| `S-21` | **FIX** | v1 - S-0 and this agree: auth and the audit writer move to step 2. |
| `S-22` | **DEFER** | Trigger: the first tax-aware model. |
| `S-23` | **DEFER** | Trigger: the first tax-aware model. |
| `S-24` | **FIX** | v1 - money arithmetic is v1; one decision plus one midpoint test. |
| `S-25` | **ACCEPT** | v1 - single user, single process, one operator. The role split and hash chain return when the audit log is relied on by someone other than its writer. |
| `S-25a` | **FIX** | v1 - self-hosted Compose is the primary target. |
| `S-25b` | **FIX** | v1 - this writes into the developer's DEV volume on every test run. |
| `S-25c` | **FIX** | v1 - blocks step 2 the moment AddNpgsqlDbContext is added. |
| `S-26` | **DEFER** | Trigger: any export path ships. The per-sink escaping rule is written down now even though nothing exports yet. |
| `S-27` | **FIX** | v1 - PATCH endpoints are in v1 and provenance forgery defeats Constitution IV invisibly. |
| `S-28` | **CUT** | CUTs with FR-2. |
| `S-29` | **DEFER** | Trigger: PRD holds real data, or any second person's data is ingested. |
| `S-30` | **FIX** | v1 - telemetry is on from step 1, so the filter is needed from step 1. |
| `S-31` | **DEFER** | Trigger: the export endpoint ships. |
| `S-32` | **FIX** | v1 for the health-degraded staleness signal - a scheduled sync exists in v1 and a silent stop is the likeliest real failure. The outbound notifier DEFERS with unattended operation. |
| `S-33` | **FIX** | v1 - Compose ships in v1 and an empty required variable defeats M-2's guard. |
| `S-34` | **FIX** | v1 - Compose ships in v1. |
| `S-35` | **FIX** | v1 - dashboards ship in v1 and this is Constitution II at the last inch. |
| `S-36` | **FIX** | v1 - the projection engine ships in v1 and the refusal is its core interaction. |
| `S-37` | **FIX** | v1 for run staleness and failed-sync state; truncated-result display DEFERS with the export. |
| `S-38` | **FIX** | v1 - starter reports are nearly free, double as FR-6 fixtures, and are the public demo. |
| `S-39` | **FIX** | v1 - resolve the direction before either side is built. |
| `S-40` | **FIX** | v1 - settled by M-4's same-origin fix. |
| `S-41` | **FIX** | v1 - seeding ships in v1. |
| `S-42` | **FIX** | v1 - document fix, and the omitted guard is the one that survives a code bug. |
| `S-43` | **FIX** | v1 - the Aspire CLI install path is the first thing a stranger needs and it is documented nowhere. |
| `S-44` | **FIX** | v1 - blocks per-surface task derivation in the new repo. |
| `S-45` | **FIX** | v1 - the spec-kit handoff does not gate without it. |
| `S-46` | **FIX** | v1 - blocks the new repo's first hour. |
| `C-1` | **DEFER** | Trigger: the first multi-year model. Widening result and /runs/compare is far cheaper before the UI than after. |
| `C-2` | **FIX** | v1 - without the slug as source_id, ten years of prior citations stop resolving. |
| `C-3` | **FIX** | v1 - cheap, and the document currently claims a completeness it does not have. |
| `C-4` | **FIX** | v1 - cheap terminology fix. |
| `C-5` | **FIX** | v1 - cheap; and the provenance ordering genuinely disagrees between two documents. |
| `C-6` | **FIX** | v1 - a read-only narrow floor comes largely free with M-24's AA reflow criterion. |
| `C-7` | **FIX** | v1 - one paragraph, and joint accounts are the norm. |
| `C-8` | **CUT** | CUTs with the Azure target. |
| `C-9` | **CUT** | CUTs with the reduced release pipeline. |
| `C-10` | **FIX** | v1 - the hygiene job ships in v1 and the allow-list is public from its first entry. |
| `C-11` | **CUT** | Aligns with M-3: FR-10.1 defers, so the triple guard and the stub-vs-vendor contract test go with it. |
| `C-12` | **CUT** | S-0 already cuts to two models. |
| `C-13` | **CUT** | WebAuthn, rate limiting and HSTS defer; argon2id is named now under M-13. |

*Dispositions per `external-claim-provenance` skill v1.0.0.*

---

---

## SCOPE CALL — settled 2026-09-18 (`D-030`)

**The dispositions below were assigned against `S-0`'s "ship the tool" v1 cut.
Kyle ruled differently, and the ruling changes how this register should be read.**

He ruled the goal is **practising the stack**, not shipping a tool — and then,
asked for the leanest option under that goal, ruled **one complete vertical slice,
then stop and assess**.

That is not a smaller version of `S-0`'s v1. It is a different shape:
`slice-01.md` — manual balance entry to a net-worth chart, ~50–80 hours, through
every layer once, with **no PRD environment, no credentials, no connector, and
generated data only**.

### What this does to the 89

**Ten findings are live.** The other 79 are **not deferred by judgement — their
surface does not exist in the slice.** No credentials means M-16/17/18 are
unreachable; no connector means M-7/12/14; one environment means M-2/25; no
reporting or projection engine means M-6/8/15/20–23.

| Live in slice 01 | |
|---|---|
| **M-1** | carry-forward within an active window; `accounts_carried` per row |
| **M-3** | Spike A — environment selection moves off launch profiles |
| **M-4** | Spike B — same-origin proxy, no origin configured anywhere |
| **M-5** | Spike C — a separate one-shot Migrator project |
| **M-9** | the partial unique index and the three-statement write order |
| **M-10** | split `account` identity from `account_source` now |
| **M-11** | declare single-currency; one `CHECK` |
| **M-24** | semantic table, keyboard, contrast, chart tabular equivalent |
| **M-26** | `LICENSE` + non-affiliation line, first commit |
| **M-27** | `permissions: contents: read`; SHA-pinned actions |

Supporting, in the layers they touch: `S-11`, `S-14`, `S-17`, `S-24`, `S-25a`,
`S-25b`, `S-25c`, `S-27`, `S-33`, `S-34`.

### Where the FIX/DEFER/CUT column still stands

It remains the right answer **for the full specified scope**, and it is what a
later slice should be triaged against. Read it as: *if this surface is ever
built, here is what is owed.* The slice does not overwrite it — `D-030` defers
the rest rather than striking it.

**One correction it forces.** The 61 FIX was computed under "ship the tool". Under
"practise the stack" roughly fifteen findings flip the other way — `S-11`, `S-12`,
`S-13`, `S-15` return with the test pyramid; `S-28` and `M-27`'s sandbox half
return with the connector; `C-8` and `C-9` return with the cloud target and the
release pipeline. Those are **not** re-dispositioned here, because none of them is
reachable in slice 01 and re-triaging unreachable surface is the exact busywork
this section exists to prevent. They are triaged when the slice that needs them is
commissioned.

---

## MUST FIX

### M-1
**Disposition: FIX** — v1 - net worth is the v1 dashboard; a defect in a founding principle propagates.

The deepest finding in the review, because the defect is in a *founding
principle*, not in a document derived from one.

Accounts do not report on the same days — brokerages give no weekend rows, a
CSV import gives month-ends, an MCP sync gives whatever the provider held that
morning. A per-date sum over the rows that exist therefore **sums a different
subset of accounts on every date**, producing a sawtooth in which net worth
drops by the value of every account that did not report that day.

The only correct daily net worth is `sum over accounts of (last balance with
as_of_date <= D)` for accounts active on D. That is carry-forward, and view
rule 2 bans it: *"No forward-fill, no interpolation, no gap-filling. A series
that stops, stops."*

The spec never distinguishes **"this account stopped reporting forever"**
(what Constitution III was actually written to prevent) from **"this account
did not report on Tuesday"** (a live account whose last known balance is still
its balance).

The UX seat reached the same place from the display side: when a closed account
drops out of the aggregate, the net-worth line **steps down by its last
balance**, which on screen is indistinguishable from losing that money. Roll
over a 401(k) and the chart shows a cliff. `api.md`'s `coverage` object is a
single top-level field per response, so it cannot even express that coverage
differs point to point within the series it accompanies.

**Fix.** Amend Constitution III to say what it means: no fill *past the end of a
series*, no interpolation *between* observations, and carry-forward *within* an
account's active window as a distinct, labelled operation. Specify
`v_net_worth_daily` as `DISTINCT ON (account_id) … ORDER BY as_of_date DESC` per
date over accounts where `D BETWEEN opened_on AND coalesce(closed_on,
'infinity')`, with per-row `accounts_carried` and `max_staleness_days` so a
carried point is visibly carried. Add **FR-5.6**: an aggregate series MUST
render a change in *membership* visually distinct from a change in *value* —
the series breaks at the date, with an annotation. Add **FR-5.7**: any
aggregate with `coverage.accounts_missing_data > 0` carries a completeness
marker on the figure itself.

### M-2
**Disposition: FIX** — v1 - the DEV assertion is in v1 and this is the least recoverable failure in the set.

The most dangerous single finding. `environments.md` calls them *"Three guards,
each independently sufficient"*. Against one routine mistake — a PRD connection
string pasted into the DEV profile while debugging a PRD data problem, with
both stacks running as FR-10.1 requires — all three pass:

1. **Guard 1 passes.** *"DEV-only routes are not registered in PRD"* keys on the
   **environment name**. The process was launched `--launch-profile dev`, so
   `/dev/reset` registers. Only the connection string is wrong.
2. **Guard 2 passes.** The assertion *"verifies the connection string names the
   DEV database"* — a **string inspection of configuration**, not a question to
   the database. `portfolio` is a substring of `portfolio_dev`; and even exact
   match proves only what configuration *says*, not what the session is
   connected to. `PGDATABASE`, a pooler mapping or an `Options` mismatch decides
   that.
3. **Guard 3 never engages.** It blocks *the DEV role* from reaching PRD. Here
   the process holds **PRD credentials**, so guard 3 is bypassed by not being
   involved. Separately, it names only `DROP` and `TRUNCATE` — a reset
   implemented as `DELETE FROM` or EF Core `EnsureDeleted` is not covered.

**And the specified test cannot catch it.** FR-13.4 tests environment = PRD,
where guard 1 alone already makes the route absent. The one state in which all
three guards are simultaneously load-bearing — environment DEV, connection
string PRD — is never tested.

**Fix.** Guard 2 asks the database, not the config: on the exact connection
about to be used, `SELECT current_database(), current_user, inet_server_port()`
and require an exact match against a DEV allow-list. Add a fourth,
non-configuration guard: a `dev_marker` row written only by the seeder and read
**inside the same transaction** as the destructive statement, so the *target
database* must assert it is DEV. Widen guard 3 to `DELETE`/`UPDATE` and make it
structural (separate `pg_hba` entries, or separate instances — which the
simultaneous-run requirement already justifies). Replace the FR-13.4 test with a
matrix over all four (environment name × connection target) combinations.

### M-3
**Disposition: CUT** — Feature cut: FR-10.1 simultaneous environments defers. The false claim in environments.md is corrected in the same edit - a cut surface still may not be described wrongly.

`environments.md` is built on this:

> ```sh
> aspire run --launch-profile prd
> aspire run --launch-profile dev
> ```
> Both run **simultaneously** — separate volumes, databases and ports.

The Aspire CLI does the opposite. `RunCommand` unconditionally calls
`FindAndStopRunningInstanceAsync` **before** starting, with no opt-out flag, and
the lookup is keyed on **the AppHost file path only** — the launch profile is
not part of the key. Aspire's own user-facing string states the constraint:

> "A running instance of this AppHost was found and will be stopped. To run
> multiple isolated instances simultaneously, run from different directories
> such as git worktree directories."

`--isolated` is not a per-profile isolation mechanism either: it sets
`DcpPublisher__RandomizePorts=true` and copies user-secrets to a throwaway id.
No volume, container-name or database isolation.

Secondary error in the same claim: a `launchSettings` profile sets
`applicationUrl`/env for the **AppHost process**; it does not assign ports to
Postgres, Api, Worker or the Vite resource. Those come from the app model, so
`deployment.md`'s `ports | launch profile` row is wrong.

**So FR-10.1 is unimplementable as specified**, and `environments.md`'s *"Both
can run at once, so nothing tempts anyone to stop PRD"* **inverts the actual
behaviour** — the tool stops PRD for you, without asking, the first time you
start DEV. That is day-one behaviour on the mechanism everything else in
`environments.md` rests on.

**Fix.** Drop launch profiles as the environment selector. Parameterise the app
model with `builder.AddParameter(...)`/configuration (volume name, database
name, port base, environment name) fed from an `ASPIRE_ENVIRONMENT`-style input,
not from `launchSettings.json`. Give each stack **its own AppHost directory** —
a git worktree, or two thin AppHost projects in separate directories sharing one
app-model library — which is exactly what Aspire's message prescribes; add
`--isolated` for port randomisation. Also set
`WithLifetime(ContainerLifetime.Session)` or give each stack distinct resource
names: persistent-lifetime containers get a **stable** DCP name with no random
suffix, so two stacks with the same resource name collide on the container name.

Note this interacts with M-2 but does not resolve it: guard 2 inspecting
configuration rather than asking the database is wrong however the stacks are
launched.

### M-4
**Disposition: FIX** — v1 - the SPA must reach the API in v1. PublishAsStaticWebsite/YARP same-origin is the v1 answer and also settles S-40.

`plan.md` claims *"Service discovery supplies the API's address, so no
environment ever hardcodes an origin."*

`WithReference(api)` injects `services__api__http__0` into the **Vite dev-server
process**. Vite exposes to client code only variables it has been told to expose
— by default those prefixed `VITE_`, via `import.meta.env`. `services__api__http__0`
is not among them, so it never reaches the browser bundle. Aspire's Vite
integration does not bridge this; the config shim it injects does exactly one
thing, HTTPS.

For the published targets it is worse: **Vite bakes `VITE_*` into the bundle at
build time, while Aspire sets environment variables at runtime.** In the Compose
target, where `web` is a static frontend, the API origin is frozen at
image-build time and Compose `.env` cannot change it — so **FR-10.5 ("the same
build artifact runs in both") breaks**.

This surfaces at step 1 of the build order, when the React shell cannot call
`/health`, and gets fixed then by hardcoding an origin — the exact thing the
spec forbids.

**Fix.** Adopt Aspire's supported pattern and say so explicitly: publish the
frontend with the YARP overload `PublishAsStaticWebsite(apiPath: "/api",
apiTarget: api)`. The browser then uses a **same-origin relative path** in every
target and no origin is configured anywhere. Mirror it in dev with a Vite
`server.proxy` entry fed by `.WithEnvironment("ASPIRE_API_URL",
api.GetEndpoint("http"))`, read in `vite.config.ts` on the Node side where the
variable is visible. Note in the spec that `PublishAsStaticWebsite` is
`[Experimental("ASPIREJAVASCRIPT001")]`.

### M-5
**Disposition: FIX** — v1 - migrations ship in v1; a separate Migrator project is the fix.

`plan.md` assigns migrations to the Worker and declares *"the API and worker
wait for Postgres to be healthy"* — the API waits for **Postgres**, not for
migrations.

Aspire's only "wait until this finished" primitive is `WaitForCompletion`, which
requires the dependency to **terminate** with a given exit code. Aspire's own
doc comment shows the intended shape: a dedicated short-lived migration tool
that dependants wait on. But the spec's Worker is long-running — *"scheduled
ingestion, migrations, seeding"* — so it never exits. `WaitForCompletion(worker)`
would hang forever, and `WaitFor(worker)` waits only for *healthy*, which says
nothing about migration completion.

So the API starts against a schema that may be empty or half-migrated — an
intermittent startup failure, or EF Core queries against missing columns: the
classic case that passes locally and fails on a cold `docker compose up`. It
also contradicts `deployment.md`'s claim that *"Migrations run as an explicit
step before the new version serves traffic — a separate `worker` invocation"*.

**Fix.** Split migrations into a **fourth project resource** that runs to
completion and exits (`src/Migrator/`, not `Worker/`):

```csharp
var migrator = builder.AddProject<Projects.Migrator>("migrator")
                      .WithReference(db).WaitFor(db);
api   .WithReference(db).WaitForCompletion(migrator);
worker.WithReference(db).WaitForCompletion(migrator);
```

This is the same artifact in Compose (a one-shot service with `restart: "no"`
that `api` and `worker` `depends_on: { condition: service_completed_successfully }`),
so `deployment.md` becomes true rather than aspirational. Update the solution
layout, the dependency diagram and the ordering sentence together.

### M-6
**Disposition: FIX** — v1 - ad-hoc SQL on the restricted role is the marquee v1 feature and D5 is the reason it is safe.

D5 is the spec's proudest security claim: *"`GRANT` cannot be argued with,
whereas query parsing can be fooled."* The claim is right in principle and the
specified implementation does not deliver it.

- **The timeout is user-disableable.** `statement_timeout` is a `USERSET` GUC.
  Because D5 forbids inspecting query text, nothing stops a submitted query
  beginning `SET statement_timeout = 0;` (Npgsql accepts multiple statements
  per command). Without multi-statement: `SELECT set_config('statement_timeout',
  '0', false)` is one ordinary read, and with connection pooling it persists to
  the *next* report on that connection. FR-6.3's control has no enforcement, and
  the verification row tests a cooperative query.
- **"Nothing else anywhere" is not what PostgreSQL grants.** A new role remains
  a member of `PUBLIC`: `CONNECT` on the database, `TEMP` on the database
  (`CREATE TEMP TABLE x AS SELECT …` writes to the volume, uncovered by
  `temp_file_limit`), `EXECUTE` on every built-in function (`pg_sleep`, the
  whole `pg_catalog` read surface), and on PostgreSQL < 15 `CREATE` on schema
  `public`. Any extension later installed into `public` — `dblink`, `http`,
  `postgres_fdw` — is `EXECUTE` to `PUBLIC` on install, converting this role
  from read-only to outbound-network-capable with no change to the stated
  grants.
- **View ownership is never assigned.** If the reporting role owns the views,
  ownership outranks every `GRANT` and it can `DROP` them.
- **The views are not `security_barrier`.** PostgreSQL may push a caller's qual
  below a view's own filters. Every view here filters something that matters
  (`deleted_at`, superseded rows, `app` columns). A non-leakproof operator in
  the caller's `WHERE` — a division that errors, a cast that errors — evaluated
  on a row the view was meant to exclude leaks that row through the error
  message. `api.md` mandates RFC 9457 problem details with a populated
  `detail`, which is the exfiltration channel.
- **The row cap has no mechanism.** A query returning one row can consume
  unbounded CPU, memory and temp space.

**Fix.** Specify the bootstrap DDL as a named, tested artifact, not prose:
`REVOKE ALL ON SCHEMA public FROM PUBLIC`, `REVOKE TEMP, CREATE ON DATABASE …
FROM PUBLIC`, `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA pg_catalog FROM
PUBLIC` with an explicit re-grant list, `ALTER ROLE reporting NOINHERIT
CONNECTION LIMIT 2 SET search_path = reporting SET work_mem = '32MB'`, views
owned by a separate migration role and created `WITH (security_barrier = true)`,
extensions installed into a schema the reporting role cannot reach. Enforce the
timeout **client-side** via Npgsql `CommandTimeout` (which issues a real cancel)
plus a watchdog calling `pg_cancel_backend`. Implement the row cap by reading
N+1 rows from the reader. Replace the two verification rows with hostile tests,
and add a **privilege-snapshot test**: enumerate every object the role can
select and every function it can execute, fail on any diff against a committed
baseline.

### M-7
**Disposition: FIX** — v1 - the MCP connector ships in v1, so these columns exist and are published.

The redaction rule is scoped to log sinks: *"enforced by a redaction filter in
the logging pipeline and a test asserting that known secret-shaped values do not
appear in captured output."* Two database columns are not log sinks.

`ingest_run.error_detail` and `connection.last_error` have **no no-secret rule**.
The omission is provably an oversight rather than a judgement, because the
sibling column got one — *"`audit_event.detail` never contains a secret —
tested"* — stated twice, in `data-model.md` and `security.md`. Neither of the
other two columns appears in the verification table.

Provider failures are exactly where credentials surface: an expired Plaid item
returns a JSON error body; an OAuth refresh returns `invalid_grant` with request
context; a .NET `HttpRequestException` routinely carries the request URI
including query-string tokens.

`v_ingest_run` ("sync history and outcomes") is one of the eleven reporting
views. A non-`security_invoker` view checks table access against the **view
owner**, so it is a deliberate privilege bridge from the reporting role into
`app`. The arbitrary-SQL role reads it; `GET /reports/{id}/export` returns it as
CSV; `GET /connections/{id}` ("detail, including credential health") is the
natural carrier of `last_error` into an API response — colliding with FR-3.2,
*"Secrets never appear in a response, in any form, masked or otherwise."*

**Fix.** Make redaction a property of the *value*, not the sink: every
provider-derived error passes one redaction function before reaching a column, a
log or a DTO. Store a bounded classified error code plus a provider request id,
never a raw body. Exclude both columns from `v_ingest_run` and from the
`/connections` DTO. Extend the existing `audit_event.detail` test to cover
database columns.

### M-8
**Disposition: FIX** — v1 for the resolver split only - it is what makes Constitution II structural rather than conventional. The ITimeSteppedModel tier DEFERS, trigger: the first model needing multi-year iteration.

`IProjectionModel` is specified as declared inputs → `Validate` → a `Run` that
is a *"pure function of resolved inputs to a result"*, with one `model_key` per
scenario. Four things do not fit:

- **Multi-year iteration.** A Roth conversion ladder is N years where year *k*'s
  ending pre-tax balance, Roth basis, realised gains and age are year *k+1*'s
  opening state — and IRMAA keys off MAGI from **two years prior**, a lag the
  interface cannot hold. Nothing describes a per-year state vector, a horizon or
  a carry-forward. A 30-year projection, which is what "tilted towards
  retirement planning" promises, is not expressible.
- **Composition.** FR-8.9 requires *"one-at-a-time sensitivity analysis over any
  other model's inputs"*. Its input is *another model plus its input set*, whose
  legal values depend on the first field — not expressible as the static
  `input_schema` the UI renders from, and requiring a bespoke control that
  FR-8.3 forbids. It must call the registry and re-resolve inputs per
  perturbation, so it is neither pure nor host-free (FR-8.8).
- **Chaining.** The conversion model's output (taxable income added) is the ACA
  model's input (MAGI). With one `model_key` per scenario and no output-to-input
  binding, the user hand-copies a number between runs — destroying the
  input-hash provenance chain FR-8.6 and Constitution VI rest on.
- **`Validate` defeats its own justification.** D6 claims *"the type system
  enforces Constitution II rather than a convention doing it"*. But `Validate` is
  **model-authored code that constructs the resolved set** — a model that reads a
  null constant and puts `0` in it produces a perfectly typed call to `Run`. The
  rule remains a convention with a checklist. Worse, resolution needs data, yet
  `Projections` may *"reference nothing"* and its tests forbid I/O — so the
  architecture test in CI job 7 fails, or resolution has no specified owner and
  `POST /models/{key}/validate` has no implementer.

**D6's own validation test cannot fail for this reason.** It says porting six
models *"reveals it early and cheaply"* — but all six are single-shot
calculators. None iterates, so none exercises the gap.

**Fix.** Split resolution out of the model: `IProjectionModel` declares
`Metadata` and `Run(ResolvedInputs<T>)` only; one engine-owned `IInputResolver`
resolves from constants/policy/records, emits the refusal, and computes the
per-input hashes. `ResolvedInputs<T>` has no public constructor, so no model can
manufacture a resolved value from a null — then Constitution II is enforced by
one tested component instead of six, and FR-8.8 holds structurally. Add a
second tier — `ITimeSteppedModel` with a horizon, opening state, `Step(year,
state, inputs)` and a terminal state. Demote sensitivity from a model to an
**engine operation** (`POST /models/{key}/sensitivity`). Let a scenario
reference an ordered composition so a chained input keeps its lineage. Add a
**seventh** port-validation model that iterates, so D6's design test can
actually fail.

### M-9
**Disposition: FIX** — v1 - storage is v1 and the restatement fixture fails at build step 2 without it.

Two adjacent bullets in `app.account_balance` contradict each other:

> Unique on `(account_id, as_of_date, source_system)` where not deleted.
> A restated balance **inserts a new row** and sets `superseded_by` on the old one.

The restated row shares all three key columns. The old row is **not** deleted —
supersession is explicitly not deletion, and `v_balance_daily` excludes
superseded rows *separately*, so they still exist undeleted. Both rows sit in
the partial index and the INSERT fails.

This is the spec's own named edge case and a **required generator fixture**
(*"a restated balance for a date already recorded | proves supersession works"*),
so it fails at build step 2. The obvious in-the-moment fix — soft-delete the
predecessor — destroys the bitemporal audit trail the whole
`as_of_date`/`observed_at` split exists to provide, and does so silently.

**Fix.** `UNIQUE (account_id, as_of_date, source_system) WHERE deleted_at IS
NULL AND superseded_by IS NULL`. Note the write ordering this forces: a partial
unique *index* cannot be `DEFERRABLE`, so the pipeline must generate the new
uuid, `UPDATE old SET superseded_by = <new id>`, then `INSERT`, in that order,
in one transaction. Add `UNIQUE (superseded_by) WHERE superseded_by IS NOT NULL`
so a chain cannot fork, and `CHECK (superseded_by <> id)`. Specify that
re-ingesting an identical value is a no-op, not a new version.

### M-10
**Disposition: FIX** — v1 - the migration path from the YAML files guarantees the double-count on day one.

`app.account` and `app.transaction` are keyed `(source_system, source_id)`
*where not deleted*. There is no identity for a real-world account separate from
one source's record of it. Three consequences:

- **Delete/undelete cycles duplicate.** FR-4.5 soft-deletes a withdrawn record;
  providers routinely re-add one (a pending transaction that posts). The partial
  index permits a *second* row with the same `(source_system, source_id)`
  because the deleted one is excluded. The upsert's lookup then returns multiple
  rows with no specified disambiguation, breaking FR-4.2 by construction.
- **A re-added account orphans its history.** Primary keys are
  application-generated, so the second row gets a new uuid; ten years of
  balances, transactions and holdings stay bound to the old `account_id`. The
  account appears twice — once with all the history, once with none.
- **Two sources double the money.** The same real-world account from two
  connectors *must* become two live account rows, both summed by
  `v_net_worth_daily`. **The migration path guarantees this**: `accounts.yaml`
  imports under `source_system = 'import:yaml'`, and attaching a live connector
  to those accounts — the entire point of migrating — creates a second row per
  account.

**Fix.** Split identity from sourcing. Keep `app.account` / `app.transaction` as
internal entities with no source columns in the key; add `app.account_source` /
`app.transaction_source` carrying `(source_system, source_id, account_id,
deleted_at, first_seen_at, provenance)` with a **full** unique index on
`(source_system, source_id)` — so an undelete clears `deleted_at` on the row that
already owns that identity rather than minting a rival. That table is also where
a merge decision gets recorded. Define the winner rule `v_net_worth_daily`
needs: one balance per `(account_id, as_of_date)` chosen by `source_strength`
then `observed_at`.

### M-11
**Disposition: CUT** — Declare v1 single-currency, drop the generator's second-currency state, CHECK one reporting currency. The fx_rate table returns with the first foreign account.

Money is `numeric(19,4)` plus `currency char(3)` on five tables. **No table
holds an exchange rate, a reporting currency, or a conversion.** Yet
`v_net_worth_daily`, `v_allocation` and `v_cash_flow_monthly` all aggregate
across accounts — and the DEV generator is *required* to produce *"an account in
a second currency | proves currency is never assumed"*, while `testing.md` tests
*"currency mismatch rejection"*. The system deliberately manufactures the case
and deliberately refuses the arithmetic, with nothing specified in between.

`api.md` compounds it: money is `{amount, currency}` — one currency per value —
so `GET /dashboards/net-worth` has no shape in which a multi-currency answer
could be returned.

Every DEV environment is seeded with this by construction, so the first
dashboard render hits it on day one.

**Fix.** Add `app.fx_rate (base_currency, quote_currency, rate_date, rate
numeric(19,10), provenance…)` with full provenance, plus a `reporting_currency`
constant. A date with no rate is a null under Constitution II, so
`v_net_worth_daily` needs a `conversion_complete` flag and the dashboard
response a currency breakdown. **Or** declare v1 single-currency explicitly in
`spec.md`, drop the second-currency generator state, and add a CHECK that every
account shares the reporting currency — but do not leave the generator producing
a case the views cannot aggregate.

### M-12
**Disposition: FIX** — v1 - the MCP connector is v1 and absence is its only removal signal; this one destroys real history.

FR-4.5 soft-deletes on withdrawal. Acceptance scenario 3 closes an account that
"has stopped reporting". Two edge cases in the same document — *"A sync fails
halfway through"* and *"A credential expires or is revoked mid-sync"* — describe
exactly the situation where records are absent for a reason that is **not**
withdrawal. Nothing distinguishes them, and no requirement defines the detection
rule for "has stopped reporting".

The connector interface has one fetch shape — *"a typed slice … since a
cursor"* — with no way to say whether a result is a **complete snapshot** or a
**delta**. MCP ingestion (FR-1.3) is a full-listing retrieval with no cursor and
no removal signal, so **absence is its only signal**, while FR-2.2's cursor sync
has explicit removals. `connection.cursor` is declared *"(opaque, per FR-2.2)"*
as if every connector had one.

Either the pipeline never reconciles by absence — and FR-4.5 is unmet for every
MCP source, i.e. for R1, the headline requirement — or it does, and a timeout or
revoked token during one sync **soft-deletes live records and closes live
accounts**. Constitution III then forbids ever refilling those series, so the
damage is permanent by design.

**Fix.** Make completeness part of the fetch result: `SliceResult { Scope,
Completeness: Snapshot | Delta, WindowFrom, WindowTo, NextCursor?,
ExplicitRemovals[] }`, with `describe` declaring which modes a connector
supports per slice. Reconcile by absence **only** for a `Snapshot` over a
declared scope, **only** in a run that reached `status = succeeded`. Add a
completeness flag to `ingest_run` and replace its success boolean with an
enumerated outcome including `partial`. Add an integration test: a mid-run
failure soft-deletes nothing and closes no account.

### M-13
**Disposition: FIX** — v1 - name argon2id + session cookie now (an evening, per C-13). Distinct hosts and __Host- cookies land with it.

**No FR in the set mentions authenticating a user to the platform.** FR-1.1 and
FR-2.1 authenticate the platform to *providers*; FR-3.x is credential storage.
The entire subject appears as one word in a build-order bullet (*"8. Hardening.
Auth, CSP…"*), one line in `security.md`, and one open-question row. `spec.md`
delegates it with *"NFR-1 Security. See `security.md`."* — so `/speckit-tasks`
derives no task, no acceptance scenario and no test obligation. Constitution
VIII's loopback-by-default rule likewise has no FR.

And the design as sketched breaks at the browser. **Cookies are scoped by host,
not by port.** `localhost:8080` (PRD) and `localhost:8081` (DEV) are the same
cookie host and the same *site*:

- PRD's session cookie is sent to the DEV listener and vice versa. All four
  isolation guards are server-side; none reaches the browser. **Constitution X
  is breached through the cookie jar.**
- `SameSite=Strict` contributes nothing — every other process on loopback (a
  Vite dev server, any `npm create` template) is same-site with PRD.
- If CSRF uses double-submit cookies, any local origin can read the CSRF cookie
  (cookies ignore port) and forge requests, so the control fails in exactly the
  deployment the spec calls primary.

Also unspecified: session lifetime, idle timeout, absolute expiry, session-id
regeneration on login (fixation), and the rate-limit key for *pre-auth*
requests — which with argon2id is a memory-exhaustion primitive against a
memory-limited container.

**Fix.** Add **FR-14** (platform access control): authentication required for
every non-health endpoint; session lifetime and revocation; loopback-by-default
and what it takes to change it; rate limiting on authentication; acceptance
scenario *"Given an unauthenticated client, when it calls any data endpoint,
then it receives 401 and no data."* Give each environment a distinct **host**
(`prd.portfolio.localhost` / `dev.portfolio.localhost`, or `127.0.0.1` vs
`127.0.0.2`) with `__Host-` prefixed cookies, as guard 5 with its own test.
Mandate the **synchronizer-token** CSRF pattern explicitly. Name the pre-auth
rate-limit key and cap concurrent argon2id operations below the memory limit.

### M-14
**Disposition: FIX** — v1 - the MCP connector uses OAuth 2.1 PKCE in v1, so the callback exists in v1.

*"a callback completes it"* appears **once, in prose**. The most
security-sensitive route in the system is in no endpoint table, no OpenAPI
document, and therefore covered by none of the contract tests — including
*"contract test over every endpoint's schema"*.

FR-1.1 specifies OAuth 2.1 with PKCE. **PKCE binds a code to the client, not to
a user session.** Nothing requires a `state` parameter, a session-bound verifier
lookup, single-use consumption, or an exact-match registered `redirect_uri`.

An attacker completes an authorisation flow against the provider for **their
own** financial account, captures the `code`, and gets the user to load
`http://localhost:<port>/api/v1/connections/callback?code=<attacker_code>` — a
bare navigation, an `<img>`, or a link, since the port range is documented and
the deployment is loopback. The app, holding one pending verifier and no state
to match, exchanges the code and stores the credential as the user's connection.
Attacker-controlled data is then ingested into PRD, poisoning every dashboard
and projection with attacker-authored transaction text.

`SameSite=Strict` does not save this, and cannot strictly protect the callback
anyway: a real provider redirect is itself a cross-site top-level navigation
that `Strict` strips the cookie from. That tension is unresolved and will be
resolved at implementation time in whichever direction makes the flow work.

**Fix.** Add the callback to the contract. Require a cryptographically random,
single-use `state` stored server-side against the initiating session with a
short TTL, matched before any exchange; the PKCE verifier stored server-side
keyed by that `state`; an exact-match fixed-port loopback `redirect_uri`; a
`Host` allow-list; and either a `SameSite=Lax` cookie scoped to the callback
route as a documented exception, or an interstitial same-site POST. Add a
contract test asserting rejection of absent, unknown, expired and already-used
`state`.

### M-15
**Disposition: DEFER** — Trigger: the export endpoint ships (FR-9.4, not in v1).

The highest-value endpoint in the API gets one table row and no controls: no
step-up authentication, no rate limit, no async treatment, no audit
precondition. `api.md`'s rate-limiting section names *"report execution and
projection runs — the two endpoints that can be made expensive"*; full export is
not among them, though it is by definition the most expensive and most
sensitive.

Browser-delivered attack is ranked **primary** in the threat model and the
financial record is asset #2. This endpoint converts one XSS, or the cookie-jar
problem in M-13, into complete disclosure in a single request — no pagination to
iterate, no rate limit to trip. A ten-line script beats every other control in
the document.

"Complete" is also never enumerated. The schema it traverses includes
`connection.credential_ref`, `audit_event.detail`, and `saved_report.sql_text`,
so FR-3.2's no-credential rule has no test surface on the one endpoint that by
design emits everything.

The `/system` group's auth posture is ambiguous generally. Only
`/system/environment` is stated unauthenticated (defensibly — its integrity
matters more than its confidentiality). `/system/version` sits in the same table
and will land in the same `MapGroup`, handing a remote attacker exact version
matching before authentication.

**Fix.** Give `/system/export` its own paragraph: step-up re-authentication
within a short window regardless of session validity; an asynchronous job
returning a run id, with the artifact fetched once from a single-use endpoint;
a hard daily cap in the rate-limit list; the `audit_event` write as a
precondition, not a side effect. Enumerate the export contents as a versioned
schema with `credential_ref` explicitly excluded, so the no-credential contract
test can cover it. State the auth posture of every `/system/*` route, with
`/system/version` authenticated.

### M-16
**Disposition: FIX** — v1 - envelope encryption is explicitly in the v1 cut, and AAD is what makes it sound.

The whole design is four sentences. Five things are missing, each deciding
whether the construction is sound:

- **No AAD.** Authenticated encryption authenticates the ciphertext against
  itself and nothing else. Nothing binds a credential blob to the row it belongs
  to. An attacker with database write access but **no KEK** — a returned stolen
  volume, a restored backup, a bug in the ingestion path — can move connection
  A's `(wrapped_dek, nonce, ciphertext)` onto connection B's row, or restore a
  **revoked** blob from an older row. FR-3.4's *"Revocation takes effect
  immediately"* is defeated by copying three columns.
- **No key identity.** Rotation with no `kek_id` beside each wrapped key means an
  interrupted rotation leaves a mixed population nothing can classify, and the
  old KEK can never be safely retired.
- **No algorithm, nonce rule or KDF.** AES-GCM with a 96-bit nonce is
  catastrophic on reuse; the spec never says whether the data key is regenerated
  per write (reauth and rotation both rewrite the same credential).
- **No table.** `data-model.md` asserts *"No table in this schema holds a secret
  value"* while D10 puts wrapped keys and ciphertext in the database. Whatever
  that table is, it is unmodelled.
- **KEK loss has no answer** — no escrow, no recovery procedure, and no startup
  behaviour. The tempting default is to start and mark every connection
  `status = 'error'`, which is indistinguishable from a provider outage and will
  be diagnosed as one.

**Fix.** Name the algorithm (AES-256-GCM or XChaCha20-Poly1305), require a fresh
data key per write, and mandate **AAD = `kek_id ‖ credential_ref ‖ version ‖
environment`** with decryption refused on mismatch. Model the table explicitly
and reword the "no table holds a secret value" sentence to "…in plaintext".
Specify rotation as a state machine ending in a query proving zero rows
reference the old KEK. Require the app to **refuse to start** on a missing or
non-decrypting KEK, naming the source.

### M-17
**Disposition: DEFER** — Trigger: PRD first holds real data - the same trigger as FR-9.2. The one-line 'the KEK is part of the recovery set' statement lands with the v1 encryption work regardless.

The KEK is *"supplied at runtime from a Docker secret or the host keyring"* and
*"never in an image layer, an environment file, or the repository"* — so a
database dump contains wrapped data keys and ciphertext but **not the KEK**. No
spec text covers backing up, escrowing or restoring it, or pairing a restore
with a specific KEK. And *"Rotating the KEK re-wraps data keys"* means every
dump taken before a rotation is permanently unwrappable once the old KEK is
discarded, with nothing telling the operator which.

**The verification is blind by construction.** FR-9.2's test *"backs up a seeded
database, restores into a fresh one, and asserts the contents match"* — seeded
**DEV** data, where credentials are *"none required"*. The one path with
encrypted material is the one path never tested.

This breaks in precisely the scenario ranked **primary** for self-hosted:
stolen laptop. The operator restores onto a new machine, restore reports
success, the financial record is intact, and **every institution connection is
dead** with no error until the next sync fails.

**Fix.** State that the KEK is part of the recovery set, with a documented
export/escrow step, and that a backup without its KEK restores data but not
access. Stamp a KEK identifier into the database so a restore detects a
mismatch at startup and refuses with a named error. Require rotation to warn
that prior backups are bound to the retired KEK, which must be retained as long
as they are. Extend the FR-9.2 test to seed an encrypted credential, restore
with the wrong KEK, and assert the named refusal.

### M-18
**Disposition: DEFER** — Trigger: PRD first holds real data. Before that there is nothing to lose.

The complete specification is *"a single documented command producing a
compressed dump, and it must be automatable."* That is a property of the
command, not a requirement that anything automate it. Nothing schedules a
backup, prunes old ones, verifies one after writing, or puts a copy anywhere but
the same disk as `portfolio-prd-data`. The only invocations are the operator
typing it and the upgrade step.

The realistic self-hosted state is **zero backups until the first upgrade, then
one, overwritten** — while PRD retention is marked *"permanent, backed up"* and
`docker compose down -v` is singled out as the one routine command that destroys
PRD data, mitigated by a backup nothing guarantees exists.

The dump is also the plaintext financial record. Its only protections are
location and git status — both addressing accidental repo publication, a
different threat. "Automatable" plus "outside the repository" means a scheduled
job writing an unencrypted `pg_dump` into a user directory, and default user
directories are commonly cloud-synced (OneDrive, Dropbox, iCloud), silently
moving the complete record into a third-party account not in the threat model.

**Fix.** The Compose stack ships a backup service, or the Worker owns a
scheduled job, on by default in PRD, with a retention policy, writing to a
bind-mounted host path that is an `.env` input — and documentation saying
plainly that a backup on the same disk is not a backup, and not in a
cloud-synced directory, with the reason. Encrypt as part of producing the dump,
using the same `ISecretStore` D10 already defines, and make the restore test
exercise the encrypted path. Record backup success/failure as a first-class row
so backup age is queryable. Add to the first-run checklist: a backup has been
taken and restored once, **before real data is entered**.

### M-19
**Disposition: FIX** — v1 - migrations ship in v1 and the fix is mostly telling the truth in the document.

FR-9.3 says migrations are *"versioned, forward-only"*. `cicd.md` then promises
*"Roll back on failure — to the previous digest, with the migration rollback
path documented per release"*. Forward-only means **there is no down migration
to document**, and the named rollback puts the old image against the
already-migrated newer schema — exactly the compatibility break the migration
made. Nothing requires the app to check the schema version it is talking to, so
the old image starts and writes malformed rows rather than refusing.

The upgrade sequence *"pull the new digests, back up, run migrations, start"*
has the backup in the right place with nothing verifying it before the
irreversible step, and no defined behaviour for the new image failing to start
after the migration has landed.

**Fix.** Replace the language with the truth: rollback across a migration is
restore-from-backup, and data written since is lost — say that in those words.
Make the upgrade a single scripted command that takes the backup, **verifies it
restores into a throwaway database**, then migrates. Require every image to
record the minimum and maximum schema version it supports and refuse startup
outside that range with a named error. State the supported upgrade-skip policy
for a self-hoster several releases behind.

### M-20
**Disposition: FIX** — Constitution I half FIXED this crossing (Amendment 2). The schedule table and the incomplete-set state DEFER, trigger: the first bracket-aware model.

The table holds one scalar `value` per row and is described as carrying *"…
thresholds, **bracket boundaries**"*. A federal bracket schedule is an ordered
set of (lower bound, upper bound, marginal rate) triples, replicated per filing
status and per year, paralleled by a separate LTCG schedule, the standard
deduction, the FPL table indexed by household size and by AK/HI, and IRMAA
tiers. `varies_by` is named but never typed; there is no `sort_order`, no
`applies_from`, no completeness notion.

**And the failure mode is silent.** Constitution II refuses on a **null**. A
bracket table assembled from independent rows fails by **missing rows**, not
nulls. A model that resolves brackets 1–6 of 7, or resolves MFJ boundaries for a
Single filer, gets seven non-null values and refuses nothing — returning a
tranche size that is confidently, silently wrong. That is the exact failure
Constitution II exists to prevent, arriving through a door it does not cover.

Separately, **state 3 ("working assumption") contradicts Constitution I**, which
says *"A figure with no source does not get stored… There is no 'roughly'
column."* The constitution's own Amendment clause says a principle quietly
weakened *"has not been amended; it has been broken."* No FR authorises state 3
either, so the feature is design with no requirement behind it — contradicting
traceability's *"no spec section without a requirement behind it."* (The device
is legitimate and carefully constrained in the source system; porting it next to
an absolute rule without amending that rule is the defect.)

Also unenforceable as written: *"No terminal action may depend on a state-3
value… Enforced at validation time, and tested."* Nothing in `projection_model`,
`scenario` or `projection_run` records whether a run feeds an irreversible
decision, so there is no flag for validation to read.

**Fix.** Add a `constant_schedule` / `constant_schedule_row` pair so an ordered,
multi-dimensional table is one addressable object with one provenance record and
a declared expected cardinality. Add a fourth state — **incomplete set** — that
resolves as a refusal naming the missing rows, so a partial schedule refuses
exactly as a null does. Amend Constitution I in its own Amendment section to
admit the openly-labelled assumption, and add an FR stating the constant states
and their constraints. Add an explicit `decision_class` on `scenario` for the
state-3 prohibition to key on, or downgrade that sentence from "enforced and
tested" to a documented convention.

### M-21
**Disposition: DEFER** — Trigger: the first tax-aware model. Four of six models defer, and these are their inputs.

The core tables have no person, taxpayer or household, and `app.account` has no
owner and no beneficiary. FR-8.5 constrains inputs to *"stored records, policy
and constants"*. Every named model needs at least one absent fact:

- Conversion sizing: filing status (bracket boundaries differ by ~2× between
  Single and MFJ) and age (RMD onset at 73 vs 75 by birth year; 59½; the 5-year
  conversion clock per conversion per owner).
- Subsidy-cliff headroom: household size and state of residence — the FPL
  denominator is indexed by household size and differs for Alaska and Hawaii,
  and whether the floor is Medicaid or a subsidised plan is state-by-state.
- Wealth threshold and income-shock reserve: whose wealth, and for a couple,
  which account belongs to which spouse.

`account_type` includes `retirement` with no owner, so in a married household
the system cannot tell whose IRA it is — which determines the RMD divisor, the
5-year clock and spousal beneficiary treatment.

Related: `app.scenario` carries **no provenance columns**. If filing status and
birthdate arrive as scenario parameters, a guessed value enters a projection
indistinguishable from a sourced one, and `input_hashes` proves *which* number
was used, never how strong it was — Constitution IV holding everywhere except
the one place a human types numbers.

**Fix.** Add `app.taxpayer` (`birth_date`, `medicare_eligible_on`,
`retirement_date`) and `app.tax_profile` versioned by `tax_year`
(`filing_status`, `state_of_residence`, `household_size`, `dependents`), each
with the provenance columns every other sourced table carries. Add `owner_id`
and `beneficiary_type` to `app.account`. Give `scenario` provenance columns, or
require each parameter to be `{value, source, source_strength}`.

### M-22
**Disposition: DEFER** — Trigger: the first tax-aware model.

Reality routinely puts several treatments in one account:

- A 401(k) holds pre-tax, designated Roth and (often) after-tax sub-balances.
  One value cannot describe it, and the conversion model needs the split.
- A traditional IRA with after-tax basis is subject to the **pro-rata rule**:
  the taxable fraction of any conversion is pre-tax ÷ total across *all*
  traditional/SEP/SIMPLE IRAs. Without a stored non-deductible basis and an
  aggregation rule, the model computes the taxable amount as 100% and is **wrong
  for anyone who has ever made a non-deductible contribution** — including
  everyone who has done a backdoor Roth.
- Roth accounts need contribution basis, per-conversion basis with its own date,
  and earnings separately — the ordering rules and the two distinct 5-year
  clocks are computed from exactly those buckets.
- `tax_free` conflates Roth, HSA and 529. `taxable` conflates a brokerage
  account with **municipal-bond interest, which is excluded from ordinary income
  but is in ACA MAGI** — the model named in FR-8.9. So the headroom model
  understates MAGI and reports cliff headroom that is not there, wrong in the
  dangerous direction, since the cliff is a discontinuity.

**Fix.** Move tax treatment onto a sub-balance entity: `app.account_tax_bucket`
(`bucket_type` ∈ pre_tax / roth_contribution / roth_conversion /
after_tax_basis / hsa / 529 / taxable, `basis_amount`, `established_on` for the
clocks, `conversion_year`, provenance), with balances and holdings carrying a
bucket reference. Add a security-level `tax_character` so muni interest reaches
MAGI correctly. Expand `account_type` to distinguish HSA, 529, 401(k)/403(b)/
457(b) and inherited IRA.

### M-23
**Disposition: FIX** — v1 - two models ship in v1, the field is cheap, and it is the false-confidence guard. A regression from the source system.

`app.projection_model` carries `input_schema` and `output_schema` and nothing
else. The seven Python models being generalised each carry, **by contract**, a
two-part `VALIDATION.` block — `HOW THE MATH WAS CHECKED:` and **`WHAT THIS
OUTPUT DOES NOT COVER:`** — plus a `FIDUCIARY_QUESTIONS` list. Verified: all 7
carry `FIDUCIARY_QUESTIONS`; 6 carry the coverage block. The ACA model's alone
names five distinct blind spots, including that it *"ignores cost-sharing
reductions, state-level subsidy, and the one-year lag between a coverage year
and the federal poverty guidelines the cliff rests on."*

None of that has a column, an API field or a display requirement. The spec has a
**mandatory-display** rule for conditional money (FR-5.4) and no equivalent for
a projection's declared limits.

This matters because the named models will optimise against one cliff while
ignoring others the spec never mentions: **IRMAA** (a cliff, keyed to MAGI two
years earlier), **NIIT** (3.8% above $200k/$250k MAGI), **LTCG stacking** (an
extra conversion dollar can push a dollar of gains from 0% to 15%, so the true
marginal rate far exceeds the displayed bracket), **state tax** (absent
entirely), and the **conversion↔ACA interaction** — the same dollar is
simultaneously bracket-filling and MAGI-raising, so two models each correct in
isolation will jointly recommend a conversion that detonates the subsidy cliff.

A user reads "convert up to $X to stay in the 24% bracket", acts, and takes an
IRMAA surcharge, an LTCG step and a lost subsidy the tool never mentioned —
while its provenance UI shows the number as fully sourced and non-conditional.
**This is a regression from the system being generalised, not merely an
omission.**

**Fix.** Add `coverage_limits` (required, non-empty) and `fiduciary_questions`
to `app.projection_model`; return both from `GET /models/{key}` and
`GET /runs/{id}`; make displaying `coverage_limits` alongside any run result
mandatory in the same binding language FR-5.4 uses, with a client conformance
test. Require every model to declare the marginal rate it actually computed and
every threshold it did **not** evaluate.

### M-24
**Disposition: FIX** — v1 - the UI ships in v1 and retrofitting semantic tables and chart alternatives after the component library is chosen is the expensive path.

A grep for `wcag|accessib|a11y|aria|keyboard|screen.reader|contrast|focus`
across `specs/` returns **zero genuine hits** (the nine matches are `aria`
inside *variant*/*variable*, and "keyboard" in the out-of-scope line). No WCAG
target, no keyboard requirement, no contrast rule, no focus management, no
accessible alternative for charts. `cicd.md` runs ESLint with no a11y plugin;
ten E2E journeys carry no accessibility assertion.

The two hardest cases are this app's core: dense financial tables are unusable
with a screen reader unless they are semantic `<table>` with real `<th>` scope,
and charts are unusable without a text alternative. Because the spec is silent,
the default outcome is div-grids and canvas charts — and retrofitting either
after the component library is chosen is expensive.

**Fix** (calibrated to a solo developer — the responsible minimum, not an audit
programme). Add to FR-7: WCAG 2.2 AA as the target, with gaps recorded rather
than undiscovered; keyboard operability with a visible focus indicator;
AA contrast in every theme; information never conveyed by colour alone; **every
chart has a tabular equivalent reachable from the same view** — nearly free
here, since dashboards are already composed from the reporting views and results
are already exportable, and it resolves the hardest a11y problem without
constraining the charting-library decision; semantic data tables with
programmatic headers. Make it verifiable: `eslint-plugin-jsx-a11y` in CI job 2,
and an `axe-core` scan with zero critical/serious violations over the existing
ten journeys — one Playwright fixture.

### M-25
**Disposition: FIX** — v1 - environment visibility (FR-10.4) is in the v1 cut and a text label costs nothing.

*"in the UI header, permanently, **with a distinct colour per environment**"* —
colour is the only differentiator specified for the header. Roughly 8% of men
have a colour-vision deficiency, and red/green is both the most reached-for
PRD/DEV pair and the most confused. FR-11.4's *"visually distinguishable"* with
no method named will be implemented as a tint.

The spec names the consequence itself: *"a user must never type a real
credential into a DEV instance believing it is PRD."* The inverse is worse and
unnamed — a user who believes PRD is DEV runs a destructive operation (see
M-2), or trusts generated numbers as real. And a greyscale screenshot posted to
a public issue is exactly the ambiguity FR-11.4 exists to prevent; a tint does
not survive greyscale.

**Fix.** Require a **text label as the primary signal** — `DEV — GENERATED DATA`
/ `PRD — REAL DATA` in the header, always present, never abbreviated to an icon,
never dismissible — with colour as reinforcement and a non-colour secondary cue.
Specify that the marker must be legible in a greyscale screenshot, which makes
FR-11.4 testable. For generated *rows*, require a textual marker, not a
background tint. Consider a distinct favicon and title prefix per environment,
since a user with both stacks running distinguishes tabs before pages.

### M-26
**Disposition: FIX** — v1 - the repo is public from the first commit, so the licence and the disclaimer are first-commit items.

R10 ("publishable repo/solution I could hand to someone") maps in
`traceability.md` only to persistence, Constitution IX and the data-hygiene job.
A grep for `licen[cs]e|copyright|trademark|non-affiliat|disclaimer` across
`specs/` returns **two hits, both about dependency licences**.

- **No LICENSE.** A public repo with no licence grants the recipient no rights;
  "hand it to someone" is not satisfied.
- **No statement that the recipient needs their own provider relationship.**
  Plaid requires each client to pass Compliance Review before Development or
  Production access; personal use appears to be a supported signup category with
  a capped free tier. So individual use looks permitted — *but only to someone
  who has themselves passed review*, and nothing tells them.
- **No trademark or non-affiliation notice.** Naming, logos and any
  Plaid/Monarch branding are governed by the providers' terms.
- **No advice disclaimer.** FR-8.9 produces Roth-conversion sizing and ACA
  subsidy headroom — outputs a reader treats as a recommendation. The spec has a
  mechanism for exactly this ("say so on its face", FR-5.4) and did not use it
  here. The only advice-adjacent text is an out-of-scope line, which is a
  statement about what will be built, not a property of the software.

**Verification caveat:** `plaid.com`, `monarchmoney.com` and `sec.gov` are
egress-blocked from this environment. The provider-terms points above rest on
**search-engine snippets, not primary text**, and must be checked against the
live terms before acting.

**Fix.** A `publication.md` making binding: an OSS licence in the first commit;
a "you must obtain your own Plaid team and Monarch account and are bound by
their terms" notice; an explicit non-affiliation line; no provider logos or
provider-derived project name without reading their brand guidelines; a
`disclaimer` field on every projection response with a matching display
requirement and contract test; and a pre-publication task to read both
providers' current terms and record the result as a decision entry.

### M-27
**Disposition: FIX** — The fork-PR and permissions half is v1 (public repo). The sandbox-credential half CUTs with FR-2.

`cicd.md` claims *"No job holds a real credential for a financial provider.
Sandboxes and stubs only."* But FR-2.5 requires the sandbox usable in DEV and
`testing.md` requires integration tests against it. **Plaid's Sandbox is not
credential-free** — it is reached with the `client_id` and secret of a real
Plaid team, the same team object that holds Production access. `environments.md`
compounds it: *"credentials that are not secret"*, which is the sentence that
produces a committed `appsettings.Sandbox.json`.

Separately, the repository-settings list is careful about SHA-pinned actions and
then **states no workflow-trigger policy and no `GITHUB_TOKEN` permissions**.
Three PR jobs execute PR-authored code (Build runs `postinstall`; E2E builds
images from the branch; Contract regenerates OpenAPI from the implementation).
Two requirements push an implementer toward `pull_request_target` precisely
because plain `pull_request` cannot satisfy them for forks — hygiene *"on every
push"* and *"All jobs are required checks"*. `pull_request_target` with a head
checkout is remote code execution with repository credentials: the exact outcome
the SHA-pinning bullet was written to prevent, reached by a different door.

**Fix.** Correct the claim: no job holds a *production* provider credential; the
sandbox pair is a real credential handled as a secret. Add a table listing every
secret the pipeline holds, its scope, and which workflows may read it. State the
trigger policy as a control: workflows executing PR code use `pull_request` only;
`pull_request_target`/`workflow_run` never check out PR head; fork PRs require
maintainer approval and reach no secret; every workflow declares
`permissions: contents: read` and escalates per job. Move sandbox tests off the
required path into a scheduled/dispatchable workflow using a GitHub Environment.
Add the sandbox `client_id` shape to the hygiene scan patterns.

---

## SHOULD FIX

Grouped by theme. Each was verified against the spec text.

### Requirements and testability

| ID | Finding | Seat |
|---|---|---|
| S-1 | **FR-4.2 contradicts supersession.** *"replaying a sync MUST NOT create duplicates or alter counts"* vs `testing.md`'s *"a restated balance inserts and links"* — an insert alters counts. FR-4.4's *"updates rather than duplicates"* opposes both. Three mutually exclusive oracles for the most-referenced integration test. | QA |
| S-2 | **FR-4.8 is internally incoherent** — *"excluded from, or explicitly flagged in, any aggregate that includes it"*, and the exclusion branch requires no disclosure, defeating Constitution V and FR-5.4 vacuously. | QA |
| S-3 | **Failure paths have no expected behaviour.** Five named integration tests (timeout, partial page, malformed record, expired credential, mid-run failure) have no oracle. FR-4.6's success boolean cannot express "partial". FR-1 has no MCP equivalent of FR-2.4's credential-staleness handling. | QA |
| S-4 | **NFR-2 is untestable and NFR-2–5 are untraced.** No dashboard named, no definition of "render", no percentile, no hardware, no transaction count; the five-minute sync cannot be measured in CI since live providers are barred. None appears in a traceability Verification cell. | QA |
| S-5 | **FR ids are not atomic.** FR-8.9 is six models under one id (and FR-13.3 attaches two test obligations to each); FR-5.1 is four dashboards; FR-7.1 is seven UI areas. FR-7.3 and FR-10.4 are the same requirement twice, mapped to different originals. | Spec-driven dev |
| S-6 | **FR-13.6 is unimplementable as written** — *"anything resembling personal financial data"* has no decision procedure, yet it is the sole enforcement of Constitution IX, whose failure the constitution calls permanent. | Spec-driven dev |
| S-7 | **`spec.md` leaks implementation** despite claiming not to: FR-6.2 restates D5's mechanism, FR-12.2/12.3 restate D11, FR-2.1–2.5 describe Plaid's specific flow — making FR-2 untestable against any second provider. | Spec-driven dev |
| S-8 | **The staleness edge case has no requirement.** FR-8.6 detects *changed* inputs by hash; it cannot detect an input that is simply old. A two-year-old balance and this morning's hash identically. | QA |

### Test automation

| ID | Finding | Seat |
|---|---|---|
| S-9 | **No container/isolation strategy for ~150 integration tests in ~2 min.** Container-per-test is 10–20 min; per-test databases need a migrated template (never mentioned); Respawn conflicts with the tests that must run outside a transaction (migrations-from-empty, `GRANT` checks, backup/restore). xUnit parallelism unstated. | Test automation |
| S-10 | **`Aspire.Hosting.Testing` cost is unbudgeted.** The app model includes Vite and Worker with health-check ordering — 15–60s per instance — while most Layer 2 tests need no host. Split into database-integration and app-model-integration with separate budgets. | Test automation |
| S-11 | **Coverage floors are enforced in a job that cannot execute the code.** `Infrastructure`/`Api`/Frontend floors are checked in the *unit* job, whose scope is `Domain` and `Projections` only. No collector, threshold tool, or cross-job merge is named. *"95% including every refusal path"* is not a coverage metric. | Test automation |
| S-12 | **E2E journeys violate the spec's own isolation rule.** One stack, one seed, ten mutating journeys; journey 10 wipes the database the other nine depend on. No reset mechanism, parallelism setting, or completion signal for the async sync in journey 2. | Test automation |
| S-13 | **A required merge check depends on a vendor sandbox and an inbound webhook a GitHub runner cannot receive.** `testing.md`'s own rationale condemns it: a suite depending on a live provider fails for reasons unrelated to the change. | Test automation |
| S-14 | **The OpenAPI drift check has no canonicalisation contract** — ordering, line endings, culture and generation method are all unspecified, making false diffs the likely steady state. Generation is also environment-dependent: a DEV-generated document advertises `/dev/seed` and `/dev/reset` as product contract and compiles them into the client. | Test automation, Software engineer |
| S-15 | **Zero-flake tolerance has no detection mechanism and a quarantine the required-checks rule forbids.** Concurrency cancellation actively destroys the repeat-run signal; no scheduled re-run, no result artifacts, no trait-based exclusion. | Test automation |
| S-16 | **The generator's contract, location and "declared ranges" are undefined** — yet tests must source all data from it and a required CI job fails on values outside ranges no document defines. It has no project in the solution layout. | Test automation |

### Architecture and data model

| ID | Finding | Seat |
|---|---|---|
| S-17 | **The `closed_on` CHECK constraint cannot exist.** PostgreSQL CHECK cannot reference another table, and `account_balance` has no `closed_on` column — so the one invariant singled out as too important for application code is enforced only by application code. The direction that actually happens (closing an account *after* balances exist) is unguarded either way. | Software engineer, Data architect |
| S-18 | **`v_allocation` is derivable from nothing.** No `asset_class` exists on any table; `security_type` is an instrument kind. "Look-through" has no weights table. `PATCH /accounts/{id}` names `asset class` and `display name` — neither is a column. `policy_rule.applies_to` is free text with no shared vocabulary, so `v_allocation_vs_policy` returns unevaluable for every rule. | Data architect, Technical writer |
| S-19 | **No dispatch mechanism for a triggered sync.** `POST /connections/{id}/sync` returns a run id; `api` and `worker` are separate containers; no queue, outbox or `LISTEN/NOTIFY` exists in any document. And no endpoint returns an `ingest_run` — `GET /runs/{id}` reads `projection_run` — so FR-7.4 has no surface for the longest operation in the system. | Software engineer |
| S-20 | **The architecture test cannot hold both halves.** `ServiceDefaults` exists to be referenced by `Api`/`Worker`, so a transitive walk fails on day one; a direct-only walk makes the `Domain`/`Projections` guarantee weaker than stated. Placed in Layer 1, reflection cannot see an unused package reference. `ServiceDefaults` is also missing from the dependency diagram. | Software engineer |
| S-21 | **Build order defers auth and the audit log to step 8, but steps 4–7 depend on both.** Step 4 uses real credentials against a system with no audit log, though FR-3.5 is unconditional. Step 5 ships the query endpoint whose rate limit is specified as *per-session* — and there are no sessions until step 8. | Software engineer |
| S-22 | **No tax lots.** One blended `cost_basis` per security per account per date; no acquisition date, no short/long split, no basis method, no realised-gain record. Any model funding a conversion's tax bill by selling assets cannot compute the gain, so it cannot compute MAGI — and the ACA answer downstream is wrong. | Retirement domain |
| S-23 | **No forward expense plan, retirement date, or income streams.** `v_cash_flow_monthly` is entirely backward-looking. The income-shock reserve has no required-expense denominator, and will silently adopt trailing spend — overstating for discretionary-heavy households and understating where retirement expenses (notably pre-Medicare premiums) have not started. | Retirement domain |
| S-24 | **Rounding semantics are unspecified** and the two engines disagree: PostgreSQL rounds half away from zero, .NET defaults to banker's rounding — so a dashboard figure and a report figure differ by a cent at every midpoint, the exact failure FR-5.5 exists to prevent. `numeric(19,4)` also truncates derived values silently. `currency char(3)` should be `varchar(3)` with a CHECK (`bpchar` blank-pads). | Data architect |
| S-25a | **The generated Compose file is not the topology `deployment.md` describes.** The publisher places every service on **one** network unconditionally, emits external endpoints as a bare `host:container` map (i.e. `0.0.0.0`, not loopback), and writes tag-based image names — there is no digest resolution anywhere in `Aspire.Hosting.Docker`. So the stated internal/front network split, the loopback-only API binding, and `cicd.md`'s "referenced by digest everywhere downstream" are all absent from the artifact. (One part *is* correct: non-external endpoints go to `expose:`, so the DB port genuinely is unpublished.) Fix by describing what is generated and moving the desired topology into `WithComposeServiceCustomization(...)` or a committed override file, with digest pinning as a release-pipeline rewrite. | .NET/Aspire |
| S-25b | **`DistributedApplicationTestingBuilder` mounts the real named data volume.** Its test defaults randomise ports and dashboard endpoints and nothing else — there is no volume stripping, renaming or opt-out in `Aspire.Hosting.Testing`. An app model declaring `WithDataVolume("portfolio-dev-data")` mounts *that exact volume* in the test run, so every local `dotnet test` writes into the developer's DEV data — and the FR-13.4 destructive-path test runs against a real named volume. Rule to state: *no test process ever mounts a named volume.* | .NET/Aspire |
| S-25c | **The architecture invariant bans the client integrations that supply the telemetry the spec credits to ServiceDefaults.** "An Aspire dependency" conflates `Aspire.Hosting.*` (app-model code, correctly AppHost-only) with `Aspire.*` client integrations such as `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, which are ordinary `IHostApplicationBuilder` extensions belonging in the consuming service — and which register the DbContext, its health check, its retry policy and its EF Core/Npgsql OTel instrumentation. ServiceDefaults gives the OTel *pipeline*, not DB spans. Reword to name the assembly prefix, not the brand. | .NET/Aspire |
| S-25 | **The audit log is writable by the application role.** One role holds `INSERT`/`UPDATE`/`DELETE` on everything including `audit_event` and `ingest_run`, so the only detection control in the design can be erased by anyone reaching API-level code execution — the primary threat on both deployments — with no tamper evidence. | Red team |

### Security and privacy

| ID | Finding | Seat |
|---|---|---|
| S-26 | **CSV formula injection.** Escaping is specified for HTML only; three export paths lead to a spreadsheet. A `merchant_name` of `=HYPERLINK(...)` or `=WEBSERVICE(...)` exfiltrates neighbouring cells — the user's own balances — on one trust prompt. The input costs an attacker one cent (a P2P memo). | Security, Red team |
| S-27 | **PATCH mass assignment can forge provenance.** DTO discipline is specified for responses only. With EF entities in scope, `[FromBody] Account` accepts `source_strength`, `deleted_at`, `observed_at`, `closed_on` — so a request can mark a hand-entered row `statement`, defeating Constitution IV invisibly, or hide rows from every view. Patch media type and omitted-vs-null semantics are also unnamed, which collides with Constitution II. | Security |
| S-28 | **Webhooks require inbound reachability the loopback rule forbids**, with no tunnel or ingress in `deployment.md` and no threat-model row. Verification has no timestamp window or replay cache, so a captured delivery replays indefinitely, forcing unbounded credential use against the live institution. In cloud, exposing the app for webhooks exposes the whole API. | Security, Red team |
| S-29 | **No purge or erasure path.** Soft-delete-only plus an append-only audit means nothing can ever leave the volume. `DELETE /connections/{id}` revokes credentials and says nothing about the ingested data — the opposite of the providers' own consumer model. A household member's transactions cannot be removed. | Privacy |
| S-30 | **Telemetry may carry financial data.** Every redaction rule is scoped to secrets. NFR-3's *"diagnose a failure without reproducing it"* plus *"malformed records fail the batch loudly"* is exactly the pressure that puts `description`, `merchant_name` and `amount` into span attributes — and the Azure path ships those spans to a third party, negating *"without their financial data leaving the machine"*. | Privacy |
| S-31 | **The export's contents are undefined**, so the strongest credential rule in the set has no test surface on the one endpoint that emits everything. | Privacy |

### Operations

| ID | Finding | Seat |
|---|---|---|
| S-32 | **Nothing observes the self-hosted deployment.** The local/cloud table credits it with the Aspire dashboard, which the same document argues is an inner-loop artifact that does not exist in the Compose topology. So NFR-3's telemetry has no consumer on the primary target, and the most likely real failure — a scheduled sync silently stopping — produces no signal for weeks, during which the gap is permanently unfillable under Constitution III. | DevOps |
| S-33 | **The `.env` is both a release artifact and never published.** `deployment.md` says publish emits one; `cicd.md` attaches "the Compose artifact" and then says never publish a `.env`. No document enumerates the required keys, so a release adding one silently starts the stack with an empty value — including, potentially, the environment name that M-2's guard depends on. | DevOps |
| S-34 | **No restart policy, healthchecks, startup ordering or resource limits** appear anywhere in the spec set (verified: zero hits for `restart:`, `healthcheck`, `depends_on`, `mem_limit`). Without `restart: unless-stopped` the stack does not survive a reboot; without a `db` healthcheck every restart races the migration step; without limits a mis-sized ingest batch takes the host down. | DevOps |

### UX

| ID | Finding | Seat |
|---|---|---|
| S-35 | **"Render a null as unverified" has no design.** Eleven characters in a right-aligned decimal column, competing with two other non-numeric states (conditional, weak provenance) for the same cell, none designed. Rendered as a dash it reads as zero; rendered as text the table becomes unscannable. And the empty state is unspecified, so a first-run PRD dashboard reads `$0.00` net worth — teaching the user on day one that this app shows absence as zero. | UX |
| S-36 | **A refusal names the missing input but offers no route to resolve it.** `{name, reason, gated_on}` is a machine key and an opaque string. The contract's own goal is *"what is missing and what would resolve it"*; nothing carries the latter. The likely erosion is not a default in the model — tests forbid that — it is the user hand-entering a guess to get past the screen, which passes every check and poisons the output. | UX |
| S-37 | **Three silently-wrong states are returned but not required to be displayed:** `truncated` on a capped result (and its export, which carries no flag at all once it leaves the app), run staleness, and stale-credential state. The staleness case is worst in scenario comparison, which is exactly where it is invisible and consequential. | UX |
| S-38 | **No first-run experience and no starter reports.** Four fixed dashboards; every other question routes to an arbitrary SQL box. Documenting the schema in the UI helps someone who already writes SQL. Verified: zero mentions of shipped example reports — a wasted teaching surface and the reason the product's main differentiator will go unused. | UX |

### Documentation integrity

| ID | Finding | Seat |
|---|---|---|
| S-39 | **OpenAPI "source of truth" is specified two opposite ways.** FR-12.2 says the document is the source of truth (contract-first); D11's *body* and `plan.md` say it is generated from the implementation (code-first) — while D11's *title* says "Contract-first". Under code-first the drift check is circular: it can only surface a stale file, which a developer "fixes" by regenerating. | Technical writer |
| S-40 | **`deployment.md` says the browser reaches only `web`**, while Constitution XI, FR-7.2 and the CORS rule require the browser to call the API directly. If `web` proxies `/api/v1`, CORS is dead code and `web` is not a static frontend; if not, the topology table and network layout are wrong. Cookie, CSRF and CORS design differ per branch. | Technical writer |
| S-41 | **Seeding has two owners** — `plan.md` gives it to the Worker, `contracts/api.md` gives `/dev/seed` to the API, while `Api` is specified to hold "no business rules of its own". E2E journey 10 goes through the UI, so it must traverse the API. | Technical writer |
| S-42 | **The DEV guard count disagrees across three documents** — three guards in `environments.md` and traceability, "two independent checks" in `api.md`. Whoever builds from the contract omits the `GRANT`-level guard, the only one that survives a code bug. `environments.md` also implies PRD `/dev/reset` returns a refusal, contradicting route absence (404). | Technical writer |
| S-43 | **Three broken cross-references:** `deployment.md` cites `README.md` for an Aspire CLI install step that is not there (so the one prerequisite a stranger needs first has no documented path anywhere); traceability's R10 cites a `README.md` the documented move discards; `security.md`'s audit section cites FR-4.6 (`ingest_run`) instead of FR-3.5/NFR-5. | Technical writer |
| S-44 | **`contracts/` holds prose, not a machine-readable contract**, so spec-kit derives one contract-test task for the whole REST surface, and there is an unresolved chicken-and-egg: the contract is "source of truth" but "generated from the implementation". | Spec-driven dev |
| S-45 | **`plan.md` has no Constitution Check gate and the constitution has no version.** Spec-kit's plan template is built around that gate; without it `/speckit-analyze` finds nothing to compare and `/speckit-implement` proceeds ungated. Re-running `/speckit-plan` to add it would regenerate the file and discard the hand-written content. | Spec-driven dev |
| S-46 | **Nothing creates the `001-portfolio-platform` branch** the handoff assumes. Spec-kit resolves the feature directory from the git branch name, so the copy lands on `main` and the first thing the new repo's owner does is debug spec-kit. The move procedure is also unordered (`--force` before or after the copy?). | Spec-driven dev |

---

## CONSIDER

| ID | Finding | Seat |
|---|---|---|
| C-1 | **Every output is a deterministic point estimate** with no real-vs-nominal declaration and no inflation-indexing flag on constants. A 30-year horizon mixing an un-indexed threshold with an inflating balance drifts systematically, and a single path hides sequence-of-returns risk — the dominant risk in early withdrawal years. Constitution VI does not forbid a seeded distribution; the schema and `POST /runs/compare` are simply shaped for scalars. Widening now is far cheaper than after the UI. | Retirement domain |
| C-2 | **The migration mapping is lossy.** No `slug` column, though the slug is the old system's entire identity scheme and every prior decision cites it; the dated net-worth series has no target and is discarded for incompletely-decomposed dates; and `allocation.yaml`'s total is never stored, so the reconciliation the spec calls important becomes unverifiable the moment the import finishes. Floor tiers have no ordering column; `constant.gated_on` points at no action table. | Data architect |
| C-3 | **Traceability asserts a completeness it does not have**, and uses ranges and an `FR-3.x` wildcard that no script can check. Three of six open items carry no `[NEEDS CLARIFICATION]` marker, so `/speckit-clarify` surfaces half of them. | Spec-driven dev |
| C-4 | **"PRD" names a local environment while "production" names the deployed runtime**, and the documents use the word for both — so `deployment.md` labels the inner loop "development" although it is the documented way to run PRD. | Technical writer |
| C-5 | **One concept, four names:** reported-but-unverified money / reported-but-unobserved transfer / pending transfer / conditional money. And provenance has four levels in the constitution, five in the schema (`derived` inserted mid-ranking, unexplained), with the test covering only three. | Technical writer |
| C-6 | **Phone widths are excluded** by FR-7.5 with no recorded reasoning, unlike KQL and Kubernetes. Checking a balance is the highest-frequency personal-finance interaction. Note the a11y interaction: a UI that breaks below tablet width also breaks at 200% zoom, which is a WCAG AA reflow failure — so a read-only narrow floor is largely obtained for free by M-24. | UX |
| C-7 | **Transactions carry data about people who never consented.** "Single-user" describes who logs in, not whose data is in the table; joint accounts are the norm. One paragraph in the README and a threat-model line, plus the purge path from S-29. | Privacy |
| C-8 | **Azure deployment states no region, residency, or consequence** — the primary story promises data never leaves the machine, and the optional path negates that without marking it, in a document that is otherwise careful to call network exposure "a deliberate act with consequences". | Privacy |
| C-9 | **Release holds the signing and registry credentials with no scoping**, and a tag push reaches them without passing the review gate that protects `main` — while Deploy, the less privileged of the two, has an approval gate. | DevOps |
| C-10 | **The data-hygiene allow-list is a public, permanent, curated record of what nearly leaked** about one identifiable person, with `git log` attribution. If the institution deny-list is drawn from the user's own connections rather than a generic public list, the list itself discloses where they bank. No rotation rule exists for a hit found after push — and a public commit object survives its branch. | Red team |
| C-11 | **Environment isolation is over-built for month one.** The triple guard and simultaneous running are sound *once PRD holds years of irreplaceable history*; in month one PRD is empty. Keep the service-level assertion and separate names (cheap at step 1, expensive to retrofit); defer port offsets and separate secret scopes. Drop the stub-versus-vendor schema contract test — an unbounded maintenance tail on a schedule you do not control — and assert the stub against the connector's own declared allow-list instead. | Scope realist |
| C-12 | **Six models is four too many for interface validation.** Two with genuinely different input shapes prove FR-8.3/8.4 nearly as well; the rest is hand-deriving tax arithmetic, which is domain research rather than .NET practice. Two of the six are not models at all — policy-rule evaluation and sensitivity are engine features wearing the interface. | Scope realist |
| C-13 | **Full authentication and hardening for a loopback-bound app.** WebAuthn is 20–40 hours and does not address the primary self-hosted threat (stolen disk — envelope encryption does). Name the decision now as argon2id + session cookie (an evening), and gate WebAuthn/rate-limiting/HSTS on the day the app binds to a non-loopback interface. If WebAuthn is wanted *as a learning exercise*, schedule it as one — but not as a security requirement it does not satisfy here. | Scope realist |

---

## INFORMATIONAL

- **Every numeric claim in the spec set is correct.** Independently verified by
  the technical writer and the broker: 11 constitution principles (I–XI), 13 FR
  groups with no gaps, 14 stated requirements (R1–R14), 12 decisions (D1–D12,
  all cited targets exist), ~990 tests (800+150+30+10, and the per-layer budgets
  match the Standards table), 13 documents, 112,966 bytes. The traceability
  sample also checks out — E2E journeys cited for R5, R6, R8 and R11 match
  `testing.md`'s numbering, and `api.md` does contain the five conformance
  properties `testing.md` cites.
- **The 11 reporting views are named in exactly one document.** Every other
  reference is the generic phrase "the reporting views", so no view name can
  currently disagree with another — but nothing ties `/dashboards/net-worth` to
  `v_net_worth_daily` either.
- **`security.md` spends most of its hostile-data section on prompt injection**
  for a system that contains no LLM, while two real sinks (CSV export, log
  fields) are unlisted. Trim to one forward-looking sentence and say plainly
  that no component passes provider text to a model.
- **D5's reasoning is correct and worth keeping** even though M-6 shows the
  implementation does not deliver it. Privilege beats parsing; the fix is to
  specify the privileges properly, not to abandon the principle. Several seats
  independently called the `GRANT`-based reporting role the most valuable single
  idea in the set.
- **Provider-terms findings are unverified.** `plaid.com`, `monarchmoney.com`
  and `sec.gov` are egress-blocked from the review environment. Every claim
  about provider obligations in M-26 and M-27 rests on search-engine snippets
  and must be checked against primary text.
- **The Aspire Compose-publisher open question is answered.**
  `Aspire.Hosting.Docker` **13.5.4 is a stable, non-prerelease package** — no
  `-preview` suffix. However the supporting compute API still carries
  `[Experimental("ASPIRECOMPUTE002")]`, so the AppHost needs a `NoWarn` or
  `#pragma`. Package shipping stable; API surface still opt-in-experimental. The
  `docker-compose.yaml` + `.env` output filenames the spec names are correct.
- **APIs confirmed to exist** in `microsoft/aspire@b477bdd` (13.6.0-preview.1):
  `AddViteApp`, `AddNodeApp`, `WithDataVolume` with explicit naming,
  `WithReference`, `WaitFor`, `WaitForCompletion`, `AddDockerComposeEnvironment`,
  `Aspire.Hosting.Testing`, `aspire publish`, `aspire deploy`, and the
  single-file `apphost.cs` model. The spec's API names were right; three of its
  *behavioural* claims were not (M-3, M-4, M-5).
- **No .NET SDK was available to the review**, so no platform claim in the specs
  was executed. Version markings remain *confirm at scaffold time*.

---

## Broker's note

Four things are worth saying beyond the list.

**First, the convergences are the signal.** Where two seats reaching from
different directions land on the same defect — the data architect and UX on
M-1, security and red team on M-6, three seats on M-8 — the finding is almost
certainly real and almost certainly structural. I verified each of those against
the spec text myself before including it.

**Second, the constitution is where the real damage is.** M-1, M-20 and M-13
are all defects in founding principles rather than in documents derived from
them: a rule that makes the flagship number uncomputable, a rule the schema
openly violates with no amendment, and a principle with no requirement behind
it at all. Those propagate. Fix them first, because several Should Fix items
dissolve once they are right.

**Third, the documents were internally plausible and externally wrong.** M-3,
M-4 and M-5 — two stacks cannot run at once, service discovery does not reach
the browser, migrations cannot be waited on — are all core mechanisms that read
correctly on the page and do not work. None of the other thirteen seats found
them, because they are not derivable from the documents; the only way to catch
them was to read Aspire's source at a pinned commit. **A spec that reasons
carefully about a platform it has never executed will be confidently wrong in
ways review cannot detect.** That is the strongest argument in this whole review
for building the skeleton early: step 1 of the build order would have surfaced
all three in an afternoon.

**Fourth, read all of this against S-0.** Fourteen specialists asked to find
problems will find problems, and roughly half the defective surface above sits
in work the scope realist argues should not be in a first release — FR-2 and
its webhooks, the Azure target, the release and deploy pipelines, saved reports,
four of six models, simultaneous environments. Cutting that surface removes the
defects with it, at no cost to the learning goal. **The most expensive mistake
available here is to treat this list as a to-do rather than as evidence for a
scope decision.**

