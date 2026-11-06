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

**Reviewed again, 2026-11-06** (round 2, the same file, "Round 2"). Two seats read the
rewrite: 17 findings, none blocking (`R2-DBS-1` to `R2-DBS-5`, `R2-TST-1` to `R2-TST-12`).
Kyle ruled `S4-9` and `S4-10`. Their fixes are applied below.

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
settle), lives in `Infrastructure`. **The Migrator runs it after every migration run,
whether or not a migration was pending.** A new table is then granted in the same run, and
a volume reused with a new password still works. **The Integration fixture runs the same
routine,** so the tests exercise the code production runs (`TST-1`). On every run it:
1. creates each login if it is missing, **without a password** (`CREATE ROLE … LOGIN`,
   catching `42710` for the race; there is no `IF NOT EXISTS` form, `EXECUTED`);
2. **re-asserts its attributes**, `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
   NOBYPASSRLS`, and fails if the login is a member of any role (`R2-DBS-3`);
3. applies the whole grant and `REVOKE` script idempotently, including `CONNECT` on the
   app's own database (`current_database()`), granted explicitly, never left to `PUBLIC`
   (`R2-TST-2`);
4. sets each password with `ALTER ROLE … PASSWORD`, as a **client-computed SCRAM verifier,
   always** (`R2-DBS-2`). A failed statement is written to PostgreSQL's own log with its
   text (`log_min_error_statement`, `EXECUTED`), and a logged verifier is not a login. The
   statement goes on a plain `NpgsqlCommand`, outside EF's logger, which logged the literal
   (`SRC-7`, `EXECUTED`). A password cannot be a bind parameter (`EXECUTED`, `42601`).

