# Slice 01 — manual balance entry to a net-worth chart

**Ruling:** `D-030`, scope amended by Kyle 2026-09-18 (the MIDDLE cut, below).
**Method:** `D-029`. **Estimate:** **~135 hours** (band 105–170), about 20–26
weeks at three 2–3h evenings a week — effective throughput is 5–8h/week, not 9.
**The original 50–80 does not exist**: two independent re-costings put the
un-amended slice at 100–160h and the amended slice at 125–205h, and the floor
that still exercises every layer is 80–120h.
**Ends in:** a written assessment and a full stop.

> **THE MIDDLE CUT — ruled 2026-09-18.** Taken out of this slice, each with a
> reason, to hold ~135h without losing technique: the ~14–22h of pre-step-1 spec
> paperwork (documents being frozen anyway — **`Amendment 3` stays**, see step 2);
> idempotency and correction semantics on `POST /balances` (**DEFERRED to the
> first connector slice** — nothing in this slice retries a POST); the
> **pre-commit** hook, keeping the CI hygiene job (**trigger: it is a hard
> prerequisite of the first slice holding a real balance**); the custom Recharts
> tooltip for `polite` (keep `filterNull={false}` and the accessible name, which
> are one-liners); drift-gate **determinism** work, keeping generation and the
> committed document with the gate advisory; and the E2E **in CI**, keeping the
> E2E itself run locally. NOT cut, and not negotiable: the deploy step, the
> second migration, the integration test against the net-worth view, the tabular
> equivalent, and the `AllowedHosts` line. Those five are the vertical-slice
> property rather than polish.

One thin feature, carried through every layer once. The goal is practising the
stack, not shipping a tool — so this document is deliberately narrow, and the
thirteen other documents in this tree are a **reference you consult**, not a plan
you execute.

Claim markers per `D-029`: `EXECUTED` · `SOURCE@<ref>` · `DOCS@<date>` ·
`INFERRED`. Anything load-bearing and `INFERRED` is a **gate**, not a caveat.

---

## What the slice is

A user types a balance for an account. It is stored with provenance. A chart
shows net worth over time. That is the entire feature.

| Layer | What gets built |
|---|---|
| **Orchestration** | Aspire AppHost: Postgres (persistent volume) + API + Vite frontend |
| **Data** | `institution`, `account`, `account_balance` with provenance columns; EF Core migrations from a separate Migrator project |
| **API** | Four endpoints, OpenAPI committed, TypeScript client generated from it |
| **Web** | One account list, one net-worth chart |
| **Tests** | One of each layer: unit, integration, contract, E2E |
| **CI** | One GitHub Actions workflow running all four |
| **Deploy** | `aspire publish` → compose → `docker compose up` locally |

## What the slice is not

**No PRD environment. No credentials. No connector. Generated data only.**

That is the decision that collapses the scope, and it does so by construction:
most of the review's Must Fix tier is not deferred here, it is **not reachable**.

Also out: SQL reporting, the projection engine, allocation and cash-flow
dashboards, the second environment, Azure, saved reports, the tax domain, and the
entire security tier beyond the basics named below.

---

## Step 0 — the spikes (do these first)

`M-3`, `M-4` and `M-5` are load-bearing claims resting at `INFERRED`. The slice
cannot be built on them. **Roughly one day, and the highest-value day in the
project.** Each spike exercises one mechanism and nothing else.

### Spike A — can two Aspire stacks run at once, and how is an environment selected?

> **Claim under test.** `environments.md` says `aspire run --launch-profile prd|dev`
> runs two stacks simultaneously.
> **Evidence against:** `SOURCE@microsoft/aspire@b477bdd` — `RunCommand` calls
> `FindAndStopRunningInstanceAsync` before starting, with no opt-out, keyed on the
> AppHost file path alone; Aspire's own string directs users to separate
> directories.

Run: one AppHost, `aspire run`, then `aspire run` again from a second terminal.
Observe whether the first is stopped. Then try two AppHost directories.

**Records:** whether launch profiles can select an environment at all, and what
actually parameterises volume name, database name and ports.
**If it fails as expected:** environment selection moves to
`builder.AddParameter(...)`/configuration, and `environments.md` is corrected.
**Blocks:** everything, because the app model's shape depends on the answer.

> **RAN — it failed as expected.** [`EXECUTED 2026-09-24`, Aspire CLI 13.5.4,
> `spikes/spike-a-two-stacks/RESULTS.md`] A second
> `aspire run -- --STACK_ENV=prd` from the same directory stops the first stack,
> **with or without `--isolated`**. Two directories run side by side, the second
> with `--isolated` for its dashboard ports. `aspire run` has **no
> `--launch-profile` option** and ignores one passed through; the first profile
> in `launchSettings.json` always applies, so launch profiles do not select an
> environment either. **Selection is configuration**: `STACK_ENV` from the
> command line named the volume, database and container, and data written in one
> stack was absent from the other. Step 1 uses `builder.AddParameter(...)` on
> that configuration. `environments.md` and `plan.md` carry the correction in
> their banners; this slice has one environment, so neither is rewritten.

