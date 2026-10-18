# Slice 02 — a file-import connector over invented balances

**Ruling:** `D-041` (planning repository, 2026-10-18). **Method:** `D-029`.
**Built by:** the agent, as slice 01 was. **Estimate:** none, on purpose: with
the agent building, a Kyle-hour estimate measures nothing, and slice 01's own
log showed agent-hour estimates running high (`slice-01-assessment.md` §2).
This document states scope; `LOG.md` records cost.
**Ends in:** a written assessment and a full stop, under slice 01's gate rule.

One technique, carried through every layer once: **ingesting from an outside
source**. A connector is anything that hands the platform balances it did not
type itself. A file is the smallest one that still raises the three problems
every later connector has:

1. **Re-import without double counting.** The same file twice is a no-op.
2. **A source that disagrees with what is stored.** A changed value restates; a
   value from a second source sits beside the first, and one rule picks which
   counts.
3. **Identity across sources.** `account_source`, built in slice 01 as
   migration 2 and never exercised, decides which account a file's row is.

Claim markers per `D-029`: `EXECUTED` · `SOURCE@<ref>` · `DOCS@<date>` ·
`INFERRED`. Anything load-bearing and `INFERRED` is a **gate**, not a caveat.

> **Where this sits against `spec.md`.** File import is neither `FR-1` (MCP) nor
> `FR-2` (HTTP API); the spec names `import:csv` only as a `source_system`
> example (`data-model.md`). What this slice exercises is the connector-agnostic
> half: `FR-4.2` (idempotent ingestion), `FR-4.3` (provenance on every sourced
> row, including the originating run), `FR-4.4` (one account, whatever the
> source), `FR-4.6` (an immutable run record) and the rule in `FR-1.5` that
> source text is data. `D-041`'s "in the spec's terms (FR-1/FR-2)" overstates
> this; the correction is owed to the planning repository as a dated append on
> `D-041`, not made here.

---

## What the slice is

A user binds an invented account to the identifier a file uses for it, uploads
a CSV or OFX file of balances, and sees what the import did: how many rows were
new, restated or unchanged, and why any were refused. The balances appear in the
slice-01 chart. Uploading the same file again changes nothing.

| Layer | What gets built |
|---|---|
| **Orchestration** | Unchanged topology. Resource limits on the compose services (`S-34`); an upload size limit that holds end to end |
| **Data** | Migration 4: `ingest_run` (immutable) and `account_balance.ingest_run_id`; the net-worth view's tie-break across sources |
| **Ingestion** | Two parsers (CSV, OFX) behind one interface, and one pipeline that owns validation, idempotency, restatement and the run record |
| **API** | Bind a source to an account; upload a file; list runs. `POST /balances` made idempotent. OpenAPI and the TypeScript client regenerated |
| **Web** | A binding form, an upload form with the run's result, and a run history |
| **Tests** | Parser unit tests, pipeline integration tests against Postgres, the contract test, one E2E upload |
| **Deploy** | The same compose path, with limits, and an upload proven through the published stack |

## What the slice is not

**No real data** (`D-041` §2). Every file the slice reads is generated, and
none is an export of a real account. Consequences, each named so nobody
rediscovers it:

- **The pre-commit hook stays out.** Its trigger is the first slice holding a
  real balance; this is not it. The CI hygiene job stays.
- **An invented OFX file proves the format, not the bank.** Institutions differ,
  and none is read here.
- **`D-034`'s per-account-type cutoffs stay deferred.** Invented data has no
  entry history to tune against; the trigger is the first slice holding real
  balances.
- **No credentials, no PRD, no second environment.** A file needs none of them.
- **No security tier.** Login, sessions and CSRF tokens (`security.md`) are not
  built. Kyle ruled on 2026-10-18 that **no real balance enters before a slice
  that builds them** (`D-041` amendment). This slice's interim defence is
  "Cross-site writes", below.

Also out: transactions, holdings and securities (balances only, as slice 01);
scheduled or watched-folder imports (a user uploads); QFX and other
OFX-dialects beyond what Spike E names; storing the uploaded file itself
(only its hash and name are kept); the projection engine, SQL reporting, Azure;
and **value-weighted coverage** (`C-389`), which is carried as an open question
below, not built.