**Wiring** (`C4-5`). The API's `WithReference(db)` is **removed**, because it hands the API
the superuser password (`SRC-6`, `EXECUTED`). Its connection string becomes a
`ReferenceExpression` over the Postgres resource's host and port and the API login's
parameter. That survives `aspire publish` (`SRC`, `EXECUTED`). The reporting
`NpgsqlDataSource` is registered **keyed**, so it can never become EF's connection, and the
API **refuses to start** without its own connection string (`SRC-8`; restored by
`R2-DBS` "Round-1 findings"). Both passwords are `AddParameter(…, GenerateParameterDefault { Special = false
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
4. reads under **one wall-clock deadline**, with **`CommandBehavior.SequentialAccess`**,
   counting **rows and bytes** on the client. Text and `bytea` cells are read in chunks
   (`GetTextReader`, `GetStream`) against the remaining byte budget. The default reader
   allocated a whole 100 MB cell before a byte could be counted; the sequential reader
   allocated none (`R2-TST-6`, `EXECUTED`). A byte is a UTF-8 byte of the returned cell, and
   the row that crosses a cap is dropped. At either cap, or at the deadline, it calls
   **`cmd.Cancel()` before disposing the reader**. Disposing alone reads the whole
   remaining result (`SRC-1`, `TST-6`, `EXECUTED`), and `CommandTimeout` re-arms on every
   read (`SRC-2`, `EXECUTED`);
5. returns the columns, the rows, and "cut off", saying which cap or the deadline cut it;
6. ends in **`ROLLBACK`**, with the user's statement the last one before it, and closes the
   connection. The backend's pid is read from the connection (`ProcessID`), never by a
   statement after the user's (`R2-DBS-5`).

On top of that: the Migrator sets role defaults (`statement_timeout`,
`transaction_timeout`, `idle_in_transaction_session_timeout`, and `temp_file_limit`, the
one that holds; `DBS-2`) as a backstop. **One run at a time** (`S4-6`): a second run,
ad hoc or saved, gets **`429` with a named problem title** (`S4-10`). The slot is released
in a `finally`, whether the run ends by its deadline, a refusal or the client going away.
The schema route does not take the slot.

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
- **any write, by every route tested** (`R2-DBS-1`: the claim is no wider than the tests).
  That includes `ALTER ROLE` on itself, large objects, temporary tables, `DO`, sequences,
  locks that block writers, and WAL messages (`pg_logical_emit_message`, whose
  non-transactional form a `ROLLBACK` does not undo, `EXECUTED`). The grants alone do not
  refuse those; the read-only transaction, the one statement and the targeted revokes do
  (`DBS-1`). DoD 10 lists every route tested;
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
  their settings, row counts of tables it cannot read, other sessions' metadata (not their
  query text) and the locks held (`DBS-8`, `R2-DBS-5`). It reads no rows outside the views.
  `security.md` says so, and it is revisited before the first real balance.
- **Holding locks for the length of a run** (`S4-9`, `R2-DBS-4`): an advisory key the API
  also takes at enrolment, and `ACCESS SHARE` on the tables beneath the views. The API can
  wait for up to the deadline. One run at a time and the 10 s deadline bound it, and the
  table locks cannot be revoked anyway.

**Revoked from `PUBLIC`, by name** (`DBS-1`, `R2-DBS-1`; never wholesale, `SCO-13`):
`EXECUTE` on `lo_create`, `lo_creat`, `lo_from_bytea` and both `pg_logical_emit_message`
overloads; `TEMPORARY` on the app's database; `USAGE` on `plpgsql`; and `CONNECT` on the
`postgres` and `template1` databases (`R2-DBS-5`). The four original revokes leave the API
login working (`R2-DBS`, `EXECUTED`).

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
| M 4: the roles on an existing volume | Decided: create if missing, then set the password every run | Step 1a in CI (the process test's second run and pre-existing role, `R2-TST-3`); step 6 by hand, over slice 03's volume |

---

## The build

Each step merges green, as slices 01–03 did.

1. **Step 1a — provisioning, the API unchanged** (`SCO-6`). `DatabaseLogins.EnsureAsync`;
   the two logins; the grant matrix; the revokes. The Migrator calls it and the fixture
   calls it. `PostgresFixture` names its database `portfolio_dev`, not `postgres`, so the
   app's database and the revoked one are different objects in tests as in production
   (`R2-TST-2`). Tests:
   - **The catalog snapshot** (`R2-TST-4`, `R2-DBS-3`), built from **effective**
     privileges (`has_table_privilege`, `has_any_column_privilege`, `has_sequence_privilege`,
     `has_schema_privilege`, `has_database_privilege`, `has_function_privilege`), not grant
     rows. Its scope: role attributes including `rolcanlogin`; memberships both ways;
     `pg_db_role_setting`; `pg_default_acl`; `pg_shdepend`; every database (`CONNECT`,
     `CREATE`, `TEMP`); every language; every non-system schema, relation, sequence and
     function, plus the named built-ins; large objects; `max_prepared_transactions = 0`.
     Databases are named by role (`app`, `postgres`, `template1`), so one committed
     `ExpectedPrivileges` serves Integration and step 6. **Plants**, each a committed test
     that flags exactly its row: a column grant; a default ACL followed by a new table;
     `GRANT TEMP` back to `PUBLIC`; `GRANT pg_read_all_data` to the reporting login.
   - **The built Migrator, as a process** (`R2-TST-3`):
     1. in a fresh container of its own, run `Migrator.dll` with both passwords (P1, R1):
        exit 0, snapshot equal, both logins connect;
     2. run it again with P2, R2, with no migration pending: exit 0, snapshot unchanged,
        P2 and R2 connect, and P1 and R1 give `28P01`;
     3. in a second container, with `portfolio_reporting` created beforehand by hand: exit
        0, snapshot equal;
     4. no run's output contains any password. A password statement made to fail on
        purpose leaves no password in the PostgreSQL container's log (`R2-DBS-2`).

     **Seen failing:** with the call removed from `Program.cs`, item 1 goes red. With the
     call inside `pending.Count > 0`, item 2 goes red. With the statement sent through
     `ExecuteSqlRawAsync`, item 4 goes red.
   - **The migration that creates `reporting` runs up, down and up**, with the routine
     called after each `up` (`R2-TST-12`; `MigrationOnRowsTests`' pattern).

   `ExpectedPrivileges.Api` has no behaviour behind it until 1b, and 1b is expected to edit
   it: that is where the matrix is first shown sufficient (`R2-TST-12`).

   > **BUILT 2026-11-06.**
   > - **The routine,** `src/Infrastructure/DatabaseLogins.cs`. Per login: create if missing
   >   without a password, then re-assert the attributes and refuse any membership or
   >   ownership. Then, in one transaction, revoke everything the two logins hold on
   >   `public`'s objects and grant the matrix again. Then the passwords, as SCRAM verifiers.
   >   The `pg_catalog` functions are revoked by looking up every overload on the server.
   >   `CONNECT, TEMPORARY` on `postgres` are both revoked from `PUBLIC`.
   > - **The Migrator** calls the routine after every run, with `DatabaseLogins__ApiPassword`
   >   and `__ReportingPassword` from the AppHost's `api-db-password` and
   >   `reporting-db-password` (`persist: true`, no specials). A missing password fails the
   >   run. `aspire publish` writes them as `${API_DB_PASSWORD}` and
   >   `${REPORTING_DB_PASSWORD}`, keys only. [`EXECUTED`]
   > - **The E2E fixture** sets both parameters, so `persist: true` never writes a test's
   >   passwords into the developer's user secrets.
   > - **The snapshot** (`tests/Integration/CatalogSnapshot.cs`), against
   >   `ExpectedPrivileges/portfolio_{api,reporting}.txt`. Besides the matrix, both files hold
   >   `PUBLIC`'s defaults, each with its reason:
   >   - `CONNECT` on `template0`, which takes no connections;
   >   - `USAGE` on `c`, `internal` and `sql`, with no `CREATE` on any schema to use them;
   >   - `USAGE` on `public`, which reveals names;
   >   - `EXECUTE` on the four trigger functions, which refuse a direct call;
   >   - `lo_unlink`, `set_config`, `pg_cancel_backend` and `pg_terminate_backend`, which
   >     reach only the login's own objects and backends;
   >   - the advisory locks (`S4-9`).
   >
   >   The reporting login holds nothing else until step 2.
   > - **Tests:** 11 new ones. `DatabaseLoginsTests`: both snapshots; four plants, each flagged;
   >   a second run changing nothing; a login with a membership refused.
   >   `MigratorProcessTests`: the built Migrator twice, and over a pre-existing login; and a
   >   failed password statement, whose verifier is in the server's log while its password is
   >   not.
   > - **Seen failing** [`EXECUTED`], each a mutation, reverted:
   >   - no call in the Migrator: both Migrator tests red;
   >   - the call only when a migration is pending: the second run red;
   >   - a password sent through EF: the output check red;
   >   - the plain password sent, not a verifier: the server-log test red;
   >   - `app_setting` dropped from the matrix: the API snapshot red, the reporting one green.
   > - **Runs:** Integration 157/157 (146 + 11). Architecture 65. Unit 129 + 1 skipped.
   >   Contract 1. E2E 27 + 1 skipped.
   > - **Open:** `G4-1` (`aspire run` twice keeps both passwords) is by hand and not yet run.
2. **Step 1b — the API switches** (`C4-5`). `WithReference(db)` removed; the
   `ReferenceExpression`; the host commands on the API login; the API refuses to start
   without its connection string. **The compiler lists every test site** (`R2-TST-5`):
   `PostgresFixture.ConnectionString` is deleted, and replaced by
   `SuperuserConnectionString`, `ApiConnectionString` and `ReportingConnectionString`.
   `CreateContext()` becomes `CreateSuperuserContext()`, for arrangement only. Every site
   must then choose. Seeding and assertions run as superuser; the API runs as
   `portfolio_api`. Tests:
   - **Who-am-I**, per kind of factory (`AuthFixture`, the `ImportApiTests` factory, the
     process test): through the app's own `PortfolioDbContext`, `current_user` is
     `portfolio_api` and not superuser. **Seen failing:** handed the superuser string.
   - **`G-7` closes:** DoD 1's table, on the app's own connection.
   - **Every existing suite stays green**, which shows the matrix is enough.
   - **The E2E:** after one API request, `pg_stat_activity` shows a `portfolio_api`
     backend and no `postgres` backend except the test's own.

   > **BUILT 2026-11-06.**
   > - **The AppHost:** the Api's `ConnectionStrings__portfolio` is a `ReferenceExpression`
   >   over the Postgres endpoint's host and port, the database name, `portfolio_api` and
   >   `api-db-password`, built the way `PostgresServerResource` builds its own.
   >   `WithReference(db)` is gone from the Api. Published, the Api's only database variable
   >   is `Host=postgres;Port=5432;Database=portfolio_dev;Username=portfolio_api;Password=${API_DB_PASSWORD}`,
   >   with no `POSTGRES_PASSWORD` and no `PORTFOLIO_*`. [`EXECUTED`: `aspire publish`] With
   >   the E2E's run-mode check, that raises `G4-2`.
   > - **The Api refuses to start** without `ConnectionStrings:portfolio`. The build-time
   >   OpenAPI tool (`GetDocument.Insider`) is excepted, since it runs the entry point with
   >   no database; the committed document is unchanged.
   > - **The fixture split, as written.** The compiler listed 37 sites. The seven that call
   >   the app's own code (`BalanceRecorder`, `ImportPipeline`, `NetWorthDay`), and the five
   >   that hand the app its connection string, now use the API login. The rest use the
   >   superuser. **The grant matrix was enough unchanged:** the whole suite passed on it
   >   first time, so `ExpectedPrivileges.Api` was not edited after all.
   > - **`ApiLoginTests`** (18 cases):
   >   - DoD 1's table, 16 statements, each run on the owner (succeeds, or `23001` from the
   >     trigger) and on the Api's own connection from its own services (`42501`);
   >   - who-am-I for both factory kinds;
   >   - the refusal to start.
   >
   >   **Two corrections to round 2,** both found by running it:
   >   - the trigger's refusal is `23001` (`restrict_violation`), not `P0001` (R2-TST-1
   >     had it wrong; `AuthSchemaTests` already asserted `23001`);
   >   - the owner's `TRUNCATE ingest_run` is refused first by `account_balance`'s foreign
   >     key (`0A000`), so that row uses `CASCADE` to reach the trigger.
   > - **E2E:** `AppModelTests` checks that the Api's connection uses `api-db-password` and
   >   that no environment value reaches the postgres password, following nested
   >   expressions. It runs locally with the E2E; CI only compiles that project, so putting it
   >   in CI is step 6's (`G4-5`). `ImportAFileTests` checks that the running Api is
   >   connected as `portfolio_api`.
   > - **Seen failing** [`EXECUTED`], each a mutation, reverted:
   >   - `WithReference(db)` back in place: both E2E checks red;
   >   - the fixture handing the Api the superuser: 17 of 18 `ApiLoginTests` red (the
   >     startup refusal needs no connection);
   >   - the startup check removed: its test red.
   > - **Runs:** Integration 175 (157 + 18), E2E 29 + 1 skipped, Architecture 65, Unit 129 +
   >   1 skipped, Contract 1.
   > - **`G-7` is closed.** `security.md` "Audit" says so, and slice 03's "Before the first
   >   real balance" item 3 is marked done.
   > - **Not built: a who-am-I for the host-command process.** `OwnerCommandsTests` runs
   >   that process with the API login's string, and its `reset-owner` passes on it, so the
   >   matrix is enough for the commands. But the test would pass on the superuser too, so it
   >   does not show which login the process holds. In compose the commands run in the
   >   `api` service, whose only database variable is the API login's. **Argued**, for DoD 10.
3. **Step 2 — the reporting schema.** The four barrier views and their `COMMENT`s.
   `data-model.md` corrected. Tests (`C4-9`, `C4-16`):
   - each refusal of "The boundary" through the endpoint, asserting its SQLSTATE (a
     misspelt table, `42P01`, does not count). The reporting login connecting to
     `postgres` gives `42501`;
   - writes judged by effect (`lo_from_bytea` succeeded with no error, `TST-5`);
     `pg_logical_emit_message` returns an LSN while its `EXECUTE` is granted, and `42501`
     after the revoke (`R2-DBS-1`);
   - the leaky-function probe, seen failing on a view without the barrier (`DBS-4`);
   - the catalog test that every `reporting` view has `security_barrier`;
   - the view rules;
   - `reporting.v_net_worth_daily` equal to the chart's view;
   - the reporting login cannot read `saved_report` or `audit_event`.

   > **BUILT 2026-11-06.**
   > - **Migration 6, `Reporting`:** the schema and four views, each `WITH
   >   (security_barrier)`, with a `COMMENT` on the schema, every view and every column. The
   >   EF model is unchanged; the migration is SQL. `Down` drops the schema. `v_account`
   >   shows the formats an account is linked under, not the identifiers. `v_balance_daily`
   >   also drops the balances of a deleted account. `v_net_worth_daily` names the chart
   >   view's seven columns.
   > - **The routine** revokes everything the two logins hold in `reporting` and grants the
   >   reporting login `USAGE` and `SELECT ON ALL TABLES IN SCHEMA reporting`, so a view added
   >   later is granted in the same run. `ExpectedPrivileges/portfolio_reporting.txt` gains
   >   exactly those five lines; the API's file is unchanged.
   > - **The refusals are run on a stand-in for the runner,** `ReportingFixture.RunAsync`:
   >   parts 1–3 and 6 (a fresh unpooled connection, `BEGIN TRANSACTION READ ONLY`, `SET LOCAL
   >   statement_timeout`, the text as a one-command batch, `ROLLBACK`). There is no endpoint
   >   until step 3, which owes **running the same table through the endpoint**, with
   >   `sqlState` in the problem details (`TST-5`).
   > - **`ReportingSchemaTests`** (52 cases):
   >   - the catalog: `reporting` holds exactly the four views, each a barrier view; every
   >     view and column has a comment;
   >   - the view rules, read on the reporting login: deleted, superseded and deleted-account
   >     rows hidden, no gap filled, `v_net_worth_daily` equal to the chart's view in both
   >     directions (`EXCEPT ALL`);
   >   - the leak probe (`DBS-4`) over `v_balance_daily` and `v_account`, with a committed
   >     control: the same probe over an **unbarriered copy** of `v_balance_daily`'s
   >     definition sees `555555`, a live balance on a deleted account. The probe is pushed
   >     into the balance scan, below the join that drops a deleted account. The superseded
   >     and soft-deleted balances do not leak even then; their `IS NULL` filters cost less
   >     than any function, as `DBS-4` found. So `v_account` does not leak unbarriered today,
   >     and has no control of its own;
   >   - 37 refusals, each by its SQLSTATE (below), and `CONNECT` to `postgres` and
   >     `template1` refused (`42501`);
   >   - **by effect** (`TST-5`): after every probe, the login's snapshot and settings, the
   >     large objects, a view's comment and every table's row count are unchanged;
   >   - the two revokes seen failing, by granting `EXECUTE` back: `pg_logical_emit_message`
   >     then returns an LSN from inside the read-only transaction; `lo_from_bytea` is still
   >     refused there (`25006`), and outside one writes a large object the login owns.
   > - **The refusals, and which layer refuses** (for DoD 10, all **shown**):
   >   - `25006`, the read-only transaction: `ALTER ROLE` itself (password, `SET work_mem`,
   >     `SET statement_timeout`), `CREATE TEMP TABLE`, `CREATE FUNCTION pg_temp…`, `COMMENT
   >     ON` a view;
   >   - `42501`, a privilege or a revoke: `lo_from_bytea`, `lo_create`, `lo_creat`,
   >     `lo_import`, both `pg_logical_emit_message` forms, `DO` (plpgsql), `nextval`, `LOCK
   >     … IN SHARE MODE` on a view, `LOCK` on a table beneath, `FOR UPDATE`, `COPY … TO
   >     PROGRAM`, `pg_read_file`, `SET ROLE`, `SET SESSION AUTHORIZATION`, and 13 reads
   >     outside the views (the tables beneath, Identity, `audit_event`, `used_setup_code`,
   >     `app_setting`, the chart's view and its spine);
   >   - `25001`: `set_config('transaction_read_only', 'off', true)`;
   >   - `42601`, one statement: `SET TRANSACTION READ WRITE; ALTER ROLE …` and `COMMIT;
   >     ALTER ROLE …`.
   > - **Two corrections to the review,** found by running it:
   >   - **`SET TRANSACTION READ WRITE` as the whole statement succeeds.** `DBS-1` found the
   >     mode could be undone only before the first query; `SET LOCAL statement_timeout`
   >     takes no snapshot, so the user's statement is still before it. It writes nothing:
   >     it is the run's one statement, `ROLLBACK` follows, and joined to a write it is
   >     `42601`. **One statement is what holds here, not the read-only mode.** Its test
   >     asserts it succeeds and nothing changes. Step 3 may close it outright by taking a
   >     snapshot before the user's text (`INFERRED`), and if so turns that test round;
   >   - `LOCK … IN SHARE MODE` on a view is `42501`, not `25006`: a lock above `ACCESS
   >     SHARE` needs a write privilege, checked first.
   > - **The up/down/up test** (`R2-TST-12`, carried from step 1a): from migration 5, up
   >   with the routine after it, the snapshot equal; down, `reporting` gone and the chart's
   >   view kept; up again, the snapshot equal.
   > - **Seen failing** [`EXECUTED`], each a mutation, reverted:
   >   - `security_barrier` dropped from `v_balance_daily` in the migration: the catalog test
   >     and that view's leak test red;
   >   - one column's `COMMENT` dropped: the comment test red, naming the column;
   >   - the reporting `SELECT` grant dropped: both snapshots, every view-rule test and the
   >     leak tests red; `pg_logical_emit_message` dropped from the revokes: its two refusals
   >     and its control red.
   >
   >   **The first run of the barrier mutation passed, wrongly.** The control then altered
   >   the real view and set `security_barrier = true` in its `finally`, repairing the
   >   mutant before the catalog test ran. It now builds its own unbarriered copy and leaves
   >   the real views alone, and the mutation goes red.
   > - **`data-model.md`** corrected: the tables are in `public`, not `app` (banner and
   >   "Two schemas"), and "The `reporting` schema" rewritten to the four views built, with
   >   the seven planned ones marked not built. **`security.md`** "Database": the two logins
   >   as built and the boundary as `DBS-1` restated it, with the runner's three parts marked
   >   step 3's.
   > - **Not built here: the `saved_report` refusal.** The table arrives in step 4; a read of
   >   it now is `42P01`, which by `TST-5` does not count. Step 4 owes that row. `audit_event`
   >   is in the table above.
   > - **Runs:** Integration 228 (175 + 52 + 1). Architecture 65. Unit 129 + 1 skipped.
   >   Contract 1. E2E 29 + 1 skipped, locally: the Migrator applies migration 6 and the
   >   routine grants the views on a real app model.
4. **Step 3 — running a query.** The six parts, the caps, one run at a time, the typing
   rule, the schema route, and problem details that pass on PostgreSQL's message, never a
   connection string. `SecretsStayOutTests` covers both new passwords, including the two
   producers that are not a `PostgresException` (`TST-14`, `R2-TST-11`). Tests, each seen
   failing:
   - **The producer table** (`R2-TST-7`), with the deadline at 2 s by option, each row
     through the endpoint, each running backend seen active by pid and then gone
     (`TST-7`):

     | Producer | Expected |
     |---|---|
     | `set_config('statement_timeout','0',true)` with `pg_sleep(600)` | timed out |
     | `count(*)` over `generate_series` to 1e11 | timed out |
     | an `ORDER BY` over `generate_series` to 1e11, with a small `temp_file_limit` | refused, `53400` |
     | `generate_series` to 1e11 in the target list | cut off by rows |
     | an unbounded `WITH RECURSIVE` | cut off by rows |

     **Seen failing:** with the deadline removed, the first two run until the test's own
     deadline. A separate test asserts the Production values: 10 s, 10,000 rows, 8 MB and
     256 MB.
   - **The caps' boundaries:** rows at cap−1, cap and cap+1; and bytes, with the cap at
     1,000 by option, at 999, 1,000 and 1,001. Cut off: false, false, true (`TST-8`,
     `R2-TST-6`).
   - **Memory:** `GC.GetTotalAllocatedBytes` across one request for a cell of 1e8 bytes,
     with the byte cap at 64 KB, stays under 16 MB. It runs in a collection with
     parallelization off. **Seen failing:** `CommandBehavior.Default` allocates at least
     the cell (`R2-TST-6`).
   - **No session state survives a run** (`R2-TST-8`): two runs report different pids; an
     advisory lock taken in a run is not held by any reporting backend once it returns;
     a statement prepared in one run is unknown in the next (`26000`). **Seen failing:** a
     pooled data source with `No Reset On Close=true`. The `set_config` row tests part 6,
     seen failing with `COMMIT` in place of `ROLLBACK`.
   - **The crash** (`R2-TST-9`), in a class of its own. Warm the API's pool. Start a long
     run, and once its backend is active, kill it with `SIGKILL` inside the container. Wait
     for PostgreSQL to answer again, and assert it restarted (the log says
     `reinitializing`, and the API's backends are gone). The run answers a named problem,
     not a hang. **The first** signed-in request afterwards answers 200, and so does a run
     of `SELECT 1`. **Seen failing:** with EF's retry disabled, that first request answers
     500. The memory-induced crash itself is shown by hand in step 6.
   - **One run at a time** (`R2-TST-10`), by order, not by time: run A sleeps, and once
     its backend is active, run B (a saved report) gets `429` and the reporting backend
     count is still 1. Abort A; once its pid is gone, run C succeeds. Repeat, with A ending
     by its deadline and by a refusal. A schema request during A answers 200. **Seen
     failing:** without the gate, B sees two backends; with the release outside `finally`,
     C gets `429`.
5. **Step 4 — saved reports.** `saved_report` (create, list, run, delete) and typed
   positional parameters. The binding probe uses values **the grant allows**, so only the
   binding can explain the result (`TST-9`): an echo report returns a hostile value byte
   for byte, and `nobody' OR 'a'='a` matches nothing.
6. **Step 5 — the page.** axe in light and dark in named states (empty, results, cut off,
   refused, timed out, saved list, schema), and the keyboard path asserted (`TST-11`).
7. **Step 6 — the published stack** (`C4-14`).
   - **In CI**, as far as it can go: the app-model test that the API's connection uses its
     own parameter and not the `postgres` password.
   - **By hand**, over slice 03's volume and through two redeploys (`R2-TST-11`): the
     shared catalog-snapshot file's output equal to `ExpectedPrivileges`; each password
     parameter's `sha256` in deploy state unchanged, its value never printed; the API
     signed in and working after each redeploy, which shows the login took the value the
     API holds; one refusal probe holding in the container; and the memory-induced crash
     under 256 MB, with the API recovering. Recorded with date, commit and machine (`D13`).
   - **The README runbook** covers three logins, their passwords and recovery (`SCO-8`).

---

## Definition of done

Each item names the test that shows it and how that test was seen failing.

1. **The API's own login.** The app connects as `portfolio_api`, which is not superuser,
   owns nothing (`pg_shdepend`) and belongs to no role, with effective privileges equal to
   `ExpectedPrivileges.Api`. Each statement below gives both its outcomes (`R2-TST-1`): the
   SQLSTATE that differs is the evidence that the grant, not the trigger, refuses.

   | Statement | As the owner, rolled back | On the app's connection |
   |---|---|---|
   | `DISABLE TRIGGER`, `DROP TRIGGER`, `DROP TABLE audit_event`, `DROP FUNCTION append_only() CASCADE` and `ingest_run`'s own function, `ALTER DATABASE … SET work_mem` | succeeds | `42501` |
   | `SET session_replication_role = replica`, then `UPDATE audit_event` | the `UPDATE` succeeds | `42501` on the `SET` |
   | `UPDATE`, `DELETE`, `TRUNCATE` on `audit_event`, `used_setup_code`, `ingest_run` | `P0001`, the trigger | `42501`, the grant |

   **`G-7` is closed**, and `security.md` "Audit" says the immutability is now a database
   boundary.
2. **The reporting login holds exactly its grant.** Its snapshot equals
   `ExpectedPrivileges.Reporting`. Every refusal in "The boundary" fails through the endpoint
   with its expected SQLSTATE. After the probes, the catalog, the large objects, ownership and
   every table's rows are unchanged. **Seen failing:** each plant is flagged; `lo_from_bytea`
   and `pg_logical_emit_message` succeed while their `EXECUTE` is still granted; the leaky
   function sees a filtered row without the barrier.
3. **Bounded.** Each producer in step 3's table ends as stated, with its backend seen active by
   pid and then gone. The caps cut at their boundaries, and the byte cap holds memory under its
   bound. No session state survives a run. One run at a time answers `429`. After a crash, the
   first request succeeds. **Seen failing:** as step 3 lists for each.
4. **Bound parameters.** The echo report returns the hostile value byte for byte; the effect
   report matches nothing for `nobody' OR 'a'='a`; a wrongly typed value answers `400` before
   the database. **Seen failing:** an interpolating runner turns both red.
5. **Live documentation.** A `COMMENT` changed at test time appears in the schema route and on
   the page. Every view and column has one. **Seen failing:** a nulled comment is named.
6. **The page.** axe is clean in light and dark in each named state, and the keyboard path is
   asserted on the focused element.
7. **The contract** (`SCO-14`, `R2-TST-11`). The new routes are in the committed OpenAPI
   document, which is byte-stable. The contract test covers them, with the committed document
   a superset of what the app serves, and stays advisory in CI as it is today.
8. **Nothing regressed, for the right reason.** Every Integration site chose its login (the
   compiler made it). Slices 01–03's DoD items hold. CI is green, including both time-zone
   jobs. The E2E is green locally, with `portfolio_api` shown in `pg_stat_activity`, recorded
   with date, commit and machine (`D13`).
9. **Published.** As step 6 lists.
10. **Each obligation below is done, or recorded as not done with a reason.** Each refusal in
    "The boundary", and every write route tested, is listed and marked **shown**, with its
    test, or **argued**. "The API serves the next request after a crash" is shown in CI;
    "one query can crash PostgreSQL" is argued in CI and shown by hand (`R2-TST-9`).

---

## Gates

`INFERRED` claims the build rests on, each with what raises it:

| Id | Claim | Raised by |
|---|---|---|
| G4-1 | `aspire run` twice keeps both passwords (`persist: true`) | Step 1a, by hand |
| G4-2 | The `ReferenceExpression` connection string works in this repo's AppHost under `aspire run` and in the published compose file | Steps 1b and 6, by hand |
| G4-3 | Cancel-then-dispose also holds on the batch path and for multi-row results (TST's GT-D) | Step 3's producer table |
| G4-4 | ~~A single 200 MB cell does not reach the API's memory whole under the byte cap~~ **Answered** (`R2-TST-6`, `EXECUTED`): only with `SequentialAccess`, which part 4 now requires. Its test is step 3's memory test | — |
| G4-5 | The app-model test runs on CI's ubuntu runner (TST's GT-A) | Step 6's first CI run |
| G4-6 | EF's retrying strategy recovers the API after PostgreSQL crash-restarts (`EXECUTED` at the Npgsql level only, `R2-TST-9`) | Step 3's crash test |

TST's round-1 gate GT-B (`DROP ROLE` in a migration's `Down`) is **moot**: the roles come
from the routine, not a migration (`R2-TST-12`).

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
