# Slice 04 — the assessment

**Status: OPEN — §5 and §6 are Kyle's.** Written 2026-11-15 against `main` at `4a00cb6`
(PR #114 merged).

**Who wrote which part.** Sections 1–4 and 7 are facts, assembled by the agent session that
did the build. Each can be checked against a commit, a test run or `LOG.md`, and none carries
a recommendation. Sections 5 and 6 are **Kyle's**: the gate asks what this taught *Kyle*, which
no one else can answer. The agent lays out candidates and evidence there and decides nothing;
every box is unticked.

**The commits** (`main`, squash-merged):

| Step | Commit | PR |
|---|---|---|
| Commissioned | `644efe2` | #103 |
| Reviewed, rulings `S4-1`..`S4-8`, rewritten | `424d1a3` | #104 |
| 1a, the logins provisioned | `3bc6d44` | #105 |
| 1b, the API on its own login | `60b7104` | #106 |
| 2, the reporting schema | `0812e8c` | #107 |
| 3, running a query | `92d9579` | #108 |
| 4, saved reports | `d59b717` | #109 |
| 5, the page | `90abed8` | #110 |
| 6, the published stack | `4a00cb6` | #114 |

---

## 1. Definition of done

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | The API's own login: not superuser, owns nothing, in no role, privileges equal to `ExpectedPrivileges.Api`; DoD 1's table with both outcomes | **Met** | `ApiLoginTests` (`60b7104`): 16 statements, each succeeding or `23001` as the owner and `42501` on the app's own connection. The snapshot (`3bc6d44`). `G-7` closed |
| 2 | The reporting login holds exactly its grant; every refusal of "The boundary" fails through the endpoint with its SQLSTATE; nothing changed after the probes | **Met** | 41 refusals, in `ReportingSchemaTests` (`0812e8c`) and through the endpoint in `ReportApiTests` (`92d9579`; the `saved_report` row in `d59b717`). The effect test after them. The four plants flagged (`3bc6d44`). `lo_from_bytea` and `pg_logical_emit_message` seen writing with `EXECUTE` granted back; the leak probe seen leaking without the barrier (`0812e8c`) |
| 3 | Bounded: each producer ends as stated with its backend seen; the caps at their boundaries; memory under its bound; no session state; `429`; the first request after a crash | **Met, with two corrections** | `ReportProducerTests`, `ReportApiTests`, `ReportMemoryTests` (343 KB, against 128 MB with the default reader), `ReportCrashTests` (`92d9579`). The corrections are in §3: one producer row is refused rather than timed out, and the stated seen-failing for part 6 did not hold |
| 4 | Bound parameters: the echo byte for byte, the injected `OR` matching nothing, a wrong value `400` before the database | **Met** | `SavedReportTests` (`d59b717`). Seen failing: an interpolating runner turned the echo, effect and typed tests red, the effect counting 2 |
| 5 | Live documentation: a `COMMENT` changed at test time appears in the route and on the page; every view and column has one | **Met** | The route (`92d9579`), the page (`ReportsPageTests`, `90abed8`), the comment test naming a nulled column (`0812e8c`) |
| 6 | The page: axe clean in light and dark in each named state; the keyboard path asserted on the focused element | **Met, with one note** | `ReportsPageTests` (`90abed8`). Seven mutations seen failing, one in the dark scheme only. **The note:** no test renders a full 10,000-row table |
| 7 | The contract: the new routes in the committed, byte-stable OpenAPI document; the contract test covering them; advisory in CI | **Met** | Regenerated in `92d9579` and `d59b717`; the contract test green locally and in CI at every merge. The report operations added to the credential scan's superset (`92d9579`, `d59b717`) |
| 8 | Nothing regressed; CI green including both time-zone jobs; the E2E green locally, with `portfolio_api` in `pg_stat_activity` | **Met, with one note** | CI green at merge on #105 to #114. Two red runs before merge, both fixed: #108 (the crash test's durability, §3) and #114 (a flaky slot test, §3). The E2E green locally at each step, last in full at step 5's tree (35 + 1 skipped, 2026-11-08); `portfolio_api` asserted in `ImportAFileTests` (`60b7104`). **The note:** step 6 changed no E2E code, and its full E2E was not rerun; `AppModelTests` ran in CI and `PublishedStackTests` by hand |
| 9 | Published, as step 6 lists | **Met, but for `G4-1`** | By hand, 2026-11-15 (`4a00cb6`), over a recreated slice 03 volume: two deploys, the snapshot equal by the shared file, the password hashes unchanged, the API working after each, refusals in the container, the memory-induced crash with the API recovering. **`G4-1`** (`aspire run` twice keeping both passwords) is not run |
| 10 | Each obligation done, or recorded as not done with a reason; each refusal and write route listed as shown or argued | **Met** | Below, and §4 |

**DoD 10, obligation by obligation** (`slice-04.md`, "Obligations this slice carries"):

| From | Item | Outcome |
|---|---|---|
| slice 03, "Before the first real balance" 3 (`D6`, `G-7`) | The application login | **Built** (`60b7104`) |
| slice 03, "Before the first real balance" 2 (`D5`) | The backup command | **Carried, and changed.** No backup command exists. The README now says a restore must run the Migrator first, since a dump carries no logins (`4a00cb6`) |
| slice 03, "Before the first real balance" 4 (`D17`) | The wider audit | **Carried**, now including report runs and `saved_report` changes (`S4-3`): never the SQL text |
| slice 03, "Before the first real balance" 1, 5, 6 | The commission, the dev page (`D9`), `C-389` | **Carried** |
| `research.md` D5 banner | "False as built" | **Built.** The banner now says what holds and why (`92d9579`) |
| slice 03's review deferrals | Each with its trigger | **Carried**, unchanged |

---

## 2. What it cost

| | Hours | How it was measured |
|---|---|---|
| **Kyle** | **0** logged | `LOG.md`, the `Kyle h` column: every slice-04 row is `—`. Kyle ruled 4 commission choices, `S4-1` to `S4-10`, and the volume in step 6, and reviewed and merged 9 PRs. None of it was logged as hours |
| **Agent**, as logged | **13.0** | `LOG.md`, the `Agent h` column: 10 rows, from the commission to step 6. Every row says "estimated": no timer ran |
| **Agent**, as measured | **≈ 8.9** for the commission through step 3 | Wall-clock on 2026-11-06, from PR #103 opening (10:58 UTC) to PR #108 opening (19:51 UTC), waits on Kyle included. The rows inside that span total 9.5. Steps 4 to 6 ran across 2026-11-07, -08 and -15 with gaps between sessions, so they have no measured figure |
| **The estimate** (`SCO-15`) | about **12** logged, **7–9** measured, for the build and assessment after the review | The build after the review logged 9.5. With the commission and the two review rows, 13.0 |

**The logged figure is overstated where it can be checked**, as in slices 01 to 03: on
2026-11-06 the estimates total more than the span they fall inside.

**Work outside both columns:** the four review seats and round 2's two seats, run as subagents
inside the 2026-11-06 span, and so inside the measured figure.

**Against the 200-hour threshold:** **not tripped, and that tells us nothing.** `slice-04.md`
said so in advance: with the agent building, this limb cannot trip. **The gate rests on the
techniques limb, §5.**

---

## 3. The review's claims, what the docs were corrected for, and what the build found

### Executed claims that were wrong once built

Each was a claim the review marked `EXECUTED`, or a seen-failing it predicted, and the build ran
something different:

- **`R2-TST-1`: the trigger's refusal is `23001` (`restrict_violation`), not `P0001`.** And the
  owner's `TRUNCATE ingest_run` is refused first by a foreign key (`0A000`), so that row uses
  `CASCADE` to reach the trigger (`60b7104`).
- **`DBS-1`: `SET TRANSACTION READ WRITE` as the whole statement succeeded.** `SET LOCAL
  statement_timeout` takes no snapshot, so the user's statement was still "before any query".
  It wrote nothing, since one statement held. Step 3 closed it: the runner takes a snapshot
  first, and it is now `25001` (`0812e8c`, `92d9579`).
- **`LOCK … IN SHARE MODE` on a view is `42501`, not `25006`.** A lock above `ACCESS SHARE` needs
  a write privilege, checked first (`0812e8c`).
- **The producer table: `count(*)` over `generate_series` in `FROM` is refused (`53400`), not
  timed out.** A function scan materialises its result, which spills past a small
  `temp_file_limit`. In the target list it streams and times out. The table has both (`92d9579`).
- **"With the deadline removed, the first two run until the test's own deadline": wrong.** Both
  end at the server's `statement_timeout`. A `set_config('statement_timeout', '0', true)` inside
  the statement does not disarm the timer armed for it (`92d9579`).
- **`R2-TST-8`'s seen-failing for part 6 (`COMMIT` for `ROLLBACK` turns the `set_config` row
  red) did not happen.** With nothing writable, the two differ only for a notification:
  `NOTIFY` runs in a read-only transaction and is delivered only at commit. That became part 6's
  test (`92d9579`).
