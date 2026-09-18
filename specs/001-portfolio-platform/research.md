# Research and Decisions

Why the platform is built the way `plan.md` describes, and what was considered
and rejected. Each entry records the decision, the reasoning, and the
alternatives - so a future reader can reopen a decision on its merits rather
than re-deriving it.

---

## D1 - C# and React

**Decision:** ASP.NET Core backend, React + TypeScript frontend.

**Why:** the project is deliberately a skills-development exercise on the stack
its author works against professionally. That is a legitimate and sufficient
reason, and it would be dishonest to dress it up as a technical evaluation.

Independently, the stack suits the work: exact decimal arithmetic is native,
long-running background services are first-class, the type system supports a
plugin interface that makes an unresolved input unrepresentable, and both the
MCP SDK and the major financial API providers ship or support C# clients.

**Alternatives:** Python end-to-end would have reused the existing projection
scripts directly, at the cost of the learning goal and of weaker interactive UI.
TypeScript end-to-end would give one language across the stack and the most
mature MCP SDK, but no reason to prefer it here.

---

## D2 - .NET Aspire for orchestration only

**Decision:** Aspire owns the app model, service discovery, telemetry wiring and
the publish paths. `Domain` and `Projections` have no Aspire dependency, and an
architecture test enforces it.

**Why:** Aspire removes real drudgery - connection strings, container lifecycle,
OpenTelemetry setup, a dashboard - and gives one description of the topology
that serves local development, Compose and Azure alike. That is worth having.

But orchestration frameworks are the kind of dependency that spreads if
unchecked, and the projection engine is the part of this system most worth
keeping portable and directly testable. Drawing the boundary explicitly, with a
test, costs one test and preserves both.

**Consequence worth stating plainly:** Aspire's AppHost is a **development
orchestrator**, not a production runtime. The self-hosted deployment is a
generated Compose file. Systems get this wrong by assuming the thing that runs
locally is the thing that runs deployed.

**Alternatives:** hand-written Compose for everything - fewer moving parts, no
dashboard, no service discovery, and topology duplicated across environments.
Leaning into Aspire everywhere - more learning of the framework, at the cost of
models that need a host to run.

---

## D3 - PostgreSQL

**Decision:** PostgreSQL, in a container with a persistent named volume.

**Why:** exact `numeric` arithmetic for money, strong time-series query support
for balance history, mature `GRANT`-based privilege separation for the read-only
reporting role, excellent EF Core support via Npgsql, small footprint, and a
permissive licence for a public project.

**Alternatives:** SQL Server is closer to a typical enterprise C# environment,
but it is a heavier container with licensing constraints that complicate a
public repository. SQLite is simplest, but concurrent ingestion and a separate
read-only reporting role - a hard requirement here - are exactly where it is
weakest.

---

## D4 - SQL only; KQL rejected

**Decision:** reporting is SQL against a documented view schema. KQL is out of
scope.

**Why:** KQL is Kusto's language. Supporting it over PostgreSQL would mean
either a translation layer or an embedded Kusto-compatible engine. A translation
layer is a correctness surface in its own right - partial coverage, subtly wrong
results in edge cases, and a permanent maintenance burden - attached to the one
feature where a wrong answer is least likely to be noticed. The requirement it
would serve is already met by SQL.

**Revisit if:** the reporting schema stabilises and a genuine need appears, at
which point a translator over a fixed, documented view surface is a far more
tractable problem than one over an evolving schema.

---

## D5 - Read-only enforced by privilege, not by parsing

**Decision:** user SQL runs on a connection whose database role holds `SELECT`
on the `reporting` schema and nothing else.

**Why:** validating query text is a losing game - comments, nested statements,
functions with side effects, dialect quirks. A role without `INSERT` cannot
insert regardless of how the statement is written. The database is the right
place for a guarantee the application cannot be talked out of.

The view schema doubles as a stable public contract: physical tables can change
without breaking saved reports.

**Alternatives:** parsing and allow-listing. Rejected as unsound. It remains
useful as a *usability* layer - a clear error before the database refuses - but
never as the security boundary.

---

## D6 - A model is a plugin behind a narrow interface

**Decision:** `IProjectionModel` with declared metadata, a validation step that
returns either resolved inputs or a refusal, and a run step reachable only with
resolved inputs.

**Why:** extensibility was a stated requirement, and the usual failure mode is
an interface so accommodating that every model needs bespoke API and UI support.
Declared input metadata lets the UI render any registered model without
modification, which is what makes "quickly build out modeling capabilities"
true rather than aspirational.