## Dates and time — ruled by Kyle, 2026-10-18

**The data layer is UTC and uniform. Conversion happens only at the edge where
a person reads or enters a value.** Kyle: "UTC in the database gets converted to
CDT when I'm using the app, and when I pick or upload a CDT timezone it gets
stored as UTC." There are three kinds of value, and the rule applies to each
differently. The split was put to Kyle, who chose both halves.

| Kind | Columns | Stored | Shown |
|---|---|---|---|
| **Moment** | `observed_at`, `created_at`, `deleted_at`, `superseded_at`, `ingest_run.started_at` / `finished_at`, `account_balance.source_as_of` (new) | `timestamptz`, UTC | In the viewer's timezone, converted in the browser. A moment a person enters converts to UTC on the way in |
| **Calendar date** | `as_of_date`, `opened_on`, `closed_on` | `date`, no timezone | **As stored, never converted, in either direction.** Oct 15 is Oct 15 to every viewer. A date string is formatted as a string and never passed through `new Date(...)` for display: that call reads it as UTC midnight, which is the previous day anywhere in the Americas |
| **"Today"** | `current_date` in the spine; the API's default `to` | The **UTC** date | The browser uses the viewer's **local** date for the chart's end and as the default for a date the person picks. In the evening in the Americas the server is a day ahead; the browser asks only up to its own today, so it never shows that day |

**Imports, under the same rule:**

- **CSV:** `as_of_date` is ISO `yyyy-mm-dd` and nothing else. `03/04/2026` is
  **refused**, not guessed: it is March 4 or April 3 depending on who wrote it.
  A guess goes wrong only when the day is 12 or less, so it passes every
  validation.
- **OFX:** `as_of_date` is **the date as written** in `DTASOF` (Spike E), the
  day the institution meant. When `DTASOF` carries a time, the full stamp is
  converted to UTC with its stated offset and stored as
  `account_balance.source_as_of`, for provenance. Example: a `DTASOF` of
  2026-10-15 at 23:00 `[-5:CDT]` is dated **2026-10-15**, with `source_as_of` 2026-10-16 04:00 UTC,
  and shown to a CDT viewer as "as of Oct 15, 11:00 PM CDT". A stamp with **no
  offset** is read as UTC. That is the specification's default as recalled, and
  stays `INFERRED` until the specification is read.
- **Nothing in the import path builds a `DateTime` or `DateTimeOffset` from a
  balance date.** The only moment an import creates is `source_as_of`, and only
  from a stamp that carries a time.

**Pinned, not assumed:** the database session timezone is set to `UTC`
explicitly (`ALTER DATABASE ... SET timezone`, in migration 4). Today
`current_date` is UTC only because the `postgres:18.3` image defaults to it.
The API reads its clock through `TimeProvider`, so tests can pin it.

## Cross-site writes — refused twice, and tested (Kyle, 2026-10-18)

The API listens on loopback with **no authentication**. Between any website the
viewer happens to visit and a write to this API there is only the browser. Spike F
found that a form upload is exactly the request such a site can send. Slice 02
does not build the security tier (`security.md`: login, `SameSite` cookies, CSRF
tokens). Kyle ruled that no real balance enters before a slice that does
(`D-041`, amendment of 2026-10-18). Until then, cross-site writes are refused by
**two independent layers**, and **each is tested**, so the defence does not rest
on an argument.

1. **Content types a cross-site page cannot send without a preflight.** Every
   write endpoint accepts only `application/json` (`POST /accounts`,
   `/balances`, `/sources`), or `text/csv` or `application/x-ofx` (`POST
   /imports`). Anything else gets `415`, in particular the three types a plain
   HTML form can send: `application/x-www-form-urlencoded`,
   `multipart/form-data` and `text/plain`. The API answers no CORS preflight, so
   a cross-origin `fetch` carrying an accepted type is never sent. [`INFERRED`
   from the Fetch standard; DoD 11 raises it to `EXECUTED`.]
