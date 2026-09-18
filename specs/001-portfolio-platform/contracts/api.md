# API Contract

> ## ⚠ FROZEN REFERENCE — read this before the document
>
> **This document is not being built.** `001-portfolio-platform/slice-01.md` is
> the only live unit of work. This file is consulted, not executed, and it is
> **corrected only when a slice is commissioned against it** — so a paragraph
> below carries no guarantee beyond what this banner says about it.
>
> Three review rounds contradicted specific claims here. They are named by line
> so you do not have to guess which paragraph is the wrong one:
>
> - **Missing write paths.** There is no account-creation endpoint and no `POST /accounts/{id}/balances` — the one write the live slice actually builds. There is also no `/transfers` resource and no constants write path, so two constitutional states (V's conditional money, II's unresolved input) are reachable **only through `/dev/seed`**, i.e. only in DEV.
> - **Missing.** No idempotency or correction semantics on a balance write, and no breaking-change detection: regenerating the document and diffing catches *drift*, not *breakage*.
> - **CSV export is unguarded.** It is the one place provider-authored text leaves the escaping boundary; a `merchant_name` of `=HYPERLINK(...)` executes on open.
>
> Anything *not* listed above is **unreviewed, not verified**. Where a claim
> about an external system carries a strength marker (`EXECUTED`,
> `SOURCE@<ref>`, `DOCS@<date>`, `INFERRED` — see `../review/process.md`), that
> marker is the claim's real weight. An unmarked external claim has not been
> checked.

The API is the product boundary (Constitution XI). Everything the UI can do is
an HTTP call any other client could make.

**Source of truth:** the OpenAPI document generated from the implementation and
committed at `contracts/openapi.json`. This file is the human-readable companion;
where they disagree, the generated document wins and the disagreement is a bug.

---

## Conventions

**Base path:** `/api/v1`. A breaking change is a new major version path
(FR-12.4). Additive changes - a new optional field, a new endpoint - are not
breaking and do not bump it.

**Authentication:** session cookie, `HttpOnly`, `Secure`, `SameSite=Strict`.
State-changing requests carry a CSRF token. No bearer tokens in browser storage.

**Money** is a JSON object, never a bare number:

```json
{ "amount": "1234.56", "currency": "USD" }
```

A string, so no client's JSON parser can round it. This is unusual enough to be
worth stating twice: a `double` anywhere in this API is a defect.

**Dates and times:** `as_of_date` is `YYYY-MM-DD`; `observed_at` is RFC 3339 UTC.
They are different facts and both appear (see `data-model.md`).

**Provenance** accompanies every sourced value:

```json
{
  "source_system": "mcp:monarch",
  "source_strength": "api",
  "observed_at": "2026-09-14T04:12:33Z",
  "ingest_run_id": "…"
}
```

**Nulls mean unverified, never zero** (Constitution II). A null value is
accompanied by `gated_on` where one is known. Clients must render a null as
"unverified", never as `0` and never as a blank cell.

**Collections** are cursor-paginated: `?limit=&cursor=`, returning
`{ "items": [...], "next_cursor": "…" }`. Offsets are not offered - they
duplicate and skip rows when data is being ingested concurrently.

**Errors** are RFC 9457 problem details:

```json
{
  "type": "https://…/errors/projection-refused",
  "title": "Projection refused: required inputs unresolved",
  "status": 422,
  "detail": "2 of 5 required inputs have no verified value.",
  "unresolved_inputs": [
    { "name": "annual_contribution_limit", "reason": "gated", "gated_on": "…" },
    { "name": "current_marginal_rate", "reason": "missing" }
  ]
}
```

A refusal is a **422 with a complete explanation**, not a 500. The client must
be able to show the user exactly what is missing and what would resolve it.

**Secrets never appear in a response**, in any form, masked or otherwise
(FR-3.2). There is no endpoint that returns a credential.

---

## Resources

### Connections - `/connections`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/connections` | list, with status and last sync |
| `POST` | `/connections` | create; returns the authorisation step to perform |
| `GET` | `/connections/{id}` | detail, including credential health |
| `DELETE` | `/connections/{id}` | revoke credentials and disable |
| `POST` | `/connections/{id}/sync` | trigger a sync; returns a run id |
| `POST` | `/connections/{id}/reauth` | begin re-authorisation |
| `GET` | `/connections/{id}/runs` | sync history |

Creation is two-phase: `POST` returns what the user must do (an OAuth
authorisation URL, or a provider link token), and a callback completes it. The
credential is written straight to the secret store and never traverses the API
again.

### Accounts - `/accounts`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/accounts` | list; filter by institution, type, open/closed |
| `GET` | `/accounts/{id}` | detail with current balance and provenance |
| `GET` | `/accounts/{id}/balances` | balance series; `from`, `to` |
| `GET` | `/accounts/{id}/holdings` | holdings as of a date |
| `PATCH` | `/accounts/{id}` | user-editable fields only - display name, tax treatment, asset class |

