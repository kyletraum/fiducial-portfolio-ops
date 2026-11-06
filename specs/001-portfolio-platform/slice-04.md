# Slice 04 — SQL reporting, bounded by the database, over invented balances

## The commission

**Recorded here, in this repository, 2026-11-06.** Slices are commissioned and gated in
this repository (planning `D-043`, 2026-10-20), as slice 03 was.

**Commissioned by Kyle, 2026-11-06,** after slice 03's gate had decided STOP
([`slice-03-assessment.md`](slice-03-assessment.md) §6). Kyle reopened it to build
`spec.md` FR-6. Four rulings the same day, each chosen from options put to Kyle:

| Ruling | Kyle's choice | The options it was chosen from |
|---|---|---|
| How much of FR-6 | **Run and save reports:** FR-6.1, 6.2, 6.3, 6.4 and 6.6. **Export (FR-6.5) is deferred** | Run only; run and save; all of FR-6 |
| The API's own database login (`D6`, gate `G-7`) | **In this slice.** The same technique as the reporting login | In this slice; reporting login only |
| The net-worth chart on the reporting views (FR-5.5) | **Later.** The chart keeps its query | Later; now |
| Data, and who builds | **Invented data only; the agent builds, under the same gate** | Same as slice 03; something else |

**Reviewed, 2026-11-06** ([`../review/slice-04-committee.md`](../review/slice-04-committee.md)).
Four seats (DBS, SRC, TST, SCO) read the first draft, with 49 findings in 18 themes
(`C4-1` to `C4-18`). Kyle ruled `S4-1` to `S4-8` the same day, each from the options put
to him. **This document is the rewrite to those rulings.** A sentence citing `C4-n`, `S4-n`
or a seat finding (`DBS-n`, `SRC-n`, `TST-n`, `SCO-n`) points there for its evidence.

**What this slice is for, by the gate's rule** (slice 03, "The gate": a slice is for a
technique, not because the next feature is obvious). Two techniques, stated narrowly so
that neither re-claims slice 02's "immutable at the database" (`SCO-4`):

1. **The privilege model as a boundary: ownership against use.** An owner can disable and
   drop a trigger without being superuser (`G-7`, `EXECUTED 2026-10-31`). So a login that
   uses tables must own none of them, nor the database, nor a schema. `PUBLIC`'s defaults
   are revoked by name. A view reads with its owner's rights, and a barrier view does not
   leak what it filters.
2. **Bounding an untrusted query from outside it.** The review showed a query can lift
   every bound it can reach (`C4-1`, `C4-2`). So the bounds that hold are the ones it
   cannot reach: a read-only transaction the app opens, one statement refused by the
   server, a fresh session, a deadline enforced by a cancel the server obeys, and caps
   counted on the client.

A third is plausible: **a view schema as a stable contract, documented by the database
itself.** It sits close to slice 01's "a generated, committed, byte-stable contract".

**Method:** `D-029`: claim markers, and an `INFERRED` claim the design rests on is a gate.
**Built by:** the agent. **Estimate:** SCO's, for the assessment to compare against
(`SCO-15`): about **12 logged / 7–9 measured agent hours** for the build and assessment
after this review. **Ends in:** a written assessment and a full stop.

**Built against** (`SCO-10`): `spec.md` FR-6; `data-model.md` "The `reporting` schema" and
`app.saved_report`; `research.md` D5; `security.md` "Database" and "Audit"; `testing.md`.
**`data-model.md` is wrong about one thing:** it places the application's tables in an
`app` schema, but they are in `public` (every migration, `SOURCE@644efe2`). This slice
corrects `data-model.md` and leaves the tables where they are.

---

## What the slice is

**Three database logins, each holding only what it uses**, enforced by PostgreSQL, tested
by trying what each must not do, and shown on the published stack.

