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
| The API's own database login (`D6`, gate `G-7`) | **In this slice.** The same technique as the reporting role | In this slice; reporting role only |
| The net-worth chart on the reporting views (FR-5.5) | **Later.** The chart keeps its query | Later; now |
| Data, and who builds | **Invented data only; the agent builds, under the same gate** | Same as slice 03; something else |

**What this slice is for, by the gate's rule** (slice 03, "The gate": a slice is for a
technique, not because the next feature is obvious): **making the database enforce the
rules.** A query the user writes cannot write, cannot read what it should not, and cannot
run without bound. That must hold however the query is written, and it is enforced by
roles and grants, not by reading the query text. The same technique gives the API a login
that cannot remove the audit trigger, which `G-7` showed it can today.

**Method:** `D-029`: claim markers, and an `INFERRED` claim the design rests on is a gate.
**Built by:** the agent. **Estimate:** none. `LOG.md` records cost. **Ends in:** a written
assessment and a full stop.

**Before the build:** a committee review of this document, as slice 03 had. This is a
first draft. Its spikes and open questions are where the review should push.

---

## What the slice is

One technique, through every layer once: **three database logins, each holding only what
it uses.** That is enforced by PostgreSQL, tested by trying what each login must not do,
and shown on the published stack.

| Login | Who uses it | Holds |
|---|---|---|
| `postgres` | The Migrator only | Everything. It owns every table, view and function |
| `portfolio_api` (name to settle) | The API | Only the table rights the API's code paths use. Not superuser, **owns nothing**, so it cannot `DISABLE` or `DROP` a trigger (`G-7`, `EXECUTED 2026-10-31`). `INSERT` and `SELECT` only on `audit_event` and `used_setup_code` |
| `portfolio_reporting` (name to settle) | User SQL, through the API | `USAGE` on the `reporting` schema, `SELECT` on its views, **and nothing else anywhere** (`research.md` D5) |

On top of the reporting login:

- **A `reporting` schema of documented views** over the data that exists today:
  - `v_account`: accounts with institution, open or closed, and provenance;
  - `v_balance_daily`: live balances, with superseded and soft-deleted rows excluded and
    no gap-filling;
  - `v_net_worth_daily`: a pass-through of the view the chart already reads, so a report
    and the chart cannot disagree, without touching the chart;
  - `v_ingest_run`: import history.

  Each column carries its meaning as a database `COMMENT`, and the UI shows that text
  (FR-6.6). Views over data that does not exist yet (transactions, holdings, allocation)
  are not built: `data-model.md` lists them for later slices.
- **Running a query** (FR-6.1, 6.3): an endpoint that runs one statement on the
  reporting login. It returns the columns, the rows up to a cap, and whether the result
  was cut off. A time bound applies that the query's own SQL cannot switch off (Spike N).
- **Saved reports** (FR-6.4): named, described, with typed parameters bound by the
  driver, never interpolated. Stored in `saved_report` (`data-model.md`), written by the
  API's login. They run only on the reporting login.
- **A reports page:** a plain text editor (no code-editor bundle, so the slice-03 CSP is
  untouched), the documented schema beside it, an accessible results table, and the
  saved list with its parameter inputs.

**The boundary, stated.** The reporting login refuses:
- any write, to anything;
- any read outside the `reporting` views: Identity's tables, passkeys, the audit log,
  `app_setting` and the physical tables beneath the views;
- running past its time bound, and returning past its row cap.

It does **not** refuse reading what the views show, which is the point of them. It also
does not hide the catalog: a role can list other tables' names in `pg_catalog` even when
it cannot read them. Spike L settles what else `PUBLIC` leaves open.

---

## What the slice is not

- **No real data** (Kyle, 2026-11-06). Invented balances only. Admitting the first real
  balance stays its own commission, bound by slice 03's "Before the first real balance".
- **No export** (FR-6.5, Kyle, 2026-11-06). Trigger: a report worth taking elsewhere. It
  brings CSV formula injection and a download path of its own.
- **The chart is unchanged** (FR-5.5, Kyle, 2026-11-06). The chart and
  `reporting.v_net_worth_daily` agree because one selects from the other.
- **No query parsing as a guard.** A clear early error is allowed as usability. Never as
  the boundary (`research.md` D5: "parsers can be fooled, `GRANT` cannot").
