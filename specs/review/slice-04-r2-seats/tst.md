# Slice 04 review, round 2: test design seat (R2-TST)

**Seat:** test design. **Date:** 2026-11-06. **Under review:** the rewritten `specs/001-portfolio-platform/slice-04.md`
on `specs/slice-04-review` at `213f49f` (build steps 1a, 1b, 2–6; "Definition of done" 1–10; Gates G4-1..G4-5), against
round 1's `slice-04-seats/tst.md` (TST-1..TST-17, its proposed DoD, GT-A..GT-D) and `slice-04-committee.md`
(C4-1..C4-18, S4-1..S4-8).

**Read:** `slice-04.md` (whole); `slice-04-committee.md` (whole); `slice-04-seats/tst.md` (whole);
`tests/Integration/{PostgresFixture,AuthFixture,TestAuth,ImportSupport,SecretsStayOutTests,OwnerCommandsTests}.cs`
and every `ConnectionStrings:portfolio` site under `tests/Integration`; `tests/E2E/AppFixture.cs`;
`tests/Architecture/NoCredentialInResponsesTests.cs`; `src/Migrator/Program.cs`; `src/Api/Program.cs` (the context
wiring); `src/AppHost/AppHost.cs` (database name, memory limits); the trigger migrations `…_IngestRun.cs`
and `…_AuthAndAudit.cs`; `.github/workflows/ci.yml`.