- **`SRC-4`: "a Parse with Describe would" check the parameter count server-side.** It did not:
  described with the declared types, PostgreSQL inferred a type for an undeclared `$2`, and the
  report saved. The count now comes from preparing the statement and reading
  `pg_prepared_statements`. Npgsql's `DeriveParameters` refuses positional `$n` (`d59b717`).

### Defects the build found that no review did

- **A keyed service that threw on resolution answered `500` ahead of the CSRF filter's `403`.**
  A route's services are bound before its endpoint filters run. Found by `AllowListTests`; the
  runner now resolves its data source on first use (`92d9579`).
- **The crash test lost a committed password in CI.** Testcontainers runs PostgreSQL with
  `fsync`, `full_page_writes` and `synchronous_commit` off, so a commit can return before its
  WAL is written. The test now takes a `CHECKPOINT` first (`92d9579`).
- **A bind mismatch (`08P01`) was classed as a lost connection** (`503`). It is now a refusal
  (`d59b717`).
- **Two tests flaky on timing:**
  - a producer test told the two deadlines apart by elapsed time; the runner now logs which one
    ended a run (`d59b717`);
  - the slot test could catch the runner's prelude as "active"; it now waits for the test's own
    statement (`4a00cb6`).
- **"Keep it" focused a button React had not yet rendered**, and focus fell to the page
  (`90abed8`).