### Spike B — how does the browser learn the API's address?

> **Claim under test.** `plan.md` says service discovery supplies it, so no
> environment hardcodes an origin.
> **Evidence against:** `SOURCE@microsoft/aspire@b477bdd` — `WithReference` injects
> into the Vite dev-server **process**; Vite exposes only `VITE_`-prefixed
> variables to client code, and bakes them at build time.

Run: `AddViteApp` with `.WithReference(api)`, and a React component that calls
`/health`. Observe whether it resolves in `aspire run`, and then in a published
compose stack.

**Records:** the actual mechanism. Candidate fix is
`PublishAsStaticWebsite(apiPath: "/api", apiTarget: api)` — a YARP same-origin
proxy, so the browser uses a relative path everywhere.
[`SOURCE@microsoft/aspire@b477bdd`; note it is `[Experimental("ASPIREJAVASCRIPT001")]`]
**Blocks:** the web layer. This is what fails on day one otherwise.

> **RAN — the claim is wrong, and the template already answers it.**
> [`EXECUTED 2026-09-24`, Aspire 13.5.4, `aspire-ts-cs-starter` 13.5.4,
> `spikes/spike-b-api-address/RESULTS.md`] In headless Edge, client code could
> see only `BASE_URL, DEV, MODE, PROD, SSR` in `import.meta.env`, in both modes.
> The API's address exists only in the Vite **process**. A relative
> `fetch('/api/health')` returned `200` both ways. Under `aspire run`, Vite's
> `/api` proxy forwarded it. Under `aspire deploy`, the server served the page
> itself from `wwwroot` via `PublishWithContainerFiles`: one origin, no
> `webfrontend` service in compose. **Use that, not `PublishAsStaticWebsite`**,
> which was not run and whose experimental flag is not needed. API routes live
> under `/api`. The published server port binds `0.0.0.0` and `[::]`, so DoD 3's
> loopback binding is a step-7 override.

### Spike C — how does the API wait for migrations?

> **Claim under test.** `plan.md` gives migrations to a long-running Worker and
> says the API waits.
> **Evidence against:** `SOURCE@microsoft/aspire@b477bdd` — `WaitForCompletion`
> requires the dependency to **terminate**; a long-running Worker never does.

Run: a one-shot Migrator project that exits 0, with
`api.WaitForCompletion(migrator)`. Confirm the API does not start until it exits.
Then confirm the same shape survives `aspire publish` as
`depends_on: { condition: service_completed_successfully }`.

**Records:** whether the published compose file carries the dependency.
**Blocks:** migrations, therefore the data layer.

> **RAN — the one-shot Migrator works, and the dependency survives publish.**
> [`EXECUTED 2026-09-24`, Aspire 13.5.4, `spikes/spike-c-migrator/RESULTS.md`]
> Under `aspire run` the API waited from 11:13:30 until the Migrator exited 0 at
> 11:13:47 and started at 11:13:48. When the Migrator exits 1 instead, the API
> is `FailedToStart` and its process never runs. `aspire publish` writes
> `api: depends_on: migrator: condition: "service_completed_successfully"`.
> **Step 2 inherits one consequence:** the Migrator's own `WaitFor(db)` publishes
> as `service_started`, not `service_healthy`, and `pg` gets no healthcheck. So in
> compose the Migrator can start before Postgres accepts connections. It must
> retry its connection, or step 7's override must add the healthcheck.
> [`INFERRED` for the race itself: the file says `service_started`; compose was
> not run.]

**Gate:** none of the three spikes' subjects may be built on until its spike has
run and its claim is restated at `EXECUTED` with the command and date.

---

## The build

### 1. Skeleton (~14–24h, incl. toolchain)

> **Two corrections worth knowing before you start.** The `aspire-ts-cs-starter`
> template ships the AppHost wiring, `vite.config.ts` proxying `/api` to
> `SERVER_HTTPS || SERVER_HTTP`, and `server.PublishWithContainerFiles(webfrontend,
> "wwwroot")` — Spike B's answer and step 7's publish path, free
> [`SOURCE@Aspire.ProjectTemplates@13.5.4`]. But **that template has no
> `ServiceDefaults` project** (the non-TS `aspire-starter` does); it inlines
> `Server/Extensions.cs`. "ServiceDefaults for telemetry and health" is a 0.5–1h
> wiring decision here, not a given. Separately, **Playwright does not need
> `pwsh`**: `Microsoft.Playwright.Program.RunWithResult(string[])` is public, so a
> three-line console project installs browsers [`SOURCE@Microsoft.Playwright@1.62.0`]
> — `C-283`'s PowerShell 5.1 constraint costs about thirty minutes, not an evening.

