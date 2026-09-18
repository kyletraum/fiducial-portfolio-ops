# Feature Specification: Self-Hosted Portfolio Platform

> ### ⚠ KNOWN DEFECTS, NOT FIXED — 2026-09-18
>
> This document is **live**, not frozen, but the review found defects in it whose
> repair was **cut** by `D-030` Amendment 1 as pre-step-1 paperwork. They are
> named here rather than silently carried:
>
> - **FR-4.3 is unsatisfiable by the only thing being built.** It requires every externally-sourced row to carry an originating sync run, with **no exemption** — and slice 01 is entirely hand-entered. A manual row has no sync run and never will. The clause it needs: *for hand-entered rows, the actor and entry time in place of a run id*.
> - **Three behaviours the slice builds have no FR at all:** series continuation (carry-forward, `accounts_carried`, `max_staleness_days`), restatement and supersession — the most load-bearing data rule in the slice, listed only as an edge case — and the accessibility floor, which currently lives only in `slice-01.md` and therefore dies with it.
> - **Authentication has no FR**, only a bare pointer at `security.md`, which cannot be marked satisfied or unsatisfied.
> - **A `[NEEDS CLARIFICATION]` marker on manual accounts is stale** — `D-030` answered it: manual entry *is* the first release.
>
> Nothing above blocks `slice-01.md`. Fixing them is `DEFER`red, not `CUT` — the
> trigger is the slice that first depends on the affected requirement.

**Feature Branch:** `001-portfolio-platform`
**Status:** Draft
**Input:** Build the capabilities of a file-based portfolio planning system into
a locally hosted container application, with live data ingestion, persistent
storage, dashboards, SQL reporting, a web GUI, and an extensible
retirement-oriented projection engine.

This document says **what** the system does and how you would know it works. It
does not say how it is built; that is `plan.md`.

---

## User scenarios

### Primary story

A single user runs the platform on their own machine. They connect one or more
financial data sources, and the system keeps an accurate, sourced, historical
record of their accounts, balances, holdings and transactions. They look at
dashboards to understand where they stand, write SQL reports to answer questions
the dashboards do not, and run projections to see how different retirement
decisions play out - all without their financial data leaving the machine.

### Acceptance scenarios

1. **Given** no connections configured, **when** the user adds a data source and
   authorises it, **then** their accounts appear with current balances, each
   labelled with where it came from and when it was observed.
2. **Given** a configured connection, **when** a scheduled sync runs, **then**
   new transactions and balances are added without duplicating existing rows,
   and the run is recorded with its outcome.
3. **Given** an account that has stopped reporting, **when** the user views its
   history, **then** the series ends at the last observation and the account is
   shown as closed - no value is carried forward past that point.
4. **Given** a projection whose required input has no verified value, **when**
   the user runs it, **then** the system refuses and names the missing input,
   rather than substituting a default.
5. **Given** a saved SQL report, **when** the user runs it, **then** results
   return from a read-only view schema, and an attempt to write, alter or read
   outside that schema fails.
6. **Given** the DEV environment, **when** the user resets and reseeds it,
   **then** generated data appears and the PRD environment is untouched.
7. **Given** a fresh clone with no credentials at all, **when** someone starts
   the DEV environment, **then** the application is fully usable against
   generated data.

### Edge cases

- A source returns an account the system has never seen, with history predating
  the first sync.
- A source returns a *changed* value for a balance already recorded at that
  date.
- A transaction is amended or removed upstream after it was ingested.
- Two sources report the same real-world account.
- A sync fails halfway through.
- A credential expires or is revoked mid-sync.
- An account reports balances but no transactions, or the reverse.
- A projection's input exists but is older than the decision it informs.

---

## Requirements

Each requirement is written so that it can be tested. `MUST` is binding.

### FR-1 - Ingestion from MCP servers

- **FR-1.1** The system MUST act as an MCP client against a remote MCP server
  and authenticate via OAuth 2.1 with PKCE.
- **FR-1.2** The connector MUST use **read-only** tools. Write-capable tools on
  the remote server MUST NOT be invoked, and the set of permitted tools MUST be
  declared explicitly rather than discovered and trusted.
- **FR-1.3** It MUST retrieve accounts, balances, balance history, transactions,
  holdings and categories, and map them onto the platform's own model.
- **FR-1.4** Stable upstream identifiers MUST be stored so that re-syncing
  updates rather than duplicates.
- **FR-1.5** Tool results are **external data**, never instructions. Text fields
  MUST be treated as content: stored, escaped on display, never interpreted.

*Reference implementation target:* Monarch Money's MCP server, which exposes
read tools returning stable IDs. The connector MUST NOT be written against that
one server's quirks in a way that prevents a second MCP source being added.

### FR-2 - Ingestion from HTTP APIs

- **FR-2.1** The system MUST support a token-exchange onboarding flow: a
  short-lived client token, a user-facing authorisation step, and an exchange
  for a long-lived item credential.