- **No KQL** (`spec.md` FR-6, out of scope).
- **No new audit kinds by default.** `security.md` lists report execution in its audit
  list, and slice 03 carried that list until real data (`D17`). Whether a report run is
  audited here is open question Q3.

---

## Step 0 — the spikes (do these first)

Each answers claims the design rests on. Every one is `INFERRED` until its spike runs.

### Spike L — what does a fresh role hold on PostgreSQL 18, and does GRANT-only hold?

`research.md`'s banner already marks D5 "true of intent, false as built". The role keeps
`PUBLIC`'s `CONNECT`, `TEMPORARY` and `EXECUTE` defaults. The statement timeout is
`PGC_USERSET`, so a query can disarm it. PostgreSQL has no row-cap setting at all. On
`postgres:18.3`, the image the app runs, as a role created with only the slice's grants:

1. Which rights does it hold through `PUBLIC`? `CONNECT` on every database; `TEMPORARY`;
   `CREATE` on `public`, which PostgreSQL 15 reportedly removed (`INFERRED`); `EXECUTE`
   on functions such as `pg_sleep`, `set_config`, `pg_read_file` and the `lo_*` family;
   and which of these the slice must `REVOKE`, and from whom.
2. What can it read that it was not granted: `pg_catalog`, `information_schema`,
   `pg_stat_activity`, including other sessions' query text, and `pg_settings`?
3. Can it write by any route: `SELECT` with a writing function, `COPY ... TO PROGRAM`,
   `CREATE TEMP TABLE`, `DO` blocks, `SET ROLE`?
4. Does a view read with its owner's rights (`security_invoker` off, the default), so the
   reporting login needs no grant on the tables beneath?

**Raises:** the exact `REVOKE` and `GRANT` script, and a test per refusal.

### Spike M — three logins under Aspire, in `aspire run` and in the published stack

Today the AppHost has one `AddPostgres` and one `AddDatabase` reference, shared by the
Migrator and the API (`AppHost.cs`).

1. How do the two new logins get passwords? Aspire secret parameters, generated, as the
   setup code is? And do they stay stable across deploys? (`G-26` showed the Postgres
   password does. The deploy state keeps generated parameters: slice 03 step 7b.)
2. How does the API's connection string name a different user? A `ReferenceExpression`
   over the Postgres resource's host and port plus the parameters, or a connection
   string resource? Which survives `aspire publish` into the compose file?
3. Who creates the roles, and when? The Migrator, after migrations, idempotently, with
   passwords from configuration? Or a migration? A migration cannot take a secret
   parameter, and a password must not sit in a committed file.
4. Does the published stack's existing volume take the new roles on a redeploy? Is there
   an idempotent `CREATE ROLE`, or does it take a `DO` block, or catching
   `duplicate_object`? (`INFERRED`: no `IF NOT EXISTS` form.)

### Spike N — bounding a query from the client

1. With `SET statement_timeout = 0` in the user's own SQL, does Npgsql's command timeout
   or cancellation still stop the query **on the server**, so `pg_stat_activity` no
   longer shows it? Or only abandon it on the client?
2. Is a row cap read as "stop after N + 1 rows, then cancel" bounded in both server work
   and the API's memory, for `SELECT * FROM generate_series(1, 1e9)`?