Aspire AppHost with Postgres (persistent named volume
[`SOURCE@microsoft/aspire@b477bdd` — `WithDataVolume(name)` takes an explicit
name]), an API project, a Migrator project, and the Vite app. `ServiceDefaults`
for telemetry and health.

Verify: `aspire run` brings everything up, the dashboard shows all resources, the
React shell reaches `/health` through whatever Spike B established.

**Architecture test now, not later.** The invariant, corrected — the obvious
wording breaks step 5 of this same document:

> **No project outside `AppHost` may reference `Aspire.Hosting.*`, with one named
> exception: the integration-test project may reference `Aspire.Hosting.Testing`
> and the `AppHost` project itself.** `Domain` references nothing outside the
> shared framework.

`S-25c`'s premise is right and verified: `Aspire.*` **client** integrations belong
in the `Api` and `Worker` composition roots — `AddNpgsqlDbContext<T>` is an
ordinary `IHostApplicationBuilder` extension wiring health checks, tracing,
metrics and `EnableRetryOnFailure`, none of it from `ServiceDefaults`
[`SOURCE@microsoft/aspire@b477bdd`]. But `DistributedApplicationTestingBuilder`
ships in **`Aspire.Hosting.Testing`**, which *matches the prefix*, and step 5
requires that type. Both official templates reference it directly, and the
starter's test project also carries a `ProjectReference` to the AppHost. **An
unexceptioned invariant fails the moment the integration-test project exists** —
and would then be quietly rewritten to exclude that project, at which point it no
longer says what this document says it says.

Assert over **direct** `PackageReference`/`ProjectReference`, not the transitive
closure, or the AppHost project reference defeats it anyway. The prefix is
otherwise a clean discriminator: of 39 client integrations under `src/Components`,
none is named `Aspire.Hosting.*`.

### 2. Data (~8–12h)

Three tables. Every row carries `source_system`, `source_id`, `source_strength`,
`observed_at`. Money is `numeric(19,4)` with an explicit `currency`.

**`M-9` applies and bites immediately** — manual entry means you *will* restate a
balance:

```sql
UNIQUE (account_id, as_of_date, source_system)
  WHERE deleted_at IS NULL AND superseded_at IS NULL
```

**The pointer is INVERTED from what earlier drafts said, and the write order is
the part that bites.** `superseded_by` pointing forward at a row that does not
exist yet aborts on the foreign key [`EXECUTED 2026-09-18`, PG 16.13]. Use
`superseded_at timestamptz` as the discriminator plus a **backward** `supersedes
uuid` FK on the new row. Then, in one transaction and **in this order**:

1. `UPDATE old SET superseded_at = now()` — no forward reference;
2. `INSERT` the new row with `supersedes = <old id>` — its FK target exists.

**Reversing those two fails**, and it is the failure a builder will hit first:
`INSERT`-then-`UPDATE` leaves both rows live for the instant between statements
and trips the partial index — `ERROR: duplicate key value violates unique
constraint` [`EXECUTED 2026-09-18`]. In EF Core this is an explicit transaction
with `ExecuteUpdate` then `Add`, **not** one `SaveChanges`.

A partial unique index cannot be `DEFERRABLE` [`DOCS@2026-09-18`], which is true
and does **not** force the original order — `EXCLUDE USING gist (…) WHERE (…)
DEFERRABLE INITIALLY DEFERRED` gives partial *and* deferrable uniqueness with
`btree_gist` [`EXECUTED 2026-09-18`]. The inversion above is preferred because it
needs neither deferral nor raw migration SQL.

**Spike D runs this before anything else is built** — schema, restatement and
view against a throwaway Postgres. 3–5h, the cheapest hours in the project, and
it is where every defect in this layer was found.

**THE STALENESS CUTOFF IS 90 DAYS — `D-034`, ruled 2026-09-20.** Constitution
`Amendment 3` condition 2 says the cutoff "is named in `slice-01.md` step 2 and
is part of this amendment, not an implementation detail." **It was not named
here until now** — `R2-B7` asked for it and the request was never carried into
the step, so the constitution pointed at a value that did not exist and whoever
wrote this migration would have invented one silently. That gap is `C-389`.

Store it as a **setting**, never a literal in a view body: a number you have to
read a view definition to discover is an implementation detail, which is what the
amendment forbids it from being.

Three rules travel with it, and all three are the amendment's, not the view's:

- **Past the cutoff an account goes UNVERIFIED and STAYS in the denominator.**
  Its series has not ended; we have not verified it. That is Principle II. Only
  `closed_on` removes it from the window, which is Principle III proper — past a
  series end no point is invented at all. Dropping a merely-stale account from
  the denominator instead is `M-1` reappearing at the right-hand edge: the total
  shrinks and the row does not say why.
- **Carry `accounts_in_window` and `accounts_verified` beside the three columns
  `Amendment 3` names.** Without the denominator, a total that fell because an
  account went stale is indistinguishable from one that fell because an account
  closed.
