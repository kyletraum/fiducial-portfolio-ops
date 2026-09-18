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

**Architecture test now, not later** — one test asserting no project outside
`AppHost` references `Aspire.Hosting.*`, and that `Domain` references nothing
outside the shared framework. `S-25c` applies: `Aspire.*` **client** integrations
are permitted in the `Api` and `Worker` composition roots; the invariant names the
`Aspire.Hosting.*` prefix, not the brand.

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

OpenAPI generated and committed. Canonicalise it (`S-14`): sorted keys, invariant
culture, LF via `.gitattributes`, and a failure message that prints the
regeneration command. Generate the TypeScript client from it.

### 4. Web (~8–12h)

An account list and a net-worth chart. TanStack Query against the generated
client.

**`M-1` is live and is the first thing you hit.** Accounts do not report on the
same days, so a per-date sum over existing rows sums a different subset daily.
Implement `v_net_worth_daily` as carry-forward **within each account's active
window**, over a date spine, using **`LEFT JOIN LATERAL (… ORDER BY as_of_date
DESC LIMIT 1) ON true`**. Carry `accounts_carried`, `max_staleness_days` **and
`accounts_unverified`** per row, so a carried point is visibly carried and a
missing one is visibly missing.

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

**Which of the four overrides the callback can actually express is a GATE on this
step, not an assumption** (`D-029`): only the port rewrite is confirmed at source.
The `pg_isready` healthcheck and the `depends_on` conditions are `INFERRED` to be
reachable through the same callback and have not been verified — establish that
first, and route whatever it cannot reach to the committed file.

Ship `.env.example`, never a populated `.env` (`S-33`).

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
6. OpenAPI drift check passes; the TypeScript client compiles.
7. The chart has a tabular equivalent and is keyboard-reachable.
8. `LICENSE` present; no personal data anywhere in the repo.
9. The eight live findings are fixed **or** explicitly recorded as not-fixed with a reason.

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
