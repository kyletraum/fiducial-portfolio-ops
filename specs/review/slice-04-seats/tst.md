# Slice 04 review: test design seat (TST)

**Seat:** test design. **Date:** 2026-11-06. **Under review:** `specs/001-portfolio-platform/slice-04.md` at `644efe2`
(first draft: "What the slice is", Spikes L/M/N, "The build" 1–6, "Definition of done" 1–9, Q1–Q5).

**Read:** `slice-04.md` (whole); `slice-03.md` §§ "What the slice is not" (`G-7`), "Before the first real balance", DoD
step notes; `slice-03-assessment.md` (`G-7` row); `review/slice-03-r2-seats/tst.md` (seat shape);
`data-model.md` §§ `saved_report`, "The `reporting` schema"; `testing.md` "What is tested"; `security.md` "Audit";
`tests/Integration/{PostgresFixture,AuthFixture,TestAuth,ImportSupport,AuditShape,AuthSchemaTests,ImportPipelineTests,NetWorthViewTests,OwnerCommandsTests,MigrationOnRowsTests,SecretsStayOutTests,SecretCapture,AllowListTests,ImportApiTests,BalanceIdempotencyTests}.cs`,
`Integration.csproj`; `tests/E2E/{AppFixture,AppModelTests,PublishedStackTests,Axe}.cs`;
`tests/Architecture/{AspireBoundaryTests,TestBoundaryTests,NoCredentialInResponsesTests}.cs`;
`tests/Contract/OpenApiDocumentTests.cs`; `src/Migrator/Program.cs`; `src/AppHost/AppHost.cs`; `src/Api/Program.cs`;
`src/Infrastructure/BalanceWrites.cs`; migration `20261024160637_AuthAndAudit.cs` (triggers); `.github/workflows/ci.yml`.