- **The coverage signal is a count, not a warning triangle**, and there is no
  second stored threshold — `max_staleness_days` is already continuous, so
  "getting stale" is a sort order. At the 25 accounts this slice's own
  performance analysis assumes, a *fully verified* chart happens on **16% of
  days at 45 and 82% at 90** [`EXECUTED 2026-09-20`, `spikes/spike-d-sql/06`],
  so a binary alarm fires on most days and is trained away.

Why 90 and not less: at the ruled cadence (monthly, realistically six weeks) the
median gap is **46 days**, so 45 sits on the median. And balances entered in one
sitting share an `as_of_date`, which makes staleness **correlated** — the failure
is not one account going stale but every account expiring the same day and
`sum()` returning NULL for the whole chart. Measured: **12.4% of days carry no
number at all at 45; none at 90** [`EXECUTED 2026-09-20`,
`spikes/spike-d-sql/07`]. 90 clears that cadence's p90 of 74 days and caps the
carried-dead-account lie at one quarter.

**Per-account-type cutoffs DEFER** — trigger: the first connector slice, where
account types acquire genuinely different natural cadences and real entry history
exists to tune against. A property valued annually and a checking account are not
the same number, and nine configurable thresholds are not this slice.

**Still open, deliberately not ruled by `D-034`:** whether coverage is weighted
by **value** rather than by account count. "5 of 25 stale" could be 2% of net
worth or 60%. Carried on `C-389`.

**`M-10` applies as a schema choice, not yet a bug.** One source means nothing
can double-count yet — but take the fix now, because retrofitting identity is the
expensive path. Keep `account` as an internal entity; put `(source_system,
source_id)` on an `account_source` row with a **full** unique index.

**`M-11`:** declare the slice single-currency. One `CHECK` that every account
shares the reporting currency. Drop the second-currency generator state.

**`S-17`:** the `closed_on` guard cannot be a `CHECK` constraint — it would need
to reference another table [`DOCS@2026-09-18`]. Use a trigger, or skip the guard
in this slice and record that you did.

**`S-24`:** decide the rounding mode once. PostgreSQL rounds half away from zero;
.NET defaults to banker's rounding — configure .NET explicitly and add one
midpoint test asserting both agree.

> **BUILT, 2026-09-24.** Two EF Core migrations: `Initial` (tables, both guard
> triggers, `v_date_spine`, `v_net_worth_daily`, `app_setting` seeded at 90) and
> `AccountSource`, which was applied to a database already holding rows. The rows
> survived, and reverting and reapplying it worked [`EXECUTED 2026-09-24`].
> **Spike D's fixture and every assertion pass against the migrated schema**:
> `spikes/spike-d-sql/run-on-migrations.sh`, re-runnable whenever a migration
> changes [`EXECUTED 2026-09-24`]. S-17: triggers, not skipped. S-24: `Money`
> names `MidpointRounding.AwayFromZero`. Its unit tests use midpoints where
> banker's rounding would differ, and fail if the mode is changed.
>
> **Found in the build, and not in any review: `MapEnum` orders the labels
> alphabetically.** Npgsql's generated migration created `source_strength` as
> `api, export, manual, scrape, statement`. PostgreSQL compares enum values by
> label order, so `'statement' > 'manual'` would have been true, silently
> reversing Constitution IV. An explicit `HasPostgresEnum` with the labels in
> declaration order fixes it. The migrated database reads `{statement, export,
> api, scrape, manual}` [`EXECUTED 2026-09-24`].
>
> **Not a database default: `currency`.** As in Spike D, every insert names it.
> The domain defaults it to the reporting currency in C#, and raw SQL must supply
> it, which the CHECK then holds to `USD`.

### 3. API (~14–24h)

> **`SEC-7`'s `Host`-header allow-list is one configuration line, not middleware.**
> The default web host already registers `HostFilteringOptions`, reads
> `AllowedHosts`, and adds `HostFilteringStartupFilter`
> [`SOURCE@dotnet/aspnetcore@release/10.0 WebHost.cs:258-272`]. Set
> `AllowedHosts` to `localhost;127.0.0.1;[::1]` and add one test. **Caveat:**
> `CreateSlimBuilder`/`CreateEmptyBuilder` go through `ConfigureWebDefaultsSlim`,
> which does **not** include that block — so this only holds for
> `WebApplication.CreateBuilder`.
>
> **The series endpoint must handle a null net worth** — see step 4. A day with no
> verified account is UNVERIFIED, not zero, and the response shape has to say so.
>
> **Cut by the middle ruling:** idempotency and correction semantics on
> `POST /balances` (`SE-11`), deferred to the first connector slice — nothing here
> retries a POST. And drift-gate **determinism**: generation and the committed
> document stay, the gate is advisory. Pick the client generator before step 4
> (`openapi-typescript` + `openapi-fetch` is the stabler pinned pair;
> `@hey-api/openapi-ts` is pre-1.0).

