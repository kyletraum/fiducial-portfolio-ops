# Spike G — which balance does the net-worth view count when two sources report the same day?

**Ran:** 2026-10-18. **Command:** `./run.sh` (Git Bash, Windows, Docker Desktop)
[`EXECUTED 2026-10-18`]
**Against:** PostgreSQL 18.3 (Debian 18.3-1.pgdg13+1), in a throwaway container
removed on exit. This is the image Aspire runs here: `Aspire.Hosting.PostgreSQL`
13.6.1 defaults to `docker.io/library/postgres:18.3`
[`SOURCE@Aspire.Hosting.PostgreSQL@13.6.1`, read from the package's string table]. Schema built by the
real EF Core migrations (`Initial`, `AccountSource`, `SpineRunsToToday`), not by a
hand-written copy [`EXECUTED 2026-10-18` — `select version()` and the
`dotnet ef database update` output].
**Result:** the claim holds. The unfixed view's answer depends on **write
order**. The ruled tie-break makes it depend on the data, and all seven
assertions pass. The same assertions **fail** against the unfixed view.

This is a spike, not a migration. The fix lands in migration 4 in step 2 of
`slice-02.md`, copied from `03-fixed-view.sql`.

---

## 1. The defect, measured

One account, two live rows on 2026-03-01: a manual entry of `100` and an export
of `200`. They have different `source_system` values, so `account_balance_live_uq`
allows both. `v_net_worth_daily`'s lateral orders by `b.as_of_date DESC LIMIT 1`
and nothing else (`02-unspecified.sql`):

| Inserted first | Plan | Net worth on 2026-03-01 |
|---|---|---|
| manual | default (index) | `100.0000` |
| manual | sequential scan | `100.0000` |
| export | default (index) | `200.0000` |
| export | sequential scan | `200.0000` |

**Two distinct answers from the same data.** The view counts whichever row was
written first. The plan did not change the answer here, but write order did. In
practice, net worth would depend on whether a user typed a balance before or
after importing a file for the same day. No rule decides it. Nothing visible
would show the difference, because both figures are plausible and the status
reads `Verified` either way.

The slice-02 claim said the pick is "unspecified: Postgres may return either,
and may change its mind between queries". Half of that was observed. Physical
order decides it, which is **not** a guarantee: a `VACUUM FULL`, a `CLUSTER` or
an update that moves a tuple can reorder rows. Only "changes between queries on
unchanged data" was **not** observed.

## 2. The rule — ruled by Kyle, 2026-10-18

Proposed in `slice-02.md` Spike G and approved by Kyle in-session, 2026-10-18:

1. **Newest `as_of_date` first.** A stronger source never reaches back past a
   newer date.
2. On the same date, **strongest `source_strength` first.** The Postgres enum is
   declared `statement, export, api, scrape, manual`, strongest first, so this
   is `ASC`.
3. Then **newest `observed_at`**.
4. Then `id`. This is **not** part of the ruling. It is only there so the order
   is total and the planner has no tie left to break. It decides nothing that
   carries meaning: two rows reaching it share a date, a strength and an
   observation instant.

## 3. The assertions (`04-assertions.sql`)

Each case is loaded in **both** insertion orders, read under **both** the
default plan and a forced sequential scan, and rolled back.

| Case | Rows | Expected | Result |
|---|---|---|---|
| **G1** | manual `100` and export `200`, same date | `200`: the stronger source | PASS, both orders |
| **G2** | export `200` on 03-01, manual `150` on 03-02 | 03-01 `200`; 03-02 `150`; 03-05 `150` (carried) | PASS: the newer weak figure outranks the older strong one |
| **G3** | CSV `300` (observed 03-03) and OFX `400` (observed 03-04), same date, both `export` | `400`: the later observation | PASS, both orders |

**Seen failing:** with `03-fixed-view.sql` skipped, the first assertion raises
`FAIL G1 manual first: 2026-03-01 under default plan, expected 200, got 100.0000`.

## 4. What step 2 inherits

- **Migration 4 replaces `v_net_worth_daily`** with `03-fixed-view.sql`'s
  definition. Only the lateral's `ORDER BY` changes; every column stays as
  migration `Initial` wrote it. `Down` restores the old definition.
- **The integration suite gets G1 to G3**, written against the API rather than
  raw SQL, so the test exercises the path a user would take.
- **The existing series index** `account_balance_series (account_id, as_of_date
  DESC)` no longer covers the whole `ORDER BY`. With one account and a handful
  of rows the planner does not care. Whether it matters at the slice's
  10,000-row import limit is **not measured here** [`INFERRED` that it does not].
  If step 2's integration test shows a regression, the index gains the other
  two keys.

## Running it

```sh
./run.sh
```

Needs Docker and the repo-local `dotnet ef` tool (`dotnet tool restore`). The
container's password is generated per run and never written to a file. The
container binds to loopback on a random port and is removed on exit, success or
failure.