2. **`Sec-Fetch-Site`, which a page's script cannot set.** A small middleware
   refuses with `403` any `POST`, `PUT`, `PATCH` or `DELETE` whose
   `Sec-Fetch-Site` is `cross-site` **or `same-site`**. A site ignores ports, so
   another server on `localhost` is the same site. `same-origin` passes, as does
   `none`, which a browser sends for a request the user typed. A request with no
   header passes too: a non-browser client such as `curl` or the tests sends
   none, and is not the threat this layer exists for. Under `aspire run`, the
   Vite proxy forwards the browser's `same-origin` header unchanged.
   [`INFERRED`; DoD 11.]

**Reads** were already covered in slice 01. The same-origin policy keeps a
cross-site page from reading a response. `AllowedHosts`
(`localhost;127.0.0.1;[::1]`) refuses any other `Host`, which is what DNS
rebinding needs.

---

## Step 0 — the spikes (do these first)

Lettering continues from slice 01 (A–D). Each claim below is load-bearing and
sits at `INFERRED`; the step that depends on it does not start until its spike
has raised it to `EXECUTED` or replaced it.

### Spike E — what does it take to read a balance out of OFX in .NET?

> **Claims under test.** (1) A balance and its date are carried in `LEDGERBAL`
> (`BALAMT`, `DTASOF`) inside a statement response, and the account's identifier
> in `ACCTID` under `BANKACCTFROM` or `CCACCTFROM`. (2) OFX 1.x is SGML with
> optional closing tags and is **not** well-formed XML; OFX 2.x is XML. (3) No
> maintained .NET library is worth a dependency for reading those few elements.
> [`INFERRED` for all three.]

Run: write one invented file in each version by hand, read it with the smallest
parser that could work (a tokenizer for 1.x, `XmlReader` for 2.x), and survey
NuGet for a maintained alternative. Record which elements were read, which
versions the slice accepts, and the date format and offset rule for `DTASOF`.

**The result decides:** whether step 2's OFX parser is one parser or two, and
whether the slice accepts 1.x at all. If 2.x XML is read, the reader runs with
DTD processing **prohibited** and external entities unresolved; that is not up
for the spike to decide.

> **RAN — all three claims hold; step 2 writes its own reader.** [`EXECUTED
> 2026-10-18`, .NET SDK 10.0.402, `spikes/spike-e-ofx/RESULTS.md`] Thirteen
> generated files. A reader of about 150 lines reads 1.0.2 SGML, with leaves
> closed or unclosed, and 2.2 XML into one element tree. **Both versions are
> accepted.** OfxNet 1.8.1, the only recently published .NET 10 parser, was run
> on the same files and is **not adopted**:
> - it read `1234,56` as `123456`, and on a de-DE machine it read the plain
>   `1234.56` as `123456`. The balance comes out 100 times too large, silently;
> - it processes DTDs, and expanded an internal entity into the account id;
> - it returned nothing, without an error, for an investment statement;
> - it threw on an unsigned `[0:GMT]` offset.
>
> Step 2 inherits these rules:
> - `DtdProcessing.Prohibit`, which the run showed refusing all four `DOCTYPE` files;
> - invariant-culture amounts only;
> - an unknown statement type refuses the file;
> - the as-of date is taken as written, not converted to UTC.
>
> The element names are corroborated by OfxNet's independent reading. They were
> **not** read from the OFX specification itself, which stays `INFERRED`.

### Spike F — how large a file gets through, and what refuses it?

> **Claims under test.** (1) Kestrel refuses a request body over 30,000,000
> bytes by default, and the multipart form reader has its own, larger limit.
> (2) A minimal-API endpoint that binds `IFormFile` carries antiforgery metadata
> and fails at request time unless antiforgery middleware runs or the endpoint
> opts out. (3) The Vite dev proxy forwards a multipart body unchanged.
> [`INFERRED` for all three; (1) and (2) are recalled from documentation, not
> read.]

Run: an endpoint taking one file, driven at the slice's limit and one byte over,
under `aspire run` (through Vite) and under the published compose stack. Record
the status code and response body at each point.

**The result decides:** the endpoint's binding (`IFormFile` with an explicit
antiforgery decision, or a raw-body read), the one number this slice enforces
(proposed: **1 MiB and 10,000 rows**, sized for a balance file and not for
transactions), and where it is enforced so a too-large file is refused before
it is read, not after.