Making `Run` unreachable without resolved inputs puts Constitution II in the
type system. A convention that says *do not default a missing input* is one
deadline away from being broken; a signature that offers nowhere to put a
default is not.

**Validation of the design:** six distinct existing models get ported. If the
interface is too narrow, that exercise reveals it early and cheaply.

---

## D7 - Provenance as schema, not convention

**Decision:** every externally-sourced row carries source system, upstream
identifier, provenance strength, observation time and originating run.

**Why:** carried forward from the planning system this generalises, where the
recurring class of defect was a figure whose origin could not be reconstructed.
Provenance in a comment or a changelog decays; provenance in a column can be
queried, displayed, and tested.

It also makes Constitution IV enforceable: the UI can show that a figure came
from a live page rather than a statement, and the user can weigh it accordingly.
A weaker source beats a null - *if* the weakness travels with it.

---

## D8 - Generated data before real connectors

**Decision:** the deterministic generator and the stub MCP server are built
before either real connector.

**Why:** three payoffs from one piece of work. The ingestion pipeline gets built
and tested with no credentials in existence. The public repository gets a demo
anyone can clone and run. And the test suite gets fixtures covering the awkward
states - a series that stops, a transfer that never lands, an input with no
verified value - which are the states the constitution's rules exist for and the
ones a naive generator omits.

Building connectors first would mean either testing against real financial data
or writing the generator later anyway, with the pipeline already shaped around
one provider's quirks.

---

## D9 - Azure Container Apps as the cloud target; Kubernetes rejected

**Decision:** three deployment targets - local Aspire inner loop, self-hosted
Docker Compose, Azure Container Apps. Not Kubernetes.

**Why:** Container Apps is Aspire's first-class path, so the same app model
drives all three with no separate deployment description. Managed identity and a
managed secret store map cleanly onto the credential design, and managed
PostgreSQL removes the operational burden that matters most in a hosted context.

Kubernetes is more portable and would be defensible for a multi-tenant service.
For a single-user application it is a large amount of operational surface for no
benefit the other two targets do not already provide.

**Note:** self-hosted Compose remains the primary target. Cloud is optional by
Constitution VIII, and the system must stay fully functional with no cloud
account.

---

## D10 - One secret-store interface, three implementations

**Decision:** `ISecretStore` with an implementation per environment - framework
parameters and user-secrets in the inner loop, envelope encryption for
self-hosted, a managed secret service in the cloud.

**Why:** the three environments have genuinely different trust models and
genuinely different right answers. A single mechanism stretched across all three
is either too weak locally or unusable in the cloud. An interface keeps the
difference at the edge instead of spreading conditionals through the code.

Envelope encryption for the self-hosted case - data keys in the database,
key-encrypting key from a Docker secret or host keyring - means a stolen database
volume alone does not yield credentials.

---

## D11 - Contract-first API, with drift as a build failure

**Decision:** OpenAPI generated from the implementation and committed; the
frontend client generated from it; CI fails on a diff.

**Why:** separate API and frontend deployables were a stated requirement, and
the standard failure is drift discovered at runtime. Making the document a
committed artifact and its regeneration a CI check turns a class of integration
bug into a build failure - which is the whole argument for contract testing,
demonstrated rather than described.

Generating the document *from* the implementation rather than hand-authoring it
keeps the two from disagreeing about what exists.

---

## D12 - Soft delete, never hard delete

**Decision:** records are marked deleted; nothing is removed.

**Why:** a source withdrawing a record is itself a fact worth keeping. An
amended transaction and one that never existed are different situations, and a
hard delete makes them indistinguishable. Historical figures must remain
reconstructible, which a destructive delete prevents.

**Cost:** queries filter on the deleted flag, and the reporting views must do so
correctly - a real and recurring source of bugs, which is why the views are the
only reporting surface.

---

## Open questions carried into implementation

- Charting library: candidates are Recharts (simple, React-native API) and
  ECharts (far more capable, heavier). Decide when the first dashboard is built
  and real rendering requirements exist, not before.
- Authentication for a single-user local application: passkey/WebAuthn is the
  strongest option and the best learning exercise; a password with a modern KDF
  is simpler. `security.md` carries the analysis.
- Whether raw provider payloads are retained after normalisation, and for how
  long. Retention is useful for reprocessing and a liability for storage and
  privacy.
