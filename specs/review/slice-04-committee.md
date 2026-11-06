# Slice 04 review — the commission draft (`slice-04.md` at `644efe2`)

**Date:** 2026-11-06. **Under review:** `specs/001-portfolio-platform/slice-04.md` as merged
in #103, a first draft.

**Seats (4)**, run in parallel. Each seat file under `slice-04-seats/` is the evidence:

| Seat | Ground | File |
|---|---|---|
| DBS | Database security: tried to break both new logins in a `postgres:18.3` container limited to 256 MB, as the published stack is | `dbs.md` |
| SRC | Source: Aspire 13.6.4, Npgsql 10.0.3, EF Core 10.0.14, read at those tags and probed | `src.md` |
| TST | Test design: can each DoD item fail, and for the right reason | `tst.md` |
| SCO | Scope and cost, against slices 01–03 | `sco.md` |

The first DBS agent was stopped by a safety check while writing. A second agent re-ran its
scripts and finished the file. DBS-1 is the first agent's text; DBS-2 to DBS-8 are the
second's.

**Raw findings:** 49 (BLOCKING 4, MUST FIX 20, SHOULD FIX 17, NOTE 8).
**Consolidated:** 18 themes below, `C4-1` to `C4-18`.

---

## The headline

**The draft's central claim is false as written: "roles and grants" cannot bound user SQL
on their own.** Three seats showed it independently. A reporting login with only `SELECT` on
the views could still do all of the following:

- **Change its own password and settings.** No privilege governs `ALTER ROLE` on oneself
  (DBS-1, `EXECUTED`).
- **Write large objects** that persist (DBS-1, TST-5, `EXECUTED`).
- **Disarm the timeout** with a second statement. Npgsql splits the text and runs every
  part (SRC-3, `EXECUTED`). Or with `set_config` inside one `SELECT`, which then carries over
  on a pooled connection to the next run (DBS-3, `EXECUTED`).
- **Run past both bounds while streaming.** `CommandTimeout` re-arms on every read, and
  disposing the reader drains the whole result (SRC-1, SRC-2, TST-6, `EXECUTED`).
- **Crash-restart PostgreSQL** with a single `string_agg`, under the 256 MB limit (DBS-2,
  `EXECUTED`).

**The boundary that does hold** has six parts. Every one is executed in a seat file:
1. **The grants.**
2. **A read-only transaction** the app opens.
3. **Exactly one statement,** refused server-side when the text is sent as a one-command
   `NpgsqlBatch` (`42601`), so nothing is parsed.
4. **A fresh, unpooled connection** per run, ending in `ROLLBACK`.
5. **A wall-clock deadline plus `cmd.Cancel()`,** which ends the server-side query.
6. **Row and byte caps** counted on the client.

Memory alone is not bounded by anything the slice controls (decision S4-1).

**The API login needs more than the draft says, and less than it has.** It updates and
deletes in place (SRC-5, SCO-1): restating a balance, every sign-in and sign-out, and
`reset-owner`. Today's `WithReference(db)` also hands it the superuser password (SRC-6). And
if it owns the database, it can drop the audit table outright (DBS-6). DoD 1's two tests
would then still pass.

**The tests would test themselves.** `PostgresFixture` migrates on its own and never runs
the Migrator, so refusal tests would only check the fixture's own role setup (TST-1).

**Cost (SCO):** about **14–15 logged / 9–10 measured agent hours** with this review,
roughly slice 03's size. Step 1 is the step to watch.

---

## Consolidated findings and proposed dispositions

**Severity** is the highest any seat gave. **Disp.**: FIX (apply to `slice-04.md`), ACCEPT
(state it, do not build), DEFER (with a trigger), or RULE (one of the decisions below).