- **`CatalogSnapshot` printed a role-wide setting's database as `0`**, not `*` as intended
  (`92d9579`).
- **Slice 03's published volume no longer existed** when step 6 began. It was recreated from
  slice 03's commit, by Kyle's ruling (`4a00cb6`).

### What `security.md`, `data-model.md` and `research.md` were corrected for

- **`data-model.md`:** the tables are in `public`, not `app`; "The `reporting` schema" rewritten
  to the four views built and the seven not built; `saved_report` as built, without
  `updated_at` and `last_run_at` (`0812e8c`, `d59b717`).
- **`security.md`:**
  - the "Database" section: the logins as built;
  - the boundary as the grants, targeted revokes, a read-only transaction with a snapshot
    first, one statement, a fresh session and a `ROLLBACK`;
  - `NOTIFY` never delivered;
  - the accepted non-refusals;
  - every run bounded from outside it;
  - parameters bound, and a save described without being run.

  The banner says the `:112` remedy (`CommandTimeout`) was not enough (`0812e8c`, `92d9579`,
  `d59b717`). "Audit" says the immutability is now a database boundary (`60b7104`).
- **`research.md`:** D5's banner, from "true of intent, false as built" to what holds and why
  (`92d9579`).
- **`testing.md`:** the Production values (10 s, 10,000 rows, 8 MB, 256 MB) and the test sizes
  (`92d9579`).

---

## 4. For each login, the refusals shown by a test, and those only argued

### `portfolio_reporting`

**Shown**, each by its SQLSTATE, directly (`ReportingSchemaTests`) and through the endpoint
(`ReportApiTests`), with every table's rows, the large objects, the login's settings and a view's
comment unchanged after them:

