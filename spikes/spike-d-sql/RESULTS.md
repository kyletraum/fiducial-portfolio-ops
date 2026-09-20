# Spike D — the SQL

**Ran:** 2026-09-18. **Command:** `service postgresql start && ./run.sh`
[`EXECUTED 2026-09-18`]
**Against:** PostgreSQL 16.13 (Ubuntu 16.13-0ubuntu0.24.04.1), throwaway
database `spike_d`, schema dropped and rebuilt on every run
[`EXECUTED 2026-09-18` — `select version()`].
**Result:** every assertion passes; `run.sh` exits non-zero on the first that
does not.

This is a spike, not a migration. It exercises the SQL of `slice-01.md` step 2
and step 4 and nothing else — no application code, no EF Core, no container.
The step-2 migrations are written against what this file records.

---

## The three established claims, verified

Established before this session; re-run here to hold them at `EXECUTED` on this
schema rather than on a sketch, then left alone.

| Claim | Verified as | Measured |
|---|---|---|
| Supersession write order is `UPDATE old SET superseded_at` **then** `INSERT` | `05-supersession.sql` 4a | both statements commit; one live row, one superseded, `supersedes` set backward |
| Reversing it trips the partial unique index | `05-supersession.sql` 4b | `ERROR: duplicate key value violates unique constraint "account_balance_live_uq"` (`23505`), raised on the `INSERT`, before the `UPDATE` ever runs |
| The view uses `LEFT JOIN LATERAL … ON true`, never `CROSS JOIN` | `04-view-assertions.sql` 4.1 / 4.2 | `LEFT JOIN` → `150.0000`, 4 in window, **2 unverified**. `CROSS JOIN` → **the same `150.0000`**, 2 in window, **0 unverified** |
| `sum()` over all-NULLs is NULL, not 0 | `04-view-assertions.sql` 4.3 / 4.6 | 2026-01-05 and 2026-07-01 both return `net_worth = NULL` with `accounts_unverified` equal to `accounts_in_window` |

The `CROSS JOIN` reading is worth stating in full because it is what makes the
defect survive review: **the headline number is identical.** `150.0000` either
way. Only the count that exists to qualify it changes, and it changes to the
value that means "nothing is wrong".

---

## What the spike settled that was not already settled

### 1. The staleness cutoff has no ruled value — recommended 90 days (see `07`)

Constitution Amendment 3, condition 2, says the cutoff "is named in
`slice-01.md` step 2 and is part of this amendment, not an implementation
detail". **It is not named there.** `slice-01.md` step 2 covers the index, the
write order, `account_source`, currency, the `closed_on` guard and rounding; no
cutoff. `R2-B7` asked for one and the request was not carried into the step.

It is stored in `app_setting.balance_staleness_days` — a one-row table, not a
literal in the view body, because the amendment says this is not an
implementation detail, and a value you have to read a view definition to
discover is one.

**The fixture in `02-fixture.sql` uses 45**, chosen so the expiry boundary falls
inside the fixture's own date range and `4.5` can assert either side of it.
That is a test fixture, not the recommendation.

**The recommendation is 90 days**, measured in `07-cadence-models.sql` against
the stated cadence ("monthly, realistically every six weeks" — Kyle,
2026-09-20). Two findings drove it:

- **45 sits on the median gap.** The stated cadence generates a median gap of
  46 days, so a 45-day cutoff expires roughly half of all perfectly normal
  intervals.
- **Batch entry makes staleness CORRELATED, and `06` could not see it.** If
  balances are entered in one sitting, every account shares an `as_of_date`, so
  they all expire on the same day and `sum()` returns NULL for the whole chart
  rather than for one account. Measured: at 45 days, **12.4% of days have no
  number at all**; at 90 days, 0%.

90 days is above the stated distribution's p90 (74 days), never blanks the
chart under any of the three entry patterns modelled, and caps the carried-dead
-account lie at one quarter. It also has a story a human can hold: *if you have
not touched an account in a quarter, it stops counting.*

**Still not a ruling** — it belongs in a decision file. The cadence model behind
it is an assumption and is stated as one in `07`.

### 2. What happens past the cutoff — the rule `R2-B7` asked for, written down

An account whose last observation is older than the cutoff **stays in the
denominator and goes unverified.** It does not leave the window.

- It is not Principle III's case. Principle III is about a series that has
  *ended* — `closed_on`. An open account that has not been updated in two months
  has not ended; we simply have not verified it.
- So it is Principle II's case: the value is missing, and missing is reported as
  missing. `accounts_unverified` rises and `net_worth` stops including it.
- Dropping it from the denominator instead would be the `M-1` defect at the
  right-hand edge: the total would quietly shrink and the row would not say why.