| # | Theme | From | Sev. | Disp. | What changes |
|---|---|---|---|---|---|
| C4-1 | The boundary is more than grants | DBS-1, SRC-3, DBS-3, DBS-5, Q5 | BLOCKING | FIX | Restate the technique and the boundary as the six parts above. Every run: fresh unpooled connection; `BEGIN TRANSACTION READ ONLY`; `SET LOCAL statement_timeout`; the user's text as a one-command `NpgsqlBatch` (multi-statement refused by the server, `42601`); `ROLLBACK`; dispose. Targeted `REVOKE`s: `lo_create`, `lo_creat`, `lo_from_bytea`; `TEMPORARY` on the database; `USAGE` on `plpgsql`; `CONNECT` on `postgres`. Say no grant refuses `ALTER ROLE` on oneself. Q5's answer is yes, and it is part of the boundary |
| C4-2 | Bounds that the query cannot lift | SRC-1, SRC-2, TST-6, TST-7, TST-8, DBS-2 | BLOCKING | FIX | One wall-clock token across the whole run, and `cmd.Cancel()` before the reader is disposed. Row **and** byte caps counted on the client while reading, then cancel; "cut off" says which. Role defaults (`statement_timeout`, `transaction_timeout`, `idle_in_transaction_session_timeout`, `temp_file_limit`) set by the Migrator as a backstop only; `temp_file_limit` is the one that holds. Tests stream from `generate_series` so the result arrives row by row, and observe the server query by pid, present and then gone (TST-7) |
| C4-3 | One query can crash-restart PostgreSQL | DBS-2 | MUST FIX | RULE (S4-1) | Memory is not bounded by any setting the slice controls. Committed data survives crash recovery; the API's open transactions do not |
| C4-4 | The API login: what it needs, and "owns nothing" made exact | SRC-5, SCO-1, DBS-6, DBS-7, TST-3 | BLOCKING | FIX | A grant matrix over the 13 tables, derived from the code paths (balance supersession `UPDATE`, Identity's `UPDATE` and `DELETE`, `reset-owner`). The two host commands run as this login too. "Owns nothing" means: not superuser or `CREATEROLE`; not owner of the database, of `public` or of any object; not in `pg_database_owner`. DoD 1 tests: `DISABLE`/`DROP TRIGGER`, `DROP TABLE audit_event`, `DROP FUNCTION append_only() CASCADE`, `TRUNCATE`, `ALTER DATABASE`, each seen failing as the owner; a positive control on the app's own connection; the exact `42501` |
| C4-5 | `WithReference(db)` leaks the superuser password to the API | SRC-6, SRC-8 | MUST FIX | FIX | Remove it from the API; set `ConnectionStrings__portfolio` from a `ReferenceExpression` over host, port and the API's parameter (survives publish, `EXECUTED`). Register the reporting `NpgsqlDataSource` keyed, so it can never become EF's connection |
| C4-6 | Role passwords | SRC-7, TST-14 | MUST FIX | FIX | `AddParameter(…, GenerateParameterDefault { Special = false }, secret: true, persist: true)` for both: without `persist` they regenerate on every `aspire run`. The Migrator creates missing roles and sets each password **on every run**, on a plain `NpgsqlCommand` outside EF's logger (EF logged the literal, `EXECUTED`), preferably as a client-computed SCRAM verifier. Tests: the Migrator's log, and `SecretsStayOutTests`, never contain either password |
| C4-7 | Tests must run the real provisioning | TST-1, TST-2, SCO-6 | BLOCKING | FIX | One provisioning routine in `Infrastructure` (create the roles if missing, apply the whole grant script idempotently, set the passwords), called by the Migrator after every migration run **and** by the Integration fixture, so the tests exercise the code production runs. One test runs the built Migrator and compares the catalog with the committed expected set. Integration seeds and asserts as superuser but drives the API as `portfolio_api`. Step 1 splits into 1a (provisioning and its tests, API unchanged) and 1b (the switch), each merged green |
| C4-8 | Grants for tables added later | DBS-7, SCO-7, TST-4 | MUST FIX | FIX | The grant script names every object explicitly (no `ALTER DEFAULT PRIVILEGES`), and C4-7's routine re-applies it on every Migrator run, so a new migration's table is granted in the same run. A missing grant fails the existing suites; an extra one fails the enumeration test. A catalog enumeration test, over every table, view, schema, sequence and function, with a committed expected set per login, and a planted extra grant to show it can fail |
| C4-9 | Probes that pass for the wrong reason | TST-5, TST-9, TST-10, TST-3 | MUST FIX | FIX | Every refusal asserts its SQLSTATE; a misspelt table (`42P01`) must not count. Writes are judged by effect: `lo_from_bytea` succeeded with no error. The binding probe uses a value the grant **allows**, so only binding can explain the result. `COMMENT` text is read live from the catalog, never from a copy |
| C4-10 | Views can leak what they filter | DBS-4, TST-17 | SHOULD FIX | RULE (S4-5) | A user's function in the `WHERE` saw soft-deleted and superseded rows through a plain view (`EXECUTED`) |
| C4-11 | Saved-report parameters | SRC-4, TST-9 | SHOULD FIX | FIX | Positional `$n` on the batch path. Npgsql's `@name` rewrite also rewrites `:name`, and an unmatched `@x` runs as an operator (`EXECUTED`). Spike N 4 is then answered |
| C4-12 | Missing pieces in the build | SCO-3, SCO-5, SRC-9 | MUST FIX | FIX | A schema-documentation route, read on the reporting login and in the contract (FR-6.6). A typing rule for results of unknown shape: `numeric` returned as a string, so money keeps its digits. The reader for arbitrary SQL specified |
| C4-13 | The document's own method | SCO-2, SCO-4, SCO-9, SCO-10, SCO-11, SCO-14, SCO-15 | MUST FIX | FIX | Restore slice 03's gate text: the 200-hour limb and "Explicitly NOT reasons to continue". Name the techniques narrowly (SCO's candidates 1–3), and flag the overlap with slice 02's "immutable at the database". Add a spike-to-step map. Name the frozen documents, and correct `data-model.md`'s `app` schema (the tables are in `public`). Say what the role split does to `D5` (a dump now carries roles) and `D17`. Restore slice 03's DoD conventions. Give the assessment a figure: SCO's estimate |
| C4-14 | The published stack, and the runbook | TST-12, SCO-8 | MUST FIX | FIX | Move every check that can leave "by hand" into CI (the Migrator-run catalog test of C4-7 covers most). The by-hand published run gets an exact oracle. The README runbook covers three logins, their passwords, and recovery |
| C4-15 | What the catalog shows the reporting login | DBS-8, Q4 | NOTE | RULE (S4-4) | Table and view names, view definitions, role names and their settings, and row counts of tables it cannot read |
| C4-16 | Tests the draft does not name | TST-13, TST-11, TST-15 | SHOULD FIX | FIX | The view rules; `reporting.v_net_worth_daily` equal to the chart's view; `saved_report` written by the API login only; the reporting login cannot read `saved_report` or `audit_event`; axe on named states (empty, results, cut off, error) and the keyboard path; contract and architecture scans that cannot pass empty |
| C4-17 | Concurrency has no bound | TST-16 | NOTE | RULE (S4-6) | Several runs at once each get the memory budget of C4-3 |
| C4-18 | Bounds as configuration; `PUBLIC` revokes kept targeted | SCO-12, SCO-13 | NOTE | FIX | The bounds come from configuration, so tests set them small and fast. Revoke only the named functions: revoking all of `PUBLIC`'s `EXECUTE` breaks ordinary SQL |

---

## Decisions for the owner

| # | Question | Options | Proposed |
|---|---|---|---|
| S4-1 | One report can crash-restart PostgreSQL (C4-3) | (a) Accept it in writing: for one local user it is availability, not integrity, narrowed by a short deadline and the caps. (b) Raise the container's memory limit, which only moves the threshold. (c) A separate PostgreSQL instance for reporting | **(a)**, with a test that the API serves the next request after a crash |
| S4-2 | The bounds' values | DBS: 10 s, 10,000 rows, 8 MB, `temp_file_limit` 256 MB. SCO: 5 s, 1,000 rows | **10 s, 10,000 rows, 8 MB, 256 MB**, from configuration; `testing.md` names them |
| S4-3 | Audit report runs? (Q3) | (a) Defer with `D17`, to real data. (b) Record kind, saved or ad hoc, rows, truncated, duration; never the SQL text | **(a)** |
| S4-4 | What the catalog shows (Q4, C4-15) | (a) Accept names and definitions, state them in `security.md`, revisit before the first real balance. (b) Narrow the catalog, which is brittle | **(a)** |
| S4-5 | Views and leaky functions (C4-10) | (a) Every `reporting` view `WITH (security_barrier)`, with a catalog test that none skips it. (b) Accept: the hidden rows are the owner's own | **(a)**: cheap at this size, and it makes the view rules a boundary |
| S4-6 | Concurrent runs (C4-17) | (a) One run at a time; a second waits or gets `429`. (b) No limit | **(a)** |
| S4-7 | Scope | (a) As ruled. (b) SCO's middle cut: saved reports create, list, run and delete only, with `date`, `text` and `numeric` parameters; no sorting or paging; a plain schema list. Saves about 1 h logged | **(b)**: the technique is untouched |
| S4-8 | The spikes | (a) Run L, M and N as drafted. (b) L and N are answered by this review (`EXECUTED` in the seat files); their results become step tests. Only Spike M's deploy questions remain, run in step 1a and step 6 | **(b)** |

### Ruled, 2026-11-06

Kyle took every proposal, each chosen from the options above:

| # | Ruling |
|---|---|
| S4-1 | **Accept the memory risk in writing.** A short deadline and the caps narrow it; a test shows the API serves the next request after a crash |
| S4-2 | **10 s, 10,000 rows, 8 MB of result, `temp_file_limit` 256 MB**, from configuration |
| S4-3 | **Report runs are not audited here.** Deferred with `D17`, to real data, alongside `saved_report` mutations |
| S4-4 | **Accept what the catalog shows**, stated in `security.md`, revisited before the first real balance |
| S4-5 | **Every `reporting` view `WITH (security_barrier)`**, with a catalog test |
| S4-6 | **One run at a time** |
| S4-7 | **The middle cut**: saved reports create, list, run and delete; `date`, `text` and `numeric` parameters; no sorting or paging; a plain schema list |
| S4-8 | **Spikes L and N folded into the build** as tests; Spike M's deploy questions run in steps 1a and 6 |

Every FIX in the table above is applied to `slice-04.md`.

---

## Confirmed by the seats (do not re-check)

- `CREATE ROLE` has no `IF NOT EXISTS` form, and a password cannot be a bind parameter
  (SRC, `EXECUTED`, `42601`).
- Npgsql's cancel reaches the server: the query leaves `pg_stat_activity`, and the
  connection stays usable (SRC, `EXECUTED`).
- A `ReferenceExpression` connection string survives `aspire publish` (SRC, `EXECUTED`).
- Generated parameters persist across deploys in deploy state (SRC, `SOURCE`; `G-26`).
- A view reads its tables with its owner's rights when `security_invoker` is off, so the
  reporting login needs no table grants (DBS, `EXECUTED`).
- Inside one read-only transaction and one statement, `ALTER ROLE`, `lo_*` and
  `CREATE TEMP TABLE` are refused (DBS, `EXECUTED`).
- Comments and string literals do not confuse Npgsql's parameter rewrite (SRC, `EXECUTED`).
- Seat-specific lists are in each seat file's "Confirmed" section.