> **RAN — all three claims hold; the upload is the raw request body.**
> [`EXECUTED 2026-10-18`, .NET 10 direct, through Vite 8.3.4, and in a Linux
> container; `spikes/spike-f-upload/RESULTS.md`]
>
> **The claims:**
> - Kestrel's default is exactly 30,000,000 bytes: `200` at it, `413` one byte over.
> - An `IFormFile` endpoint with no antiforgery decision fails with `500`
>   ("contains anti-forgery metadata, but a middleware was not found").
> - Vite forwards bodies unchanged within limits.
>
> **Three findings decide the binding:**
> 1. **A multipart upload is written to disk.** The form reader spilled every file
>    over 64 KiB to an `ASPNETCORE_*.tmp` file while the request ran.
> 2. **Taking a form means opting out of antiforgery.** A multipart POST is what any
>    website's plain form can send to `127.0.0.1` without a preflight.
> 3. **Through the dev proxy, "too large" arrives as `502`, not `413`**: 19 times in
>    20 at 1 byte over 1 MiB.
>
> **So `POST /imports` takes the file as the raw body:**
> - `text/csv` or `application/x-ofx` only. Anything else is `415`, which was shown
>   refusing a multipart post, and which is also the cross-site defence.
> - `RequestSizeLimitAttribute(1 MiB)` on the endpoint, shown refusing at 1 MiB + 1
>   with and without `Content-Length`.
> - Read into memory, so no temp file.
>
> The browser checks `File.size` before sending. **1 MiB stands.** The 10,000-row
> limit is the parser's. **Still a gate on step 3:** how the raw body is described in
> OpenAPI, and whether the generated client sends a `File` unchanged (`INFERRED`).

### Spike G — which balance does the net-worth view count when two sources report the same day?

> **Claim under test.** `v_net_worth_daily` picks the newest live balance per
> account with `ORDER BY b.as_of_date DESC LIMIT 1` and no further key
> [`SOURCE@20260924134306_Initial.cs`]. Slice 01 had one source, so the pick was
> unique. With a manual and an imported balance on the same date the pick is
> **unspecified**: Postgres may return either, and may change its mind between
> queries. [`INFERRED` that it is observable; the SQL is read, not run.]

Run: one account, a manual balance and an import balance on the same date with
different values; query the view repeatedly, and under a plan that changes
(more rows, `ANALYZE`). Then add the tie-break below and show it fixed.

**The proposed rule** (Constitution IV: sources are not equally strong): newest
`as_of_date` first; on the same date, **strongest `source_strength`** first
(the enum is declared strongest first, so ascending); then newest
`observed_at`. A stronger source never reaches back past a newer date: a manual
figure typed today outranks an export from last week. **This rule is the one
judgement in the slice that Kyle should read before step 2**; everything else
follows from the spec.

> **RAN — the claim holds, and the rule is RULED.** [`EXECUTED 2026-10-18`,
> PostgreSQL 18.3 on the real migrations, `spikes/spike-g-same-day-pick/RESULTS.md`]
> With a manual `100` and an export `200` on one date, the unfixed view returned
> `100` when the manual row was written first and `200` when the export was,
> under the default plan and a forced sequential scan alike. **Write order
> decides net worth**, and nothing on the page would show it. The plan did not
> change the answer here; "changes its mind between queries" was not observed.
> **Kyle approved the rule above on 2026-10-18.** A final `id` key is added only
> to make the order total. The fixed view passes seven assertions across both
> insertion orders and both plans, and the same assertions fail on the unfixed
> view. Step 2's migration 4 copies `03-fixed-view.sql`; the integration suite
> gets the three cases through the API.

---

## The build

### 1. Ingestion boundary

`plan.md` "Ingestion": a connector **parses and maps**; one **pipeline** owns
what an import means to the data. Here:

- `IBalanceSource` (or similar): bytes in, a list of parsed rows out, each
  `(source_id, as_of_date, amount, currency?)`, or per-line errors. No database
  access. Two implementations, CSV and OFX.
- `ImportPipeline`: takes parsed rows and a `source_system`, and does
  everything else in step 2.
- **The architecture test gains a rule:** the parsers reference neither EF Core
  nor the `DbContext`. Seen failing once, as slice 01's rules were.

