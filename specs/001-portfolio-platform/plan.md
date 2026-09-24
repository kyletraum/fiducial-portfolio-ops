# Implementation Plan: Self-Hosted Portfolio Platform

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
> - **`:119` — WRONG.** *Service discovery supplies the SPA's API address.* It never does, in either mode. The browser calls a **relative path**; under `aspire run` the Vite dev server proxies `/api` using `SERVER_HTTP`/`SERVER_HTTPS` injected into the Vite **process**, and at publish the assets are folded into the API container. Service discovery feeds the proxy, not the client. [`SOURCE@microsoft/aspire@b477bdd`, `aspire-ts-cs-starter`]
> - **`:125` — WRONG.** *Two simultaneous stacks by launch profile.* `aspire run` stops any running instance unconditionally, keyed on the **AppHost file path alone** — the launch profile is not part of the key. Simultaneity needs two directories. ~~Launch profiles still work for *selection*.~~ [`SOURCE@…RunCommand.cs:358-370`, `AppHostSocketManager.cs:89-98`] **Spike A, 2026-09-24:** they do not — `aspire run` has no `--launch-profile` option and always applies the first profile; selection is configuration (`builder.AddParameter`). Two directories confirmed; `--isolated` does not avoid the stop. [`EXECUTED 2026-09-24`, Aspire 13.5.4, `spikes/spike-a-two-stacks/RESULTS.md`]
> - **`:213` — WRONG.** *The Worker owns migrations as a long-running service.* It must **terminate**: `WaitForCompletion` requires it, and that is what emits `service_completed_successfully`. A one-shot Migrator is right. [`SOURCE@microsoft/aspire@b477bdd`; **`EXECUTED 2026-09-24`**, Aspire 13.5.4, Spike C — `spikes/spike-c-migrator/RESULTS.md`]
> - **`:122` — TRUE IN RUN MODE ONLY.** *The API and worker wait for Postgres to be healthy.* The Compose publisher's `service_healthy` branch is **commented out**; everything but `WaitForCompletion` falls through to `service_started`. [`SOURCE@…DockerComposeServiceResource.cs:208-230`; **`EXECUTED 2026-09-24`**, Spike C: `WaitFor(db)` and `WaitFor(pg)` both publish as `service_started`, and `pg` gets no healthcheck]
> - **`:173` — OVERCLAIMED.** *`IProjectionModel`'s Validate/Run split makes Constitution II a type-system property.* It does not. A heterogeneous registry erases to `Run(object)`, and defaulting happens *inside* `Validate`, which no signature can see. The refusal tests are the enforcement.
> - **`:80` vs `:168` — CONTRADICTORY.** `Projections` references *nothing*, and `Validate` *resolves* inputs. Resolution is data access. Both cannot hold.
> - **`:290` — CORRECTED.** The Compose risk row: `WithComposeServiceCustomization` **does not exist**. The API is `PublishAsDockerComposeService`, and no `ASPIRECOMPUTE002` `NoWarn` is needed.
>
> Anything *not* listed above is **unreviewed, not verified**. Where a claim
> about an external system carries a strength marker (`EXECUTED`,
> `SOURCE@<ref>`, `DOCS@<date>`, `INFERRED` — see `../review/process.md`), that
> marker is the claim's real weight. An unmarked external claim has not been
> checked.

**Spec:** `spec.md` · **Principles:** `../constitution.md`

This document says **how** the system is built. Rationale for the choices lives
in `research.md`.

> **Version claims are unverified.** The session that wrote this had no .NET SDK
> available and could not reach the vendor documentation sites. Every version and
> package name below is marked *confirm at scaffold time*. Treat them as the
> starting point for `aspire --version` and `dotnet list package`, not as facts.

---

## Stack

| Layer | Choice | Confirm at scaffold time |
|---|---|---|
| Runtime | .NET (current LTS) | exact SDK version |
| Orchestration | Aspire | major version and package set |
| API | ASP.NET Core Minimal APIs | - |
| Data access | EF Core + Npgsql | provider version |
| Database | PostgreSQL | image tag, pinned by digest |
| Frontend | React + TypeScript + Vite | versions |
| Frontend data | TanStack Query | - |
| Charts | see `research.md` | - |
| Tests | xUnit, Testcontainers, Aspire testing package, Vitest, Playwright | package names |
| CI | GitHub Actions | - |

---

## Solution layout

