# Slice 04: database security seat (DBS)

**Seat:** Database security. **Date:** 2026-11-06. **Under review:** `specs/001-portfolio-platform/slice-04.md` on `main` at `644efe2` (first draft).

**Read:**
- `specs/001-portfolio-platform/slice-04.md` (all of it); `security.md` banner, "Database", "Audit"; `research.md` banner and D5; `data-model.md` "The `reporting` schema" and `app.saved_report`; `testing.md:110-111`.
- `src/Infrastructure/Migrations/` (`Initial`, `SpineRunsToToday`, `IngestRun`, `AuthAndAudit`); `src/AppHost/AppHost.cs`; `src/Migrator/Program.cs`.
- For the API's grants: `src/Api/AuthEndpoints.cs`, `src/Api/OwnerCommands.cs`, `src/Api/Endpoints.cs`, `src/Infrastructure/BalanceWrites.cs`, `src/Infrastructure/PortfolioDbContext.cs`.

**Ran** (scratch `…\scratchpad\s4-dbs\`, scripts `setup.sql`, `r1.sql`–`r8.sql`, `a1.sql`; nothing in the repo):
- My own container: `postgres:18.3` (`PostgreSQL 18.3 (Debian 18.3-1.pgdg13+1)`), with `--memory=256m` to match the published stack's `MemoryLimit("256M")` (`AppHost.cs:46`). It is removed now.
- `setup.sql`: a database `portfolio_dev`. Tables in `public` shaped like the migrations (`account`, `account_balance` with `deleted_at`/`superseded_at`, `app_setting`, `audit_event` with `append_only` row and `TRUNCATE` triggers, `identity_user`, `identity_user_claim` with an identity column, and `identity_user_passkey` with `ON DELETE CASCADE`). A `public.v_net_worth_daily`. A `reporting` schema of plain views (`security_invoker` off, no `security_barrier`). The slice's grants only:
  - `portfolio_reporting`: `USAGE` on `reporting`, `SELECT` on its views.
  - `portfolio_api`: `SELECT, INSERT` on `audit_event`; `SELECT, INSERT, UPDATE, DELETE` on `identity_user`; `SELECT, INSERT` on `identity_user_claim`; `SELECT` on the balance tables; `UPDATE (superseded_at)` on `account_balance`. It owns nothing.
- As `portfolio_reporting`:
  - PUBLIC privileges, catalog reads and writes (`r1`, `r2`);
  - the same routes inside `BEGIN TRANSACTION READ ONLY` (`r3`, `r4`);
  - password change, checked over TCP with the scram rule;
  - memory exhaustion with a victim `portfolio_api` session open;
  - leaky-function and error-oracle probes, with and without `security_barrier` (`r5`);
  - an advisory lock held against the API's enrolment lock;
  - extended-protocol multi-statement via psql `\bind` (`r6`);
  - the effect of targeted `REVOKE`s and role settings (`r7`, `r8`);
  - `set_config` against an armed role-level timeout.
- As `portfolio_api` (`a1`): trigger, table, ownership and setting attacks; the app's write paths. Then the same role as **database owner**.
- Re-run 2026-11-06 in a second container (`s4-dbs2-pg`, same image and limit, removed after): `setup.sql`, `r1`–`r8`, `a1`, the memory probe with and without role limits, plus `s4-dbs2-leak.sql`–`leak3.sql` (costed and `security_barrier` views) and `s4-dbs2-sess.sql` (state across runs on one session) in the scratchpad root.

---

## Findings

### DBS-1: the reporting login can write, persistently, by routes no `GRANT` or `REVOKE` closes
**BLOCKING (step 2; the slice's purpose, DoD 2).**
**Where:**
- "What this slice is for" (`:20-23`): "enforced by roles and grants, not by reading the query text";
- the login table (`:45`);
- "The boundary, stated" (`:70-72`), "any write, to anything";
- Spike L 3 (`:116-117`).

**The defect:**
- `EXECUTED` (`r2`), as `portfolio_reporting` with only the slice's grants, in autocommit:
  - `ALTER ROLE portfolio_reporting SET statement_timeout = 0` and `SET work_mem = '2000MB'` succeed. `pg_db_role_setting` then holds `{statement_timeout=0,work_mem=2000MB}` for every later session.
  - `ALTER ROLE portfolio_reporting PASSWORD 'changed-by-user-sql'` succeeds. Over TCP the old password then fails (`FATAL: password authentication failed`) and the new one logs in. One report breaks the reporting login until the Migrator resets it.
  - `CONNECTION LIMIT` and `CREATEDB` on itself are refused.
- `EXECUTED` (`r2`): `lo_from_bytea(0, …)` and `lo_create(0)` succeed. The objects persist in `pg_largeobject` owned by the role, outlive the session, and nothing removes them. That is a write and an unbounded disk sink.
- `EXECUTED` (`r2`): `CREATE TEMP TABLE`, `INSERT` into it, `CREATE FUNCTION pg_temp.f()`, `DO $$…$$` and `PREPARE` all succeed.
- **No privilege governs `ALTER ROLE` on oneself.** `INFERRED`; I found no `REVOKE` that stops it. The only refusal I found is a read-only transaction:
  - `EXECUTED` (`r3`): inside `BEGIN TRANSACTION READ ONLY`, `ALTER ROLE … PASSWORD`, `ALTER ROLE … SET`, `lo_from_bytea`, `lo_create` and `CREATE TEMP TABLE` are refused with "cannot execute … in a read-only transaction". So is the same inside `DO … EXECUTE`.
- **A read-only transaction can be undone by the statement before the query** (`EXECUTED`, `r3`, `r8`):
  - `SET TRANSACTION READ WRITE` as the first statement of the app's `READ ONLY` transaction succeeds;
  - `COMMIT; ALTER ROLE … SET work_mem='3GB'` succeeds;
  - with `default_transaction_read_only = on` set on the role, `SET default_transaction_read_only = off` followed by `ALTER ROLE … SET work_mem='1GB'` succeeds.
- **The same undo is refused inside a single statement.** `EXECUTED` (`r3`, `r4`): `set_config('transaction_read_only','off',true)` in a `SELECT`, and `SET TRANSACTION READ WRITE` inside a `DO` block, both fail with "transaction read-write mode must be set before any query". `DO $$ BEGIN COMMIT; END $$` inside the transaction fails with "invalid transaction termination".
- So "roles and grants" alone cannot deliver "any write, to anything". The boundary takes three things grants do not provide:
  1. a read-only transaction issued by the app;
  2. exactly one user statement per transaction;
  3. no session state carried between runs (DBS-3).

**What to do:**
- Restate the technique at `:20-23` and in the boundary: **grants, plus a read-only transaction the app opens, plus one statement, plus a fresh session.** State that no grant refuses `ALTER ROLE` on oneself.
- **Enforce one statement without parsing.** `EXECUTED` (`r6`): the server's extended protocol refuses a multi-command text ("cannot insert multiple commands into a prepared statement"), even with no parameters. Spike N 3 should establish how Npgsql sends one unsplit `Parse` (positional `$1` parameters or `EnableSqlRewriting = false`; `INFERRED`, SRC's ground). Then the server refuses, which `D5` allows. This makes Q5 part of the boundary (see the answers).
- In the step 2 script, also `REVOKE EXECUTE ON FUNCTION lo_create(oid), lo_creat(integer), lo_from_bytea(oid, bytea) FROM PUBLIC`, `REVOKE TEMPORARY ON DATABASE … FROM PUBLIC` and `REVOKE USAGE ON LANGUAGE plpgsql FROM PUBLIC`. `EXECUTED` (`r7`, `r8`): after these, each is refused with "permission denied". That covers the case where the transaction guard fails.
- DoD 2 tests, each seen failing first:
  - `ALTER ROLE <self> PASSWORD` and `ALTER ROLE <self> SET`, through the endpoint;
  - `lo_from_bytea`;
  - `CREATE TEMP TABLE`;
  - `DO`;
  - `SET TRANSACTION READ WRITE; ALTER ROLE …` as one request text, refused as multi-statement;
  - `COMMIT; …`, refused the same way.

### DBS-2: one reporting `SELECT` crash-restarts PostgreSQL, the time bound fires late, and the row cap does not bound bytes
**MUST FIX (boundary `:73-74`, step 3, DoD 3; Q2).**
**Where:** "cannot run without bound" (`:21-22`); "running past its time bound, and returning past its row cap" (`:74`); Spike N 2 (`:146-147`).

**The defect:**
- `EXECUTED`, in the 256 MB container, as `portfolio_reporting` with default settings and no `SET`:
  - `SELECT length(string_agg(repeat('x',1000),'')) FROM generate_series(1,200000)` was killed. The log reads "client backend … was terminated by signal 9: Killed", then "terminating any other active server processes", then crash recovery.
  - A `portfolio_api` session with an open transaction was cut off with it: "terminating connection because of crash of another server process … roll back the current transaction and exit". One report takes the API's connections down, not only its own (re-run 2026-11-06, same result).
- **Nothing in the database bounds that memory.** `EXECUTED` (role settings by `postgres`):
  - with `ALTER ROLE portfolio_reporting SET work_mem='1MB'` and `SET statement_timeout='2s'`, the same query at 230 000 and 260 000 rows was still killed by signal 9, after 4.9 s and 4.2 s. The 2 s timeout never fired.
  - at 200 000 rows it was cancelled ("canceling statement due to statement timeout"), but at 6.0 s, not 2 s. A plain `count(*)` over `generate_series(1,1e8)` was cancelled at 2.4 s. **The time bound fires late exactly when memory is growing** (`INFERRED`: the container under memory pressure).
  - `work_mem` governs sort and hash spill, not an aggregate's state; `string_agg` ignores it. `INFERRED`; I know of no per-backend memory limit in PostgreSQL 18.
  - A single value is capped at 1 GB ("requested length too large"), which is above the 256 MB container.
- **`temp_file_limit` does hold**, and is the one bound the role cannot raise. `EXECUTED` (`r7`, `r8`): set on the role to 64 MB, `SET temp_file_limit` and `ALTER ROLE <self> SET temp_file_limit` are both refused ("permission denied to set parameter"), and a 2e7-row sort fails with "temporary file size exceeds temp_file_limit (65536kB)". `statement_timeout`, `transaction_timeout`, `work_mem` and `idle_in_transaction_session_timeout` are user-settable (`EXECUTED`, `r2`: `SET` succeeds), so no role default of theirs binds a query that changes them.
- **A row cap does not bound bytes.** `EXECUTED`: `SELECT repeat('x',50000000) FROM generate_series(1,2)` delivered 100 000 002 bytes to the client in 3.7 s, under the 2 s server timeout. Two rows, 100 MB, into the API's memory.
- **A timeout set inside the statement does not disarm the current one.** `EXECUTED`: with the role at 2 s, `SELECT set_config('statement_timeout','0',true), pg_sleep(4)` was cancelled at 2.001 s. But see DBS-3 for the next run.

**What to do:**
- Say in the boundary (`:74`) that **memory is not bounded by the database.** A report can crash-restart PostgreSQL; crash recovery keeps committed data, but cuts the API's open transactions. For a local single-user app that is availability, not integrity. Kyle should accept that in writing or pay for one of:
  - a lower bound on what a report may hold: a short time bound (seconds, not minutes) to narrow the window, accepting that it fires late;
  - a larger container memory limit than the published 256 M (`AppHost.cs:46`), which only moves the threshold;
  - a separate PostgreSQL instance for reporting, which is out of proportion here.
- Set on the role, by the Migrator: `temp_file_limit` (it holds); `statement_timeout`, `transaction_timeout`, `idle_in_transaction_session_timeout` as defaults only. The bound that must hold is the app's: `SET LOCAL statement_timeout` sent by the app inside its read-only transaction before the user statement, plus Spike N 1's client-side cancel that reaches the server.
- **Read the result with a byte cap as well as a row cap**, counted on the client while reading (`CommandBehavior.SequentialAccess` and chunked reads, then cancel; `INFERRED`, SRC's ground). Report "cut off" for either.
- DoD 3 tests, each seen failing first: a wide-row query (`repeat('x', 5e7)` × 2) cut at the byte cap; `SET statement_timeout = 0` refused or ineffective; the server-side query gone from `pg_stat_activity` after the cap or cancel; and, if Kyle accepts the crash, a test that the API serves the next request after one (TST's ground).

### DBS-3: a run leaves session state on a pooled connection, and the next run inherits it, including a disarmed time bound
**MUST FIX (step 3, DoD 3; DBS-1's third condition).**
**Where:** "Running a query" (`:60-62`); Spike N (`:141-153`). The draft does not say which connection a run uses. Npgsql pools by default (`INFERRED`, SRC's ground).

**The defect:**
- `EXECUTED` (one session, as the pool would hold it): run A is a single `SELECT` in `BEGIN TRANSACTION READ ONLY … COMMIT`, calling `set_config('statement_timeout','0',false)`, `set_config('work_mem','2000MB',false)`, `set_config('default_transaction_read_only','off',false)`, `set_config('search_path','pg_temp,public,reporting',false)` and `pg_advisory_lock(…)`. All succeed in a read-only transaction.
- Run B on the same session then shows `statement_timeout` 0, `work_mem` 2000MB, `default_transaction_read_only` off and the changed `search_path`; holds the advisory lock; and `pg_sleep(3)` runs to the end past the role's 2 s. **The role default is gone for every later run on that connection.**
- `EXECUTED`: also surviving a committed read-only transaction: `PREPARE leftover …`, `LISTEN ch`, and a `DECLARE … CURSOR WITH HOLD` (materialised, held in memory).
- `EXECUTED`: if the app ends the run with `ROLLBACK` instead, the `set_config` values, the cursor and the `LISTEN` are undone; **`PREPARE` and session advisory locks survive.** A leftover prepared statement could collide with a name the driver uses later (`INFERRED`).
- `EXECUTED`: `DISCARD ALL` clears all of it (settings back to the role's, no prepared statements, cursors, advisory locks or channels). It is refused inside a transaction ("DISCARD ALL cannot run inside a transaction block").
- Not reachable by any reset: `ALTER ROLE <self> SET` (DBS-1). It persists in `pg_db_role_setting`.

**What to do:**
- Run every report on **a fresh, non-pooled connection** (its own data source with `Pooling=false`), closed after the run. Then nothing a run leaves can reach the next. Alternatively, a dedicated pool whose reset is `DISCARD ALL` is shown to run before reuse (`INFERRED`: Npgsql's reset-on-close; SRC's ground). The fresh connection is simpler to show.
- Inside it, the sequence is fixed and the app's: `BEGIN TRANSACTION READ ONLY`, `SET LOCAL statement_timeout`, the user's one statement (one `Parse`, DBS-1), then always `ROLLBACK`, never `COMMIT`.
- Tests, each seen failing first: run A calls `set_config('statement_timeout','0',false)`; run B is `pg_sleep` past the bound and must be cancelled. Run A takes `pg_try_advisory_lock(1)`; run B (or a superuser check of `pg_locks`) finds no lock held by a reporting backend.

### DBS-4: a plain view's filter can be overtaken by the user's function, so a report sees soft-deleted and superseded rows
**SHOULD FIX (step 2, the view rules).** Today it needs a function the user creates, which DBS-1's fixes refuse; `security_barrier` makes the view rule hold regardless.
**Where:** `v_balance_daily`, "superseded and soft-deleted rows excluded" (`:51-52`); step 2's view tests (`:164-166`). The draft does not say whether the views are `security_barrier`.

**The defect:**
- `EXECUTED` (`r5`; re-run with `s4-dbs2-leak2.sql`): a temp `plpgsql` function with `COST 0.0000001` that `RAISE NOTICE`s its argument, used in `WHERE` over a view:
  - over `v_balance_daily` as `setup.sql` builds it (`deleted_at IS NULL AND superseded_at IS NULL`): only the live row is seen. `IS NULL` costs nothing, so it stays first;
  - over the same view with the filter written as `coalesce(deleted_at,'infinity') = 'infinity'` (a costed operator): the function saw **`424242` (soft-deleted) and `777777` (superseded)**. The result set still showed one row; the values left through the notice;
  - the same costed view `WITH (security_barrier)`: only the live row seen. `EXPLAIN` shows the user qual in a `Subquery Scan` above the view's filter.
- `EXECUTED` (`r5`, `s4-dbs2-leak3.sql`): error oracles with built-ins only (`1/(balance - 424242)`, a `CASE … 1/(balance-balance)`) did **not** leak on any of these views. The planner kept the view's quals first. `INFERRED`: a costlier view filter (a join, a function call) could change that.
- So whether a view leaks depends on how its `WHERE` is written, which is not a property anyone will test for.
- `EXECUTED`: creating the function needs `TEMPORARY` and `plpgsql`. Inside a read-only transaction, `CREATE FUNCTION` is refused ("cannot execute CREATE FUNCTION in a read-only transaction"), and DBS-1's `REVOKE`s refuse it too.

**What to do:**
- Create every `reporting` view `WITH (security_barrier)`. The cost is lost pushdown of non-leakproof quals, which this data size does not feel.
- Step 2 test, seen failing first against a view without the barrier: the test fixture (superuser) creates a cheap `NOTICE`-raising function, grants `EXECUTE` to the reporting role, runs it as a filter over each view, and asserts no notice carries an excluded row (seed a soft-deleted and a superseded balance with distinctive values).
- A catalog test: every view in `reporting` has `security_barrier` in `pg_class.reloptions`, so a view added later cannot skip it.

### DBS-5: the reporting login can hold the API's enrolment lock, and a read-only transaction does not stop it
**SHOULD FIX (step 2, Spike L 1).**
**Where:** Spike L 1 (`:110-113`) lists `pg_sleep`, `set_config`, `pg_read_file`, `lo_*`; not the advisory-lock family. The API takes `pg_advisory_xact_lock(hashtext('portfolio-enrolment'))` (`SOURCE@644efe2 src/Api/AuthEndpoints.cs:97`).

**The defect:**
- Advisory locks are one key space per database, shared by every role (`INFERRED`, consistent with the next line).
- `EXECUTED`: the reporting login, inside `BEGIN TRANSACTION READ ONLY`, takes `pg_advisory_lock(hashtext('portfolio-enrolment'))` and commits. A `portfolio_api` session's `pg_advisory_xact_lock` on the same key then waits, and fails at its `lock_timeout` ("canceling statement due to lock timeout"). Without a timeout it waits for as long as the reporting session lives.
- `EXECUTED` (`r3`): the session lock survives `ROLLBACK` and is still in `pg_locks` afterwards.
- `EXECUTED` (`r7`, then a re-check): revoking `pg_advisory_lock(bigint)` from `PUBLIC` refuses that overload only. `pg_advisory_lock(integer,integer)` and `pg_try_advisory_lock(bigint)` still succeed. There are 21 `pg_*advisory*` functions.
- Impact is low here: enrolment runs once, before any report can run. The same holds for any advisory lock the API adds later.

**What to do:**
- In the step 2 script, revoke `EXECUTE` from `PUBLIC` on every `pg_*advisory*` function (a `DO` block over `pg_proc` in the Migrator, run as `postgres`), and grant it back to `portfolio_api` for the one it uses.
- A fresh connection per run (DBS-3) also releases any lock when the run ends.
- Test, seen failing first: as the reporting login, `pg_try_advisory_lock(1)` and `pg_advisory_lock(1,1)` are refused with "permission denied".

### DBS-6: if `portfolio_api` owns the database, it can drop the audit table and the append-only function
**BLOCKING (step 1, DoD 1, `G-7`).**
**Where:** the login table, "owns nothing" (`:44`); DoD 1 (`:183-186`), which tests only `DISABLE TRIGGER` and `DROP TRIGGER`.

**The defect:**
- `EXECUTED` (`a1`, the API owning nothing): `DISABLE TRIGGER`, `DROP TRIGGER`, `DROP FUNCTION append_only()`, `ALTER TABLE … ADD COLUMN`, `OWNER TO`, `TRUNCATE`, `UPDATE`/`DELETE` on `audit_event`, `SET session_replication_role`, `CREATE TRIGGER` and `CREATE TABLE public.…` are all refused ("must be owner", "permission denied"). `VACUUM FULL` is skipped with a warning.
- `EXECUTED` (`a1`, after `ALTER DATABASE portfolio_dev OWNER TO portfolio_api`): on PostgreSQL 18 the `public` schema is owned by `pg_database_owner`, so the database owner owns `public`. Then:
  - `DROP TABLE audit_event` **succeeds**; the audit trail is gone;
  - `DROP FUNCTION public.ao2() CASCADE` (a function like `append_only()`, owned by `postgres`) **succeeds** and drops the trigger with it ("drop cascades to trigger a2"). Without `CASCADE`, `DROP FUNCTION append_only()` passes the privilege check and fails only on the dependency;
  - `CREATE TABLE public.evil` and `VACUUM FULL audit_event` succeed;
  - `ALTER DATABASE … SET default_transaction_read_only = off`, `GRANT TEMPORARY ON DATABASE … TO portfolio_reporting` and `GRANT CREATE ON SCHEMA public TO portfolio_reporting` succeed, undoing DBS-1's step 2 script for the reporting login.
  - `ALTER TABLE … DISABLE TRIGGER` and `DROP TRIGGER` are still refused. **DoD 1's two tests would pass while the trail can be dropped.**
  - `DROP TABLE reporting.r` is refused ("permission denied for schema reporting"): `reporting` is owned by `postgres`.
- How the database comes to be owned is Spike M's ground (Aspire's `AddDatabase` and the Migrator; SRC's).

**What to do:**
- State "owns nothing" as: not superuser, not `CREATEROLE`, not the owner of the database, of any schema, or of any object; not a member of `pg_database_owner` or any role that is.
- DoD 1 tests, as `portfolio_api`, each seen failing first by running them as the database owner: `DROP TABLE audit_event`; `DROP FUNCTION append_only() CASCADE`; `TRUNCATE audit_event`; `ALTER DATABASE … SET`; and a catalog check that `pg_database.datdba` for the app's database, and the owner of `public`'s schema, resolve to `postgres`.

### DBS-7: the API login's grant list in the draft is not the one the code needs, and later migrations will not be granted
**MUST FIX (step 1; Q1).**
**Where:** the login table (`:44`), "`INSERT` and `SELECT` only on `audit_event` and `used_setup_code`", which reads as the whole list; Q1 (`:205-208`).

**The defect:**
- The code updates in place, so Q1's "no `UPDATE` at all" would break it:
  - `SOURCE@644efe2 src/Infrastructure/BalanceWrites.cs:59`: `ExecuteUpdateAsync(… SupersededAt …)` on `account_balance`.
  - `SOURCE@644efe2 src/Api/AuthEndpoints.cs:156` (`AddOrUpdatePasskeyAsync`, the sign count on every sign-in) and `:170` (`UpdateSecurityStampAsync` on sign-out): Identity's store updates `identity_user` and `identity_user_passkey` (`INFERRED` which columns; SRC's ground).
  - `SOURCE@644efe2 src/Api/Endpoints.cs:78` and the ingest path insert `institution`, `account`, `account_source`, `ingest_run`, `account_balance`.
- `EXECUTED` (`a1`): a **column grant** does what `BalanceWrites` needs and no more: `UPDATE account_balance SET superseded_at = now() …` succeeds; `UPDATE account_balance SET balance = 1` is refused ("permission denied for table account_balance").
- `EXECUTED` (`a1`): `INSERT … RETURNING id` into `identity_user_claim`'s identity column works with `INSERT` alone, no sequence grant.
- `EXECUTED` (`a1`): `DELETE FROM identity_user` with no grant on `identity_user_passkey` still deleted the passkey row through `ON DELETE CASCADE`. A parent's `DELETE` grant reaches its children.
- `EXECUTED` (`a1`): the API login can `ALTER ROLE portfolio_api SET statement_timeout = 0` and change its own password, as DBS-1 found for reporting. Only API code can issue that, so it is a NOTE, but a password change would lock the API out at its next start.
- `INFERRED`: `GRANT … ON ALL TABLES IN SCHEMA` is a one-time grant. A table or view a later migration adds gets nothing, for either login.

**What to do:**
- Answer Q1: `UPDATE` is needed. Grant `UPDATE (superseded_at)` on `account_balance` as a column grant, and only the Identity columns the store writes. No `DELETE` unless a path deletes (none found; `DELETE` on `identity_user` cascades into passkeys).
- The Migrator re-applies the whole grant script, idempotently, after every migration run, so a new table or view is granted in the same run.
- Test, seen failing first: a committed expected list, compared to `information_schema.role_table_grants` and `role_column_grants` for both logins. A missing grant fails the existing suites (step 1's green run); an extra grant fails this one.

### DBS-8: what the catalog shows the reporting login, and the other `PUBLIC` rights left open
**NOTE (Spike L 2; Q4).** Mostly answers Spike L; two small leaks to accept or close.
**Where:** "It also does not hide the catalog" (`:76-78`); Spike L 2 (`:114-115`).

**The defect (what it reads), `EXECUTED` (`r1`):**
- `pg_stat_activity`: other sessions' `query` is `<insufficient privilege>`, including `postgres`'s. Only its own query is shown. The API's SQL text does not leak this way.
- `pg_settings` / `current_setting`: `statement_timeout`, `work_mem`, `temp_file_limit` are readable; `data_directory`, `config_file`, `hba_file` are refused ("Only roles with privileges of the pg_read_all_settings role"). `pg_file_settings`, `pg_hba_file_rules` refused.
- `pg_roles`: every role's name, password masked `********`. `pg_authid`, `pg_shadow` refused.
- `pg_db_role_setting` is readable: a report can see the roles' `ALTER ROLE … SET` values.
- **Row counts of unreadable tables:** `pg_stat_all_tables.n_live_tup` gave `identity_user` 1, `identity_user_passkey` 1, `audit_event` 0, `account_balance` 3 (including the soft-deleted and superseded rows the view hides). That is a count, not content.
- `pg_get_viewdef('reporting.v_balance_daily')` returns the view's SQL, naming the tables and filter beneath.
- Refused: direct reads of `identity_user`, `account_balance`, `public.v_net_worth_daily` ("permission denied"); `pg_statistic`; `pg_read_file`, `lo_import`, `pg_ls_waldir`. `pg_stats` returns rows only for readable columns (`INFERRED` from its definition).
- `EXECUTED` (`r1`, `r2`): `CONNECT` on `postgres` and `portfolio_dev`, `TEMPORARY` on both; no `CREATE` on `public` (PostgreSQL 15's change holds); `COPY … TO PROGRAM`/`TO` file, `SET ROLE`, `SET SESSION AUTHORIZATION`, `CREATE EXTENSION`, `COMMENT ON` a view, all refused.

**Other rights, `EXECUTED`:**
- `pg_terminate_backend` is executable (`r1`). `INFERRED` (docs): only backends of the same role, so a report can end another report run, not the API's.
- `pg_notify` succeeds even in a read-only transaction (`r3`). No `LISTEN` in the API today, so nothing receives it.
- `LOCK TABLE reporting.v_account IN ACCESS SHARE MODE` succeeds and locks `public.account` beneath it (`r3`); `ACCESS EXCLUSIVE` and `FOR SHARE` are refused. Any `SELECT` takes that lock; held for the run, it blocks a Migrator `ALTER TABLE`. The time bound limits it.
- `txid_current()` succeeds in a read-only transaction (`r3`).

**What to do:**
- Accept the catalog as the draft does (Q4), and say what it includes: names, view definitions, role names, role settings, and **row counts** of Identity and audit tables. With invented data that is fine; record it as a trigger to revisit before the first real balance.
- `REVOKE CONNECT ON DATABASE postgres FROM PUBLIC` (or grant the reporting login `CONNECT` only on the app's database), so a reporting login cannot open the maintenance database. `INFERRED`: nothing in the app needs `PUBLIC` there.
- No `REVOKE` on catalog views (brittle, as Q4 says). If the counts matter later, `REVOKE SELECT ON pg_stat_all_tables, pg_stat_user_tables, pg_class …` is the place, at the cost of tools.

---

## Confirmed (do not re-check)

All `EXECUTED` on `postgres:18.3` in a 256 MB container, as a role holding only the slice's grants:
- Views read with the owner's rights: the reporting login reads `reporting.v_balance_daily` with no grant on `account_balance`, and is refused the table itself (Spike L 4).
- No `CREATE` on `public` for a non-owner; `COPY TO PROGRAM`/file, `SET ROLE`, `SET SESSION AUTHORIZATION`, `CREATE EXTENSION`, `pg_read_file`, `lo_import`, `lo_export` are refused.
- A read-only transaction refuses `ALTER ROLE`, `lo_*` writes, `CREATE TABLE`/`FUNCTION`, also from inside `DO … EXECUTE`. It can be undone only by a statement **before** the first query (`SET TRANSACTION READ WRITE`, `SET transaction_read_only = off`, `COMMIT`), never from within one.
- The extended protocol refuses a multi-command text in one `Parse`, with or without parameters.
- `temp_file_limit` set on a role cannot be raised by the session or by `ALTER ROLE <self> SET`; it fails the query at the limit.
- A `statement_timeout` armed for a statement is not disarmed by `set_config` inside that statement.
- `REVOKE … FROM PUBLIC` on `TEMPORARY`, `lo_create`/`lo_creat`/`lo_from_bytea`, `plpgsql` and an advisory overload each turn their route into "permission denied".
- `portfolio_api` owning nothing (not the database): refused `DISABLE`/`DROP TRIGGER`, `DROP FUNCTION`, `ALTER TABLE`, `OWNER TO`, `TRUNCATE`, `UPDATE`/`DELETE` on `audit_event`, `session_replication_role`. Column-level `UPDATE` grants hold.
- `pg_stat_activity` hides other sessions' query text from the reporting login.
- `DISCARD ALL` resets settings, prepared statements, cursors, advisory locks and `LISTEN`; it cannot run inside a transaction.

## Answers to Q2, Q4, Q5

- **Q2 (where the time bound and row cap live).** Both, with different jobs:
  - **Role defaults are a backstop, not the bound.** `statement_timeout`, `transaction_timeout` and `work_mem` are user-settable, and a run can clear them for the next run on a pooled connection (DBS-3). Only `temp_file_limit` binds (DBS-2). Set all of them on the role anyway, by the Migrator.
  - **The bound is the app's:** a fresh connection; `BEGIN TRANSACTION READ ONLY`; `SET LOCAL statement_timeout`; one statement; `ROLLBACK`; plus Spike N 1's client cancel reaching the server. The row cap and a **byte cap** are counted on the client, then cancel.
  - Neither bounds memory (DBS-2): that needs Kyle's acceptance or a container change.
  - Values are Kyle's call; a proposal: 10 s, 10 000 rows, 8 MB of result, `temp_file_limit` 256 MB. `testing.md` should name whichever are chosen.
- **Q4 (`pg_catalog` visibility).** Accept, as drafted, with the list in DBS-8 stated: table and view names, view definitions, role names and their `ALTER ROLE` settings, and row counts of tables it cannot read. Close `CONNECT` on the `postgres` database. Revisit before the first real balance.
- **Q5 (one statement only?).** **Yes, and it is part of the boundary, not usability.** A read-only transaction is undone by a statement placed before the user's query (DBS-1, `r3`, `r8`). The server enforces one statement itself when the text goes in one extended-protocol `Parse` (`r6`). That is the server refusing, not a parser, so it is within `D5`. How Npgsql is made to send it unsplit is Spike N 3 (SRC's ground).

**Overlaps:** SRC (Npgsql pooling reset, statement splitting, byte-capped reads, cancel; Aspire's database ownership in Spike M); TST (the tests named under each finding, and the crash-recovery test in DBS-2).