The parsers live where `plan.md` puts connectors (`Infrastructure`), unless the
architecture rule is easier to hold as its own project; that is the builder's
call, recorded in the commit.

**The CSV format is this platform's own**, not an institution's: a header row
`account,as_of_date,balance` and an optional `currency` column which, if
present, must be `USD` (`M-11`, single currency). Dates ISO 8601. Balances are
decimal strings parsed by the existing `MoneyText` rules, so the API and the
importer cannot disagree about what a number is. Any other column is refused,
not ignored: an unread column is a silent drop.

### 2. Data and the pipeline

**Migration 4** (applied to a database that already has rows, as migration 2
was):

- `ingest_run`: `id`, `source_system`, `file_name`, `file_sha256`,
  `started_at`, `finished_at`, `status` (`succeeded` | `failed`), counts
  (`rows_read`, `created`, `restated`, `unchanged`), `error_detail`. **Immutable
  once written**, enforced by a trigger that refuses `UPDATE` and `DELETE`, as
  `account_close_guard` enforces `closed_on`: `FR-4.6` is too important to rest
  on application code.
- `account_balance.ingest_run_id`, nullable (manual rows have none),
  referencing `ingest_run` (`FR-4.3`).
- `account_balance.source_as_of`, `timestamptz` nullable: the source's own
  stamp, as a UTC moment, when it carried a time ("Dates and time", above).
- The database's `timezone` set to `UTC`.
- The view's tie-break from Spike G.