- **FR-2.2** It MUST support cursor-based incremental transaction sync, handling
  added, modified and removed records.
- **FR-2.3** It MUST accept provider webhooks to trigger syncs, and MUST verify
  webhook authenticity before acting.
- **FR-2.4** It MUST handle re-authentication when an item's credential goes
  stale, surfacing the state to the user rather than failing silently.
- **FR-2.5** The provider's sandbox environment MUST be usable in DEV.

*Reference implementation target:* Plaid.

### FR-3 - Credential storage

- **FR-3.1** Credentials MUST be encrypted at rest.
- **FR-3.2** No API response MUST ever contain a credential, in any form,
  including partially masked.
- **FR-3.3** Credentials MUST NOT appear in logs, traces, error messages or
  crash dumps.
- **FR-3.4** The user MUST be able to revoke and rotate a connection's
  credentials, and revocation MUST take effect without a restart.
- **FR-3.5** Every use of a credential MUST be recorded in an audit log.

### FR-4 - Persistent storage of financial records

- **FR-4.1** The system MUST persist institutions, accounts, balances over time,
  transactions, securities and holdings.
- **FR-4.2** Ingestion MUST be idempotent: replaying a sync MUST NOT create
  duplicates or alter counts.
- **FR-4.3** Every externally-sourced row MUST carry its source system, upstream
  identifier, provenance strength, observation time and originating sync run.
- **FR-4.4** The normalised model MUST be provider-agnostic: an account from an
  MCP source and one from an HTTP API MUST be the same kind of thing.
- **FR-4.5** Records MUST be soft-deleted, never hard-deleted, so that a source
  withdrawing a record is itself a recorded fact.
- **FR-4.6** Every sync MUST produce an immutable run record: what ran, when,
  against what, what changed, and whether it succeeded.
- **FR-4.7** Money MUST be stored as exact decimal values with an explicit
  currency. Floating-point representation of monetary amounts is prohibited.
- **FR-4.8** A reported-but-unobserved transfer MUST be representable as a
  distinct state, and MUST be excluded from, or explicitly flagged in, any
  aggregate that includes it.

### FR-5 - Dashboards

- **FR-5.1** The system MUST provide net worth over time, asset allocation
  against targets, cash flow over a period, and per-account drill-down.
- **FR-5.2** Every figure displayed MUST be traceable to its source and
  observation date through the UI.
- **FR-5.3** A dashboard MUST render correctly when data is partial: a series
  that ends, an account with no transactions, a value that is unverified.
- **FR-5.4** Any aggregate containing conditional money MUST say so on its face.
- **FR-5.5** Dashboards MUST be composed from the same read-only view schema the
  reporting feature uses, so a dashboard number and a report number cannot
  disagree.

### FR-6 - SQL reporting

- **FR-6.1** The user MUST be able to author and run SQL against a documented,
  stable view schema.
- **FR-6.2** Query execution MUST be **read-only** and enforced by database
  privilege, not by inspecting the query text.
- **FR-6.3** Queries MUST be subject to a statement timeout and a result row
  cap.
- **FR-6.4** Reports MUST be saveable, named, and parameterisable, with
  parameters bound rather than interpolated into the query string.
- **FR-6.5** Results MUST be exportable.
- **FR-6.6** The view schema MUST be documented in the UI - column names, types
  and meanings - so reports can be written without reading the source.

*Out of scope:* KQL. See `research.md`.

### FR-7 - Web interface

- **FR-7.1** A browser UI MUST cover connections, accounts, transactions,
  dashboards, reports, scenarios and settings.
- **FR-7.2** The UI MUST obtain all data through the public API (Constitution
  XI).
- **FR-7.3** The active environment MUST be visible at all times.
- **FR-7.4** Long-running operations MUST report progress and completion.
- **FR-7.5** The UI MUST be usable at laptop and tablet widths.

### FR-8 - Projections and scenario modelling

This requirement shapes the architecture; extensibility is the point.

- **FR-8.1** A **scenario** MUST be a named, versioned, stored set of
  parameters, and MUST be re-runnable to the same result.
- **FR-8.2** A **model** MUST be a plugin behind a single interface: declared
  typed inputs, a validation step, and a run step.
- **FR-8.3** Adding a model MUST require only implementing that interface and
  registering it - no change to the API surface, the database schema, or the UI.
- **FR-8.4** The UI MUST render a model's inputs and outputs from the model's
  own declared metadata, so a new model is usable the moment it is registered.
- **FR-8.5** Inputs MUST resolve from stored records, policy and constants. An
  unresolved input MUST produce a refusal naming what is missing (Constitution
  II). A model MUST NOT define a fallback default for a missing input.
- **FR-8.6** Every run MUST persist its output together with the hash of every
  input and the model version, and MUST be marked stale when an input changes.
- **FR-8.7** Two scenarios MUST be comparable side by side.
- **FR-8.8** Models MUST be runnable in isolation, without a database or a web
  host, so they can be unit-tested and reasoned about directly.