3. Does Npgsql split a multi-statement text, `SELECT 1; SET ...; SELECT ...`, and run
   every part? Can the endpoint refuse more than one statement without parsing SQL?
   (Npgsql's own batching rules; `INFERRED`.)
4. Npgsql rewrites `@name` parameters in the text before sending it. Can a comment or a
   string literal containing `@x` confuse that rewrite? Or is positional `$1` the safer
   contract for saved reports?

---

## The build (outline; refined after the spikes, as slices 02 and 03 were)

1. **Step 1 — the API's own login (`D6`).** The Migrator provisions `portfolio_api`, and
   the API switches to it. The Integration fixture runs the app as that role. **`G-7`
   closes when a test, as that role, is refused `DISABLE TRIGGER` and `DROP TRIGGER`.**
   Every existing suite stays green: the grants cover every path the API uses.
2. **Step 2 — the reporting schema and its login.** The four views, their comments, the
   reporting role and Spike L's script. Tests: each refusal in "The boundary", the view
   rules (soft-deleted and superseded rows excluded, no gap-filling), and
   `reporting.v_net_worth_daily` equal to the chart's view.
3. **Step 3 — running a query.** The endpoint, Spike N's bounds, and problem details for a
   refused or failed query that pass on PostgreSQL's message, never a connection string.
   Every unsafe request carries slice 03's CSRF token, and the endpoint is behind the
   session like every other.
4. **Step 4 — saved reports.** `saved_report`, create, read, update and delete, typed
   parameters bound by the driver, and a test that a parameter value of SQL stays a value.
5. **Step 5 — the page.** The editor, schema documentation, the results table, the saved
   list and parameters. axe in both schemes, as slice 03 did.
6. **Step 6 — the published stack.** Deploy over slice 03's volume: the roles appear,
   their passwords hold across a redeploy, the API runs as its own login, and the
   reporting refusals hold in the container. The E2E runs locally, as before.

---

## Definition of done (draft; the review will tighten it)

1. The API connects as a login that is neither superuser nor the owner of anything. As
   that login, `DISABLE TRIGGER` and `DROP TRIGGER` on `audit_event` are refused.
   **`G-7` is closed**, and `security.md` "Audit" says the immutability is now a database
   boundary.
2. The reporting login can `SELECT` the `reporting` views and **nothing else**. Each
   route Spike L found is refused by a test that was seen failing first.
3. A query cannot outrun its time bound, even after `SET statement_timeout = 0`, and the
   server-side query is gone afterwards. A result past the cap is cut, and says so.
4. A saved report's parameters are bound: a value of `1; DROP TABLE account` is returned
   as a value, or refused as the wrong type, and nothing changes.
5. The schema documentation the page shows is the database's own `COMMENT` text.
6. The page passes axe in light and dark, and works by keyboard alone.
7. Slices 01–03's DoD items still hold. CI is green, including both time-zone jobs, and
   the E2E is green locally.
8. On the published stack: the API runs as its own login, the reporting refusals hold,
   and the logins survive a redeploy over an existing volume.
9. Each obligation below is done, or recorded as not done with a reason.

---

## Open questions for the review

- **Q1. Login names, and whether the API needs `UPDATE` anywhere.** Balances supersede
  rather than update (`testing.md`: "a restated balance inserts and links rather than
  overwriting"). Does any API path update in place? If none does,
  the API gets no `UPDATE` at all.
- **Q2. Where the time bound and row cap live:** the role's defaults, the API's command
  timeout, or both (Spike N). And what values: `testing.md` names neither.
- **Q3. Audit report runs?** `security.md` lists report execution, and `D17` carried it to
  real data. A run is cheap to record (kind, saved report or ad hoc, row count, never the
  SQL's results). Is the SQL text itself safe to store in the audit row?
- **Q4. `pg_catalog` visibility.** Accept that the reporting login can list table names
  it cannot read, or narrow it (`REVOKE` on catalog views is possible, but brittle)?
- **Q5. One statement only?** Refuse multi-statement text (Spike N, 3). A clear refusal
  is usability; the grants stay the boundary either way.

---

## Obligations this slice carries

| From | Item | Here |
|---|---|---|
| slice 03, "Before the first real balance" 3 (`D6`, `G-7`) | The application role | **Built here** (Kyle, 2026-11-06) |
| slice 03, "Before the first real balance" 1, 2, 4–6 | Commission, backup (`D5`), wider audit (`D17`), dev page (`D9`), `C-389` | **Carried.** Still owed before the first real balance |
| `research.md` D5 banner | The reporting role "false as built" | **Built here**, to Spike L's findings |
| slice 03's review deferrals | Each with its trigger | **Carried**, unchanged |

---

## The gate — unchanged

`LOG.md` carries on with both columns, one line per session, written as it goes.

> **The default at the end of slice 04 is STOP.** A fifth slice, or the first real
> balance, requires its own commission, written and dated in this repository.

Write `slice-04-assessment.md`. It answers:

- What did it cost, in both columns?
- Which of Spikes L, M and N were wrong, and what in `security.md`, `data-model.md` and
  `research.md` was corrected as a result?
- For each login, which refusals were **shown** by a test, and which only argued?
- Which techniques did this slice teach that Kyle did not already have, each with its
  commit, and none already claimed by slices 01–03? **Fewer than three means STOP.**
- Is a fifth slice wanted, or the first real balance?

## Then stop