Measured (`4.5`): accounts A and B observed 2026-03-10.
On **2026-04-24** (45 days) → `350.0000`, 4 in window, 1 unverified, `max_staleness_days = 45`.
On **2026-04-25** (46 days) → `200.0000`, **4 in window**, **3 unverified**.
The denominator holds; the numerator drops; the row says so.

Past `closed_on` is the other case and behaves the other way (`4.6`): on
2026-06-30 account E contributes `25.0000` with 5 in window; on 2026-07-01 it is
**absent from the denominator entirely** — not carried, not zeroed, not
interpolated. The spine extends 20 days past the close, so that absence is a
real reading and not a missing row.

### 3. `accounts_in_window` is a fourth column, and Amendment 3 names only three

Amendment 3 requires `accounts_carried`, `max_staleness_days` and
`accounts_unverified`. Without the denominator beside them, the two rules above
are indistinguishable on the wire: a total that fell because an account went
stale (still in window, now unverified) and a total that fell because an account
closed (gone from the window) produce different `accounts_unverified` values but
neither is interpretable without knowing how many accounts the day covered.

`v_net_worth_daily` therefore carries `accounts_in_window` and
`accounts_verified` as well. The series endpoint should carry both; `R2-B8`
already establishes that endpoint does not yet exist.

### 4. The spine starts at the earliest `opened_on`, not the first observation

A day on which accounts existed and nothing had been entered is a day the chart
must show as UNVERIFIED, not a day it may omit — otherwise the single most
common real state of a fresh install (accounts created, no balances yet) renders
as an empty chart rather than as "we do not know".

The spike's spine is unbounded because a spike has no request. **The series
endpoint passes an explicit `from`/`to`**, or a real account opened years ago
produces years of leading NULL rows.

### 5. `sum()` ignoring NULLs understates on *every* partially-verified day

Not just the all-NULL one. 2026-06-30 returns `25.0000` with 4 of 5 accounts
unverified — arithmetically correct, and meaningless as a net worth. The NULL
day is the loud version of a quiet problem that is present on most days.

**Consequence for step 3 and step 4:** `accounts_unverified > 0` is the badge
condition, and `net_worth IS NULL` is a distinct branch on top of it. Two
states, not one.

---

## Decisions taken in the schema, with the finding each answers

| | Decision | Why not the obvious thing |
|---|---|---|
| `M-9` | `superseded_at` + backward `supersedes` FK; partial unique index on `(account_id, as_of_date, source_system) WHERE deleted_at IS NULL AND superseded_at IS NULL` | a forward `superseded_by` names a row that does not exist yet |
| `M-9` | second partial unique index on `supersedes` | without it the restatement history is a graph and "current" stops being well defined |
| `M-10` | `account_source (source_system, source_id)` with a **full** unique constraint | partial would let a soft-deleted binding release a source id to a different account |
| `M-11` | a `slice_currency` **domain** whose check is the literal `'USD'`, used by both tables | `R2-M9`: a cross-row `CHECK` cannot see another row. A domain is the literal form and cannot be applied to one table and forgotten on the other |
| `S-17` | trigger, in **both** directions — insert-a-balance-after-close and close-behind-a-live-balance | a `CHECK` cannot reference another table (`4.9` shows both rejections); the second direction is the one that actually happens and is unguarded if you write only the first |
| `S-24` | PostgreSQL rounds half **away from zero**, confirmed at `round(2.5)=3`, `round(-2.5)=-3`, `round(0.125,2)=0.13` (`4.8`) | .NET `Math.Round` defaults to banker's rounding, so the `Money` type passes `MidpointRounding.AwayFromZero` explicitly and the midpoint test asserts both sides agree |
| `R2-B5` | `ORDER BY as_of_date DESC LIMIT 1` inside the lateral | `DISTINCT ON` is rejected outright — `SELECT DISTINCT ON expressions must match initial ORDER BY expressions` (`42P10`, `4.7`) — and yields one row per account, not a daily series |

## Not measured here, deliberately

Performance. `slice-01.md` records the triangular form as ~30% over budget at
this slice's cadence and the `LATERAL` form at an ~11x margin, both
`EXECUTED 2026-09-18`. Re-deriving a settled measurement is not what the spike
is for. For reference, the 201-row fixture view returns in ~2.3 ms.

## Open, and blocking step 2

1. **The staleness cutoff value.** 90 days is the measured recommendation
   (`07`); 45 is the test fixture. Amendment 3 makes the number part of the
   amendment, so it wants a ruling and a decision file, not a constant chosen
   by whoever wrote the migration.
2. **`slice-01.md` step 2 does not name it**, which is the documentary half of
   the same gap.
