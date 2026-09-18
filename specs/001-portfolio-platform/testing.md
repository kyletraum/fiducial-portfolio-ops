# Testing

The test suite is a deliverable, not scaffolding. This project is partly a
training artifact, so each layer states not only *what* it tests but *why it
lives at that layer rather than one above it* - which is the part usually left
unsaid and the part that actually decides whether a suite stays fast and useful.

---

## The pyramid

```
        ╱ E2E ╲          ~10 tests      minutes    real browser, real stack
      ╱ Contract ╲       ~30 tests      seconds    API vs OpenAPI document
    ╱  Integration ╲     ~150 tests     ~2 min     real Postgres, real app model
  ╱      Unit        ╲   ~800 tests     <30 sec    no I/O at all
```

The shape is not decoration. Each layer up costs roughly an order of magnitude
more to run and more to diagnose when it fails. A failing unit test names the
function; a failing E2E test names the application. Push every test as far down
as it can go while still testing something real.

**The inversion to avoid:** a suite where most confidence comes from E2E tests.
It is slow, it is flaky, and a failure tells you only that something,
somewhere, broke.

---

## Layer 1 - Unit

**Scope:** `Domain` and `Projections`. Pure logic, no I/O.

**Why here:** these assemblies have no dependencies (`plan.md`), so their tests
need no host, no database and no fixtures. Money arithmetic, provenance
comparison, date handling, closed-account invariants and every projection model
are *decidable from inputs alone*. Testing them through the API would be slower
and would mean an API bug and a domain bug look identical.

**What is tested:**

- Money arithmetic: rounding, currency mismatch rejection, sign conventions.
- Value-object invariants: an account cannot close before it opens; a balance
  cannot post after its account closed.
- Provenance ordering: `statement` outranks `api` outranks `scraped`.
- **Every projection model, twice** (FR-13.3):
  - **correctness** - known inputs produce known outputs, with values derived by
    hand and the derivation recorded in the test, so the test is evidence rather
    than a snapshot of whatever the code did first;
  - **refusal** - a null or unresolved input **must** produce a refusal naming
    the input (Constitution II). Every model. No exceptions.
- The architecture invariant: no Aspire dependency outside `AppHost` and
  `ServiceDefaults`; no third-party dependency in `Domain` or `Projections`.
- Frontend units in Vitest and React Testing Library: rendering logic, the
  unverified-versus-zero distinction, conditional-money badges, the
  series-that-stops chart case.

**Rules:** no database, no filesystem, no network, no clock - time is injected.
A unit test that needs a container is an integration test in the wrong project.

---

## Layer 2 - Integration

**Scope:** everything that crosses a process boundary. Real PostgreSQL via
Testcontainers; the real app model via the Aspire testing package.

**Why here:** these behaviours are *properties of the infrastructure*, not of
the code in isolation. A mocked database happily accepts a violated constraint,
returns rows in an order the real one would not, and knows nothing about
privileges. Mocking here would test the mock.

**Rule: no substitute for the database** (FR-13.2). Not an in-memory provider,
not SQLite standing in for Postgres. The provider differences that matter -
`numeric` semantics, constraint enforcement, `GRANT` behaviour, timezone
handling - are precisely what these tests exist to verify.

**What is tested:**

- Migrations apply cleanly from empty, and are idempotent.
- Repositories round-trip every entity with provenance intact.
- **Idempotent ingestion** (FR-4.2): replaying a sync changes no counts.
- Soft delete: a withdrawn record is marked, not removed, and disappears from
  the views.
- The closed-account constraint rejects a later balance **at the database level**.
- Supersession: a restated balance inserts and links rather than overwriting.
- **The reporting role**: cannot write, cannot read `app`, cannot exceed the
  statement timeout or row cap (`security.md`).
- Views exclude soft-deleted rows, never gap-fill, and flag conditional money.
- The ingestion pipeline end to end against the stub MCP server and the provider
  sandbox, including failure paths: timeout, partial page, malformed record,
  expired credential, mid-run failure.
