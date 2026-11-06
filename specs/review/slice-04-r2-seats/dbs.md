# Slice 04, round 2: database security seat (R2-DBS)

**Seat:** Database security, round 2. **Date:** 2026-11-06. **Under review:** `specs/001-portfolio-platform/slice-04.md` on `specs/slice-04-review` (the rewrite, after `213f49f`), against round 1 (`../slice-04-committee.md`, `../slice-04-seats/dbs.md`, `../slice-04-seats/src.md`) and rulings `S4-1`..`S4-8`.

**Read:**
- `slice-04.md` (all of it); `slice-04-committee.md`; round-1 `dbs.md` and `src.md`.
- Repo, for the API's write paths and the objects the views sit on: `src/Infrastructure/BalanceWrites.cs:52-59`, `src/Infrastructure/ImportPipeline.cs:95-142`, `src/Infrastructure/Migrations/*` (functions, triggers, FKs, `v_net_worth_daily`), `tests/Integration/PostgresFixture.cs`.

**Ran** (scratch `…\scratchpad\s4r2-dbs\`, nothing in the repo):
- My own container `s4r2-dbs-pg`: `postgres:18.3`, `--memory=256m`. Removed at the end.
- `setup2.sql`: round 1's `setup.sql` with every `reporting` view `WITH (security_barrier)`, plus `used_setup_code`, `saved_report`, a sequence, the rewrite's four targeted `REVOKE`s, and role defaults `statement_timeout=10s`, `temp_file_limit=256MB` on the reporting login.
- Every reporting probe is run the way step 3 runs a query: `BEGIN TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'; <one statement>; ROLLBACK;` on a fresh connection as `portfolio_reporting`. Scripts are named per finding.

---

## Findings

**In short:** the six parts hold for everything round 1 found, and for most of what I added (see Confirmed). One built-in function still writes past the read-only transaction, which makes "any write, by any route" false as stated. A password can also reach the server log during provisioning. The rest are notes.

### R2-DBS-1: `pg_logical_emit_message` writes to the WAL from inside the read-only transaction, and `PUBLIC` can execute it
**MUST FIX (step 2, DoD 2; "The boundary, stated", first bullet).**
**Where:** "The boundary, stated" (`:147-150`), "any write, by any route"; "Revoked from `PUBLIC`, by name" (`:166-168`).

**The defect:**
- `EXECUTED` (`p1.out`): as `portfolio_reporting`, inside `BEGIN TRANSACTION READ ONLY; SET LOCAL statement_timeout`, the one statement `SELECT pg_logical_emit_message(false,'p','x')` succeeded and returned a WAL position (`0/1C1A720`). So did the `transactional = true` form.
- `DOCS@2026-11-06 https://www.postgresql.org/docs/18/functions-admin.html` (replication functions): a non-transactional message is written to the WAL immediately, whether or not the transaction commits. So `ROLLBACK`, the sixth part, does not undo it.
- `EXECUTED`: both overloads in PG 18 are `(boolean,text,text,boolean)` and `(boolean,text,bytea,boolean)`. After `REVOKE EXECUTE … FROM PUBLIC` on both, the same statement fails with `42501 permission denied for function pg_logical_emit_message`.
- Impact here is low. The WAL is recycled at checkpoints, nothing decodes it (no logical slot), and the 10 s deadline limits the volume. But it is a write that persists past `ROLLBACK`, and the slice claims there are none.

**What to do:**
- Add both `pg_logical_emit_message` overloads to the targeted revoke list. That makes five function revokes, still by name (`SCO-13`).
- Narrow the claim to what is shown: "any write, by any route **we tested**", and list the routes in DoD 10.
- Step 2 test, seen failing first: while the grant is still in place the statement returns an LSN. After the revoke it fails with `42501`.

### R2-DBS-2: a failed password statement writes the plaintext password to PostgreSQL's log
**SHOULD FIX (step 1a; `C4-6`).**
**Where:** "Provisioning" (`:78-87`): "sent as a quoted literal, **or preferably** as a client-computed SCRAM verifier".

**The defect:**
- `EXECUTED`: as `postgres`, `ALTER ROLE no_such_role PASSWORD 'PROBE-SECRET-1'` failed, and `docker logs` then held `STATEMENT: ALTER ROLE no_such_role PASSWORD 'PROBE-SECRET-1'`. The reason is `log_min_error_statement = error`, the image's default (`EXECUTED`, `SHOW`).
- So keeping the literal out of EF's logger (`SRC-7`) is not enough. Any failure of that statement puts the password into the container log: a race on first create, a typo in the role name, a lock timeout. The container log is what `docker compose logs` shows.
- `INFERRED`: with a SCRAM verifier, a log line holds the verifier and not a password. A verifier cannot be used to log in directly.
- Other roles cannot read the statement while it runs. `pg_stat_activity` shows other roles' `query` as `<insufficient privilege>` (round-1 `DBS-8`, re-seen here in `p2.out` for the API's session).