```
GET  /api/v1/accounts
GET  /api/v1/accounts/{id}/balances
POST /api/v1/accounts/{id}/balances
GET  /api/v1/system/health
```

Explicit request and response DTOs in **both** directions (`S-27`) — never
model-bind an entity, or a request can set `source_strength` and forge provenance.

OpenAPI generated and committed. Generate the TypeScript client from it.

**`S-14` is corrected — three of its four limbs were wrong, and the real fix is
one sentence.** Do not write a canonicalisation script; **pin the generation
method**:

> Generate **only** via the build-time tool, with `OpenApiDocumentsDirectory` set
> explicitly (it defaults to `obj/`, so committing the document requires it).
> Never curl the runtime `/openapi/v1.json`. On failure, print
> `dotnet build -p:OpenApiDocumentsDirectory=<dir>`.

Why each original limb fell [`SOURCE@dotnet/aspnetcore@release/10.0`,
`SOURCE@microsoft/OpenAPI.NET@v2.12.2`]:

- **"sorted keys" — already done upstream.** `GetOpenApiDocumentAsync`
  re-materialises `Components.Schemas` with `OrderBy(kvp => kvp.Key)` under
  `StringComparer.Ordinal`; `paths` is built in route-registration order and is
  deterministic for fixed source. A general key-sorter would have to run on *both*
  sides at compare time — at which point the committed document is no longer what
  the generator emits.
- **"invariant culture" — already satisfied, but only on one path.** The
  build-time tool wraps the stream in an `InvariantStreamWriter` whose
  `FormatProvider` is hardcoded to `InvariantCulture`. The runtime endpoint has no
  such guarantee. So this limb was never about culture — it was about *which
  generator you use*, which is what the rule above now says.
- **"LF via `.gitattributes`" — wrong target, and counterproductive.**
  `OpenApiWriterBase` sets `Writer.NewLine = "\n"` unconditionally, so the output
  is LF on Windows too. And `.gitattributes` normalises *git's* view, not the bytes
  a test compares: if a CRLF ever did appear, `eol=lf` would hide it from
  `git diff` while an in-process byte comparison still failed.
- **"a failure message that prints the command" — sound**, and the command above
  is the part that was missing.

**A contradiction this document carried, now resolved.** The middle cut removes
"drift-gate **determinism** work" and the step-3 preface repeats it — while this
line prescribed exactly that work and DoD 6 stated it as a pass/fail gate. **The
cut stands:** generation and the committed document stay, the drift check is
advisory, and DoD 6 is worded accordingly.

### 4. Web (~8–12h)

An account list and a net-worth chart. TanStack Query against the generated
client.

**`M-1` is live and is the first thing you hit.** Accounts do not report on the
same days, so a per-date sum over existing rows sums a different subset daily.
Implement `v_net_worth_daily` as carry-forward **within each account's active
window**, over a date spine, using **`LEFT JOIN LATERAL (… ORDER BY as_of_date
DESC LIMIT 1) ON true`**. Carry `accounts_carried`, `max_staleness_days` **and
`accounts_unverified`** per row, so a carried point is visibly carried and a
missing one is visibly missing. **The window's end is the 90-day cutoff ruled in
step 2 (`D-034`)**, and the two columns that make its two failure modes
distinguishable — `accounts_in_window` and `accounts_verified` — are named there
too.

Three things this sketch got wrong in earlier drafts, each measured:

- **`DISTINCT ON (account_id) … ORDER BY as_of_date DESC` is rejected outright** —
  the `DISTINCT ON` expressions must match the leftmost `ORDER BY`
  [`SOURCE@postgres@REL_18_STABLE select.sgml:1224`] — and semantically it yields
  one row *per account*, not a daily series.
- **`CROSS JOIN LATERAL` silently cancels the unverified count.** Under `CROSS
  JOIN` an account with no observation produces no row, so `count(*) -
  count(balance)` is identically zero. Measured on the four-account fixture:
  `CROSS JOIN` → `150.0000 / 0`; `LEFT JOIN … ON true` → `150.0000 / 2`
  [`EXECUTED 2026-09-18`]. **Use `LEFT JOIN LATERAL … ON true`.**
- **`sum()` over all-NULLs returns NULL, not 0.** On a day where every in-window
  account is unverified, `net_worth` comes back **empty** [`EXECUTED 2026-09-18`].
  The series endpoint and the chart both need an explicit null-net-worth branch —
  and per Constitution II that day is UNVERIFIED, never zero.

The naive triangular completion (`LEFT JOIN … ON b.as_of_date <= c.d`) is only
catastrophic at a **daily** cadence; at this slice's hand-entered cadence it is
about 30% over the 2-second budget, not 4,500%. The `LATERAL` form is preferred
on simplicity and an ~11x margin, not on an emergency.