| Login | Who uses it | Holds |
|---|---|---|
| `postgres` | The Migrator only | Everything. It owns the database, the schemas, and every table, view and function |
| `portfolio_api` | The API, and the two host commands, which run in the API's image (`SCO-1`, `SRC-5`) | The grant matrix below, and nothing else. **Owns nothing** (`C4-4`): not superuser or `CREATEROLE`; not the owner of the database, of `public`, or of any object; not a member of `pg_database_owner` |
| `portfolio_reporting` | User SQL, through the API's query runner | `USAGE` on `reporting` and `SELECT` on its views. `PUBLIC`'s defaults revoked by name (below). **Owns nothing** |

**The API login's grant matrix** (`C4-4`, from the code paths, `SRC-5` and `SCO-1`):

| Object | Grant | The path that needs it |
|---|---|---|
| `account_balance` | `SELECT, INSERT`, and `UPDATE (superseded_at)` as a column grant | A restatement sets `superseded_at` before the insert (`BalanceWrites.cs:55-59`) |
| `identity_user` | `SELECT, INSERT, UPDATE, DELETE` | Enrolment and every sign-in update the row; sign-out and `revoke-sessions` rotate the stamp; `reset-owner` deletes it |
| `identity_user_passkey` | `SELECT, INSERT, UPDATE` | Every sign-in updates the sign count. Its rows leave with the user by `ON DELETE CASCADE`, which needs no grant on the child (`SRC-5`, `EXECUTED`) |
| `saved_report` | `SELECT, INSERT, DELETE` | Step 4. There is no edit, by the middle cut (`S4-7`) |
| `audit_event`, `used_setup_code`, `ingest_run` | `SELECT, INSERT` only | Append-only by design |
| Every other table and `public.v_net_worth_daily` | What its paths use, derived in step 1a | The existing suites, run as this login, fail on a missing grant. The enumeration test fails on an extra one (`C4-8`) |

**Provisioning** (`C4-6`, `C4-7`). One routine, `DatabaseLogins.EnsureAsync` (name to
settle), lives in `Infrastructure`. It creates each login if it is missing (there is no `IF
NOT EXISTS` form, `EXECUTED`), applies the whole grant and `REVOKE` script idempotently,
and sets both passwords. **The Migrator runs it after every migration run.** A new table is
then granted in the same run, and a volume reused with a new password still works. **The
Integration fixture runs the same routine,** so the tests exercise the code production
runs (`TST-1`). The password statement goes on a plain `NpgsqlCommand`, outside EF's
logger, because EF logged the literal (`SRC-7`, `EXECUTED`). It is sent as a quoted literal,
or preferably as a client-computed SCRAM verifier, since a password cannot be a bind
parameter (`EXECUTED`, `42601`).

**Wiring** (`C4-5`). The API's `WithReference(db)` is **removed**, because it hands the API
the superuser password (`SRC-6`, `EXECUTED`). Its connection string becomes a
`ReferenceExpression` over the Postgres resource's host and port and the API login's
parameter. That survives `aspire publish` (`SRC`, `EXECUTED`). The reporting
`NpgsqlDataSource` is registered **keyed**, so it can never become EF's connection
(`SRC-8`). Both passwords are `AddParameter(…, GenerateParameterDefault { Special = false
}, secret: true, persist: true)`. Without `persist` they regenerate on every `aspire run`
(`SRC-7`, `SOURCE`). Deploys keep them in deploy state, as they keep `postgres`'s (`G-26`).

**The reporting schema.** Four views over the data that exists, each `WITH
(security_barrier)` (`S4-5`), each column with a `COMMENT`:

- `v_account`: accounts with institution, open or closed, and provenance;
- `v_balance_daily`: live balances, with superseded and soft-deleted rows excluded and no
  gap-filling;
- `v_net_worth_daily`: a pass-through of `public.v_net_worth_daily`, the view the chart
  reads. A report and the chart therefore agree by construction (FR-5.5, `Kyle 2026-11-06`);
- `v_ingest_run`: import history.