A balance series for a closed account **ends at `closed_on`**. The response
carries `series_ends_at` and `series_end_reason` so the client renders a stop
rather than a gap (Constitution III). Clients must not extend the line.

### Transactions - `/transactions`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/transactions` | list; filter by account, date range, category, merchant, amount, pending |
| `GET` | `/transactions/{id}` | detail |
| `PATCH` | `/transactions/{id}` | user categorisation and notes only |

Provider-authored text (`description`, `merchant_name`) is returned as data.
Clients escape it on render and never treat it as markup or instruction
(FR-1.5).

### Dashboards - `/dashboards`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/dashboards/net-worth` | series with assets, liabilities, net worth |
| `GET` | `/dashboards/allocation` | current allocation, optionally against policy |
| `GET` | `/dashboards/cash-flow` | income, expense and transfer by month and category |
| `GET` | `/dashboards/summary` | headline figures for the landing page |

Every dashboard response carries:

```json
{
  "data": [ … ],
  "has_conditional_money": true,
  "conditional_detail": { "pending_transfer_count": 2, "amount": {…} },
  "coverage": { "accounts_included": 17, "accounts_missing_data": 1 }
}
```

`has_conditional_money` is **mandatory to display** when true (FR-5.4,
Constitution V). A client that ignores it is non-conforming - which is why it is
a required field rather than an optional hint.

Served from the `reporting` views, so a dashboard figure and a report figure
cannot disagree (FR-5.5).

### Reports - `/reports`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/reports` | saved reports |
| `POST` | `/reports` | save a report |
| `GET` | `/reports/{id}` | detail |
| `PUT` | `/reports/{id}` | update |
| `DELETE` | `/reports/{id}` | delete |
| `POST` | `/reports/{id}/run` | run with bound parameters |
| `POST` | `/reports/adhoc` | run an unsaved query |
| `GET` | `/reports/schema` | the `reporting` view schema, documented |
| `GET` | `/reports/{id}/export` | results as CSV or JSON |

Execution goes to the restricted read-only connection every time (D5).
Parameters are bound. The response carries `row_count`, `truncated` and
`elapsed_ms` so a user can see when they hit the row cap rather than silently
receiving a partial answer.

`/reports/schema` returns column names, types and descriptions - enough to write
a query without reading the source (FR-6.6).

### Projections - `/models`, `/scenarios`, `/runs`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/models` | registered models with metadata |
| `GET` | `/models/{key}` | declared input and output schemas |
| `POST` | `/models/{key}/validate` | resolve inputs; report what is missing without running |
| `GET` | `/scenarios` | list |
| `POST` | `/scenarios` | create |
| `GET` | `/scenarios/{id}` | detail, current version |
| `PUT` | `/scenarios/{id}` | update - creates a new version |
| `POST` | `/scenarios/{id}/run` | run; returns a run id |
| `GET` | `/runs/{id}` | result, refusal detail, input hashes, staleness |
| `GET` | `/runs?scenario_id=` | run history |
| `POST` | `/runs/compare` | compare two runs side by side |

`GET /models/{key}` returns the schema the UI renders from, so a newly
registered model is usable with no frontend change (FR-8.4).

`POST /models/{key}/validate` is the dry run: it answers "could this be
computed, and if not, what is missing" without producing a stored result. It is
what the UI calls to show a "2 inputs unresolved" state before the user commits.

### Environment and system - `/system`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/system/environment` | `PRD` or `DEV`, and whether data is generated |
| `GET` | `/system/health` | liveness and readiness |
| `GET` | `/system/version` | application and engine versions |
| `POST` | `/system/export` | full data export |

`GET /system/environment` returns `{ "environment": "DEV", "data_is_generated":
true }`. The UI renders this permanently (FR-7.3, FR-11.4). It is
unauthenticated so the banner is correct even on the login screen - a user must
never type a real credential into a DEV instance believing it is PRD.

### DEV-only - `/dev`

Registered **only** when the environment is DEV. In PRD these routes do not
exist - they are not 403, they are absent (Constitution X).

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/dev/seed` | generate data from a seed value |
| `POST` | `/dev/reset` | drop and recreate DEV data |
| `GET` | `/dev/seed-profiles` | available generation profiles |

Route absence is the outer guard; the service-level environment assertion is the
inner one. Two independent checks, because the consequence of this one failing
is destroying somebody's real financial history.

---

## Rate limiting

Per-session limits on report execution and projection runs - the two endpoints
that can be made expensive. Exceeding a limit returns 429 with `Retry-After`.

## Conformance

A client conforms if it:

1. renders nulls as unverified, never as zero or blank;
2. displays `has_conditional_money` when true;
3. does not extend a balance series past `series_ends_at`;
4. escapes all provider-authored text;
5. surfaces refusal detail rather than reporting a generic failure.

These are contract tests, not documentation (FR-12.3). Each is a genuine way a
client can silently misrepresent someone's financial position while appearing to
work correctly.