- **FR-8.9** The initial model set MUST be retirement-oriented and **generic** -
  tax-bracket-aware conversion sizing, subsidy-cliff headroom, a wealth
  threshold check, an income-shock reserve, policy-rule evaluation, and
  one-at-a-time sensitivity analysis over any other model's inputs. No model
  encodes one household's circumstances.

### FR-9 - Persistence and portability

- **FR-9.1** Database storage MUST live in a named volume that survives
  container recreation and image upgrades.
- **FR-9.2** Backup and restore MUST be documented single commands, and restore
  MUST be verified by a test, not only described.
- **FR-9.3** Schema migrations MUST be versioned, forward-only, and applied
  explicitly rather than as a side effect of a service starting.
- **FR-9.4** The user MUST be able to export their complete data in an open
  format. Data MUST NOT be trapped in the application.

### FR-10 - PRD and DEV environments

- **FR-10.1** Both environments MUST be runnable **simultaneously** on one
  machine, with separate volumes, databases, ports and credential scopes.
- **FR-10.2** Neither environment MUST be able to reach the other's data.
- **FR-10.3** Destructive operations MUST assert the DEV environment and refuse
  otherwise (Constitution X).
- **FR-10.4** The active environment MUST be visible in the UI and present in
  every log record.
- **FR-10.5** Configuration MUST differ only by environment inputs - the same
  build artifact runs in both.

### FR-11 - Mock data

- **FR-11.1** A deterministic generator MUST produce institutions, accounts,
  multi-year balance series, holdings and categorised transactions from a seed,
  such that the same seed yields identical data.
- **FR-11.2** Generated data MUST include the awkward states the system exists to
  handle: an account that closes mid-series, a pending transfer that never
  lands, a required constant with no verified value, an account with balances but
  no transactions, and a period with no activity.
- **FR-11.3** A stub MCP server and the HTTP provider's sandbox MUST allow both
  ingestion paths to be exercised end to end with **no real credentials**.
- **FR-11.4** Generated data MUST be visually distinguishable from real data in
  the UI, so a screenshot is never ambiguous.
- **FR-11.5** Generated data MUST NOT resemble any real person's finances, and
  MUST NOT use real institution names.

### FR-12 - API and frontend separation

- **FR-12.1** API and frontend MUST be separately built, separately imaged, and
  independently deployable.
- **FR-12.2** An OpenAPI document MUST be the source of truth for the contract.
- **FR-12.3** The frontend's API client MUST be generated from that document,
  and CI MUST fail if the implementation and the document disagree.
- **FR-12.4** The API MUST be versioned, and a breaking change MUST be a version
  change.
- **FR-12.5** The frontend MUST hold no database credentials and no business
  rules.

### FR-13 - Test automation and delivery

- **FR-13.1** The test suite MUST be layered as a pyramid: many fast isolated
  tests, fewer integration tests against real infrastructure, fewer contract
  tests, and a small number of end-to-end tests.
- **FR-13.2** Integration tests MUST run against a real database, not a
  substitute.
- **FR-13.3** Every projection model MUST have both correctness tests and
  refusal tests.
- **FR-13.4** The DEV-only assertion guarding destructive operations MUST have a
  test proving it refuses in PRD.
- **FR-13.5** CI MUST run the full suite on every pull request and MUST block
  merge on failure.
- **FR-13.6** CI MUST include a job that fails the build if anything resembling
  personal financial data is committed (Constitution IX).
- **FR-13.7** CI MUST include dependency, code and container security scanning.
- **FR-13.8** Releases MUST produce versioned images and a deployable artifact.

Details: `testing.md`, `cicd.md`.

---

## Non-functional requirements

- **NFR-1 Security.** See `security.md`. Binding, not advisory.
- **NFR-2 Performance.** A dashboard MUST render within two seconds against ten
  years of daily balances across twenty-five accounts. A sync of that dataset
  MUST complete within five minutes.
- **NFR-3 Observability.** Every sync and projection MUST be traceable through
  structured logs and traces, sufficient to diagnose a failure without
  reproducing it.
- **NFR-4 Operability.** A new user MUST reach a running DEV environment with
  generated data in one documented command.
- **NFR-5 Auditability.** Credential use, data mutation and report execution
  MUST be recorded immutably.

## Out of scope

- Multi-tenant or multi-household use. The system is single-user.
- Executing trades, moving money, or any write to an external financial system.
- Tax filing, or advice presented as professional guidance.
- Mobile applications.
- KQL (`research.md`).
- Kubernetes (`research.md`).

## Open questions

- `[NEEDS CLARIFICATION]` Retention: how long are raw provider payloads kept
  before being discarded in favour of the normalised record?
- `[NEEDS CLARIFICATION]` Conflict resolution when two sources report the same
  real-world account - automatic merge, or always a user decision?
- `[NEEDS CLARIFICATION]` Should the system support manual accounts with
  hand-entered balances in the first release, or only synced ones?