**Running a query: the six parts of the boundary** (`C4-1`, `C4-2`). Every run:
1. takes a **fresh, unpooled connection** on the reporting login, so no `set_config`,
   `PREPARE` or advisory lock survives from an earlier run (`DBS-3`, `DBS-5`, `EXECUTED`);
2. opens **`BEGIN TRANSACTION READ ONLY`** and sends **`SET LOCAL statement_timeout`**;
3. sends the user's text as **a one-command `NpgsqlBatch` with positional `$n`
   parameters**. PostgreSQL itself refuses a multi-statement text (`42601`, `SRC-3` and
   `DBS-1`, `EXECUTED`), so nothing is parsed and `D5` holds. Positional parameters avoid
   Npgsql's `@name` and `:name` rewrite (`SRC-4`, `EXECUTED`);
4. reads under **one wall-clock deadline**, counting **rows and bytes** on the client. At
   either cap, or at the deadline, it calls **`cmd.Cancel()` before disposing the reader**.
   Disposing alone reads the whole remaining result (`SRC-1`, `TST-6`, `EXECUTED`), and
   `CommandTimeout` re-arms on every read (`SRC-2`, `EXECUTED`);
5. returns the columns, the rows, and "cut off", saying which cap or the deadline cut it;
6. ends in **`ROLLBACK`**, and closes the connection.

On top of that: the Migrator sets role defaults (`statement_timeout`,
`transaction_timeout`, `idle_in_transaction_session_timeout`, and `temp_file_limit`, the
one that holds; `DBS-2`) as a backstop. **One run at a time** (`S4-6`): a second waits or
gets `429`.

**Values** (`S4-2`), from configuration, so tests set them small (`SCO-12`): **10 s, 10,000
rows, 8 MB of result, `temp_file_limit` 256 MB.** `testing.md` names them.