| Route | Refused by | SQLSTATE |
|---|---|---|
| `ALTER ROLE` on itself: its password, `SET work_mem`, `SET statement_timeout` | the read-only transaction | `25006` |
| `CREATE TEMP TABLE`; `CREATE FUNCTION pg_temp…`; `COMMENT ON` a view | the read-only transaction | `25006` |
| `lo_from_bytea`, `lo_create`, `lo_creat`, `lo_import`; both `pg_logical_emit_message` forms | the revokes, and `lo_import`'s superuser check | `42501` |
| `DO` | `USAGE` on `plpgsql` revoked | `42501` |
| `nextval`; `LOCK` on a view in `SHARE` mode and on a table beneath; `FOR UPDATE` | privileges | `42501` |
| `COPY … TO PROGRAM`; `pg_read_file`; `SET ROLE`; `SET SESSION AUTHORIZATION` | privileges | `42501` |
| `SET temp_file_limit`; `set_config('temp_file_limit', …)` | a superuser parameter | `42501` |
| `set_config('transaction_read_only', 'off', true)`; `SET TRANSACTION READ WRITE` | the snapshot taken first | `25001` |
| a second statement after `SET TRANSACTION READ WRITE` or after `COMMIT` | the one-command batch | `42601` |
| 14 reads outside the views: the tables beneath, Identity's three, `audit_event`, `used_setup_code`, `app_setting`, `saved_report`, the chart's view and its spine | no grant | `42501` |
| connecting to `postgres` or `template1` | `CONNECT` revoked | `42501` |

**Also shown:**
- **A row a view hides:** never seen by a leaky function in its `WHERE`, with a control over an
  unbarriered copy that does see one (`0812e8c`).
- **`NOTIFY` and `pg_notify`:** not refused, and never delivered. The `ROLLBACK` holds it, shown
  by a listener and a committed control, and seen failing with `COMMIT` (`92d9579`).
- **The deadline, the row cap and the byte cap:** at their boundaries, with the backend seen and
  then gone, and memory under its bound (`92d9579`).
- **No session state surviving a run:** pids, an advisory lock, a prepared statement
  (`92d9579`).
- **In the published container**, by hand: `public.account` and `lo_from_bytea` refused with
  `42501` (`4a00cb6`).

**Argued, not shown** (the accepted non-refusals, `S4-1`, `S4-4`, `S4-9`):
- **Exhausting memory:** accepted. **Shown by hand** that one query crash-restarts PostgreSQL
  under 256 MB, and **shown in CI** (by `SIGKILL`) and by hand that the API serves the next
  request.
- **Seeing names, view definitions, role settings and row counts in the catalog:** argued only.
  No test reads the catalog as the login.
- **Holding locks for the length of a run**, the advisory key and `ACCESS SHARE`: argued only.
  No test measures the API waiting on a run.

### `portfolio_api`

**Shown:**
- **DoD 1's 16 statements, two outcomes each:** the owner succeeds, or the trigger refuses with
  `23001`; the app's own connection is refused with `42501`. So the grant, not the trigger,
  refuses (`60b7104`).
- **The grant matrix:** sufficient, since every existing suite passed on it unchanged
  (`60b7104`), and exact, by the snapshot (`3bc6d44`).
- **Owning nothing and belonging to no role:** in the snapshot. A login with a membership is
  refused by the routine (`3bc6d44`).

**Argued, not shown:** that the host commands (`revoke-sessions`, `reset-owner`) run as
`portfolio_api`. Their test passes on that login's string, but would pass on the superuser's too.
In compose the commands' only database variable is the API login's (`60b7104`).

---

## 5. Techniques — **Kyle to complete**

The gate's words (`slice-04.md`): *"three techniques this slice taught that Kyle did not
already have. Each must come with the commit that demonstrates it, and must **not** be any of
the twenty-one slices 01 to 03 already claimed."*

**The fact that frames this section:** Kyle logged no hours, and an agent session did the build.
What follows are techniques the build **demonstrates**, each with its commit. Whether any is now
**Kyle's**, understood well enough to use without the agent, only Kyle can answer. **A technique
merely present in the repository does not satisfy this limb.**

Tick the ones that are genuinely yours; strike the rest. **Fewer than three means STOP.**

- [ ] **Ownership against use.** A login that uses tables and owns none of them, nor a schema or
      the database. `PUBLIC`'s defaults are revoked by name. Proved by a snapshot of
      **effective** privileges, with plants that each flag exactly their line. The commission's
      first technique.
      `3bc6d44`, `60b7104`.
- [ ] **Bounding an untrusted query from outside it.**
      - a read-only transaction with a snapshot taken first;
      - one statement, refused by the server rather than parsed;
      - a fresh session, and a `ROLLBACK` shown by `NOTIFY`;
      - a client deadline enforced by a cancel;
      - caps counted per cell, then a cancel before the reader is disposed.

      The commission's second technique.
      `92d9579`.