```
src/
  AppHost/              Aspire app model - the only orchestration entry point
  ServiceDefaults/      telemetry, health checks, resilience, service discovery
  Api/                  HTTP surface; no business rules of its own
  Domain/               entities, value objects, invariants. No dependencies.
  Projections/          IProjectionModel and the model implementations
  Infrastructure/       EF Core, connectors (MCP, HTTP provider), secret stores
  Worker/               scheduled ingestion, migrations, seeding
web/                    React + Vite application
tests/
  Unit/                 Domain and Projections. No I/O.
  Integration/          real Postgres, real app model
  Contract/             OpenAPI conformance
  E2E/                  Playwright against a seeded DEV stack
```

### The Aspire boundary

**Aspire orchestrates. It does not leak into the domain.**

- `AppHost` and `ServiceDefaults` reference Aspire.
- `Domain` and `Projections` reference **nothing** - not Aspire, not EF Core,
  not ASP.NET.
- `Infrastructure` references EF Core and the connector SDKs, but not Aspire.

This is an invariant with a test, not an aspiration: an architecture test
asserts that no assembly outside `AppHost` and `ServiceDefaults` has an Aspire
dependency, and that `Domain` and `Projections` have no third-party dependency
at all. It buys two things that matter for a training project - models that run
in a unit test with no host (FR-8.8), and a system that survives Aspire being
swapped out.

### Dependency direction

```
AppHost ──▶ Api ──▶ Projections ──▶ Domain
   │         │           │
   │         └──▶ Infrastructure ──┘
   └──▶ Worker ──▶ Infrastructure
```

`Domain` is a sink: everything points at it, it points at nothing.

---

## The Aspire app model

Aspire is code-first: the app model is a program that describes resources and
their relationships, and the CLI runs, publishes and deploys from it.

Resources:

- **Postgres** - a container resource with a **persistent named volume** and a
  named database. The volume name and database name are environment inputs, so
  PRD and DEV get separate storage (`environments.md`).
- **Api** - a project resource, referencing Postgres. Receives the connection
  string by injection; never has it hardcoded.
- **Worker** - a project resource, referencing Postgres. Owns migrations and
  seeding.
- **web** - the Vite application as a first-class resource, referencing `Api`.
  Service discovery supplies the API's address, so no environment ever hardcodes
  an origin.

Ordering is explicit: the API and worker wait for Postgres to be healthy; the
frontend waits for the API.

Environment selection is a launch profile. The same app model, parameterised by
volume name, database name and port range, produces two independent stacks that
can run at the same time.

### What Aspire gives, and what it does not

It gives service discovery, injected connection strings, OpenTelemetry wiring
through `ServiceDefaults`, container lifecycle in the inner loop, a dashboard
for logs, traces and metrics, and a publish path to Docker Compose and to Azure.

It is **not the production runtime**. In the inner loop it runs projects as
processes and dependencies as containers. The self-hosted deployment is a
generated Compose file. `deployment.md` covers the distinction, which is easy to
get wrong and expensive to discover late.

---

## Component design

### Ingestion

A connector implements one interface regardless of transport:

- **describe** what it can supply,
- **authenticate** and report credential health,
- **fetch** a typed slice (accounts, balances, transactions, holdings) since a
  cursor,
- **map** provider records onto the platform's model with provenance attached.