**What to do:**
- Make the SCRAM verifier the rule, not the preference. Or set `SET LOCAL log_min_error_statement = panic` on the provisioning connection before the password statement. As `postgres` it can.
- Create the role without a password (`CREATE ROLE … LOGIN`, catching `42710` for the race), then `ALTER ROLE … PASSWORD`, so the one statement carrying the secret is the one least likely to fail.
- Step 1a test, alongside "the Migrator's log contains neither password": make the password statement fail on purpose (a wrong role name in a test seam), then assert the PostgreSQL container's log does not contain the password.

### R2-DBS-3: "owns nothing" and `ExpectedPrivileges` must be read as effective privileges, and `EnsureAsync` must re-assert an existing role's attributes
**SHOULD FIX (step 1a, DoD 1 and 2).**
**Where:** the login table (`:64-65`); step 1a's enumeration test (`:224-226`); "creates each login if it is missing" (`:79-80`).

**The defect:**
- `DOCS@2026-11-06 https://www.postgresql.org/docs/18/infoschema-role-table-grants.html`: `information_schema.role_table_grants` lists only grants whose grantor or grantee is a currently enabled role. Privileges that come from `PUBLIC`, or from membership in a predefined role (`pg_read_all_data`, `pg_write_all_data`, `pg_signal_backend`), are not listed there. Round-1 `DBS-7` proposed exactly that view as the source.
- R2-DBS-1 is a live case. A `PUBLIC` `EXECUTE` would never appear in a grants-table snapshot.
- The "owns nothing" list names `SUPERUSER` and `CREATEROLE` only. `CREATEDB`, `REPLICATION` and `BYPASSRLS` are left out, and so are memberships. `INFERRED`: `pg_database_owner` cannot have explicit members, so the `datdba` check is what covers it.
- "Create if missing" leaves a role that already exists exactly as it is. A role left on a reused volume with a stronger attribute or a membership keeps it. Only the by-hand step 6 snapshot would see that. `INFERRED`.

**What to do:**
- Build the snapshot from `has_table_privilege`, `has_column_privilege`, `has_sequence_privilege`, `has_schema_privilege`, `has_database_privilege` and `has_function_privilege` for each login over the objects in `public` and `reporting`. For functions, compare against the targeted-revoke list, not the whole catalog. Add the role's attributes, and assert that `pg_auth_members` has no row for the role.
- In `EnsureAsync`, on every run: `ALTER ROLE … LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS`. Fail, or revoke, if the role is a member of anything.
- The planted-extra test should include one `GRANT pg_read_all_data TO portfolio_reporting` (overlap with TST).

### R2-DBS-4: round 1's advisory-lock fix (`DBS-5`) was dropped without being accepted
**NOTE (the "does not refuse" list).**
**Where:** "Running a query", part 1 (`:109-110`); "It does not refuse" (`:156-164`).

**The defect:**
- The fresh connection releases a session lock when the run ends, and the rewrite says so. Round 1's revoke of the `pg_*advisory*` family is gone, and it is not in the accepted list either.
- So for up to the deadline, one report can hold the API's enrolment key. `EXECUTED` round 1 (`DBS-5`): the API's `pg_advisory_xact_lock` then waits. `EXECUTED` here (`p2`): the API login's `pg_advisory_xact_lock` still works after the four revokes, so revoking the family and granting back that one overload remains possible.

**What to do:** either add it to "does not refuse" ("holds an advisory key or `ACCESS SHARE` locks on the base tables for up to the deadline"), or restore `DBS-5`'s revoke. Accepting it is proportionate.

### R2-DBS-5: the "does not refuse" list misses a few things it does not refuse
**NOTE (`S4-4`; DoD 10).**
**The defect:**
- `EXECUTED` (`p2.out`): the reporting login reads other sessions' `usename`, `state` and `wait_event` from `pg_stat_activity` (`query` stays hidden), and reads `pg_locks` (`p1.out`).
- `EXECUTED` (`p2`): `CONNECT` on `template1` is still `PUBLIC`'s. The revoke names only `postgres`. A run cannot open a connection by itself, so this matters only if the reporting password leaks.
- `EXECUTED` (`p1.out`): `pg_notify` and `pg_export_snapshot` succeed. `DOCS@2026-11-06 sql-notify.html`: a notification is delivered only on commit, so `ROLLBACK` discards it.
- `EXECUTED` (`p1.out`): `PREPARE TRANSACTION 'x'`, as the one statement, is refused only because `max_prepared_transactions = 0`. It would otherwise end the read-only transaction and keep it past the session. `INFERRED`.
- `INFERRED` (from round-1 `r3`): a single `SET TRANSACTION READ WRITE` or `COMMIT` is harmless only because nothing follows the user's statement except `ROLLBACK`. The six parts do not say that.