- Webhook signature verification, valid and invalid.
- The MCP allow-list: a stub advertising a write tool; assert it is never called.
- **The DEV assertion refuses in PRD** (FR-13.4).
- Secret store: encrypt, decrypt, rotate, revoke; no plaintext at rest.
- Backup and restore round-trip (FR-9.2) - back up a seeded database, restore
  into a fresh one, assert contents match.

**Fixtures** come from the deterministic generator (`environments.md`), so the
awkward states are covered by construction rather than by someone remembering to
write them.

---

## Layer 3 - Contract

**Scope:** the API against its OpenAPI document, and the generated client
against both.

**Why here:** the API and frontend are separately deployable (FR-12.1), so
nothing else catches drift until runtime. These tests are cheap and they convert
a whole class of integration bug into a build failure - the clearest available
demonstration of why contract testing exists.

**What is tested:**

- The generated OpenAPI document matches the committed one; a diff fails the
  build (FR-12.3).
- The generated TypeScript client compiles against the document.
- Every response conforms to its declared schema.
- **No endpoint's schema contains a credential field** (FR-3.2).
- Money is always the `{amount, currency}` object, never a bare number.
- Error responses conform to RFC 9457; a refusal is a 422 carrying
  `unresolved_inputs`.
- The five client conformance properties in `contracts/api.md` - nulls rendered
  as unverified, conditional money displayed, series not extended past its end,
  provider text escaped, refusal detail surfaced.
- The stub MCP server's tool schemas match the real server's published schemas,
  so the stub cannot drift into testing a fiction.

---

## Layer 4 - End to end

**Scope:** Playwright against a DEV stack seeded with generated data.

**Why here, and why so few:** these are the only tests that prove the assembled
system works for a person. They are also the slowest and the flakiest, and a
failure localises to nothing. So: roughly ten, each covering a journey no lower
layer can, and none covering logic a lower layer already does.

**The journeys:**

1. Connect a mock institution and see accounts appear with provenance.
2. Trigger a sync and watch it complete, with the run recorded.
3. Open the net worth dashboard; verify the conditional-money badge appears when
   an unlanded transfer exists.
4. Drill into a closed account; verify the chart **stops** and says why.
5. Write ad-hoc SQL and get results; attempt a write and see it refused.
6. Save a parameterised report, run it with parameters, export results.
7. Create a scenario, run a projection, view the result.
8. Run a projection with an unresolved input; verify the UI shows **which**
   input is missing, not a generic error.
9. Compare two scenarios side by side.
10. Reset and reseed DEV; verify the environment banner is correct throughout.

**Not tested here:** anything decidable at a lower layer. No "does the
arithmetic work" E2E test.

---

## Standards

**Coverage** - a floor, not a target:

| Layer | Floor |
|---|---|
| `Domain` | 90% |
| `Projections` | 95% - including every refusal path |
| `Infrastructure` | 70% |
| `Api` | 80% |
| Frontend | 70% |

Coverage measures what was executed, not what was verified. High coverage with
weak assertions is worse than honest gaps, because it produces false confidence.
Review assertions, not percentages.

**Speed** - unit under 30 seconds, integration under 2 minutes, contract under
30 seconds, E2E under 5 minutes. A suite developers avoid running is a suite
that stops catching things. If a budget is exceeded, fix the suite rather than
raising the budget.

**Flake policy** - zero tolerance. A test that fails intermittently is quarantined
**with an issue** within one day and fixed or deleted within a week. Tolerated
flake teaches the team to ignore red, which costs more than the test was ever
worth.

**Determinism** - time is injected, randomness is seeded, no test depends on
another's state or on execution order. Every test creates and disposes its own
data.

**Test data** - always from the generator, never copied from real data, never
invented ad hoc per test (Constitution IX).

---

## What is deliberately not tested

- Third-party library behaviour. Test the integration, not the dependency.
- Framework wiring that a compile failure already catches.
- Exact chart pixels. Test the data feeding the chart.
- Real provider APIs in CI. Sandboxes and stubs only - a test suite that depends
  on a live financial provider is a suite that fails for reasons unrelated to the
  change under test.