Two implementations: an MCP client (official C# MCP SDK) and an HTTP provider
client. Neither reaches the database; both return mapped records to a single
**ingestion pipeline** that owns idempotent upsert, soft-delete, run recording
and audit. One place decides what a sync means to the data, and connectors stay
thin enough to be worth writing a third of.

Provider text is data. It is stored and escaped on output; it never reaches an
interpreter, a query, or a prompt.

### Projections

```
IProjectionModel
  Metadata      id, version, title, description, declared inputs and outputs
  Validate      resolve inputs; return either a resolved set or a refusal
                naming each missing input
  Run           pure function of resolved inputs to a result
```

`Run` is only reachable with a resolved input set, so the type system enforces
Constitution II rather than a convention doing it. Models are discovered through
a registry populated at startup; the API exposes the registry, and the UI renders
inputs and results from declared metadata (FR-8.4). Adding a model is one class
and one registration.

Every run persists its result with the hash of each input and the model version.
Changing an input marks prior runs stale rather than deleting or rewriting them.

### Reporting

User SQL executes on a **separate database connection using a role with SELECT
only on a `reporting` schema of views**. The guarantee is a database privilege,
not query parsing - parsers can be fooled, `GRANT` cannot. A statement timeout
and a row cap bound cost. Saved-report parameters are bound, never interpolated.

Dashboards read the same views, so a dashboard figure and a report figure cannot
disagree (FR-5.5).

### API

Minimal APIs grouped by resource, versioned by path. OpenAPI is generated from
the implementation and committed; CI regenerates it and fails on a diff, making
drift a build failure rather than a support question. The TypeScript client is
generated from the same document.

The API holds no business rules: it validates, authorises, delegates to `Domain`
or `Projections`, and maps results to responses.

### Frontend

React with TypeScript, built by Vite. Server state through TanStack Query
against the generated client - components never hand-write a fetch. Routes match
the API's resources. A persistent environment banner (FR-7.3, FR-11.4) makes the
active environment and the real-versus-generated distinction unmissable.

---

## Cross-cutting

**Migrations** are owned by the worker and applied behind an explicit flag. The
API never migrates on startup: two API replicas racing to migrate is a failure
mode worth designing out even in a single-user system.

**Money** is decimal with an explicit currency, end to end - database, domain,
API and UI. Binary floating point never touches a monetary value.

**Time** is stored as UTC instants with the observation's own date preserved
separately, because a balance's *as-of date* and the *moment it was observed*
are different facts and conflating them corrupts history.

**Telemetry** comes from `ServiceDefaults`. A sync emits one span per run with
child spans per provider call and per mapped batch; a projection emits one span
per run carrying the model id and version. This is the difference between
diagnosing a failed sync from its trace and reproducing it by hand.

**Configuration** differs between environments only by inputs. The same build
artifact runs in both (FR-10.5).

---

## Build order

**Superseded 2026-09-18 by `D-030`.** The order below was horizontal — all the
storage, then all the connectors, then all the reporting — and the fourteen-seat
review found that it put the first user-visible screen at **step 6 of 9**, an
estimated 285–410 hours before a chart of your own money exists. `S-0` named that
as the most likely cause of the project being abandoned.

The project is now built as **vertical slices**: one thin feature carried through
every layer once, then a stop and an assessment before the next is commissioned.

### The current unit of work

**`slice-01.md` — manual balance entry to a net-worth chart.** ~50–80 hours.
No PRD environment, no credentials, no connector, generated data only.

Its order is:

0. **Spikes.** `M-3`, `M-4` and `M-5` are load-bearing claims resting at
   `INFERRED` and the slice may not be built on them (`D-029`). One day.
1. **Skeleton** — AppHost, Postgres, API, Migrator, Vite; architecture test.
2. **Data** — three tables with provenance; supersession; single-currency.
3. **API** — four endpoints, committed OpenAPI, generated client.
4. **Web** — account list, net-worth chart, accessible.
5. **Tests** — one per layer, demonstrating the shape.
6. **CI** — one workflow running all four.
7. **Deploy** — `aspire publish` → compose, running locally.

Then **stop and assess**. `D-030` §2 makes that a gate, not a milestone.

### Why slices rather than layers

For a practice project, breadth is worthless and depth per technique is the whole
point. A slice that touches orchestration, a real database, the API/SPA boundary,
a contract-first client, four test layers, CI and a deployment teaches more than
three times as much product built horizontally — and it produces something that
runs, which the horizontal order does not do until most of the budget is spent.

### What happened to the rest

Deferred to slices that may or may not be commissioned, **not struck**: the
connectors and their webhook and re-auth machinery, the cloud target, the release
and deploy pipelines, the SQL reporting surface, the projection engine, the tax
domain, the second environment, and the security tier that exists because PRD
holds real finances.

The remaining documents in this tree describe that work and stay accurate as a
**reference**. Their value is that when a later slice needs webhook verification
or an envelope-encryption scheme, the thinking is already done and adversarially
reviewed. They are consulted, not executed.


## Risks

| Risk | Response |
|---|---|
| Aspire's Compose publishing is stable as a package, but the file it generates is not the topology `deployment.md` describes, and health-based ordering is silently dropped in it | Not a GA risk, a fidelity one: the generated file needs an override for host-IP binding, healthcheck-based `depends_on` and the unrequested dashboard service (`deployment.md`). Customise with `PublishAsDockerComposeService`, not the non-existent `WithComposeServiceCustomization`. `slice-01.md` step 7 is where this is executed |
| MCP server tool surface may change | Declare permitted tools explicitly; contract-test the stub against the real server's schema |
| Provider rate limits | Cursor-based incremental sync, backoff, and runs that resume rather than restart |
| Projection interface proves too narrow | Not exercised by `slice-01.md`, which has no projection engine. The test is porting several distinct models in the slice that commissions the engine; a narrow interface fails there, cheaply |
| Real data reaching a public repository | Constitution IX as a CI job from the first commit, not added later |
