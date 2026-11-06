# Slice 04, round 1 — scope realist (SCO)

**Seat:** scope realist. **Date:** 2026-11-06. **Under review:**
`specs/001-portfolio-platform/slice-04.md` on `main` at `644efe2` (first draft, PR #103).

**Read:** `slice-04.md` (whole); `slice-03.md` (commission, "What the slice is/is not",
step 0's preamble, obligations, "Before the first real balance", the gate, "Then stop");
`slice-03-assessment.md` §1, §2, §4, §5; `slice-02-assessment.md` §2, §5;
`slice-01-assessment.md` §2, §5; `LOG.md` (all three tables and the slice-04 row);
`README.md` "Running it" (the runbook, `:115-175`); `spec.md` FR-5.5, FR-6; `data-model.md`
banner, "Two schemas", `saved_report`, "The `reporting` schema"; `security.md` "Database",
"Verification"; `research.md` banner D5; `specs/review/slice-03-committee.md` "Axis 2" and
"The scope realist's middle cut"; `specs/review/slice-03-r2-seats/sco.md`. Code, for claims
the draft rests on: `src/Infrastructure/BalanceWrites.cs`, `src/Api/AuthEndpoints.cs`,
`src/Api/OwnerCommands.cs`, `src/AppHost/AppHost.cs`, the five migrations. `git log main`
for merge times and the step-per-PR pattern.

Line numbers are `slice-04.md` at `644efe2` unless another file is named.

---

## Findings

### SCO-1 · MUST FIX (step 1, Q1) · the API updates and deletes in place today; Q1's premise is false

**Where:** Q1 `:205-208`; step 1 `:159-162`; the `portfolio_api` row `:44`.

**The defect.** Q1 reasons that balances "supersede rather than update", so the API may need
no `UPDATE`. Supersession *is* an `UPDATE`: `BalanceWrites` sets `superseded_at` with
`ExecuteUpdateAsync` before the insert [`SOURCE@644efe2` `src/Infrastructure/BalanceWrites.cs:59`].
That order is slice 01's ticked technique ("UPDATE before INSERT", `slice-01-assessment.md:161-164`).
The API also updates Identity rows: `UpdateSecurityStampAsync` on sign-out and in
`revoke-sessions`, and `AddOrUpdatePasskeyAsync` on every sign-in (the sign count)
[`SOURCE@644efe2` `src/Api/AuthEndpoints.cs:104,156,170`; `src/Api/OwnerCommands.cs:54`].
`reset-owner` deletes the user, and its passkeys go with it by cascade
[`SOURCE@644efe2` `OwnerCommands.cs:71`]. Both host commands run as the `api` service
(`README.md:149-152`), so after step 1 they connect as `portfolio_api` too. The draft does not
mention them. Written to Q1's premise, step 1 cannot merge green: sign-in, restatement and
`reset-owner` all fail.

**What to do.** Replace Q1 with a grant matrix in step 1: each of the 13 tables
[`EXECUTED`: `CreateTable` count over the five migrations] against the verbs the API uses,
derived from its code paths, *including* `OwnerCommands` and Identity's store. Mark the one
identity column (`identity_user_claim`) for `USAGE` on its sequence only if a path inserts
claims. Settle the two login names in the commission now; it costs nothing. Grant at table
level. Column-level `UPDATE (superseded_at)` is a tightening for the review to decide, not a
default.

### SCO-2 · MUST FIX (the gate) · "The gate — unchanged" is changed

**Where:** `:232-247` against `slice-03.md:1230-1246`.

**The defect.** Slice 03's gate has three parts in its quoted block: the technique limb
("three techniques… with the commit… not any of the [n] already claimed"), **Kyle's hours
under 200**, and **"Explicitly NOT reasons to continue: that the slice works, and that the
next slice is obvious."** Slice 04's block keeps only the STOP default. The technique limb
survives as a question to the assessment (`:245-246`). The hours limb and the
"NOT reasons" sentence are gone, as is slice 03's "Stated, not smoothed" admission that the
hours limb cannot trip [`SOURCE@644efe2`, both files]. This is the project's stopping rule,
and the heading says it did not move.

**What to do.** Restore slice 03's block verbatim, with "the twenty-one slices 01–03 already
claimed" (6 + 7 + 8 [`EXECUTED`, counted from the three §5 lists]) and "slice-04
assessment". Keep the "Stated, not smoothed" paragraph.

### SCO-3 · MUST FIX (steps 3, 5; DoD 5) · FR-6.6's schema documentation has no route

**Where:** `:57-58` ("the UI shows that text"); step 5 `:173-174`; DoD 5 `:193`.

**The defect.** The page shows `COMMENT` text, but no step builds the API surface that reads
it. Step 3's endpoint runs user SQL. Reading `pg_description` and `information_schema.columns`
through that endpoint would make the page's documentation depend on the user's query route
and its row cap. Nothing says which login reads the comments, what shape they take in the
OpenAPI contract, or that the contract test covers them. It is small, but it is a route, a
contract change and a test, and it sits on the allow-list and CSRF accounting slice 03 built.

**What to do.** In step 3, add `GET /api/v1/reports/schema`. It is read on the reporting
login, so it shows exactly what that login can see, and it returns view, column, type and
comment. It is behind the session, committed to the contract, and DoD 5's test compares it
with `pg_description`.

### SCO-4 · SHOULD FIX (the commission) · "the database enforces the rules" overlaps two ticked techniques

**Where:** `:19-24`, `:37-39`; gate `:245-246`.

**The defect.** Slice 02's first tick is "the run record **immutable at the database**:
triggers refuse `UPDATE`/`DELETE`/`TRUNCATE`" (`slice-02-assessment.md:170-173`). Slice 03's
"default-deny authorization, asserted by request over every endpoint… the test seen
failing" (`slice-03-assessment.md:200-202`) is the same test shape as "tested by trying what
each login must not do" (`:38`). So is slice 02's "proving a defence by breaking it". Stated at
the commission's altitude, the slice's technique is part claimed already. Kyle will have to
judge the overlap at the gate, as slice 03's §5 had to, and the commission can make that
judgement easier.

**What to do.** Name the techniques in the commission at the altitude that is new: PostgreSQL's
privilege model (ownership against use, the `PUBLIC` defaults, and a view as a privilege
boundary), and bounding a query from outside it. Add one sentence naming the overlap with
slice 02's trigger immutability and slice 03's default-deny test, so the assessment does not
count the testing shape twice. "Candidate techniques" below lists them.

### SCO-5 · SHOULD FIX (step 3) · a result of unknown shape has no typing rule, and money is in it

**Where:** step 3 `:60-62`, `:167-170`.

**The defect.** The endpoint returns "the columns, the rows". The views return `numeric`
balances and net worth. The API's rule since slice 01 is money as decimal strings (`LOG.md`
row "3 · API", 2026-09-24), and `FR-4.7` forbids floats. A generic JSON writer over a data
reader puts `numeric` out as a JSON number. That rounds in the browser past 2^53, and it
silently changes the API's money contract. Dates, `timestamptz`, `interval`, `bytea`,
arrays and `NULL` also each need a rule. None is named, and the OpenAPI shape of a dynamic
result (columns with their PostgreSQL type, rows as arrays of strings or nulls) is not
chosen. [`INFERRED` for the JSON writer's default; it is a design choice either way.]

**What to do.** One bullet in step 3. Every cell is a string or `null`, written in the
invariant culture, and each column carries its PostgreSQL type name. Test it with
`numeric(19,4)` near the limit, a `timestamptz` and a `NULL`.

### SCO-6 · SHOULD FIX (build order) · step 1 switches every suite before the provisioning it rests on has run once

**Where:** steps 1–2 `:159-166`; Spike M `:123-139`.

**The defect.** Step 1 builds the provisioning mechanism (roles created by the Migrator,
passwords from parameters, an idempotent `CREATE ROLE`) *and* switches the API, the
Integration fixture and the E2E onto it in one merge. 14 files in `tests/Integration` and
`tests/E2E` open a connection or read a connection string directly
[`EXECUTED`: grep for `ConnectionString|NpgsqlConnection`]. Each must be sorted into "seed or
assert as the owner" or "act as the app", and the G-7 test has to be the latter. The
reporting role, on the other hand, is purely additive: nothing existing reads it. Slice 03's
round 2 found four ordering defects of exactly this kind ("switched on" before its
prerequisites; `slice-03-r2-seats/sco.md` SCO-1, -2, -6, -7). Step 1 first is defensible,
because every later step's tests then run as the real role and a missing grant shows up when
the feature is built. But the draft does not say why it chose that order, and the first
merge carries the two riskiest changes together.

**What to do.** Either split step 1 into **1a**, the provisioning mechanism, proven on the
reporting role and nothing switched, and **1b**, the API switch with the fixture split, or
keep the order and state the reason. My preference is the split. 1b is then the step that
touches every suite, with the mechanism already green. Step 2 keeps the views and the
refusal tests.

### SCO-7 · SHOULD FIX (steps 1, 4) · no rule for grants on tables that do not exist yet

**Where:** step 1 `:159-162`; step 4 `:171-172`; `saved_report` "written by the API's login"
`:63-65`.

**The defect.** Step 4's migration 6 (`saved_report`) is the first table created after the
switch. Nothing says who grants the API its rights on it: a grant in the migration, a script
the Migrator re-applies after every migration, or `ALTER DEFAULT PRIVILEGES`. Nothing makes a
forgotten grant fail a test rather than a request. The reporting views raise the same
question for every later slice's views (`data-model.md` lists seven more).

**What to do.** In step 1, pick one mechanism and write it down. Add a catalog test that
reads every table and view in the schemas and asserts each one's grants against the matrix
from SCO-1, so a new object without a decision fails it. That test is cheap (about 0.2 h).
It is also the repository's own default-deny pattern again, so count it once, under slice
03, not as a new technique.

### SCO-8 · SHOULD FIX (step 6) · the runbook is not in scope, and it now has three passwords

**Where:** step 6 `:175-177`; `README.md:122-124`, `:146-171`.

**The defect.** The README says the `.env.<Environment>` file "holds the generated database
password". It warns that `--clear-cache` regenerates it and that the volume then refuses it.
It also runs the two recovery commands as `api`. After this slice there are three logins.
Whether `--clear-cache` breaks the two new ones depends on Spike M item 3: if the Migrator
resets their passwords on every run, they heal and only `postgres` is fatal
[`INFERRED`, conditional on Spike M]. The commands now run as `portfolio_api` (SCO-1). An
operator debugging a report needs to know how to connect as the reporting login, and must
not do it as `postgres`. Slice 03's committee scoped "the runbook" as named step-7 work
(`slice-03-committee.md` Axis 2, OPS), and slice 03's step 7 rewrote it.

**What to do.** Step 6 adds: the README's password paragraph for three logins; `--clear-cache`
restated to match Spike M's answer; the recovery commands' login; and one line for a
`psql` session as the reporting login.

### SCO-9 · SHOULD FIX (step 0) · no spike-to-step map, and Spike M item 4 cannot run in step 0

**Where:** Step 0 `:99-101`; Spike M item 4 `:137-139`; step 6 `:175-177`.

**The defect.** Slice 03's step 0 said that the step depending on an `INFERRED` claim does not
start until its spike has raised it (`slice-03.md:125-128`). Slice 04 says only "do these
first". Which step waits on which spike is left to inference: L gates 2; M items 1–3 gate 1;
N gates 3 and 4. Spike M item 4, a redeploy over slice 03's existing volume, needs the
roles to exist in a published stack, which only step 1's code produces. As a step-0 spike it
means building step 1 twice. Slice 03 had the same defect with `G-12` (round 2 `SCO-10`).

**What to do.** Add slice 03's sentence and a one-line map. Move M item 4 to step 6, where
DoD 8 already tests it. Item 2's "survives `aspire publish`" stays in step 0: publishing is
cheap.

### SCO-10 · SHOULD FIX (the commission) · the frozen documents it builds against are not named, and one is wrong about the schema

**Where:** the commission `:5-31`; the gate's assessment question `:242-243`.

**The defect.** `data-model.md`, `security.md` and `research.md` are frozen and "corrected only
when a slice is commissioned against" them (`data-model.md:3-8`). Slice 03 named the sections
it was commissioned against (`slice-03.md:115-119`). Slice 04 asks the assessment what was
corrected but never commissions anything. One correction is already known. `data-model.md`
puts the physical model in an **`app`** schema, with "nothing outside the ingestion pipeline
writes to `app`" (`:27-31`). As built, every table and `v_net_worth_daily` are in `public`, and
the migrations name no schema [`SOURCE@644efe2` `src/Infrastructure/Migrations/`].
`saved_report` is written by the API, not by ingestion. `security.md` "Database" grants
"rights only on the `app` schema" (`:127-128`).

**What to do.** Add slice 03's paragraph. Commission against `security.md` "Database",
"Audit" (G-7's sentence) and "Verification" (`:357-358`); `data-model.md` "Two schemas",
`saved_report` and "The `reporting` schema"; and `research.md` D5. Say that the physical
tables stay in `public`: moving them is not this slice.

### SCO-11 · SHOULD FIX (obligations) · the table does not say what the role split does to `D5` and `D17`

**Where:** obligations `:223-228`; `slice-03.md:1216-1228`.

**The defect.** Two interplays with "Before the first real balance" are silent.
- **`D5`, backup.** Once grants name three roles, a `pg_dump` carries `GRANT`s to roles it
  does not create. Roles are cluster objects, dumped only by `pg_dumpall --globals-only`,
  which also writes the role password verifiers. So a restore onto a fresh volume needs
  either the Migrator to run first or the globals file, and the globals file holds
  credential material that must be handled as a secret [`INFERRED`, recalled from the
  `pg_dump` and `pg_dumpall` documentation, not re-read; it is load-bearing for `D5`'s design,
  not for this slice].
- **`D17`, wider audit.** `saved_report` create, update and delete are data mutations, and
  `D17` carries those to real data, as it does report runs (Q3).

The obligations row "slice 03's review deferrals… unchanged" (`:228`) also lists no items.
Slice 03's round 2 found that a bare reference loses them (`SCO-11` there).

**What to do.** Add a row for each: `D5`, with the sentence above as a note for that
commission; `D17`, with `saved_report` mutations and runs, "Carried, trigger real data". List
slice 03's seven deferrals by name (`slice-03.md:1206-1214`), or cite that table explicitly.

### SCO-12 · NOTE (steps 2–3) · the bounds must be configuration, or the suite waits on them

**Where:** Q2 `:209-210`; DoD 3 `:189-190`.

A timeout test with a production value of seconds makes every run wait. A `generate_series(1,
1e9)` cap test does not need to wait, but its control does. Make both values configuration.
Tests set them small, and one class pins the production numbers, as slice 03 did for the
limiter (round 2 `SCO-9`).

### SCO-13 · NOTE (step 2, Spike L) · revoking `PUBLIC`'s `EXECUTE` can cost more than it looks

**Where:** Spike L item 1 `:110-113`.

`v_date_spine` calls `generate_series`, `min`, `max` and `greatest`
[`SOURCE@644efe2` `20260924134306_Initial.cs:212-221`]. My recollection is that functions in
a view are checked against the querying role, not the view's owner [`INFERRED`]. If so, a
broad `REVOKE EXECUTE … FROM PUBLIC` breaks the views it protects. The narrow form, revoking
named functions (`pg_sleep`, `lo_*`, `pg_read_file`…), needs a list someone maintains. DBS
should settle which is true. Scope's point: Spike L's answer can add a step-2 sub-task, and it
should be priced when the spike reports, not found in step 2.

### SCO-14 · NOTE (DoD) · two of slice 03's DoD conventions are missing

**Where:** DoD `:181-199`.

There is no item for the contract: three or four new routes, committed and byte-stable, as
slices 01–03 required. There is no evidence line for the E2E-backed items (date, commit,
machine; `D13`). DoD 7's "Slices 01–03's DoD items still hold" covers the existing routes,
not the new ones. Add one line each.

### SCO-15 · NOTE (the commission) · "Estimate: none" stands; give the assessment a figure to compare with

**Where:** `:27`.

As in slice 03 (round 2 `SCO`, Axis 2, "DEFER"), no estimate goes in the commission. The
assessment should compare its cost with this seat's figure below and say what moved it.

---

## Cost estimate

**Basis.** Logged agent hours are the agent's own estimates and overstate. Measured, from
merge timestamps:
- slice 01: ≈ 5.1 measured against 8.8 logged, about 0.58 (`slice-01-assessment.md:59-60`);
- slice 02: ≈ 5.2 against 9.4, about 0.55 (`slice-02-assessment.md:68-69`);
- slice 03: ≈ 10 against 12.6 inside the same span, about 0.79 (`slice-03-assessment.md:57-58`).

For slice 03's spikes and build alone, from `#74` merging (09:37 −0500) to `#85`
opening (≈ 16:50 −0500), it is ≈ 7.2 h measured against 10.3 logged, about 0.70 [`EXECUTED`:
`git log` merge times, and the `LOG.md` rows for Spikes I and J and steps 1–7b summed]. Per-step
merge deltas are not usable: the branches were stacked.

**Measured-style** below takes 0.6–0.75 of the logged figure.

| Step | Logged-style | Measured-style | What drives it |
|---|---|---|---|
| 0 · Spikes L, M (items 1–3), N | 2.0 | 1.2–1.5 | L is catalog probing in a container (~0.6). **M is the Aspire question**: how a second user's connection string survives `aspire publish` (~0.9; "the place a day can go"). N, on Npgsql cancel, the cap and batching (~0.5) |
| 1 · the API's login (1a and 1b, SCO-6) | 2.0 | 1.2–1.5 | Migrator provisioning and idempotent roles; the grant matrix over 13 tables (SCO-1); the fixture split over 14 files; `OwnerCommands`; the G-7 refusals; the catalog grant test (SCO-7). **Can double** |
| 2 · the reporting schema and login | 1.5 | 0.9–1.1 | Four views and their `COMMENT`s; Spike L's script; one test per refusal (≈ 10–15); the view rules; reporting equal to the chart. +0.3 if SCO-13 bites |
| 3 · running a query | 1.5 | 0.9–1.1 | Bounds, cap and cancel; problem details; result typing (SCO-5); the schema route (SCO-3); contract regenerated |
| 4 · saved reports | 1.25 | 0.75–0.9 | Migration 6 and its grants; CRUD; typed parameters; a value of SQL stays a value; contract |
| 5 · the page | 2.0 | 1.2–1.5 | Textarea, schema panel, accessible results table with a "cut off" notice, saved list with parameter inputs; axe in both schemes; E2E. Slice 03's step 4 was its largest at 2.0 logged, and this is the same kind of work |
| 6 · the published stack | 1.25 | 0.75–0.95 | Two redeploys over slice 03's volume; passwords stable; refusals in the container; M item 4; runbook (SCO-8) |
| Assessment | 0.5 | 0.3–0.4 | |
| **Spikes, build and assessment** | **≈ 12.0** | **≈ 7.2–9.0** | |
| Review and dispositions (if a committee, as slice 03) | 1.5–2.3 | inside the measured span | Slice 03 logged 2.3 for its two rounds' dispositions (`LOG.md:79-80`) |
| **Slice total, with the 0.5 commission already logged** | **≈ 14–15** | **≈ 9–10** | |

**Against slices 02 and 03.** Slice 02 cost 9.4 logged and ≈ 5.2 measured. Slice 03 cost 13.9
logged and ≈ 10 measured, review included. Slice 04 as drafted lands at about slice 03's size,
slightly under. It has fewer layers than slice 03 (no new auth, no hook, no logging tier), but
the same "touch every suite once" step, moved from slice 03's step 4 to step 1.

**The step to watch: step 1.** Spike M decides it. If Aspire cannot express a second
user's connection string in the published compose file, step 1 grows by a hand-written
override, and so does step 6. **The trigger to stop and re-cut** is step 1 passing 3 h logged.
If that happens, the cause will be the fixture split, not the grants.

---

## Candidate techniques

None below is one of the 21 already ticked (slice 01: 6, slice 02: 7, slice 03: 8). Each
overlap is flagged for Kyle to judge, not counted for him.

1. **PostgreSQL's privilege model as a boundary: ownership against use.** An owner can
   `DISABLE` and `DROP` a trigger, and a superuser is not the only danger (G-7's second
   probe). `PUBLIC`'s defaults are revoked deliberately. A view reads its tables with its
   owner's rights. The role owns nothing and is shown refused. **New.** *Overlap:* slice 02's
   "immutable at the database" is triggers, a rule on rows; this is who may remove the rule.
   The testing shape ("tried and refused, seen failing") is slice 02's and slice 03's, so do not
   count it again.
2. **Bounding an untrusted query from outside it.** A timeout the query's own `SET` cannot
   disarm. A row cap enforced by reading N + 1 rows and then cancelling, because PostgreSQL has
   none. The cancel shown on the server (`pg_stat_activity`), not just the client giving up.
   **New.** *Overlap:* slice 02's "measuring before limiting" is about sizing a limit; this is
   about a limit the bounded party cannot lift. Distinct.
3. **A documented view schema as a stable contract, with the database as its only source.**
   Views as the API, `COMMENT` as the documentation, the UI reading the catalog, and
   `reporting.v_net_worth_daily` equal to the chart's view by construction. **Moderate.**
   *Overlap:* slice 01's "a contract that is generated, committed and byte-stable" is the same
   idea at the HTTP layer.
4. **Secrets and identities provisioned through the app model.** Three logins with generated
   parameters, created idempotently by a one-shot Migrator, and surviving a redeploy over an
   existing volume. **Weak.** *Overlap:* slice 01's "the AppHost is not the production
   runtime" and slice 03's "the published artifact is not the thing you ran… a deploy that
   keeps its generated secrets" cover most of it.
5. **A result of unknown shape, typed at the edge** (SCO-5): money stays a string through a
   generic reader. **Weak to moderate.**
6. **Driver-bound parameters for user-authored SQL text** (Spike N item 4: Npgsql's `@name`
   rewrite against positional `$1`). **Weak:** parameter binding is not new to Kyle, and the
   rewrite hazard is a footnote.

**Verdict.** Three are plausible (1, 2 and 3), with 1 and 2 strong. The limb is reachable, but
less generously than slice 03's eight: 4 to 6 are close to existing ticks or small. **"The
database enforces the rules" is distinct from slice 03's ticks in mechanism.** Slice 03's were
HTTP- and application-layer, and its second tick ("the application refuses") is the opposite
placement. It is not distinct from slice 02's first tick, unless it is stated as candidate 1
(SCO-4).

---

## A middle cut, if one is wanted

The draft is already close to its smallest. Kyle's rulings removed export and the chart, and
the four views are cheap (about 0.3 h together; keep all four, since `v_ingest_run` is the only
view over non-balance data). The smaller middle is:

**KEEP, because it is the technique:** the three logins and the API owning nothing (G-7
closed); Spike L's script and a refusal test per route; the bounds with the server-side cancel
shown; the four views with their `COMMENT`s and the schema route; the run endpoint; the page in
its plainest form; the published stack.

**CUT or simplify:**
- **Spike N item 4:** choose positional `$1` parameters for saved reports and skip the spike
  (−0.2).
- **Saved reports:** create, list, run and delete; no edit, since delete and recreate covers it.
  Parameter types `date`, `text` and `numeric` only, with no defaults and no `last_run_at`
  (−0.4).
- **The results table:** no client-side sorting or paging; the cap is the paging (−0.3).
- **The schema panel:** a plain list per view, with no search (−0.1).

**DEFER, each with its trigger:**
- auditing report runs and `saved_report` mutations (Q3): `D17`'s trigger, real data;
- narrowing `pg_catalog` (Q4): an object name or comment that is itself sensitive;
- column-level grants (SCO-1): a second writer to `account_balance`.

**The cut that would matter.** Saved reports could go whole (−1.25, and about −0.4 from the page).
Their technique (candidate 6) is the weakest of the slice's. **But Kyle ruled them in**
(`:14`), and FR-6.4's "bound, never interpolated" is half of FR-6's boundary story. I would
keep them in their simplified form.

**Middle cut total:** ≈ 11.0 logged, ≈ 6.6–8.3 measured, for spikes, build and assessment
(against ≈ 12.0 and 7.2–9.0).

**Does the page belong?** Yes. FR-6.1 says the user authors and runs, and FR-6.6 says the
documentation is shown in the UI. Without a page the slice ends at an API nobody uses. In its
plain form it is the cheapest proof that FR-6.6 is met.

---

## Positions on Q1–Q5

- **Q1 (names, and `UPDATE`).** The premise is false (SCO-1). The API needs `UPDATE` on
  `account_balance`, `identity_user` and `identity_user_passkey`, and `DELETE` on the Identity
  tables for `reset-owner`. Use a table-level grant matrix derived from the code paths. Settle
  the names `portfolio_api` and `portfolio_reporting` in the commission now.
- **Q2 (where the bounds live, and their values).** Use both, and say which is the boundary.
  Set a role default (`ALTER ROLE … SET statement_timeout`) as the backstop. The client's
  cancellation is the bound the query cannot lift, if Spike N item 1 shows it stops the query
  on the server. Take the values from configuration (SCO-12). Over invented data, something
  like 5 s and 1,000 rows is plenty, and the assessment can revisit it. Picking them is not
  review work.
- **Q3 (audit runs?).** Defer, by `D17`, to the real-data commission, with `saved_report`
  mutations alongside (SCO-11). If Kyle wants runs audited anyway, the smallest form records
  the kind, the saved report or "ad hoc", the row count, the truncated flag and the duration.
  **Not the SQL text.** Ad hoc SQL is where a pasted value lands, and storing it would extend
  slice 03's canary test to the audit table. Storing it costs more than it tells.
- **Q4 (`pg_catalog`).** Accept that the reporting login can list names it cannot read. State
  it in `security.md`, and test only that it reads no rows. Narrowing the catalog is brittle,
  and it is not this slice's technique. Do *not* accept other sessions' query text in
  `pg_stat_activity` (Spike L item 2). That is a read of the API's own queries, and it must be
  shown refused.
- **Q5 (one statement only?).** Yes, if the protocol gives it for free. Spike N item 3 should
  check whether a parameterised or prepared command refuses a multi-statement text without the
  API parsing anything [`INFERRED`]. If it is not free, do not parse: the grants stay the
  boundary. Q5 matters only if Q2 puts the timeout on the server side, where a second statement
  could `SET` it. If the client's cancel is the bound, a refusal is purely usability.