- [ ] **Barrier views, proved by a leaky function and an unbarriered control.** A view reads
      with its owner's rights, and `security_barrier` keeps a cheap function from seeing what
      the view filters out.
      `0812e8c`.
- [ ] **A refusal identified by the layer that refused it.** Each probe asserts its SQLSTATE, so
      a misspelt name (`42P01`) cannot pass, and the code says whether a privilege, the
      read-only transaction or the one statement held.
      `0812e8c`, `92d9579`.
- [ ] **Memory bounded by streaming, shown by an allocation delta.** `SequentialAccess` and
      chunked readers, with every cell counted against its budget and a non-text cell checked
      by its wire length first. Measured with `GC.GetTotalAllocatedBytes` in a non-parallel
      collection: 343 KB, against 128 MB.
      `92d9579`.
- [ ] **A crash-restart as a deterministic test.** A `SIGKILL` of the run's backend, the path an
      out-of-memory kill takes, with the API's pool warm and EF's retry seen to be what recovers
      it. Plus the test database's own durability settings, which made a committed row
      disappear.
      `92d9579`, `4a00cb6`.
- [ ] **Asking the server instead of parsing.**
      - parameters bound positionally, and the binding probed with values the grant allows;
      - a saved statement described and prepared by PostgreSQL, never parsed by the app.

      `d59b717`.
- [ ] **A schema documented by the database itself.** `COMMENT`s on every view and column, read
      live by a route and shown on the page. A comment changed at test time appears; a nulled
      one is named. The commission's "plausible third".
      `0812e8c`, `92d9579`, `90abed8`.
- [ ] **An upgrade in place, verified by hashes and never values.** A recreated old volume, two
      deploys, one check script run by hand and in CI alike, and a planted grant that the next
      deploy removed unasked.
      `4a00cb6`.

*Overlaps to judge:*
- **The SQLSTATE and the crash candidates sit close to slice 02's "proving a defence by breaking
  it".**
- **The memory candidate sits close to slice 02's "measuring before limiting".**
- **The schema candidate sits close to slice 01's "a contract that is generated, committed, and
  byte-stable".** The commission said so of it (`SCO-4`).
- **The upgrade candidate sits close to slice 03's "the published artifact is not the thing you
  ran".**

Kyle should decide whether each is new or the same.

Kyle's own, if different: ________________________________________________

---

## 6. The decision — **Kyle to complete**

`slice-04.md`: *"Explicitly NOT reasons to continue: that the slice works, and that the next
slice is obvious."*

- [ ] **STOP.** The default.
- [ ] **A fifth slice**, which requires **its own commission, written and dated in this
      repository**. If chosen: which technique is it for?
      ________________________________________________
- [ ] **Admitting the first real balance**, which also requires its own commission. It is bound
      by:
      - slice 03's "Before the first real balance": the backup (`D5`, now owing the logins'
        restore order), `D17` (now including report runs), the dev page (`D9`) and `C-389`;
      - the catalog's names, which `S4-4` says to revisit before the first real balance.

**Kyle's hours stayed under 200** (0 logged), so the gate rests on §5 and on Kyle's own
judgement of what the slice taught.

---

## 7. Owed elsewhere

**Nothing to the planning repository.** The slice was commissioned, reviewed, ruled and built
here, and this assessment is its record.

**In this repository**, for whatever comes next, with nothing scheduled:
- **`G4-1`:** `aspire run` twice keeping both passwords (`persist: true`). By hand, not run.
- **The full E2E at step 6's tree.** Step 6 changed no E2E code; a full run would close DoD 8's
  note.
- **A full 10,000-row table in the browser:** not measured. There is no paging, by the middle cut
  (`S4-7`).
- **The host commands' login:** argued, not shown (§4).
- **A declared parameter the statement never uses** is saved, and is bound and ignored. Preparing
  the statement cannot see it (`d59b717`).
- **Catalog narrowing** (`S4-4`): trigger, an object name or comment that is itself sensitive, or
  the first real balance.
- **Dependabot:** #112 (the NuGet patch updates) was failing only on the flaky slot test now fixed
  (`4a00cb6`), and has been asked to rebase. #111 (npm) is green. #113 (.NET 11) was closed; a rule
  ignoring major versions of the .NET packages is proposed, not written.