**Constitution III does not yet permit any of this.** `Amendment 3` — carry-forward
*within* an active window permitted, fill *past* a series end still forbidden —
**is a gate on this step** and is the one piece of pre-step-1 paperwork the middle
cut keeps.

**`M-24`, minimal version:** a semantic `<table>` with real `<th scope>`, keyboard
operability with a visible focus ring, AA contrast, and a **tabular equivalent for
the chart** — nearly free, since the data is already in hand, and it is the
expensive thing to retrofit after a component library is chosen.

### 5. Tests (~18–28h)

> **The E2E runs locally, not in CI** (middle cut) — it keeps DoD 7's keyboard and
> table assertions and its one `AxeBuilder` call, which is the only mechanical
> check on the accessibility floor. The integration test against
> `v_net_worth_daily` is **not** cut: it is the only verification of the one piece
> of novel logic in the build.

**One of each layer. Not a suite — a demonstration of the shape.**

| Layer | The one test |
|---|---|
| Unit | Money arithmetic: rounding at a midpoint, currency-mismatch rejection. No I/O. |
| Integration | Testcontainers + real Postgres: restate a balance, assert the old row is superseded and the unique index does not reject the insert. |
| Contract | Regenerate OpenAPI, assert no diff against the committed document. |
| E2E | Playwright: enter a balance, assert the chart moves. |

**`S-25b` applies:** `DistributedApplicationTestingBuilder` mounts the real named
volume — it randomises ports and nothing else
[`SOURCE@microsoft/aspire@b477bdd`]. Factor the app model so `WithDataVolume`
applies only behind a parameter the test builder leaves off. **No test process
ever mounts a named volume.**

Coverage floors: `Domain` only. Everywhere else, none — a floor measured in a job
that cannot execute the code is decoration (`S-11`).

### 6. CI (~10–18h, starting at commit two)

> **A `dotnet build` job lands in commit two**, before the AppHost exists, so every
> later step adds one job to a pipeline that is already green rather than building
> the whole pipeline at the end. Actions minutes are free on a public repository
> [`DOCS@2026-09-18` github/docs], so the cost here is wall-clock, never money.
> The **pre-commit hook is cut**; the CI hygiene job stays. Trigger that reopens
> the hook: it is a hard prerequisite of the first slice holding a real balance.

One workflow: build → unit → integration → contract → E2E.

**`M-27`'s live half:** `permissions: contents: read` at the top level, escalated
per job. No `pull_request_target` with a head checkout. Actions pinned by SHA.

E2E stays in the workflow here — in this slice it is four tests, and watching it
run in CI is part of the exercise.

### 7. Deploy (~4–8h)

`Aspire.Hosting.Docker` + `aspire publish` → `docker-compose.yaml` + `.env`.
[The package is stable, non-prerelease — `DOCS@2026-09-18`, api.nuget.org. It
needs **no** `ASPIRECOMPUTE002` `NoWarn`: the only member carrying that attribute
is `DockerComposeEnvironmentResource.GetHostAddressExpression`, which an AppHost
never calls, and `[Experimental]` diagnoses at the call site
— `SOURCE@microsoft/aspire@b477bdd`. The experimental diagnostic this slice does
hit is `ASPIREJAVASCRIPT001`, from `PublishAsStaticWebsite`.]

Expect the generated file **not** to match `deployment.md`'s described topology
(`S-25a`): one flat network, external endpoints written as a bare
`host:container` string with no host IP, tag-based project images
[`SOURCE@microsoft/aspire@b477bdd`]. Override, with
`PublishAsDockerComposeService` or a committed override file (`S-34`) — note
`WithComposeServiceCustomization` does **not** exist:

- `127.0.0.1:` on the published API port. **DoD 3 depends on this** and nothing
  else in the slice supplies it; the .NET base images also set
  `ASPNETCORE_HTTP_PORTS=8080`, so Kestrel binds `0.0.0.0` inside the container
  and loopback is purely a property of the publish string.
- a `pg_isready` healthcheck and explicit `depends_on` conditions. This is
  load-bearing, not polish: the publisher's `service_healthy` branch is
  commented out, so `WaitFor` degrades to `service_started`
  [`DockerComposeServiceResource.cs:184-206`]. `WaitForCompletion` survives, so
  Spike C's second half passes and masks this.
- `.WithDashboard(false)`, or the publish emits an externally-bound Aspire
  dashboard with `restart: always` that no document lists.
- `restart: unless-stopped`.

**WHERE THE OVERRIDE LIVES — ruled 2026-09-18, because getting this wrong is a
silent failure.** The `aspire publish` output directory is git-ignored as a
directory (`R2-M4`: the generated `.env` is an accumulator, not a regenerated
file, so ignoring the file alone is not enough). **An override placed in that
directory is therefore un-committed by the same rule that protects the `.env`** —
it would work on the machine that generated it and vanish for everyone else,
including CI and a fresh clone.