**`source_system` per format:** `import:csv` and `import:ofx`
(`data-model.md`'s convention). **`source_strength`:** `Export` for both.

**The pipeline, in order:**

1. **Refuse before reading** what Spike F says to refuse.
2. **Parse everything first.** Any line that fails, the whole file fails.
3. **Resolve identity.** Each row's `source_id` is looked up in `account_source`
   under the file's `source_system`. **An unbound identifier fails the file**;
   the importer never creates an account or guesses a binding. The user binds,
   then uploads again.
4. **Validate against the account**: inside its `opened_on`/`closed_on` window
   (the existing check constraint is the backstop, not the check).
5. **Write in one transaction**, every row through the same restatement rule as
   `BalanceRecorder`: **unchanged** if a live row for `(account, date, source)`
   holds the same amount, **restated** if it holds a different one, **created**
   otherwise. One transaction for the file, inside the execution strategy, so a
   retry replays the whole import and never half of it.
6. **Record the run**, succeeded or failed. A failed run writes no balance and
   still writes its row.

**All or nothing, deliberately.** `data-model.md` lists a `partial` status; this
slice never produces it. A partly imported balance file leaves net worth
describing a moment that never existed, and re-importing after a fix is cheap.

**Idempotency, stated as the test will state it:** the same file twice produces
a second run with `created = 0, restated = 0, unchanged = N`, and no new
`account_balance` row (`FR-4.2`: no duplicates, no altered counts). A file
whose hash matches an earlier run is **still processed**, not short-circuited:
the outcome must come from the data, not from remembering the bytes.

**`POST /balances` gets the same rule** — the slice-01 deferral (`D-030`
Amendment 1). Posting the same value twice today supersedes a row with an
identical one; after this slice it returns the existing row, `200` instead of
`201`, and writes nothing. A different value still restates. **No
`Idempotency-Key` header:** the key is the content `(account, date, source,
amount)`, which a retried request repeats on its own.

### 3. API

All under `/api/v1`, OpenAPI regenerated and committed, client regenerated:

- `POST /accounts/{id}/sources` — `{ sourceSystem, sourceId }`. Only the two
  `import:*` systems are accepted. A pair already bound to **any** account is
  refused (`account_source_identity` is unique across all rows: a source id
  once seen is never re-bindable).
- `POST /imports?fileName=...` — one file as the **raw request body** (Spike F;
  not `multipart/form-data`). The format comes from `Content-Type`: `text/csv` or
  `application/x-ofx`, and anything else is `415`. The limit is
  `RequestSizeLimitAttribute(1 MiB)` on the endpoint, and the body is read into
  memory, never a temp file. Returns the run: counts, and on failure the refused
  lines with line numbers and reasons. `201` with the run whether it succeeded or
  failed; `413` or `415` only for what is refused before a run begins.
- `GET /imports` — the run history, newest first, bounded (`SE-23`).
- `BalanceResponse` gains `ingestRunId`.

**File text is data** (`FR-1.5`, applied to a file): an identifier or a file
name is stored as given, length-bounded, returned escaped, and never reaches a
query except as a parameter. An error message quotes a refused line **without**
echoing more than a bounded prefix of it.

### 4. Web

- On the account row: bind an import identifier.
- An upload form: file picker, submit, then the run's counts and any refused
  lines. Keyboard-reachable and labelled, with the result announced
  (`aria-live`), as slice 01's forms are. It checks `File.size` against 1 MiB
  **before** sending, because through the dev proxy an over-limit upload
  usually comes back as `502` (Spike F). It sends the `File` as the body with
  its `Content-Type`, not a `FormData`.
- A run history table.
- The chart is unchanged; it already reads the view.

### 5. Tests

| Layer | What it proves |
|---|---|
| **Unit** | CSV and OFX parsers: the happy file, every refusal named in step 1, and OFX per Spike E's versions. The Domain coverage floor holds |
| **Integration** | Against Postgres (Testcontainers): same file twice is a no-op; a changed value restates; unbound identifier fails with nothing written; a failed run is recorded; `ingest_run` refuses UPDATE and DELETE; Spike G's tie-break; `POST /balances` twice writes one row |
| **Architecture** | The new rule: parsers reach no database |
| **Contract** | The committed OpenAPI document, as slice 01 |
| **E2E** | Bind, upload a generated file in the browser, see the counts and the chart move; upload it again, see `unchanged` |

**Fixtures are generated in code, never committed as files.** Two reasons. An
invented-data rule is easier to hold when no file in the repository looks like
an export. And OFX's `DTASOF` is a fourteen-digit timestamp
(`YYYYMMDDHHMMSS`), which the hygiene scanner's account-number rule refuses on
sight in a data file — correctly, since it cannot tell the two apart. A builder
in the test project writes the file at test time from obviously invented values
(institution `Example Bank`, identifiers like `EXAMPLE-CHK-1`). If a committed
fixture ever becomes necessary, its allow-list entry is reviewed with a reason,
per `scripts/hygiene.py`; never a wildcard.

### 6. CI

No new workflow. The new tests run in the existing jobs; the E2E stays local,
as slice 01's middle cut left it.

### 7. Deploy

- **Resource limits** (`S-34`, deferred by slice 01 to "the first slice that
  runs an ingest batch"): through `Service.Deploy` → `deploy.resources.limits`
  in the app model, where slice 01 put the other overrides. Memory on `api` and
  `postgres`, sized from what Spike F's largest file actually used, not guessed.
- **The upload limit holds in the published stack**, shown with the one-byte-over
  file from Spike F against `docker compose up`.
- Everything slice 01 proved still holds: API on loopback, database port
  unpublished, data survives a restart.

---

## Definition of done

1. Spikes E, F and G run; their claims restated at `EXECUTED` with command and
   date, or replaced by what was found.
2. Under `aspire run`: bind, upload a CSV and an OFX file, both appear in the
   chart; the same files again change nothing.
3. Under `docker compose up` from the published artifact: the same, with the
   resource limits present in the compose file and the over-limit file refused.
4. The tests in step 5 green; the CI jobs green on a pull request.
5. The architecture test passes, including the new rule.
6. The committed OpenAPI document regenerates identically, and the client
   compiles.
7. The upload form and run history are keyboard-reachable, labelled, and axe
   reports no violations.
8. No committed file is an export, real or invented; the hygiene job is green
   with no new allow-list entry, or each new one carries its reason.
9. Each obligation below is done, or recorded as not done with a reason.
10. **The dates rule holds away from UTC.** The rule was not exercised before,
    because every test process and container ran in UTC. Required:
    - the unit and integration suites green with the process timezone at
      UTC+14 (`Pacific/Kiritimati`) and at UTC−11 (`Pacific/Pago_Pago`);
    - the E2E green with the browser at `America/Chicago` (Playwright
      `timezoneId`), its "today" computed in that zone and not from the test
      machine's clock;
    - a pinned clock at 23:30 local, and one across the US daylight-saving
      change on 2026-11-01;
    - `DTASOF` cases: 23:59 at `-5` keeps the written date; date only; `[0:GMT]`
      and `[+0:GMT]`; `+14`;
    - a CSV with `03/04/2026` refused;
    - a calendar date rendered in the browser unchanged in every one of those
      timezones.
11. **Cross-site writes are refused, each layer shown on its own and both
    together.**
    - *Integration*, against the API: on every write endpoint, each of the
      three form content types gets `415`, and nothing is written. A valid body
      with `Sec-Fetch-Site: cross-site`, and again with `same-site`, gets
      `403`, and nothing is written. `same-origin`, `none` and no header are
      accepted.
    - *E2E*, in a real browser: Playwright serves an attacker page from a
      second origin, `http://attacker.localhost:<port>`, and from another port
      on `127.0.0.1`. The page tries:
      - a plain HTML form post of a file to `/api/v1/imports`;
      - a `fetch` of `text/csv` to `/imports`;
      - a `fetch` of JSON to `/balances`.

      Afterwards, the API shows nothing was written.
    - The E2E records whether each request **reached** the API, so a browser's
      own local-network blocking cannot pass the test for the wrong reason.

## Obligations this slice carries

| From | Item | Here |
|---|---|---|
| `D-030` Amendment 1 | Idempotency and correction on `POST /balances` | **Built**, step 2 |
| Kyle, 2026-10-18 | Dates and time: UTC data layer, conversion at the edge; calendar dates never converted | **Built**, steps 2–5; DoD 10 |
| Kyle, 2026-10-18 | Cross-site writes refused by two layers, both tested | **Built**, steps 3 and 5; DoD 11 |
| `D-041`, amendment of 2026-10-18 | No real balance before a slice that builds `security.md`'s tier | **Holds**: this slice reads invented data only (`D-041` §2) |
| `slice-01.md` (`S-34`) | Resource limits, "the first slice that runs an ingest batch" | **Built**, step 7 |
| `D-034` §5 | Per-account-type staleness cutoffs | **Deferred**: no real entry history (`D-041` §4) |
| `C-389` | Coverage weighted by value, not account count | **Open question**, below |
| `D-030` Amendment 1 | Pre-commit hook | **Deferred**: the first real balance |
| `slice-01-assessment.md` | Internal/front network split (`S-25a`) | Not this slice's technique; stays open |
| `slice-01-assessment.md` | The 629 kB bundle | Not this slice's technique; stays open |
| Dependabot PR #12 | `@types/node` on the runtime's major | Held in `.github/dependabot.yml`; nothing to do |

### The open question — value-weighted coverage (`C-389`)

The chart's status counts accounts: one stale account of five is `Partial`
whatever its size. An importer makes the case sharper, because a file may
refresh four small accounts and miss the large one. Weighting by value needs a
value for the account that is missing, which is exactly the figure that is
stale. **Not built here and not ruled here;** the assessment says whether the
slice made the case for it. Ruling it is Kyle's call.

## The gate — slice 01's rule, unchanged (`D-041` §6)

`LOG.md` carries on with both columns, one line per session, written as it goes.

> **The default at the end of slice 02 is STOP.** A third slice requires a new
> decision file in the planning repository.
>
> The default is overridden only if the slice-02 assessment names **three
> techniques this slice taught that Kyle did not already have** — each with the
> commit that demonstrates it, and **not** the six slice 01 already claimed —
> **and** Kyle's hours stayed under **200** (`LOG.md`, `Kyle h`).
>
> **Explicitly NOT reasons to continue:** that the slice works, and that the
> next slice is obvious.

**Stated, not smoothed:** the agent builds, so the hours limb cannot trip, and
the gate rests on the techniques limb and on Kyle's own judgement of what the
slice taught. An untripped threshold is not evidence the slice was cheap.

## Then stop

Write `slice-02-assessment.md`. It answers:

- What did it cost, in both columns?
- Which of Spikes E, F and G were wrong, and what else in `specs/` is suspect as
  a result?
- Which of the three problems at the top did the slice actually exercise, and
  which only in form?
- Did the slice make the case for value-weighted coverage?
- **Is a third slice wanted at all** — and if so, which technique is it for?

A third slice begun without that assessment written has broken the gate.