**Ran** (all `EXECUTED 2026-11-06`, in my own throwaway `postgres:18.3` container `s4-tst-pg`, removed afterwards; scripts
and output in the seat's scratch directory):
- `psql` as `postgres`: a model of the slice (a table, an `audit_event` with an append-only trigger, `reporting.v_account`,
  `portfolio_api` with `INSERT, SELECT` on `audit_event`, `portfolio_reporting` with `USAGE` + `SELECT` on the view only).
  Then a privilege sweep (`has_*_privilege`, `pg_roles`, `pg_auth_members`, `pg_shdepend`).
- `psql` as each role: the refusal probes cited below, with `\set VERBOSITY verbose` to read the SQLSTATEs.
- `dotnet run probe.cs` / `probe2.cs` (Npgsql **10.0.3**, the version `tests/Integration` resolves): command timeout
  vs `SET statement_timeout = 0`; reader disposal after N+1 rows; `Cancel()`; the pool reset; multi-statement text;
  `@x` in a literal; the auth-failure message; first-row latency of the `FROM`-clause and target-list `generate_series`.
- No repository test was run: the design has no code yet, and the existing suites are unchanged by it.

---

## Findings

**Count:** 2 BLOCKING, 8 MUST FIX, 5 SHOULD FIX, 2 NOTE.

### TST-1: BLOCKING (step 1; DoD 1, 2, 7, 8). The role script would be tested by the fixture's own copy, not by what the Migrator runs

**Markers:** `SOURCE@644efe2`. `tests/Integration/PostgresFixture.cs:22-28` migrates by itself (`db.Database.MigrateAsync()`).
`src/Migrator/Program.cs` is top-level statements that nothing in `tests/` calls. `Integration.csproj` references Api,
Infrastructure and Ingestion, not Migrator.

**Where:** step 1 ("The Migrator provisions `portfolio_api` … The Integration fixture runs the app as that role") and Spike M.3.

**The defect.** Spike M.3 leans towards "the Migrator, after migrations, idempotently, with passwords from configuration".
The fixture never runs the Migrator, so the roles in tests must come from somewhere else. The obvious place is a
`CREATE ROLE … GRANT …` block in `PostgresFixture`. Every refusal test (DoD 1, 2, 4) would then prove the test's own
script, and could stay green while the Migrator provisions something different, or nothing. DoD 8 is the only check on
the real script, and it runs by hand.

**What to do.**
- **One source.** Put every `GRANT`/`REVOKE`, and `CREATE ROLE … NOLOGIN`, in a **migration**. A migration is committed
  SQL, and the fixture and the Migrator already apply it the same way. Only "make it a login, with this password" is a
  post-migration step: `ALTER ROLE … LOGIN PASSWORD`, because a migration cannot take a secret. Make that step one
  Infrastructure method, for example `DatabaseLogins.EnsureAsync(superuserConnection, apiPassword, reportingPassword)`.
  The Migrator calls it, and `PostgresFixture.InitializeAsync` calls the same method after `MigrateAsync`. The fixture
  then contains no role SQL, and a review can check that.
- **The process test.** Run the built Migrator, as `OwnerCommandsTests.The_built_Api_runs_a_command_and_exits_without_serving`
  runs `Api.dll`. Use a fresh, un-migrated container, with `ConnectionStrings__portfolio` and both password variables set.
  Assert exit 0, then compare `CatalogSnapshot(role)` (TST-4) with the snapshot of a fixture-provisioned database. They
  must be equal. This needs a `ProjectReference` to Migrator. `AspireBoundaryTests` checks direct packages only, so it
  allows that.
- **Idempotency in CI, not only by hand (Spike M.4).**
  - (a) Run `EnsureAsync` twice. Both logins still connect, and the snapshot is unchanged.
  - (b) A fixture that creates `portfolio_reporting` *before* `MigrateAsync`. Roles are cluster-wide, so on a dev server
    a role can already exist when a fresh database is migrated. The migration must still apply.
  - (c) A `Migration3Fixture`-style class: stop at `AuthAndAudit`, write rows, migrate up, then down, then up again
    (`MigrationOnRowsTests.cs:29-57`). `DROP ROLE` in a `Down` fails while the role holds privileges anywhere
    (`INFERRED`), so the `Down` path needs this test.
  - (d) `EnsureAsync` with a *different* password the second time. Decide whether it rotates the password or keeps the
    old one, and assert that. Spike M.1's "stable across deploys" depends on which.
- **Seen failing:** remove one `GRANT` from the migration. The process test, the snapshot test and the affected
  behavioural test all go red.

### TST-2: MUST FIX (step 1; DoD 1, 7). Tests left on the superuser string stay green for the wrong reason, and the domain-level tests never meet the grants

**Markers:** `SOURCE@644efe2`. Seven sites hand the Api a real database as the superuser:
- `AuthFixture.cs:33`, which `AuthApiTests`, `OwnerCommandsTests` and `SecretsStayOutTests` inherit;
- `ImportApiTests.cs:24` (the `ApiTests` base);
- `BalanceIdempotencyTests.cs:21`;
- `ClockTests.cs:24`;
- `OwnerCommandsTests.cs:116` (the process test's `ConnectionStrings__portfolio`).

Every arrangement and domain call goes through `PostgresFixture.CreateContext()`, also as the superuser
(`ImportSupport.ImportAsync`, `NewAccountAsync`, `BindAsync`, `ImportPipelineTests`, `RestatementTests`,
`SameDayPickTests`). The API writes with `UPDATE`: `BalanceWrites.cs:59` sets `superseded_at` through
`ExecuteUpdateAsync`. Identity updates `identity_user` and passkeys, and `reset-owner` deletes `identity_user`.

**The defect.**
- "Every existing suite stays green" passes trivially for any site that is not moved. Nothing makes a site fail when it
  still hands over the superuser string.
- The domain-level classes run `ImportPipeline` and `BalanceRecorder` on a superuser context. A grant missing for one of
  their paths (`ingest_run INSERT`, the `account_balance` column `UPDATE (superseded_at)`) is caught only if some HTTP
  class happens to drive that exact path.
- Q1's premise ("Balances supersede rather than update … the API gets no `UPDATE` at all") is false of the code. A grant
  list written from it fails restatement, sign-in and reset-owner.

**What to do.**
- **Force a choice at every site.** Rename `PostgresFixture.ConnectionString` to `SuperuserConnectionString`, and add
  `ApiConnectionString` and `ReportingConnectionString`, built with `NpgsqlConnectionStringBuilder` from the container's
  string. The compiler then lists every site.
- **Run the domain tests under the grants.** `CreateContext()` becomes the API login. `CreateSuperuserContext()` is for
  arrangement only (seeding soft-deleted rows, reading `audit_event` deltas).
- **Make a wrong login fail loudly.** Option, needs a production change: a physical-connection initializer on the Api's
  data source refuses a session where `rolsuper` is true or `pg_shdepend` shows the role owns anything. Then every
  factory still handed the superuser string fails at its first query. A test hands the Production factory
  `SuperuserConnectionString` and expects that refusal. It also catches a deploy mistake.
- **Who-am-I.** From `factory.Services`, resolve `PortfolioDbContext` and run `SELECT current_user, rolsuper FROM pg_roles
  WHERE rolname = current_user`. Expect `('portfolio_api', false)`. Also expect
  `count(*) FROM pg_shdepend WHERE refobjid = 'portfolio_api'::regrole AND deptype = 'o'` = 0 (TST-4).
- **Write the API's grant table into step 1 from the code paths, with the test that drives each path:**
  - restatement: `RestatementTests`, `BalanceIdempotencyTests.…Supersedes`;
  - Identity: `AuthApiTests`;
  - reset-owner `DELETE`: `OwnerCommandsTests`;
  - `saved_report` `UPDATE` (`updated_at`, `last_run_at`, `data-model.md:263-264`).

  Then commit `ExpectedPrivileges.Api` and assert the snapshot equals it, so any widening shows up in review.

### TST-3: MUST FIX (DoD 1). The `G-7` test needs its positive control, the app's own connection, and the exact refusal

**Markers:** all four `EXECUTED 2026-11-06`, as a non-owner `portfolio_api` holding `INSERT, SELECT`:
- `ALTER TABLE audit_event DISABLE TRIGGER …` gives `42501 must be owner of table audit_event`;
- `DROP TRIGGER …` gives `42501 must be owner of relation audit_event`;
- `UPDATE audit_event` gives `42501 permission denied for table audit_event` (the grant answers before the trigger);
- `SET session_replication_role = replica` gives `42501 permission denied to set parameter`.

`slice-03.md:97-104` records the by-hand `G-7` sequence.

**The defect.** "As that login, `DISABLE TRIGGER` … are refused" passes in three wrong ways:
- a misspelt trigger or table gives `42704`/`42P01`;
- the test opens a connection with a login it built itself, not the app's;
- `Assert.ThrowsAsync<PostgresException>` is satisfied by any error.

Also, once the API has no `UPDATE`, the existing trigger tests (`AuthSchemaTests.cs:66-76`) still run as the superuser.
They show the trigger, not the grant. The two layers need a test each.

**What to do.** Write `G-7` as a committed test, inverted:
- **(a) Control.** As the superuser, in a transaction that is rolled back: `DISABLE TRIGGER audit_event_append_only`
  succeeds, and `pg_trigger.tgenabled = 'D'`.
- **(b) The refusals.** Run them on a connection taken from the app's own `PortfolioDbContext`
  (`db.Database.GetDbConnection()` from `factory.Services`). Each of `DISABLE TRIGGER`, `DROP TRIGGER`,
  `ALTER TABLE … OWNER TO`, `SET session_replication_role`, `TRUNCATE`, `UPDATE` and `DELETE` gives
  `SqlState == 42501`, and `MessageText` equals the texts above.
- **(c) Repeat for the other tables.** `used_setup_code` and `ingest_run` have the same triggers.
- **(d) Seen failing.** Point (b) at `SuperuserConnectionString`. It goes red on the first statement.

### TST-4: MUST FIX (DoD 2). "Nothing else anywhere" needs an enumeration from the catalog, with a committed expected set and a planted extra

**Markers:**
- `EXECUTED 2026-11-06`, for a role holding only `USAGE` on `reporting` and `SELECT` on one view:
  - `has_schema_privilege(…'USAGE')` is true for `public`, `pg_catalog`, `information_schema` and `reporting`;
  - `CONNECT` is true on `postgres`, `template0` and `template1`, and `TEMP` is true on `postgres`;
  - `EXECUTE` is true via `PUBLIC` for `lo_create`, `lo_from_bytea`, `lo_put`, `lo_unlink`, `nextval`, `setval`,
    `set_config`, `pg_sleep`, `pg_notify`, `pg_advisory_lock`, `pg_cancel_backend` and `pg_terminate_backend`, and on
    the app's own trigger function in `public`;
  - `pg_read_file`, `pg_ls_dir`, `lo_import`/`lo_export` and `pg_reload_conf` are false.
- `EXECUTED`: after `GRANT SELECT (name) ON account TO colgrant`, `has_table_privilege` is **false** and
  `has_any_column_privilege` is **true**. A table-level sweep misses column grants.
- `EXECUTED`: `pg_shdepend WHERE refobjid = role` lists every ACL entry (`deptype 'a'`) and every owned object
  (`'o'`, a large object included) across databases.

**The defect.** "Each route Spike L found is refused by a test" samples. It proves only the routes someone thought of.
The things that let a role write or read beyond its grant are spread over a dozen catalogs. A list of probes is green
while one of them is open.

**What to do.**
- **Write `CatalogSnapshot(role)`**, one SQL file under `tests/` that both Integration and the by-hand step 6 check use
  (TST-12). Run as the superuser, it returns sorted rows over:
  - `pg_roles` attributes: `rolsuper`, `rolcreaterole`, `rolcreatedb`, `rolreplication`, `rolbypassrls`, `rolinherit`,
    `rolconnlimit`, `rolvaliduntil`;
  - `pg_auth_members`, in both directions;
  - `pg_db_role_setting` for the role (its `statement_timeout` default, if Q2 puts one there);
  - `pg_default_acl` rows naming the role, which would grant on future objects;
  - `pg_shdepend` for the role (`'a'` and `'o'`);
  - for every `pg_database`: `CONNECT`, `CREATE`, `TEMP`;
  - for every `pg_namespace` outside `pg_toast*`/`pg_temp*`: `USAGE`, `CREATE`;
  - for every `pg_class` in a non-system schema: each of `SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER,
    MAINTAIN` via `has_table_privilege`, and the same via `has_any_column_privilege`;
  - every sequence, via `has_sequence_privilege` (`USAGE, SELECT, UPDATE`);
  - every `pg_proc` in a non-system schema, via `has_function_privilege`, plus a named deny-list of built-ins (the
    `lo_*` writers, `pg_terminate_backend`, `pg_notify`, `dblink*` if installed);
  - `has_parameter_privilege` for the `PGC_SUSET` settings that matter (`session_replication_role`, `log_*`, `lo_compat_privileges`);
  - `pg_largeobject_metadata` owned by the role.
- **Commit `ExpectedPrivileges.Reporting`** and assert equality. It is Spike L's output, with each `PUBLIC`-derived row
  marked kept or revoked. Do the same for `Api`.
- **Seen failing, kept as a committed test.** In a fresh class: `GRANT SELECT (display_name) ON public.account TO
  portfolio_reporting`. The snapshot differs on exactly that row (the column case a table sweep misses). Revoke it in
  `finally`. Repeat with `ALTER DEFAULT PRIVILEGES … GRANT SELECT ON TABLES TO portfolio_reporting`, then a new table.
- **Future objects.** After provisioning, create a probe table in `public` and a probe view in `reporting` as the
  superuser. The snapshot must say whether each is readable, and the expected set fixes the answer. A per-object
  `GRANT` and `ALTER DEFAULT PRIVILEGES` give different answers.

### TST-5: MUST FIX (DoD 2, step 2). Behavioural probes must assert *why* they failed, and writes must be judged by effect

**Markers:**
- `EXECUTED 2026-11-06` as `portfolio_reporting`:
  - `SELECT * FROM account` gives `42501 permission denied for table account`;
  - `SELECT * FROM acount` (a typo) gives `42P01 relation "acount" does not exist`;
  - `SELECT … FOR UPDATE` on the view gives `42501`;
  - `COPY … TO PROGRAM` gives `42501`.
- `EXECUTED`: `CREATE TEMP TABLE`, `CREATE FUNCTION pg_temp.f() …` and `pg_notify` **succeed**.
- `EXECUTED`: **`SELECT lo_from_bytea(0, 'written by reporting')` succeeds.** `pg_largeobject_metadata` goes 0 → 1, and
  `pg_shdepend` then shows the reporting role *owning* a large object. A durable write, through `PUBLIC`'s `EXECUTE`,
  with no table involved.

**The defect.**
- A probe asserted as "throws" or "the endpoint answers 4xx" passes on a typo, on a `search_path` that omits `public`
  (42P01 again), or on Q5's one-statement refusal. None of those is the grant.
- "Any write, to anything" refused cannot be shown by collecting `permission denied` errors. The large-object write
  raises nothing.

**What to do.**
- **One probe table, one theory.** Each row holds a schema-qualified SQL and its expected SQLSTATE (and message) as
  `portfolio_reporting`. Run each row *through the endpoint*. The endpoint's problem details must carry PostgreSQL's
  `sqlState` as an extension, or no test can tell `42501` from `42P01`. Write that into step 3.
- **Positive control per row.** The same SQL runs successfully as the superuser in a rolled-back transaction, so the
  object exists and the statement is valid. That rules out the typo class. (Leave `TO PROGRAM` out of the control.)
- **Judge writes by effect, not only by error.** Around the whole write battery (`INSERT`/`UPDATE`/`DELETE` on each view
  and table, `SELECT nextval/setval`, the `lo_*` writers, temp objects, `DO` blocks, `SET ROLE`, `CREATE` in every
  schema), compare before and after, as the superuser:
  - `CatalogSnapshot`;
  - `count(*) FROM pg_largeobject_metadata`;
  - `pg_shdepend … deptype 'o'` for the role (expect zero);
  - per table, `count(*)` and `max(xmin::text::bigint)`.

  Every value must be unchanged. TST-4's enumeration and this oracle together cover what neither covers alone.
- **Decide and pin.** Temp objects and `pg_notify` are not durable writes to app data. Spike L decides refuse
  (`REVOKE TEMP ON DATABASE … FROM PUBLIC`) or accept. Either way a row in the probe table records the decision.

### TST-6: BLOCKING (step 3; DoD 3; Spike N.1, N.2). The obvious time-bound test passes while a streaming query runs unbounded

**Markers:**
- `EXECUTED 2026-11-06`, Npgsql 10.0.3, `CommandTimeout = 2`, SQL `SET statement_timeout = 0; SELECT pg_sleep(600)`:
  `NpgsqlException` (inner `TimeoutException`) at **2.0 s**, and the server query is gone.
- `EXECUTED`: `SELECT generate_series(1, 100000000000)` with `CommandTimeout = 10`. Read 6 rows, then dispose the reader
  without cancelling. **The backend was still `active` at 4 min 0 s.** Npgsql drains the rest of the result, each read
  succeeds, and the command timeout never fires. I terminated the backend by hand.
- `EXECUTED`: the same with `cmd.Cancel()` before dispose. Reader closed in 0.1 s, and the backend went `idle`.
- `EXECUTED`: first row of `SELECT g FROM generate_series(1, 20000000) g` (a `FROM`-clause function) at **1.42 s**, and of
  `SELECT generate_series(1, 20000000)` (target list) at **0.00 s**. A `FROM`-clause function materializes before the
  first row.
- `EXECUTED`: `SELECT 1; SET statement_timeout = 0; SELECT 2` runs every part (2 result sets).

**The defect.**
- The natural DoD 3 test, `SET statement_timeout = 0; SELECT pg_sleep(…)` against Npgsql's command timeout, goes green.
  The natural row-cap implementation, "read N + 1, stop, dispose", is unbounded in server work and time for a query
  that streams. So DoD 3 can pass while the slice's central claim ("cannot run without bound") is false.
- Spike N.2's own example, `SELECT * FROM generate_series(1, 1e9)`, measures materialization (temp files, before any
  row arrives), not the cap. That is a different bound (`temp_file_limit`), and the spike would read its result as the
  cap's.

**What to do.** DoD 3 becomes a producer table, each row run through the endpoint with the bound set small by option
(`Reporting:Timeout = 00:00:02`). Each row asserts the *kind* of outcome:

| Producer | Exercises | Expected |
|---|---|---|
| `SET statement_timeout = 0; SELECT pg_sleep(600)` | the client bound, quiet server | timeout problem, `57014` or the client timeout's title |
| `SELECT set_config('statement_timeout','0',false), pg_sleep(600)` | disarming by function | same |
| `SELECT generate_series(1, 1e11)` (target list) | streaming past the cap | **truncated result, not a timeout**, returned well inside the bound |
| `SELECT g FROM generate_series(1, 1e11) g` | materializing (temp files) | timeout problem; temp files released (`pg_stat_database.temp_files` read before and after, or `temp_file_limit` refusal `53400` if Spike L sets one) |
| `SELECT count(*) FROM generate_series(1, 1e11)` | CPU, no rows | timeout problem |
| `WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM r) SELECT n FROM r` | unbounded recursion, streaming | truncated |

The third row is the one that fails today's obvious design. Seen failing: dispose without `Cancel()`, and that row turns
into a timeout. Then every row must also pass TST-7's "gone" oracle. A second test, on the unmodified Production
factory, pins the real bound and cap from configuration, as slice 03 pinned the limiter's numbers.

### TST-7: MUST FIX (DoD 3). "Gone from `pg_stat_activity`" has two traps; observe it by pid, after seeing it present

**Markers:** both `EXECUTED 2026-11-06`:
- A poll of `SELECT count(*) FROM pg_stat_activity WHERE state = 'active' AND query LIKE '%<marker>%'` **always found
  itself** (count 1 after the query was gone). The poller's own text contains the marker.
- After the cancelled reader, the backend's row read `191:idle`. `pg_stat_activity.query` keeps the *last* query's text
  while idle, so "no row with the marker" is false for a pooled connection, even when the query has stopped.

**The defect.** As written, the oracle is red when the bound works, because of the idle text and the self-match. Nothing
in the draft rules out the opposite case either: "absent" checked before the query started, which is green when nothing
was bounded.

**What to do.**
- **Mark each producer.** Its SQL carries a comment `/* probe-<guid> */`.
- **Presence first (the positive control).** As the superuser, poll until a row with `pid <> pg_backend_pid()`,
  `state = 'active'` and the marker appears. Record its `pid`. Use a 10 s deadline, and fail if it never appears. The
  endpoint call runs as a background `Task` meanwhile.
- **Then absence.** Await the response. Poll until `pid` is gone, or its `state <> 'active'`, or its
  `query_start` has moved on. Deadline 10 s. Every producer is sized at minutes (`pg_sleep(600)`, `1e11`), so a
  client-only abandon stays visibly active far past the deadline. The margin is minutes against seconds, not a race.
- **No tight timing assertions.** Assert at most "returned within bound + 20 s". The two time-zone jobs and the main job
  run Integration on shared `ubuntu-latest` runners, with classes in parallel. Durations are evidence for the
  assessment, not oracles.
- **Clean-up.** The fixture's `DisposeAsync` terminates any backend still owned by `portfolio_reporting`, so a failing
  test leaves nothing running in a container shared by the class.

### TST-8: MUST FIX (DoD 3). The cap needs its boundary, the pooled session needs a reset test that can fail, and rows do not bound bytes

**Markers:**
- `EXECUTED 2026-11-06`: with `Maximum Pool Size=1`, one borrower ran `SET statement_timeout = '1234ms'; SET
  search_path = pg_temp`. The next borrower saw `search_path = "$user", public`: Npgsql's reset on return restored it.
  My `statement_timeout` check in the same probe was **vacuous**, because the value restored and the role default were
  both 0.
- `INFERRED`: `SELECT repeat('x', 200000000)` is one row of about 200 MB. A row cap does not bound the API's memory, and
  the Api container's limit is 512M (`AppHost.cs`, `MemoryLimit("512M")`).

**What to do.**
- **The cap.** Set `Reporting:RowCap = 5` by option. `generate_series(1, n)` for n = 4, 5 and 6 returns 4/5/5 rows,
  with `truncated` false/false/**true**. "Says so" means the flag in the body and the text on the page (E2E, TST-11).
- **The pooled session (`SET` cannot outlive its query).**
  - The test's reporting data source uses `Maximum Pool Size=1`, so the second request gets the same physical connection.
    Assert that it does: `pg_backend_pid()` is equal in both requests.
  - Run 1: `SET statement_timeout = 0; SET search_path = pg_temp; SET TimeZone = 'Pacific/Kiritimati'`.
  - Run 2: `SHOW` each. Expect the role's or the database's value. Each value must differ from what run 1 set: if Q2 sets
    a role `statement_timeout` of, say, `5s`, `0` is distinguishable, and `TimeZone` is `UTC` from the database setting.
  - Seen failing: `No Reset On Close=true` on that data source.
- **Bytes.** Add a byte bound to DoD 3 (per result, or per cell, with "cut off" saying so), and a producer row
  `SELECT repeat('x', 200000000)`. Expect a refusal or a truncation, with the Api still healthy afterwards (`GET
  /api/v1/system/health` 200).

### TST-9: MUST FIX (DoD 4, step 4). `1; DROP TABLE account` cannot fail; probe the binding with something the grant allows

**Markers:** all `EXECUTED 2026-11-06`:
- As `portfolio_reporting`, the interpolated text `SELECT id FROM reporting.v_account WHERE id = 1; DROP TABLE account`
  returns the row, then `42501 must be owner of table account`.
- In a `READ ONLY` transaction, `DROP TABLE` gives `25006`.
- Npgsql 10.0.3 runs every statement of a multi-statement text (TST-6).
- `SELECT '@x' AS lit, @x AS bound -- @x` with `x = "v'); DROP TABLE account; --"` returned `lit = @x` and the value
  byte for byte.

**The defect.** With the value interpolated, "nothing changes" still holds three ways:
- the grant refuses the `DROP`;
- a read-only transaction refuses it;
- Q5's one-statement check refuses the text.

The probe measures the grant, which DoD 2 already covers, not the binding.

**What to do.** Replace it with probes whose interpolated reading is *allowed* and *visible*:
- **Echo.** A saved report `SELECT @v::text AS v` (or `$1`, after Spike N.4). Run it with
  `x'); SELECT 1; -- @y $1 \' "`. The single cell equals the input exactly.
- **Effect.** A saved report `SELECT count(*) AS n FROM reporting.v_account WHERE display_name = @name`, over a seed of
  3 accounts. `nobody' OR 'a'='a` gives `n = 0`. The control, a real name, gives `n = 1`. Interpolated, it gives 3.
- **Type.** An `int` parameter given `1; DROP TABLE account` answers 400 with a named title *before* the database. Assert
  that no `pg_stat_activity` row with the report's marker ever appeared.
- **Literal `@`.** `SELECT '@name' AS lit, @name AS v -- @name` keeps `lit = '@name'`. That is Spike N.4 as a regression
  test.
- **Seen failing (recorded in the assessment):** a commit whose runner does `sql.Replace("@v", value)`. Echo and effect
  go red. The old `DROP TABLE` probe stays green, which shows why it was dropped.

### TST-10: SHOULD FIX (DoD 5). The `COMMENT` oracle must be read live, or a hard-coded copy passes

**Markers:** `INFERRED`.

**The defect.** "The documentation the page shows is the database's own `COMMENT` text" passes if the page or the endpoint
carries the text as a constant and the test compares it with the same constant.

**What to do.**
- **Live text.** In Integration, as the superuser: `COMMENT ON COLUMN reporting.v_account.display_name IS '<guid>'`.
  The schema endpoint returns `<guid>`. Restore it in `finally`. One E2E case does the same and reloads the page.
- **Every column documented.** Enumerate over `information_schema.columns` for `table_schema = 'reporting'`. Each column
  has a non-empty `col_description`, and each view an `obj_description`. Seen failing: `COMMENT … IS NULL` on one
  column, and that column is named.
- **Which login reads it.** Say which login the schema endpoint reads comments on. If Q4 narrows the reporting login's
  catalog access, the endpoint's test must run on that narrowed role.

### TST-11: SHOULD FIX (DoD 6, step 5). axe on an empty page proves little; name the states and the keyboard path

**Markers:** `SOURCE@644efe2` `tests/E2E/Axe.cs`, and the route-fulfil pattern `SectionFailureTests`, as slice 03 used it.

**What to do.**
- **axe, light and dark, in each state:**
  - empty;
  - results, including a truncated set and a 1-row set;
  - a refused query (`42501` text);
  - a timeout;
  - the saved list with a report's typed parameter inputs open;
  - the schema documentation expanded.
- **Produce states with `page.RouteAsync`.** Fulfil the run endpoint with the recorded problem and result bodies. The
  slow states are then deterministic and need no real slow query in a browser test.
- **Keyboard, asserted on `document.activeElement`:**
  - Tab *out of* the `<textarea>` reaches the Run button. A tab-to-indent editor is a keyboard trap (WCAG 2.1.2), and
    axe cannot see it.
  - Run by Enter or Space.
  - Focus lands on the result summary.
  - The summary is `role="status"`, with "first 5 rows shown, result cut off"; an error is `role="alert"`.
  - The results are a `<table>` with `<th scope="col">` from the column names.

### TST-12: MUST FIX (DoD 8, step 6). Move every check that can leave "by hand" into CI, and give the by-hand one an exact oracle

**Markers:**
- `SOURCE@644efe2`: `ci.yml:3-5` (E2E not in CI); `PublishedStackTests.cs:16-17,58-60` (skipped unless env vars are set);
  `AppModelTests.cs:39-50` evaluates the api's environment callbacks without starting the app.
- `INFERRED`: `DistributedApplicationTestingBuilder.CreateAsync` without `StartAsync` runs on CI's runner (gate GT-A).

**The defect.** DoD 8 is the only check of three things:
- that the AppHost gives the API the API login;
- that the Migrator creates the roles over an existing volume;
- that the passwords hold across a redeploy.

All three are by hand. "Passwords hold" has no oracle, and the obvious one, logging in, needs the password.

**What to do.**
- **In CI.** TST-1's idempotency tests cover the redeploy-over-existing-data logic.
- **App-model test** (`AppModelTests` style):
  - the api's `ConnectionStrings__portfolio` is a `ReferenceExpression` whose value providers include the
    `api-db-password` parameter and `Username=portfolio_api`, and **not** the postgres password parameter;
  - the reporting connection names the reporting parameter;
  - both parameters are `Secret` with a `GenerateParameterDefault`;
  - the Migrator receives both passwords.

  If GT-A holds, run it in CI (`dotnet test --project tests/E2E --filter FullyQualifiedName~AppModelTests`).
- **E2E (local).** As the superuser (`app.GetConnectionStringAsync("portfolio")`), after one API request:
  `SELECT DISTINCT usename FROM pg_stat_activity WHERE datname = 'portfolio_dev' AND pid <> pg_backend_pid()` contains
  `portfolio_api` and no `postgres` backend other than the test's own.
- **Published (by hand, D13).** A script runs `docker compose exec postgres psql -U postgres -f
  tests/sql/catalog-snapshot.sql` (TST-4's file) before the redeploy, after the redeploy, and after a second redeploy.
  Each output is diffed against `ExpectedPrivileges`. Passwords "hold" when
  `SELECT rolname, md5(rolpassword) FROM pg_authid WHERE rolname LIKE 'portfolio_%'` is equal across the two redeploys.
  That compares the hashes without seeing a password. Then the Api answers `/api/v1/accounts` signed in. Record all
  three outputs in the assessment.

### TST-13: SHOULD FIX (step 2, 4). Tests the draft does not name

**Markers:** `SOURCE@644efe2`. `NetWorthViewTests.cs:16-28` is the seed pattern. `data-model.md:291-297` lists the view
rules, including rule 4, the "stable contract".

**What to do:**
- **`v_balance_daily` rules.** A seed with one account holding:
  - a live row on 01-10;
  - a superseded pair on 01-20 (old and new);
  - a soft-deleted row on 01-25;
  - nothing from 01-11 to 01-19.

  The view returns exactly {01-10, 01-20 (new)}. Expected figures come from the seed, as `NetWorthViewTests` derives its
  own. Seen failing: a view without the `superseded_at` filter returns 3 rows. Run once through the endpoint, as the
  reporting login.
- **`v_net_worth_daily` equality, not vacuous.** On the `NetWorthFixture` seed, `count(*) > 0`, and `EXCEPT ALL` is empty
  in both directions. Then, through HTTP, the report `SELECT * FROM reporting.v_net_worth_daily WHERE as_of_date BETWEEN …`
  agrees cell by cell with the chart endpoint's JSON for the same range. On an empty database the `EXCEPT` alone passes
  trivially.
- **The stable contract.** A committed snapshot of `reporting`'s columns (name, type, ordinal), compared exactly.
  `reloptions` show `security_invoker` is not set on any reporting view: the reporting login relies on the owner's
  rights. Also, the four views exist after the full migration chain and after up-down-up (TST-1 c).
- **`saved_report`:**
  - created, read, updated and deleted over HTTP;
  - `SELECT current_user` saved as a report, and run ad hoc, both return `portfolio_reporting`, which shows reports run
    only on that login;
  - as the reporting login, `public.saved_report`, `public.audit_event`, `public.identity_user_passkey`,
    `public.app_setting`, `public.used_setup_code` and `public."__EFMigrationsHistory"` each give `42501` (TST-5's table).
- **Value fidelity in results:**
  - a 20-digit `numeric` with four decimal places, and `2^53 + 1` as an `int8`, both round-trip as strings, consistent
    with the API's decimal-string rule *(the probe's literals are described rather than quoted: the repository's
    hygiene hook reads a long digit run as an account number)*;
  - `SELECT '2026-01-10'::date, '2026-01-10 23:30Z'::timestamptz` gives exact ISO text. The two time-zone jobs then
    test this for free.
- **CSRF and the session on the new endpoints.** `AllowListTests.Bodies` already fails on any unsafe endpoint without a
  row. Add the rows (Confirmed).
- **If Q3 says yes:** add the new audit kind to `AuditShape`, with a delta-per-action test.

### TST-14: SHOULD FIX (step 3). Extend `SecretsStayOutTests` to the two new passwords, with a non-Postgres producer

**Markers:**
- `EXECUTED 2026-11-06`: a wrong reporting password gives `28P01: password authentication failed for user
  "portfolio_reporting"`. Neither the message nor `ToString()` contained the wrong password.
- `SOURCE@644efe2`: `SecretsStayOutTests.cs:111-118` (its 500 producer uses a passwordless string).

**The defect.** "Problem details that pass on PostgreSQL's message, never a connection string" is shown only if the test
produces failures that are *not* `PostgresException`. Connect refused, a timeout and an auth failure are where Npgsql's
messages carry host, port and user. The current secret list has no database password.

**What to do.**
- **Secrets.** Add both login passwords to `secrets`.
- **Producers, each through the endpoint:**
  - `42501` (refused);
  - `57014` (timed out);
  - `42601` (syntax);
  - `22P02` (a bad typed parameter);
  - a reporting data source with the wrong password (`28P01`);
  - one pointed at a closed port.
- **Assert on the bodies.** A `PostgresException` passes `MessageText` and `SqlState` only, never `Detail` or `Where`
  unless Spike L says otherwise. Anything else gets a fixed title. No body, log or span contains `Password=`, either
  password, the reporting port, or `Username=`.
- **A parameter canary.** Use a canary as a saved-report parameter value, and keep G-24's check that parameter logging is
  off.

### TST-15: SHOULD FIX (step 3, 4). Contract and architecture: keep the scans non-vacuous, and test "which login" by behaviour, not by grep

**Markers:** `SOURCE@644efe2`: `NoCredentialInResponsesTests.cs:28-30` (the operation superset); `ci.yml:97-101`
(Contract is advisory); round 2 TST-3, where slice 03 learned that a name grep is vacuous.

**What to do.**
- Add the new operation ids (run, list/get/create/update/delete saved report, schema) to the `Assert.Superset` in
  `NoCredentialInResponsesTests`.
- Commit the regenerated `portfolio-api.json` and `schema.d.ts`.
- **Do not** write an Architecture grep for "only X references the reporting connection string". Use TST-2's and TST-13's
  `current_user` tests: the DbContext is `portfolio_api`, and the run endpoint is `portfolio_reporting`. Those fail
  whichever way the wiring is wrong.
- The one structural test worth having: Migrator receives the passwords but no reporting connection string. Read it
  from the app model (TST-12).

### TST-16: NOTE (step 3). Concurrency has no bound in the DoD

`INFERRED`. N concurrent long reports hold N reporting connections and N request threads. Either the reporting pool's
`Maximum Pool Size` is the bound, and the N+1th run gets a prompt, named refusal rather than a `Timeout` wait on the
pool, or this is recorded as accepted. If it is a bound, test it with N+1 parallel `pg_sleep(600)` producers and TST-7's
clean-up.

### TST-17: NOTE (Spike L.4). Predicate pushdown into the filtering views

`EXECUTED 2026-11-06`: as the reporting login, `WHERE note::int = 1` and a `pg_temp` `RAISE NOTICE` function of cost
1e-7, over a view filtering `deleted_at IS NULL`, saw only the live row's value. So the simple case does not leak.
Keep one such probe in TST-5's table (a cast error and a temp-function notice over `v_balance_daily`). It costs one row,
and it turns red if a later view or index lets the planner evaluate user predicates first. Whether to declare the views
`security_barrier` is DBS's call.

---

## Proposed DoD rewrite

1. **The API's own login.** In Integration, the app's `PortfolioDbContext` connects as `portfolio_api`: `rolsuper` is
   false, `pg_shdepend` shows it owns nothing, and its privileges equal the committed `ExpectedPrivileges.Api`. On that
   connection, `DISABLE TRIGGER`, `DROP TRIGGER`, `OWNER TO`, `SET session_replication_role`, `UPDATE`, `DELETE` and
   `TRUNCATE` on `audit_event`, `used_setup_code` and `ingest_run` each fail with `42501` and the recorded message.
   **Seen failing:** the same statements succeed as the owner in a rolled-back transaction, and the test goes red when
   handed the superuser string. The roles come from the committed migration and `DatabaseLogins.EnsureAsync`. A process
   test of the built Migrator produces the same catalog snapshot. `G-7` is closed, and `security.md` "Audit" says so.
2. **The reporting login holds exactly its grant.** `CatalogSnapshot(portfolio_reporting)` equals the committed
   `ExpectedPrivileges.Reporting` (Spike L's script, each `PUBLIC` default kept or revoked by name). **Seen failing:** a
   planted column grant and a planted default ACL are each flagged. Every probe in the refusal table fails through the
   endpoint with its expected SQLSTATE, and passes as the owner. After the write battery, the catalog, the large objects,
   ownership and every table's count and `xmin` are unchanged. **Seen failing:** `lo_from_bytea` while `EXECUTE` is
   still granted.
3. **Bounded.** Every row of the producer table (sleep after `SET statement_timeout = 0`, `set_config`, streaming,
   materializing, CPU, recursive, wide row) ends as its stated outcome: timed out, truncated, or refused for size. Each
   backend is first seen `active` by pid, then not `active` within 10 s. The cap's boundary is n = cap−1, cap, cap+1,
   with `truncated` false, false, true. A `SET` in one run is not visible to the next run on the same physical
   connection. **Seen failing:** reader disposal without `Cancel()` turns the streaming row into a timeout;
   `No Reset On Close=true` fails the session test. The Production bound, cap and byte limit are pinned by their own test.
4. **Bound parameters.** The echo report returns the hostile value byte for byte. The effect report returns `0` for
   `nobody' OR 'a'='a`, and `1` for the control. A wrongly typed value answers 400 before the database. A literal `@name`
   survives. **Seen failing:** an interpolating runner turns echo and effect red, recorded in the assessment.
5. **Live documentation.** A `COMMENT` changed at test time appears in the schema endpoint and on the page. Every
   `reporting` column and view has one. **Seen failing:** a nulled comment is named.
6. **The page.** axe is clean in light and dark in each named state: empty, results, truncated, refused, timed out,
   saved list with parameters, schema open. The keyboard path is asserted on `activeElement`, with no trap in the editor
   and the result announced by `role=status`, or `role=alert` for an error.
7. **Nothing regressed, for the right reason.** Every Integration site that touches a real database chooses a login
   explicitly. The domain tests run on `portfolio_api`. Slices 01–03's DoD items hold. CI is green, including both
   time-zone jobs, which now assert exact date and time text in results. The E2E is green locally, and shows
   `portfolio_api` in `pg_stat_activity`.
8. **Published.** The app-model test shows the api's connection uses the API login's parameter, and not the postgres
   password; in CI if GT-A holds. By hand on a deploy over slice 03's volume:
   - the catalog-snapshot file's output equals `ExpectedPrivileges` before and after two redeploys;
   - `md5(rolpassword)` is unchanged across them;
   - the API is signed in and working;
   - one refusal probe holds in the container.

   All of it is recorded with date and commit (D13).
9. Each obligation is done, or recorded as not done with a reason. Each refusal in "The boundary" is marked **shown**,
   with its test name, or **argued**.

## Gates (INFERRED claims this seat rests on)

| Id | Claim | Finding | Raised by |
|---|---|---|---|
| GT-A | `DistributedApplicationTestingBuilder.CreateAsync` without `StartAsync` runs on CI's ubuntu runner | TST-12 | One CI run of `AppModelTests` |
| GT-B | `DROP ROLE` in a migration's `Down` fails while the role holds privileges elsewhere | TST-1 | TST-1 (c) itself |
| GT-C | A 200 MB single cell reaches the Api's memory whole, under a row cap | TST-8 | The wide-row producer, with the Api's working set read before and after |
| GT-D | Npgsql's cancel-then-dispose holds for multi-result text and the `@`-rewrite path, not only the single statement probed | TST-6 | TST-6's table run against the endpoint |

## Confirmed (do not re-check)

- **Refusal SQLSTATEs on `postgres:18.3`:**
  - a non-owner's `DISABLE` and `DROP TRIGGER` give `42501` ("must be owner of table/relation audit_event");
  - `UPDATE` without the grant gives `42501` before the trigger runs;
  - `SET session_replication_role` gives `42501`;
  - `COPY … TO PROGRAM` gives `42501`;
  - `SELECT … FOR UPDATE` on a view gives `42501`;
  - a missing relation gives `42P01`;
  - `DROP` in a read-only transaction gives `25006`.

  (`EXECUTED 2026-11-06`)
- **`PUBLIC` leaves open, for a role granted only `USAGE` + one view's `SELECT`:**
  - `USAGE` on `public`; `CONNECT` on every database; `TEMP` (temp tables *and* temp functions created);
  - `EXECUTE` on the `lo_*` writers, `nextval`, `setval`, `set_config`, `pg_sleep`, `pg_notify`, the advisory locks,
    `pg_cancel_backend`, `pg_terminate_backend`, and the app's trigger function;
  - **a large-object write succeeds**.

  `pg_read_file`, `pg_ls_dir`, `lo_import`, `lo_export` and `pg_reload_conf` are refused. `CREATE` on `public` is false
  (PG15+). (`EXECUTED`)
- **`has_table_privilege` misses column grants; `has_any_column_privilege` sees them.** `pg_shdepend` lists ACL entries and
  ownership across databases, large objects included. (`EXECUTED`)
- **Npgsql 10.0.3:**
  - `CommandTimeout` stops a quiet server query (`pg_sleep`) after `SET statement_timeout = 0`;
  - disposing a reader mid-stream drains, with no timeout (4 min observed);
  - `Cancel()` before dispose stops it at once;
  - multi-statement text runs every part;
  - the pool reset restores `search_path`;
  - `@x` in a literal or a comment is left alone;
  - a `28P01` message names the user, not the password.

  (`EXECUTED`)
- **`pg_stat_activity`:** the poller matches its own text, and an idle backend keeps its last query's text. (`EXECUTED`)
- **The harness:**
  - `AllowListTests` enumerates `EndpointDataSource` and fails on an unsafe endpoint with no body row, so the new POSTs
    are forced into the CSRF check;
  - the `OwnerCommandsTests` process pattern and `MigrationOnRowsTests`' up-down-up pattern carry straight over to the
    Migrator and the new migration;
  - `AppModelTests` already evaluates the api's environment callbacks without starting the app;
  - `Axe.AssertCleanAsync` plus a `ColorScheme` context covers both schemes.

  (`SOURCE@644efe2`)