So, in order of preference:

1. **Prefer the app model.** `PublishAsDockerComposeService<T>(this
   IResourceBuilder<T>, Action<DockerComposeServiceResource, Service>)` exists
   [`SOURCE@microsoft/aspire@b477bdd DockerComposeServiceExtensions.cs:35`] and
   `Service.Ports` is a `List<string>`, so the `127.0.0.1:` rewrite can be done in
   **committed C# that regenerates correctly**, with no file-location question at
   all. Note it is *not* `WithComposeServiceCustomization`, which does not exist.
   `.WithDashboard(false)` takes the **environment** resource, not the API
   resource.
2. **Where the callback cannot express it, the file is
   `deploy/docker-compose.override.yaml` — committed, and deliberately NOT in the
   publish output directory.** Run it as `docker compose -f <generated> -f
   deploy/docker-compose.override.yaml up`, and put that command in the README so
   nobody runs the generated file alone and quietly loses the loopback binding.

**THE GATE IS ANSWERED — YES, all four, and no spike was needed**
[`SOURCE@microsoft/aspire@b477bdd`]. `Service` carries `Healthcheck?`,
`Dictionary<string, ServiceDependency> DependsOn` (with `Condition`), `Restart`,
`Ports` and `Deploy`. The callback runs **after** `BuildComposeServiceAsync` and
**after** the network assignment, so it can overwrite the `service_started` entry
the publisher wrote. Its constraint is `where T : IComputeResource`, which both
`ContainerResource` and `ProjectResource` implement, so it attaches to Postgres as
well as to the API. The vendor's own test drives `Restart`, `Networks`, `Labels`,
`ShmSize` and `ContainerName` through it. **The `pg_isready` and `depends_on`
limbs move from `INFERRED` to `SOURCE@`.** (`Healthcheck` requires `Interval`,
`Timeout` and `StartPeriod`.)

Two further hooks nothing had named, which shrink the override file's job further:
**`ConfigureComposeFile(Action<ComposeFile>)`** (whole file, including `networks:`)
and **`WithProperties(e => e.DefaultNetworkName = …)`**. The internal/front network
split `deployment.md` describes **is** expressible in committed C#.

**The gate was pointed at the wrong limb — the FALLBACK is what cannot do the
job.**

- **`ports` does not merge, it appends.** The Compose spec keys it on
  `{ip, target, published, protocol}`, so a generated `"8080:8080"` and an
  override `"127.0.0.1:8080:8080"` are *different* entries: you get **both**
  bindings, a bind conflict, and DoD 3 silently unmet. Removing the generated one
  needs `ports: !override [...]` or `!reset []` (Compose ≥ 2.24.4)
  [`DOCS@2026-09-18` compose-spec].
- **No override file can delete a service**, so `.WithDashboard(false)` has no
  file route at all.
- What the file *can* do cleanly: `restart` (scalar, replaced), `healthcheck`
  (merged) and `depends_on` (merged).

**So do all four in the app model.** The committed file is for what the app model
genuinely cannot express, and two of these four are not in that set.

**One limb of `S-34` was silently dropped, and is restored here as an explicit
DEFER: resource limits.** The original names four things; this slice covers three.
Trigger: the first slice that runs an ingest batch — *"without limits a mis-sized
ingest batch takes the host down"*. For then: `mem_limit` is not in Aspire's
`Service` model; the route is `Service.Deploy` → `deploy.resources.limits`.

**`S-33` is corrected — it named the wrong file, and described as a task
something the publisher already does** [`SOURCE@microsoft/aspire@b477bdd`]:

> Commit **`deploy/.env.example`** — outside the publish output directory, for the
> same reason the override file lives there. **Never commit `.env` or
> `.env.<environment>`.**

- **`aspire publish` already writes a keys-only `.env`**: `Add(key, value: null,
  …)` then `Save(includeValues: false)`, producing a commented, enumerated key
  list. *That* is the example file, under the name Compose actually reads. The
  original finding's "no document enumerates the required keys" is discharged by
  the generator, not by a document.
- **The file holding resolved secrets is `.env.<environment>`**, written by the
  *deploy* step with `Create` + `onlyIfMissing: false` + `includeValues: true`.
  "Never a populated `.env`" pointed at the one variant that is never populated,
  and said nothing about the one that is.
- **A root `.env.example` is read by nothing**, needs a copy step this slice does
  not give, and — if placed in the publish output directory — is un-committed by
  the very rule that protects the `.env`.

**`M-26`:** a `LICENSE` in the first commit, and a line saying the project is not
affiliated with any financial institution. One file, and the repo is public from
commit one.

---

## Definition of done