**Ran** (all `EXECUTED 2026-11-06`, in my own container `s4r2-tst-pg`, `postgres:18.3`, removed afterwards; scripts and
output in the seat's scratch directory `s4r2-tst/`):
- `psql`: a model of `audit_event` with the repo's `append_only()` row and `TRUNCATE` triggers; the owner's
  `UPDATE`, `DELETE`, `TRUNCATE`, `DISABLE TRIGGER`, `DROP TABLE`, `SET LOCAL session_replication_role = replica`
  and two `ALTER DATABASE` forms, each in a rolled-back transaction (`model.sql`).
- `psql` as a non-owner login: `SET session_replication_role`; what survives `ROLLBACK` of a `READ ONLY`
  transaction: `set_config(…, false)`, a session advisory lock, `PREPARE` (`session.sql`).
- A crash-restart by `docker exec … kill -9 <backend pid>` of a `pg_sleep(600)` backend: the server log, the recovery
  time, the container's state, `pg_stat_database` before and after.
- `dotnet run crash.cs` (Npgsql 10.0.3): a pooled data source's idle connection, used after that crash.
- `dotnet run wide.cs` (Npgsql 10.0.3): `SELECT repeat('x', 1e8)` read in `CommandBehavior.Default` and in
  `SequentialAccess`, with `GC.GetTotalAllocatedBytes(true)` read around `ReadAsync` and around the cell read.
- `dotnet run tcdefault.cs`: `Testcontainers.PostgreSql` 4.16.0's `PostgreSqlBuilder` default constants, by
  reflection.
- No repository test was run: nothing of slice 04 is built yet, and the existing suites are unchanged.

---

## Findings

**Count:** 0 BLOCKING, 9 MUST FIX, 2 SHOULD FIX, 1 NOTE.

### R2-TST-1: DoD 1's "seen failing" cannot be met as written, and it drops `SET session_replication_role`

**Severity:** MUST FIX (DoD 1; step 1b).

**Where:** DoD 1: "`TRUNCATE`, `UPDATE` and `DELETE` on `audit_event`, `used_setup_code` and `ingest_run` … each fail
with `42501`. **Seen failing:** each succeeds as the owner in a rolled-back transaction." Step 1b: "each refusal of
`C4-4` seen failing as the owner".

**The defect.**
- `EXECUTED 2026-11-06`: as the owner, on the repo's trigger shape, `UPDATE`, `DELETE` and `TRUNCATE` on
  `audit_event` do **not** succeed. Each gives `P0001 audit_event is append-only: UPDATE refused` (and `DELETE`,
  `TRUNCATE`) from `append_only()`. `SOURCE@213f49f`: `used_setup_code` carries the same two triggers
  (`AuthAndAudit.cs:212-217`) and `ingest_run` its own (`IngestRun.cs:96-102`, `ingest_run_immutable()`). So 9 of the
  DoD's statements have no "succeeds as the owner" control. An implementer must either drop the control or invent one.
- That is the right shape, stated wrongly. The two layers differ by SQLSTATE: the owner meets the trigger (`P0001`),
  the API login meets the grant first (`42501`, round 1 `EXECUTED`). That difference is the evidence that the grant,
  not the trigger, is refusing.
- `EXECUTED`: as the owner, `SET LOCAL session_replication_role = replica` then `UPDATE audit_event` **succeeds**:
  the second known way past an append-only trigger without `DISABLE`. As a non-owner, non-superuser login it gives
  `42501 permission denied to set parameter "session_replication_role"`. Round 1's proposed DoD 1 listed it; the
  rewrite's list does not, and step 1b's `C4-4` list (from the committee) never had it. `ALTER TABLE … OWNER TO`
  (also in TST's list) is lower value: a non-owner is refused it anyway.
- `ALTER DATABASE` names no form. `EXECUTED`: `ALTER DATABASE … SET work_mem` runs in a transaction; `SET TABLESPACE`
  gives `25001` in one, so "in a rolled-back transaction" fails for that form.

**What to do.** Rewrite DoD 1's control as a table, one row per statement, with two expected outcomes:

| Statement | As the owner, rolled back | On the app's connection |
|---|---|---|
| `ALTER TABLE t DISABLE TRIGGER …`, `DROP TRIGGER …`, `DROP TABLE audit_event`, `DROP FUNCTION append_only() CASCADE`, `ALTER DATABASE <db> SET work_mem = '8MB'` | succeeds | `42501` |
| `SET session_replication_role = replica` then `UPDATE audit_event …` | `UPDATE 1` | `42501` on the `SET` |
| `UPDATE`, `DELETE`, `TRUNCATE` on each of the three tables | `P0001` (the trigger) | `42501` (the grant) |

Run the right column on `db.Database.GetDbConnection()` from `factory.Services` (TST-3 b), and add `ingest_run`'s own
function to the `DROP FUNCTION` row.

### R2-TST-2: In Integration the app's database is named `postgres`, the one whose `CONNECT` the slice revokes

**Severity:** MUST FIX (step 1a; DoD 1, 2, 8). Overlaps DBS.

**Where:** "Revoked from `PUBLIC`, by name … and `CONNECT` on the `postgres` database"; step 1a "the fixture calls it";
step 6 "the catalog snapshot equal to `ExpectedPrivileges`".

**The defect.**
- `EXECUTED 2026-11-06` (reflection on `Testcontainers.PostgreSql` 4.16.0): `PostgreSqlBuilder.DefaultDatabase =
  postgres`. `SOURCE@213f49f`: `PostgresFixture.cs:18` builds with no `WithDatabase`, so every Integration class, and
  the Migrator process test if it reuses the builder, migrates into `postgres`. Production migrates into
  `portfolio_dev` (`AppHost.cs:47`).
- So in Integration the revoke removes `PUBLIC`'s `CONNECT` from the **app's own database**. A routine written for
  production, where both logins connect through `PUBLIC`'s default `CONNECT` on `portfolio_dev`, then locks both
  logins out of every Integration test. The quick fix, granting `CONNECT` on `postgres` to the logins, makes the
  Integration snapshot differ from the published one on exactly the revoked row. Either way the "cannot connect to
  `postgres`" refusal can never be shown in Integration, and step 6's by-hand equality with `ExpectedPrivileges` fails
  or is edited to pass.

**What to do.**
- `PostgresFixture` builds with `.WithDatabase("portfolio_dev")` (or another name that is not `postgres`), so the app's
  database and the revoked one are different objects in tests as in production.
- The routine grants `CONNECT` on `current_database()` to both logins **explicitly** and never relies on `PUBLIC`'s.
  `ExpectedPrivileges` names databases by role (`app`, `postgres`, `template1`), not by literal name, so one committed
  set serves Integration and step 6.
- A refusal test: the reporting login connecting to `postgres` gives `42501 permission denied for database`.

### R2-TST-3: Step 1a's Migrator test can pass with a Migrator that never provisions, and the idempotency tests are gone

**Severity:** MUST FIX (step 1a; DoD 1, 9). Weakens TST-1.

**Where:** step 1a: "a process test of the **built Migrator** producing the same catalog snapshot (`TST-1`)";
"Provisioning": "**The Migrator runs it after every migration run.** … a volume reused with a new password still
works"; Spike M 4 "Runs in: Step 6".

**The defect.** The question put to this seat: if the Migrator never calls the routine, which test goes red?
- **Only the process test, and only if its database is fresh.** Every other test gets its roles from the fixture's
  call. The rewrite drops round 1's "a fresh, un-migrated container". `SOURCE@213f49f`: the existing process-test
  pattern (`OwnerCommandsTests.cs:116`) points the child process at the class fixture's database. Copied, the Migrator
  runs against a database the fixture has already provisioned, and "the same catalog snapshot" holds whether the
  Migrator calls the routine or not. Roles are cluster-wide, so even a fresh *database* in the fixture's server is not
  enough: it must be a fresh *container*.
- **"Same" as what is unsaid.** Comparing with the fixture's live snapshot is equal by construction when both come from
  the routine; comparing with the committed `ExpectedPrivileges` is the oracle.
- **The snapshot does not cover the passwords.** A Migrator that creates the roles and sets no password, or the wrong
  one, matches the catalog. Nothing logs in with what the Migrator was given.
- **"After every migration run" has no test for the run with nothing pending.** That is the slice-03-volume case: the
  schema is current, the role exists, the password is new. `SOURCE@213f49f`: `Migrator/Program.cs:37-39` computes
  `pending`; a routine placed inside an `if (pending.Count > 0)` passes every test the rewrite names. TST-1's (a) run
  twice, (b) role already exists before migrating, and (d) a different password the second time are all missing.
  Spike M 4 moved to step 6, by hand.
- The log test ("contains neither password") has no seen-failing. `SOURCE`: the Migrator has no `appsettings.json`, so
  the host default (`Information`) applies and EF's `Executed DbCommand` text would appear. The test can fail. Say how.

**What to do.** Step 1a's process test, written out:
1. A fresh container of its own (`new PostgreSqlBuilder(Image).WithDatabase("portfolio_dev").Build()`), nothing run
   against it.
2. Run `Migrator.dll` with `ConnectionStrings__portfolio` and both password variables (P1, R1). Exit 0. Snapshot equals
   the committed `ExpectedPrivileges` for both logins. `portfolio_api`/P1 and `portfolio_reporting`/R1 each open a
   connection.
3. Run it again with P2, R2: zero migrations pending. Exit 0, snapshot unchanged, P2 and R2 connect, P1 and R1 give
   `28P01`.
4. Before step 2 in a second container: `CREATE ROLE portfolio_reporting` as the superuser, then run the Migrator. Exit
   0, snapshot equal.
5. Capture stdout and stderr of every run: none contains P1, P2, R1 or R2. **Seen failing:** the password statement
   sent through `ExecuteSqlRawAsync`.
6. **Seen failing for the whole test:** comment out the call in `Program.cs`; step 2 goes red. Guard it inside
   `pending.Count > 0`; step 3 goes red.

This is the CI form of Spike M 4. Step 6 by hand then only confirms it on the real volume.

### R2-TST-4: The enumeration test's scope, as written, misses three of the named revokes and every role attribute

**Severity:** MUST FIX (step 1a; DoD 1, 2). Weakens TST-4.

**Where:** step 1a: "every table, view, schema, sequence and function, for both logins, against a committed
`ExpectedPrivileges`, with a planted extra grant"; C4-8 the same.

**The defect.** `SOURCE@213f49f`, the rewrite's own text against its own list:
- `TEMPORARY` **on the database** and `CONNECT` **on the `postgres` database** are database privileges. `USAGE` **on
  `plpgsql`** is a language privilege. None is a table, view, schema, sequence or function. Three of the five named
  revokes are outside the test that is meant to pin them.
- "Owns nothing" (`pg_shdepend 'o'`), "not superuser or `CREATEROLE`", "not a member of `pg_database_owner`"
  (`pg_auth_members`), the role defaults the Migrator sets (`pg_db_role_setting`: `statement_timeout`,
  `transaction_timeout`, `idle_in_transaction_session_timeout`, `temp_file_limit`), default ACLs and large objects are
  all outside it too. DoD 1 names `pg_shdepend` once, without a test.
- "A planted extra grant" does not say which. A table-level `GRANT SELECT` is caught by any sweep. Round 1 `EXECUTED`:
  a **column** grant leaves `has_table_privilege` false; only `has_any_column_privilege` sees it. The plant that shows
  the sweep's coverage is the column grant, and the plant that shows the "no `ALTER DEFAULT PRIVILEGES`" rule is a
  default ACL.

**What to do.** Replace step 1a's list with TST-4's `CatalogSnapshot(role)` scope, by name: `pg_roles` attributes
(including `rolcanlogin`), `pg_auth_members` both ways, `pg_db_role_setting`, `pg_default_acl`, `pg_shdepend` (`a`
and `o`), every database (`CONNECT`, `CREATE`, `TEMP`), every language (`USAGE`), every non-system schema, every
relation (table-level **and** `has_any_column_privilege`), every sequence, every non-system function plus the named
built-ins, the `PGC_SUSET` parameters, large objects. Plants, each a committed test: a column grant, a default ACL
followed by a new table, and `GRANT TEMP ON DATABASE` back to `PUBLIC`. Each flags exactly its row.

### R2-TST-5: Step 1b says every site "names its login" but gives no mechanism that makes a forgotten site fail

**Severity:** MUST FIX (step 1b; DoD 1, 8). Weakens TST-2 and TST-12's E2E check.

**Where:** step 1b: "Every Integration site that touches the database names its login (`TST-2`). Seeding and
assertions run as superuser; the API runs as `portfolio_api`. … Every existing suite stays green, which shows the
matrix is enough." DoD 8 "Every Integration site names its login."

**The defect.** How do the existing suites get the API onto `portfolio_api` without passing for the wrong reason?
- `SOURCE@213f49f`: one property, `PostgresFixture.ConnectionString` (`PostgresFixture.cs:20`), feeds every site that
  hands the API a real database: `AuthFixture.cs:33` (which `AuthApiTests`, `OwnerCommandsTests`, `SecretsStayOutTests`
  and the in-process `OwnerCommands` at `OwnerCommandsTests.cs:92` inherit), `ImportApiTests.cs:24`,
  `BalanceIdempotencyTests.cs:21`, `ClockTests.cs:24`, and the process test's `ConnectionStrings__portfolio` at
  `OwnerCommandsTests.cs:116`. Any of them left on that property keeps the superuser, and stays green. "Stays green,
  which shows the matrix is enough" is then true of whatever was switched, and silent about the rest.
- DoD 1 asserts "the app connects as `portfolio_api`" but no step names the who-am-I test, so nothing shows *which*
  factory was checked.
- Round 1's "the domain tests run on `portfolio_api`" (proposed DoD 7) is gone: the rewrite's "seeding and assertions
  run as superuser" now covers `ImportPipelineTests`, `RestatementTests` and `SameDayPickTests`, which call the
  pipeline and recorder themselves through `CreateContext()` (`ImportSupport.cs:75-78`). Their paths are also driven
  over HTTP, so this is a narrowing, not a hole; say it is chosen.
- `SOURCE`: `tests/E2E/AppFixture.cs:51-59` starts the real AppHost, so after 1b the E2E's API runs on whatever the
  AppHost wires, and nothing in it asserts which login that is. The 28 E2E tests pass on `postgres` too.

**What to do.**
- **The compiler lists the sites.** Delete `ConnectionString`; add `SuperuserConnectionString`, `ApiConnectionString`
  and `ReportingConnectionString` (built with `NpgsqlConnectionStringBuilder` from the container's string and the
  fixture's generated passwords). `CreateContext()` becomes `CreateSuperuserContext()` for arrangement. Every site must
  then choose. Say this in step 1b.
- **Who-am-I, per factory kind.** A test on `AuthFixture.Api(…)`, on the `ImportApiTests` factory, and on the process
  test: `SELECT current_user, rolsuper FROM pg_roles WHERE rolname = current_user` through the app's own
  `PortfolioDbContext` gives `('portfolio_api', false)`. **Seen failing:** hand one `SuperuserConnectionString`.
- **E2E:** after one API request, as the superuser (`app.GetConnectionStringAsync`), `pg_stat_activity` for the app
  database contains a `portfolio_api` backend and no `postgres` backend but the test's own (TST-12). Add it to DoD 8's
  E2E evidence.

### R2-TST-6: The byte cap, as specified, is counted after Npgsql has already buffered the whole row

**Severity:** MUST FIX (part 4 of "Running a query"; step 3; DoD 3; G4-4). Overlaps SRC.

**Where:** part 4: "reads under one wall-clock deadline, counting **rows and bytes** on the client"; step 3 "a wide
row cut at the byte cap"; G4-4 "A single 200 MB cell does not reach the API's memory whole under the byte cap".

**The defect.**
- `EXECUTED 2026-11-06`, Npgsql 10.0.3, `SELECT repeat('x', 1e8)` (one 100 MB cell): in `CommandBehavior.Default`,
  `ReadAsync()` alone allocated **128 MB** before any cell was touched, and `GetString` another 190 MB. In
  `CommandBehavior.SequentialAccess`, `ReadAsync()` allocated 0 MB, and reading the cell through `GetTextReaderAsync`
  in 8 KB chunks up to a 64 KB budget allocated 0 MB (rounded), then `Cancel()`.
- So a client-side byte count over values from the default reader bounds the *response*, not the API's memory: the row
  is whole in memory first. G4-4 is answered: **false** for the default reader, true for the sequential one. The
  rewrite must say which, or the obvious implementation passes "a wide row cut at the byte cap" (the response is cut)
  while the API holds the whole cell. The Api container's limit is 512M (`AppHost.cs:84`), so a 400 MB cell kills it.
- The byte cap has no unit and no boundary. Rows get cap−1, cap, cap+1 (TST-8); bytes get one wide row. Bytes of what
  (UTF-8 of the returned text, wire bytes)? A row whose cell crosses the cap: dropped, or cut mid-cell?

**What to do.**
- Part 4: "the reader runs with `CommandBehavior.SequentialAccess`; text and `bytea` cells are read through
  `GetTextReader`/`GetStream` in chunks against the remaining byte budget". G4-4 becomes that rule plus its test.
- Define the unit (for example, UTF-8 bytes of each cell's returned string) and the cut (the crossing row is dropped).
  Then a boundary test exactly like the row cap's: with `Reporting:ByteCap = 1000` by option, results of 999, 1000 and
  1001 counted bytes give cut off false, false, true.
- The memory oracle: `GC.GetTotalAllocatedBytes(true)` before and after the request, on a cell of 1e8 bytes with the
  cap at 64 KB. Expect under 16 MB. That figure is process-wide and xUnit runs classes in parallel, so put this test in
  a collection with `DisableParallelization = true`. **Seen failing:** `CommandBehavior.Default` allocates at least
  the cell (128 MB here).

### R2-TST-7: No step 3 test shows the deadline against a query that lifts `statement_timeout` itself

**Severity:** MUST FIX (step 3; DoD 3). Weakens TST-6's producer table.

**Where:** step 3's tests: the streaming producer at the row cap; the cap boundary; the wide row; `SET
statement_timeout = 0` "refused as a second statement"; `set_config` across runs; the crash. DoD 3 "Each producer ends
as stated: timed out, …". "Values … 10 s … from configuration, so tests set them small".

**The defect.**
- The slice's second technique is "bounds it cannot reach … a deadline enforced by a cancel the server obeys". The one
  producer that shows that bound is a **single** statement that disarms the server-side one:
  `SELECT set_config('statement_timeout', '0', true), pg_sleep(600)`. It is not in step 3. The `SET … ; SELECT …` row
  shows the one-statement rule (`42601`), not the deadline. The streaming row ends at the row cap, also not the
  deadline. So no named test ends in "timed out", and DoD 3's "timed out" outcome has no producer.
- DoD 3's only seen-failing ("disposing without `Cancel()`") is for the row cap. Remove the wall-clock deadline and keep
  `SET LOCAL statement_timeout`: every named test still passes.
- TST-6's materializing row (`SELECT g FROM generate_series(1, 1e11) g`, where `temp_file_limit` should answer `53400`,
  "the one that holds") and its CPU row (`count(*)`) are gone too, so the backstop role defaults are tested by no
  behaviour, only by the snapshot (and see R2-TST-4: not even there).
- "Values … from configuration, so tests set them small" lost round 1's "a second test pins the real values on the
  Production factory" (proposed DoD 3). Nothing fails if the shipped default becomes 10 minutes.

**What to do.** Put TST-6's table back into step 3, with the deadline at 2 s by option, each row through the endpoint,
each running row observed by pid (TST-7):

| Producer | Expected |
|---|---|
| `SELECT set_config('statement_timeout','0',true), pg_sleep(600)` | timed out (the app's deadline) |
| `SELECT count(*) FROM generate_series(1, 1e11)` | timed out |
| `SELECT g FROM generate_series(1, 1e11) g ORDER BY g DESC` with `temp_file_limit` set small on the test role | refused, `53400` |
| `SELECT generate_series(1, 1e11)` | cut off by rows |
| `WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM r) SELECT n FROM r` | cut off by rows |

**Seen failing:** the deadline removed, the first two rows run until the poll's deadline. One more test on the
unmodified Production options asserts 10 s, 10,000 rows, 8 MB and 256 MB as configured.

### R2-TST-8: "`set_config` in one run not visible to the next" cannot fail: the `ROLLBACK` undoes it whatever the connection

**Severity:** MUST FIX (step 3; DoD 3).

**Where:** step 3 "`set_config` in one run not visible to the next (`DBS-3`)"; DoD 3 "A setting made in one run is not
visible to the next"; part 1 "a **fresh, unpooled connection** … so no `set_config`, `PREPARE` or advisory lock
survives"; part 6 "ends in **`ROLLBACK`**".

**The defect.** `EXECUTED 2026-11-06`, as a non-superuser login on one session:
- `BEGIN TRANSACTION READ ONLY; SELECT set_config('statement_timeout','1234ms',false); ROLLBACK;` then `SHOW
  statement_timeout` gives `0`. The session-level `set_config` is rolled back with the transaction. (Committed, the
  same call persists: `my.x = kept`.)
- `SELECT pg_advisory_lock(42)` inside the same kind of transaction, then `ROLLBACK`: the lock is **still held**
  (`pg_locks` count 1).
- `PREPARE p AS SELECT 1` inside it, then `ROLLBACK`: `pg_prepared_statements` count **1**.

Part 6 alone defeats the `set_config` probe. Pool the reporting connection and reuse one physical session, and the test
stays green. It shows `ROLLBACK`, which part 6 already claims, and never shows part 1. The two things part 1 exists for,
the advisory lock and `PREPARE`, survive `ROLLBACK` and are the ones to probe.

**What to do.** Replace the probe:
- run 1 `SELECT pg_backend_pid()`, run 2 the same: the pids differ;
- run 1 `SELECT pg_advisory_lock(42)`; then, as the superuser, no advisory lock is held by any `portfolio_reporting`
  backend once run 1 has returned (poll by pid, TST-7);
- run 1 `PREPARE p AS SELECT 1`; run 2 `EXECUTE p` gives `26000` (no such prepared statement).

**Seen failing:** a pooled reporting data source with `Maximum Pool Size=1` and `No Reset On Close=true`: same pid, the
lock held, `EXECUTE p` succeeds. Keep the `set_config` row, but as the test of part 6, with its seen-failing
`COMMIT` in place of `ROLLBACK`.

### R2-TST-9: The crash test: deterministic in CI by `SIGKILL`, not by memory, and vacuous unless the pool is warm

**Severity:** MUST FIX (step 3; DoD 3; S4-1).

**Where:** step 3 "a crash: the API serves the next request (`S4-1`)"; DoD 3 "After a crash, the API serves the next
request"; no seen-failing given.

**The defect.**
- **Inducing it by memory is not safe in CI.** `SOURCE@213f49f`: `PostgresFixture.cs:18` sets no memory limit, so
  DBS-2's `string_agg` crash (seen under the published stack's 256M) has no ceiling on a GitHub runner. It grows until
  the host's OOM killer picks a victim, which can be the test host or another class's container. Setting a limit by a
  create-parameter modifier makes it possible, but which process the kernel kills inside the cgroup (a backend, or the
  postmaster, which ends the container) is not the test's choice. `INFERRED`.
- **Inducing it by signal is deterministic.** `EXECUTED 2026-11-06`: `kill -9` of a `pg_sleep(600)` backend
  (`docker exec`, the container's own `kill`). The log reads `client backend (PID n) was terminated by signal 9:
  Killed`, `terminating any other active server processes`, `all server processes terminated; reinitializing`,
  `database system is ready to accept connections` **0.25 s** later. The container stays up, and
  `pg_postmaster_start_time()` is unchanged. Another session got `terminating connection because of crash of another
  server process`. `pg_stat_database.xact_commit` fell from 21 to 3: the stats are reset. That is the path an OOM kill
  of a backend takes, so it is a faithful model of S4-1's event.
- **Other classes are unaffected:** one container per class (`PostgresFixture.cs:9`, `IClassFixture`). Tests in the
  same class run in sequence, but they would inherit dead pool connections, so the crash test needs a class of its
  own.
- **Vacuous unless the API's pool holds an idle connection at the crash.** `EXECUTED`, Npgsql 10.0.3: a pooled data
  source's idle connection, used after the crash, threw `NpgsqlException` ("Exception while reading from stream",
  `IsTransient = true`). The next attempt succeeded on a new backend. So "the next request" works because of EF's
  retrying strategy (`SOURCE`: `Api/Program.cs:34`, `EnrichNpgsqlDbContext`; `SecretsStayOutTests.cs:50` turns it off
  to get a 500). A test that crashes before the API has opened any connection passes with or without the retry.

**What to do.** One class, `ReportingCrashTests`, its own fixture:
1. Warm the pool: a signed-in `GET /api/v1/accounts`, then, as the superuser, record the `portfolio_api` backend pids.
2. Start `SELECT pg_sleep(600) /* probe-<guid> */` through the run endpoint. Poll until its backend is active by pid.
3. `await container.ExecAsync(["kill", "-9", pid])`. Poll as the superuser until `SELECT 1` succeeds, deadline 30 s.
   Assert the crash happened: the container log contains `reinitializing`, and the recorded API pids are gone.
4. The run request answers a named problem, not a hang (within its deadline + 20 s).
5. **The oracle:** the *first* signed-in `GET /api/v1/accounts` after that answers 200, and so does a run of `SELECT 1`.
6. **Seen failing:** the same class with `Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:DisableRetry=true`: step 5's
   first request answers 500.

The memory-induced crash itself (DBS-2's query, under 256M) is shown by hand on the published stack in step 6 and
recorded with `D13`. DoD 10 marks S4-1's "the API serves the next request" **shown** (by the signal) and "one query can
crash it" **argued** in CI, **shown** by hand.

### R2-TST-10: "One run at a time" is undecided between two behaviours, and neither has a test

**Severity:** SHOULD FIX (step 3; DoD 3; S4-6).

**Where:** "**One run at a time** (`S4-6`): a second waits or gets `429`." Step 3 lists "one run at a time" with no
test; DoD 3 does not mention it.

**The defect.** `INFERRED`. "Waits or `429`" are two designs with different tests, and the wait variant has no bound:
N waiting requests each hold a request and a thread for up to the deadline. A gate written as a `SemaphoreSlim`
released only on the success path stays taken after a timeout, a cancel or a client abort, and every later run is
refused; nothing in the rewrite would notice. Which routes take the slot (ad hoc run, saved run, the schema route on
the same login) is also unsaid. Timing-based tests ("the second took longer") flake on shared runners.

**What to do.** Decide `429` with a named problem title (simpler, and testable by order, not time). Then, through the
endpoint, with TST-7's presence-by-pid:
1. Run A (`pg_sleep(600)`, deadline 30 s by option). Poll until A's backend is active.
2. Run B (a saved report, so both routes share the gate): `429`, and, as the superuser, the `portfolio_reporting`
   backend count is still 1.
3. Abort A's HTTP request (cancel its `HttpClient` token). Poll until A's pid is gone.
4. Run C (`SELECT 1`): 200.
5. Repeat 1–4 ending A by its deadline, and by a `42501` refusal, instead of an abort.

**Seen failing:** the gate removed, step 2 sees two reporting backends; the release moved out of `finally`, step 4
answers `429`. Say whether the schema route takes the slot; if it does not, a schema request during A answers 200.

### R2-TST-11: DoD 7 and step 6 name checks without the oracle round 1 gave them

**Severity:** SHOULD FIX (DoD 7, 9; step 3, 6).

**Where:** DoD 7 "the committed OpenAPI document, which is byte-stable, and the contract test covers them"; step 6
"By hand … the passwords unchanged"; step 3 "`SecretsStayOutTests` covers both new passwords (`TST-14`)".

**The defect.**
- `SOURCE@213f49f`: `ci.yml:97-101`, the contract job is `continue-on-error: true`. DoD 7's "byte-stable" can be red at
  merge. That is the middle cut, not a defect, but DoD 7 should say "advisory in CI, green locally at the DoD commit",
  as DoD 8 says of the E2E. TST-15's other half is gone: the new operation ids added to the `Assert.Superset` in
  `NoCredentialInResponsesTests.cs:28`, so the credential scan is shown to have seen them.
- Step 6's "the passwords unchanged" has no oracle; logging in needs the password. Round 1's oracle (TST-12,
  `md5(rolpassword)` equal across redeploys) **no longer works under the rewrite**, and it was rightly not carried:
  `EXECUTED 2026-11-06`, `ALTER ROLE portfolio_api PASSWORD 'x'` run twice with the same `'x'` gave three different
  `md5(rolpassword)` values (a fresh SCRAM salt each time), and the routine "sets both passwords" on every run. But
  nothing replaced it. The catalog snapshot file (one SQL file under `tests/`, run by `psql -f` in step 6, TST-4) is
  also unsaid, so the by-hand comparison may use a different query from CI's.
- "Covers both new passwords" keeps TST-14's secret list and drops its producers. A refused or timed-out query is a
  `PostgresException`; the messages that carry host, port and user come from a wrong password (`28P01`) and a closed
  port on the reporting data source.

**What to do.** DoD 7: add the superset row and "advisory in CI". Step 6: the oracle for "unchanged" is a hash of each
password **parameter** as deploy state holds it (`sha256`, compared before and after each redeploy, the value never
printed), plus the API signed in after each redeploy, which shows the role took the value the API holds. Name the
shared snapshot file. Step 3: the two non-`PostgresException` producers into `SecretsStayOutTests`.

### R2-TST-12: The gates: G4-4 is answered, GT-B is moot, and step 1a's matrix is unchecked until 1b

**Severity:** NOTE (Gates; step 1a, 1b).

- **G4-4** is answered by R2-TST-6 (`EXECUTED`): the default reader holds the whole cell; the sequential reader does
  not. It stops being a gate and becomes a rule with its test. Its raiser, "the API's working set read", is a poor
  oracle in an in-process test with parallel classes; use the allocation delta in a non-parallel collection.
- **G4-1** (`aspire run` twice) and **G4-2** (the `ReferenceExpression` under `aspire run` and publish) are rightly
  by-hand claims. **G4-3** (GT-D) and **G4-5** (GT-A) carry over correctly, with the right raisers.
- **GT-B** (`DROP ROLE` in a `Down`) is moot: the roles are created by the routine, not by a migration. Say so, so the
  assessment does not list it as unraised. Up-down-up (TST-1 c) still matters for the step-2 migration that creates
  `reporting`: its `Down` drops a schema that `portfolio_reporting` holds `USAGE` on. `MigrationOnRowsTests.cs:29-57`'s
  pattern, with the routine called after each `up`, covers it.
- A missing gate: **"EF's retrying strategy recovers the API after a crash-restart"** (`INFERRED` at the EF level;
  `EXECUTED` at the Npgsql level, R2-TST-9). Raised by R2-TST-9's test.
- **Step 1a can merge green with the API unchanged.** The fixture's call creates the roles in each class's own
  container; the API still uses the superuser string, so no existing test changes outcome. The cost: 1a commits
  `ExpectedPrivileges.Api` with no behaviour behind it (no path runs as `portfolio_api` until 1b), so 1b will likely
  edit it. Say that this edit is expected in 1b and is where the matrix is first shown sufficient.

---

## Round-1 findings: applied correctly?

| Round 1 | Verdict | Note |
|---|---|---|
| TST-1 (one source; the built Migrator; idempotency in CI) | **weakened** | One routine, fixture and Migrator both: applied. "Fresh container", the login-with-the-given-password check, and (a) twice, (b) role first, (d) new password: lost (R2-TST-3) |
| TST-2 (every site chooses; domain tests under the grants; who-am-I) | **weakened** | Stated as an outcome with no mechanism; domain tests moved to superuser; who-am-I unnamed (R2-TST-5). The grant table from the code paths: applied |
| TST-3 (`G-7` with control, the app's connection, exact `42501`) | **weakened** | App's connection and `42501`: applied. The control is mis-stated for `UPDATE`/`DELETE`/`TRUNCATE`; `session_replication_role` dropped (R2-TST-1) |
| TST-4 (catalog enumeration, committed set, planted extra) | **weakened** | Scope cut to objects; databases, languages, role attributes, memberships, role settings left out; the plant unspecified (R2-TST-4) |
| TST-5 (SQLSTATE per probe; writes judged by effect) | applied | Step 2 and DoD 2 carry both, with `lo_from_bytea` as the seen-failing |
| TST-6 (producer table; cancel before dispose) | **weakened** | Cancel-before-dispose and the streaming row: applied. The deadline, CPU and materializing rows and the Production pin: lost (R2-TST-7) |
| TST-7 (presence then absence by pid) | applied | With a fresh connection per run, "gone" is now the pid's exit, a cleaner oracle than round 1's |
| TST-8 (cap boundary; pooled reset; bytes) | **weakened** | Boundary: applied. The pooled-reset test is replaced by a probe that `ROLLBACK` defeats (R2-TST-8). Bytes: added, but counted after buffering (R2-TST-6) |
| TST-9 (binding probe the grant allows) | applied | Echo and effect, positional `$n`; the literal-`@name` case is moot under `$n` |
| TST-10 (`COMMENT` read live) | applied | DoD 5, with the nulled-comment seen-failing |
| TST-11 (axe in named states; keyboard) | applied | Step 5 and DoD 6. Round 1's `page.RouteAsync` method for the slow states is not named; it is how, not what |
| TST-12 (CI over by hand; exact by-hand oracle) | **weakened** | App-model test in CI: applied. E2E `pg_stat_activity` check lost (R2-TST-5); the password oracle no longer works and was not replaced (R2-TST-11); the shared snapshot file unnamed |
| TST-13 (view rules, equality, `saved_report`, value fidelity) | applied | Step 2's list, `S4-7`'s cut, and the typing rule. The 20-digit `numeric` round-trip is implied by "numeric as a string"; name it in step 3's tests |
| TST-14 (passwords in `SecretsStayOutTests`; non-Postgres producers) | **weakened** | Secrets: applied. The `28P01` and closed-port producers: lost (R2-TST-11) |
| TST-15 (non-vacuous scans; login by behaviour) | **weakened** | The behavioural login test survives only through R2-TST-5's who-am-I; the `NoCredentialInResponsesTests` superset is lost (R2-TST-11) |
| TST-16 (concurrency) | applied as a ruling | S4-6 rules it; its test is not designed (R2-TST-10) |
| TST-17 (predicate pushdown probe) | applied | Superseded by S4-5: barrier views, the leaky-function probe seen failing without the barrier, and the catalog test |

Nothing from round 1 is contradicted, and nothing is lost whole. Seven items are weakened, each by a dropped clause
that held the test to the right reason.

---

## Confirmed (do not re-check)

- **Owner-side outcomes on the repo's trigger shape, `postgres:18.3`:** `UPDATE`, `DELETE`, `TRUNCATE` give `P0001`
  from `append_only()`; `DISABLE TRIGGER` then `UPDATE` succeeds; `DROP TABLE` succeeds; `SET LOCAL
  session_replication_role = replica` then `UPDATE` succeeds; `ALTER DATABASE … SET` runs in a transaction, `SET
  TABLESPACE` gives `25001`. (`EXECUTED 2026-11-06`)
- **A non-superuser's `SET session_replication_role`** gives `42501 permission denied to set parameter`. (`EXECUTED`)
- **Across `ROLLBACK` of a `READ ONLY` transaction:** a session `set_config` is undone; a session advisory lock and a
  `PREPARE` survive. (`EXECUTED`)
- **Re-setting the same password** changes `rolpassword` (a new SCRAM salt). (`EXECUTED`)
- **`kill -9` of a backend:** crash-restart, ready in 0.25 s, container up, postmaster start time unchanged, cumulative
  stats reset; the log says `reinitializing`. (`EXECUTED`)
- **Npgsql 10.0.3 after a crash-restart:** the first use of an idle pooled connection throws a transient
  `NpgsqlException`; the next succeeds. (`EXECUTED`)
- **Npgsql 10.0.3, one 100 MB cell:** `CommandBehavior.Default` allocates 128 MB at `ReadAsync`;
  `SequentialAccess` with a chunked `GetTextReaderAsync` allocates none of note before `Cancel()`. (`EXECUTED`)
- **`Testcontainers.PostgreSql` 4.16.0** defaults the database, user and password to `postgres`. (`EXECUTED`)
- **The harness:** one container per class; every site that hands the API a real database reads one property; the
  Migrator logs at `Information` with no `appsettings.json`; the API's EF context retries by default; the contract job
  is advisory; the E2E starts the real AppHost. (`SOURCE@213f49f`)