**What to do:**
- Add session metadata and lock visibility to "Seeing names in the catalog".
- Revoke `CONNECT` on `template1` as well, or say why not.
- Put `max_prepared_transactions = 0` in the catalog snapshot.
- Add to part 6: "the user's statement is the last one before `ROLLBACK`". Read the backend pid from the connection (`ProcessID`), not by a statement after it (overlap with TST-7).

---

## Round-1 findings: applied correctly?

- **DBS-1:** applied. The read-only transaction, one statement and targeted revokes are all present. The revoke list is incomplete by one function (R2-DBS-1).
- **DBS-2:** applied. Memory is accepted (`S4-1`), `temp_file_limit` is named as the one bound that holds, and there is a byte cap.
- **DBS-3:** applied. A fresh unpooled connection, `ROLLBACK`, and the `set_config` test.
- **DBS-4:** applied. Barrier views, the leak probe seen failing, and the catalog test. Not re-run here: the reporting login cannot create the probe function (`DO` and `TEMP` are refused, `p1.out`), so the fixture-made function is the right test.
- **DBS-5:** **lost.** The fix was dropped and not accepted (R2-DBS-4).
- **DBS-6:** applied. "Owns nothing" and DoD 1's list. It lacks the attributes and memberships (R2-DBS-3).
- **DBS-7:** applied. Matrix, column grant, re-applied every run. The enumeration source needs to change (R2-DBS-3).
- **DBS-8:** applied in substance (`S4-4`, `CONNECT` on `postgres`). `template1` was missed (R2-DBS-5).
- **SRC-1, SRC-2:** applied (cancel before dispose; one wall-clock deadline). `SRC-2`'s question about a broken connection leaving the backend running is covered by `SET LOCAL statement_timeout`. Leave it as is.
- **SRC-3, SRC-4:** applied (one-command batch; positional `$n`).
- **SRC-5:** applied. `saved_report` correctly loses `UPDATE` under the middle cut. `ingest_run` really is insert-only (`SOURCE@213f49f ImportPipeline.cs:95-142`).
- **SRC-7:** applied. The verifier should be required (R2-DBS-2).
- **SRC-8:** applied as "keyed". The half saying the API refuses to start without `ConnectionStrings:portfolio` is **lost** (NOTE).
- **SRC-9:** applied (typing rule, `numeric` as a string).

## Confirmed (do not re-check)

All `EXECUTED` on `postgres:18.3` in a 256 MB container, as one statement inside `BEGIN TRANSACTION READ ONLY; SET LOCAL statement_timeout`, as `portfolio_reporting` with the rewrite's grants and revokes (`p1.out`, `p2.out`):
- `query_to_xml` cannot carry a second statement out of the transaction: `SET TRANSACTION READ WRITE` and `ALTER ROLE` give `0A000` ("not allowed in a non-volatile function"); `COMMIT` gives "invalid query"; `set_config('transaction_read_only','off')` gives `25001`.
- Refused: `nextval` on a sequence (`42501`); `LOCK … ROW EXCLUSIVE` and `FOR KEY SHARE` on a view (`42501`); `LOAD` (`42501`); `DO` (`42501`, `plpgsql`); `CREATE TEMP TABLE` (`25006`); `lo_from_bytea` (`42501`); `pg_current_logfile` (`42501`); `pg_terminate_backend` of the API's backend ("permission denied to terminate process").
- The four revokes do not break the API login. Its `INSERT`, `INSERT … RETURNING` on an identity column, the column-grant `UPDATE … WHERE id = ANY(…)` (the `ExecuteUpdateAsync` shape) and `pg_advisory_xact_lock` all succeed. The `plpgsql` trigger `append_only()` still fires with `USAGE` on `plpgsql` revoked ("audit_event is append-only: UPDATE refused").
- `CONNECT` on `postgres` is refused after the revoke; on `template1` it is still held.
- Order: `SET LOCAL` after `BEGIN … READ ONLY` and before the user's statement is correct. A timeout armed for a statement is not disarmed from inside it (round 1).

**Overlaps with TST:** the pid read without a statement (R2-DBS-5), the planted membership grant (R2-DBS-3), and the failing-statement log test (R2-DBS-2).