1. All three spikes run; their claims restated at `EXECUTED` with command and date.
2. `aspire run` brings the stack up; a balance entered in the browser appears in the chart.
3. `docker compose up` from the published artifact does the same, with the API on loopback and the database port unpublished.
4. Four tests, all green in CI on a pull request.
5. The architecture test passes.
6. The committed OpenAPI document regenerates identically from the build-time
   tool, and the TypeScript client compiles. (The drift *check* is advisory per
   the middle cut — this item is about the document, not a gate.)
7. The chart has a tabular equivalent and is keyboard-reachable.
8. `LICENSE` present; no personal data anywhere in the repo.
9. The eight live findings are fixed **or** explicitly recorded as not-fixed with a reason.

## The gate's two prerequisites — start these on day one

Both are `D-030` Amendment 1 items and both are prerequisites of step 1, not
deliverables of the assessment. That ordering is the whole point: at week 20, on
the fourth consecutive evening of push-wait-red, the decision to continue gets
made by exhaustion unless a number was written down while things were still
interesting.

### 1. `LOG.md` — one line per session, written as you go

`/LOG.md`. Date, step, hours, one clause, in **two** hour columns — `Kyle h`,
which the threshold counts, and `Agent h`, which is recorded and does not gate
(Kyle, 2026-09-20). **Hours are the only input the assessment needs that cannot
be reconstructed afterwards** — a figure recalled
months later is a recalled figure, and this project's parent system forbids those
as a source. Everything else is in git.

### 2. The abandon threshold — written now, applied then

**The gate judges TECHNIQUE, not usefulness** (Kyle, 2026-09-18). This matters
for what the threshold can say. Slice 01 runs on generated data, so the chart at
the end is a chart of invented money and **cannot** answer *"is a chart of my own
money worth this?"* — that question is not on the table and the assessment must
not pretend to answer it. What the slice can answer is whether this stack is
worth more evenings. So:

> **The default at the gate is STOP.** A second slice requires a new decision
> file, written and dated, not a continuation.
>
> The default is overridden only if the assessment can name, in writing:
>
> - **three techniques this taught that Kyle did not already have** — named
>   specifically, with the commit that demonstrates each. "Learned Aspire" does
>   not count; "learned that the AppHost is not the production runtime, and here
>   is the generated compose file that proves it" does.
> - **and** that actual cost did not exceed **200 hours of Kyle's own time**
>   (`LOG.md`, the `Kyle h` column). Beyond that, the practice-per-hour rate has
>   fallen far enough that a different project teaches more.
>   **Agent-session hours do NOT count toward the threshold** (Kyle,
>   2026-09-20). They are still recorded in `LOG.md`'s `Agent h` column, because
>   a cost nobody wrote down cannot be argued about later — they are simply not
>   the number this limb reads.
>
>   **What that ruling does, stated because it is not obvious:** this limb no
>   longer bounds what the *project* costs, only what *Kyle* spends. If most of
>   the work is done by agent sessions the threshold cannot trip however long
>   the slice runs, and the gate then rests entirely on the three-techniques
>   limb above. That is coherent — the threshold was always about whether the
>   evenings were well spent — but the assessment must not read an untripped
>   threshold as evidence that the slice was cheap.
>
> **Two things are explicitly NOT reasons to continue:** that the slice works,
> and that the next slice is obvious. Both will be true and neither is evidence
> about the thing being decided.

Write both files before step 1. They take twenty minutes and they are the only
part of this plan designed to survive your own enthusiasm.

## Then stop

**The gate is real** (`D-030` §2). Momentum after a working slice points straight
at the next one, which is exactly why the stop is written down.

Write the assessment. It answers:

- What did it actually cost, against the 50–80 hour estimate?
- Which of the three spikes' claims were wrong, and what else in `specs/` is
  suspect as a result?
- Which techniques did this genuinely teach, and which were mechanical?
- Where was the spec set wrong, over-built, or useful?
- **Is a second slice wanted at all** — and if so, which technique is it for?

A second slice begun without that assessment written has broken the gate.

---

## Findings live in this slice

Ten, of 89. The rest are not deferred — their surface does not exist here.

| | What to do |
|---|---|
| **M-1** | Carry-forward within an active window; `accounts_carried` per row |
| **M-3** | Spike A; environment selection moves off launch profiles |
| **M-4** | Spike B; same-origin proxy so no origin is configured anywhere |
| **M-5** | Spike C; a separate one-shot Migrator project |
| **M-9** | The partial unique index, and the three-statement write order |
| **M-10** | Split `account` identity from `account_source` now |
| **M-11** | Declare single-currency; one `CHECK` |
| **M-24** | Semantic table, keyboard, contrast, chart tabular equivalent |
| **M-26** | `LICENSE` + non-affiliation line, first commit |
| **M-27** | `permissions: contents: read`; SHA-pinned actions |

Supporting: `S-11`, `S-14`, `S-17`, `S-24`, `S-25a`, `S-25b`, `S-25c`, `S-27`,
`S-33`, `S-34`.

*Claim markers and dispositions per `external-claim-provenance` skill v1.0.0.*