**Results of unknown shape** (`C4-12`, `SCO-5`, `SRC-9`): each value is read by its
PostgreSQL type. `numeric` is returned as a **string**, so money keeps every digit. Dates
and times follow the slice-02 rule (UTC data; the page shows the viewer's zone).

**Saved reports** (FR-6.4, the middle cut `S4-7`): create, list, run and delete. Typed
parameters `date`, `text` and `numeric`, bound positionally. Stored in `saved_report`,
written by the API login, and run only on the reporting login.

**The schema documentation** (FR-6.6, `SCO-3`): a route that reads the views' and columns'
`COMMENT` text **live from the catalog**, on the reporting login, in the contract. The page
shows that, never a copy (`TST-10`).

**The page:** a plain text editor (no code-editor bundle, so slice 03's CSP is untouched),
the schema as a plain list, an accessible results table with a "cut off" notice and no
sorting or paging (`S4-7`), and the saved list with its parameter inputs.

**The boundary, stated.** The reporting login refuses:
- **any write**, by any route. That includes `ALTER ROLE` on itself, large objects,
  temporary tables and `DO`. The grants alone do not refuse those; the read-only
  transaction, the one statement and the targeted revokes do (`DBS-1`);
- **any read outside the `reporting` views**, including rows a view filters out
  (`security_barrier`, `DBS-4`), Identity's tables, the audit log, `saved_report` and the
  tables beneath the views;
- **running past its deadline, or returning past its row or byte cap.**

**It does not refuse** (accepted, `S4-1`, `S4-4`):
- **Exhausting memory.** One query can crash-restart PostgreSQL. Committed data survives
  crash recovery; the API's open transactions do not. For one local user this is
  availability, not integrity. The deadline, the caps and one run at a time narrow it, and
  a test shows the API serves the next request after a crash.
- **Seeing names in the catalog:** table and view names, view definitions, role names and
  their settings, and row counts of tables it cannot read (`DBS-8`). It reads no rows
  outside the views. `security.md` says so, and it is revisited before the first real
  balance.

**Revoked from `PUBLIC`, by name** (`DBS-1`; never wholesale, `SCO-13`): `EXECUTE` on
`lo_create`, `lo_creat` and `lo_from_bytea`; `TEMPORARY` on the database; `USAGE` on
`plpgsql`; and `CONNECT` on the `postgres` database.

---

## What the slice is not

- **No real data** (Kyle, 2026-11-06). Invented balances only. Admitting the first real
  balance stays its own commission, bound by slice 03's "Before the first real balance".
- **No export** (FR-6.5, Kyle, 2026-11-06). Trigger: a report worth taking elsewhere. It
  brings CSV formula injection and a download path of its own.
- **The chart is unchanged** (FR-5.5, Kyle, 2026-11-06).
- **No query parsing as a guard.** The server refuses a second statement; the app never
  reads the SQL (`research.md` D5).
- **No audit of report runs or `saved_report` changes** (`S4-3`). Deferred with `D17`, to
  real data. If built then: kind, saved or ad hoc, rows, cut off and duration. **Never the
  SQL text,** where a pasted secret would land.
- **No narrowing of the catalog** (`S4-4`). Trigger: an object name or comment that is
  itself sensitive, or the first real balance.
- **The middle cut** (`S4-7`): no editing a saved report (delete and recreate), no sorting
  or paging in the results, and no schema search.
- **No KQL** (`spec.md` FR-6, out of scope).

---

## Step 0 — the spikes

**Spikes L and N are answered by the review** (`S4-8`). Their questions were run in the
seat files (`EXECUTED`), and each answer becomes a test in the step that needs it:

| Spike question | Answered in | Becomes |
|---|---|---|
| L 1–3: `PUBLIC`'s rights; reads not granted; writes by other routes | `DBS-1`, `DBS-3`, `DBS-5`, `DBS-8` | Step 2's revoke script and refusal tests |
| L 4: a view reads with its owner's rights | DBS "Confirmed" | Step 2: no table grants for reporting |
| N 1: a cancel reaches the server | SRC N 1 | Step 3's deadline tests |
| N 2: the row cap and server work | `SRC-1`, `TST-6` | Step 3: cancel before dispose |
| N 3: multi-statement text | `SRC-3` | Step 3: the one-command batch |
| N 4: `@name` rewriting | `SRC-4` | Step 4: positional `$n` |

**Spike M, the deploy questions, still runs**, inside the steps that need it (`SCO-9`):

| Spike M question | Status | Runs in |
|---|---|---|
| M 1: passwords and their stability | `SOURCE` (`SRC-7`): `persist: true` in run mode; deploy state across deploys | Step 1a: `aspire run` twice keeps them. Step 6: two redeploys |
| M 2: a second user's connection string through publish | `EXECUTED` on a throwaway publish (SRC) | Step 1b: in the repo's AppHost |
| M 3: who creates the roles | Decided: the provisioning routine, from the Migrator | Step 1a |
| M 4: the roles on an existing volume | Decided: create if missing, then set the password every run | Step 6, over slice 03's volume |

---

## The build

Each step merges green, as slices 01–03 did.

1. **Step 1a — provisioning, the API unchanged** (`SCO-6`). `DatabaseLogins.EnsureAsync`;
   the two logins; the grant matrix; the revokes. The Migrator calls it, and the fixture
   calls it. Tests:
   - the enumeration test: every table, view, schema, sequence and function, for both
     logins, against a committed `ExpectedPrivileges`, with a planted extra grant to show
     it can fail (`TST-4`);
   - a process test of the **built Migrator** producing the same catalog snapshot
     (`TST-1`);
   - the Migrator's log contains neither password (`SRC-7`).
2. **Step 1b — the API switches** (`C4-5`). `WithReference(db)` removed; the
   `ReferenceExpression`; the host commands on the API login. Every Integration site that
   touches the database names its login (`TST-2`). Seeding and assertions run as
   superuser; the API runs as `portfolio_api`. **`G-7` closes**, with each refusal of
   `C4-4` seen failing as the owner and passing as the API login, a positive control on
   the app's own connection, and the exact `42501` (`TST-3`). Every existing suite stays
   green, which shows the matrix is enough.
3. **Step 2 — the reporting schema.** The four barrier views and their `COMMENT`s.
   `data-model.md` corrected. Tests (`C4-9`, `C4-16`):
   - each refusal of "The boundary" through the endpoint, asserting its SQLSTATE (a
     misspelt table, `42P01`, does not count);
   - writes judged by effect (`lo_from_bytea` succeeded with no error, `TST-5`);
   - the leaky-function probe, seen failing on a view without the barrier (`DBS-4`);
   - the catalog test that every `reporting` view has `security_barrier`;
   - the view rules;
   - `reporting.v_net_worth_daily` equal to the chart's view;
   - the reporting login cannot read `saved_report` or `audit_event`.
4. **Step 3 — running a query.** The six parts, the caps, one run at a time, the typing
   rule, the schema route, and problem details that pass on PostgreSQL's message, never a
   connection string. `SecretsStayOutTests` covers both new passwords (`TST-14`). Tests,
   each seen failing:
   - a streaming producer (`generate_series`) cut at the row cap, its backend seen active
     by pid and then gone (`TST-6`, `TST-7`);
   - the cap at n = cap−1, cap and cap+1 (`TST-8`);
   - a wide row cut at the byte cap;
   - `SET statement_timeout = 0` refused as a second statement;
   - `set_config` in one run not visible to the next (`DBS-3`);
   - a crash: the API serves the next request (`S4-1`).
5. **Step 4 — saved reports.** `saved_report` (create, list, run, delete) and typed
   positional parameters. The binding probe uses values **the grant allows**, so only the
   binding can explain the result (`TST-9`): an echo report returns a hostile value byte
   for byte, and `nobody' OR 'a'='a` matches nothing.
6. **Step 5 — the page.** axe in light and dark in named states (empty, results, cut off,
   refused, timed out, saved list, schema), and the keyboard path asserted (`TST-11`).
7. **Step 6 — the published stack** (`C4-14`).
   - **In CI**, as far as it can go: the app-model test that the API's connection uses its
     own parameter and not the `postgres` password.
   - **By hand**, over slice 03's volume and through two redeploys: the catalog snapshot
     equal to `ExpectedPrivileges`; the passwords unchanged; the API signed in and working
     as its own login; one refusal probe holding in the container. Recorded with date,
     commit and machine (`D13`).
   - **The README runbook** covers three logins, their passwords and recovery (`SCO-8`).

---

## Definition of done

Each item names the test that shows it and how that test was seen failing.

1. **The API's own login.** The app connects as `portfolio_api`, which is not superuser and
   owns nothing (`pg_shdepend`), with privileges equal to `ExpectedPrivileges.Api`. On that
   connection, `DISABLE` and `DROP TRIGGER`, `DROP TABLE audit_event`, `DROP FUNCTION
   append_only() CASCADE`, `TRUNCATE`, `UPDATE` and `DELETE` on `audit_event`, `used_setup_code`
   and `ingest_run`, and `ALTER DATABASE` each fail with `42501`. **Seen failing:** each
   succeeds as the owner in a rolled-back transaction. **`G-7` is closed**, and `security.md`
   "Audit" says the immutability is now a database boundary.
2. **The reporting login holds exactly its grant.** Its catalog snapshot equals
   `ExpectedPrivileges.Reporting`. Every refusal in "The boundary" fails through the endpoint
   with its expected SQLSTATE. After the probes, the catalog, the large objects, ownership and
   every table's rows are unchanged. **Seen failing:** a planted grant is flagged;
   `lo_from_bytea` succeeds while its `EXECUTE` is still granted; the leaky function sees a
   filtered row without the barrier.
3. **Bounded.** Each producer ends as stated: timed out, cut off by rows, cut off by bytes, or
   refused. Each backend is seen active by pid, then gone. A setting made in one run is not
   visible to the next. After a crash, the API serves the next request. **Seen failing:**
   disposing without `Cancel()` lets the streaming producer run past the deadline.
4. **Bound parameters.** The echo report returns the hostile value byte for byte; the effect
   report matches nothing for `nobody' OR 'a'='a`; a wrongly typed value answers `400` before
   the database. **Seen failing:** an interpolating runner turns both red.
5. **Live documentation.** A `COMMENT` changed at test time appears in the schema route and on
   the page. Every view and column has one. **Seen failing:** a nulled comment is named.
6. **The page.** axe is clean in light and dark in each named state, and the keyboard path is
   asserted on the focused element.
7. **The contract** (`SCO-14`). The new routes are in the committed OpenAPI document, which is
   byte-stable, and the contract test covers them.
8. **Nothing regressed, for the right reason.** Every Integration site names its login.
   Slices 01–03's DoD items hold. CI is green, including both time-zone jobs, and the E2E is
   green locally, recorded with date, commit and machine (`D13`).
9. **Published.** As step 6 lists.
10. **Each obligation below is done, or recorded as not done with a reason.** Each refusal in
    "The boundary" is marked **shown**, with its test, or **argued**.

---

## Gates

`INFERRED` claims the build rests on, each with what raises it:

| Id | Claim | Raised by |
|---|---|---|
| G4-1 | `aspire run` twice keeps both passwords (`persist: true`) | Step 1a |
| G4-2 | The `ReferenceExpression` connection string works in this repo's AppHost under `aspire run` and in the published compose file | Step 1b and step 6 |
| G4-3 | Cancel-then-dispose also holds on the batch path and for multi-row results (TST's GT-D) | Step 3's producer table |
| G4-4 | A single 200 MB cell does not reach the API's memory whole under the byte cap (TST's GT-C) | Step 3's wide-row producer, with the API's working set read |
| G4-5 | The app-model test runs on CI's ubuntu runner (TST's GT-A) | Step 6's first CI run |

---

## Obligations this slice carries

| From | Item | Here |
|---|---|---|
| slice 03, "Before the first real balance" 3 (`D6`, `G-7`) | The application login | **Built here** (Kyle, 2026-11-06) |
| slice 03, "Before the first real balance" 2 (`D5`) | The backup command | **Carried, and changed** (`SCO-11`): a dump now carries three logins and their grants, so the restore must recreate the roles before the data. The routine of step 1a does that, and the backup's design must say so |
| slice 03, "Before the first real balance" 4 (`D17`) | The wider audit | **Carried**, now including report runs and `saved_report` changes (`S4-3`) |
| slice 03, "Before the first real balance" 1, 5, 6 | Commission, dev page (`D9`), `C-389` | **Carried** |
| `research.md` D5 banner | The reporting role "false as built" | **Built here**. The banner is updated to say what holds and why |
| slice 03's review deferrals | Each with its trigger | **Carried**, unchanged |

---

## The gate — slice 01's rule, unchanged

`LOG.md` carries on with both columns, one line per session, written as it goes.

> **The default at the end of slice 04 is STOP.** A fifth slice, or the first real balance,
> requires its own commission, **written and dated in this repository**, as this one is.
>
> The default is overridden only if the slice-04 assessment names **three techniques this
> slice taught that Kyle did not already have**. Each must come with the commit that
> demonstrates it, and must **not** be any of the twenty-one slices 01 to 03 already
> claimed. **And** Kyle's hours must have stayed under **200** (`LOG.md`, `Kyle h`).
>
> **Explicitly NOT reasons to continue:** that the slice works, and that the next slice is
> obvious.

**Stated, not smoothed:** the agent builds, so the hours limb cannot trip. The gate rests
on the techniques limb and on Kyle's own judgement of what the slice taught.

Write `slice-04-assessment.md`. It answers:

- What did it cost, in both columns, against the estimate above?
- Which of the review's executed claims were wrong once built, and what in `security.md`,
  `data-model.md` and `research.md` was corrected as a result?
- For each login, which refusals were **shown** by a test, and which only argued?
- Which techniques did this slice teach that Kyle did not already have, each with its
  commit?
- Is a fifth slice wanted, or the first real balance?

## Then stop
