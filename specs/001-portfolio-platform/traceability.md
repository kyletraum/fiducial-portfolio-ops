# Traceability

> ### ⚠ KNOWN DEFECTS, NOT FIXED — 2026-09-18
>
> This document is **live**, not frozen, but the review found defects in it whose
> repair was **cut** by `D-030` Amendment 1 as pre-step-1 paperwork. They are
> named here rather than silently carried:
>
> - **The Constitution coverage table stops at XI.** Amendment 1 added principles XII and XIII the same day this file was last retargeted, and Amendment 3 has since amended III. Three rows are therefore incomplete or false: I does not mention the working-assumption state, III names a `closed_on` `CHECK` that cannot be built, and XII/XIII have no rows.
> - **Its claim that every requirement has a home does not hold** — see `spec.md`'s note above for three behaviours with no FR behind them.
>
> Nothing above blocks `slice-01.md`. Fixing them is `DEFER`red, not `CUT` — the
> trigger is the slice that first depends on the affected requirement.

Every requirement as originally stated, mapped to where the specification
satisfies it. No requirement without a home; no spec section without a
requirement behind it.

---

## Original requirements

Stated at the outset, in the words they were given:

| # | Requirement as stated |
|---|---|
| R1 | Be able to pull data from MCP servers, like Monarch. |
| R2 | Be able to pull data from API sources, like Plaid. |
| R3 | Store credential data securely. |
| R4 | Persistently and safely keep track of accounts, transactions, and balances from multiple accounts and institutions. |
| R5 | Be able to create dashboards on data it stores. |
| R6 | Allow reports based on SQL against the schema we create for our persistent storage. |
| R7 | Have a web application interface as a GUI. |
| R8 | Allow projections and scenario modeling for finances, tilted towards retirement planning, easily extendable. |
| R9 | Have security in mind for database/persistent storage, any API access, and any UI elements. |
| R10 | Publishable repo/solution I could hand to someone; my data needs to live on in a persistent container. |
| R11 | PRD versus DEV environments when running locally. |
| R12 | For DEV, requirements around mock data. |
| R13 | Separate API versus front end. |
| R14 | Automated testing via the test automation pyramid, with supported CI/CD pipelines and testing. |

R6 originally said "KQL or SQL". KQL was dropped by decision; see `research.md`
D4.

---

## Map

| # | Functional requirements | Design | Verification |
|---|---|---|---|
| **R1** | FR-1.1 - FR-1.5 | `plan.md` ingestion; `research.md` D8 | stub MCP server contract tests; allow-list test (`security.md`); integration tests |
| **R2** | FR-2.1 - FR-2.5 | `plan.md` ingestion | sandbox integration tests; webhook signature tests |
| **R3** | FR-3.1 - FR-3.5 | `security.md` credentials; `research.md` D10; `data-model.md` `connection.credential_ref` | no-credential-in-response contract test; no-secret-in-logs test; rotation and revocation integration tests |
| **R4** | FR-4.1 - FR-4.8 | `data-model.md` in full; `research.md` D7, D12 | idempotency, soft-delete, supersession, closed-account constraint integration tests |
| **R5** | FR-5.1 - FR-5.5 | `contracts/api.md` dashboards; `data-model.md` `reporting` views | E2E journeys 3 and 4; view integration tests |
| **R6** | FR-6.1 - FR-6.6 | `data-model.md` `reporting`; `research.md` D4, D5; `security.md` database | reporting-role privilege tests; timeout and row-cap tests; E2E journeys 5 and 6 |
| **R7** | FR-7.1 - FR-7.5 | `plan.md` frontend; `contracts/api.md` | Vitest units; all E2E journeys |
| **R8** | FR-8.1 - FR-8.9 | `plan.md` projections; `research.md` D6; `data-model.md` projection tables; `contracts/api.md` models and scenarios | per-model correctness **and refusal** unit tests (FR-13.3); E2E journeys 7, 8, 9 |
| **R9** | NFR-1, and FR-1.5, FR-3.x, FR-6.2 | `security.md` in full | the verification table in `security.md` - every row a test |
| **R10** | FR-9.1 - FR-9.4, FR-11.5 | Constitution IX; `environments.md` persistence; `deployment.md` self-hosted; `README.md` | backup/restore round-trip test; data-hygiene CI job (`cicd.md`) |
| **R11** | FR-10.1 - FR-10.5 | Constitution X; `environments.md` isolation | DEV-assertion-refuses-in-PRD test (FR-13.4); E2E journey 10 |
| **R12** | FR-11.1 - FR-11.5 | `environments.md` mock data; `research.md` D8 | generator determinism tests; awkward-state fixtures used across integration tests |
| **R13** | FR-12.1 - FR-12.5 | Constitution XI; `plan.md` API and frontend; `contracts/api.md` | OpenAPI drift check; generated-client compile; conformance contract tests |
| **R14** | FR-13.1 - FR-13.8 | `testing.md`; `cicd.md` | the pipelines themselves - the PR pipeline is the check |

---

## Constitution coverage

Each principle, and what makes it more than a statement of intent:

| Principle | Enforced by |
|---|---|
| I - no unsourced figures | provenance columns on every sourced row (`data-model.md`); `allocation.yaml` recomputed rather than imported |
| II - null means unverified | `Run` unreachable without resolved inputs (`research.md` D6); refusal is a stored outcome; refusal tests on every model |
| III - closed accounts stop | `closed_on` check constraint; views never gap-fill; `series_ends_at` in the API; generated closing account; E2E journey 4 |
| IV - provenance strength recorded | `source_strength` enum; ordering unit-tested; surfaced in the UI |
| V - conditional money says so | `pending_transfer` table; `has_conditional_money` required in dashboard responses; client conformance test |
| VI - determinism | `input_hashes` and `engine_version` on every run; staleness recomputed; runs never rewritten |
| VII - secrets never plaintext | `security.md` credentials; `credential_ref` is a pointer; redaction filter plus test |
| VIII - local-first | loopback default; self-hosted is the primary target; cloud optional throughout `deployment.md` |
| IX - no personal data in repo | data-hygiene CI job on every push (`cicd.md`); generator invents names; `.gitignore` from first commit |
| X - DEV cannot reach PRD | three independent guards (`environments.md`); refusal test (FR-13.4) |
| XI - API is the only way in | `contracts/api.md`; frontend holds no database access; architecture test on dependency direction |

---

## Open items

Retargeted 2026-09-18. The original table routed each item to a step of the
horizontal build order that `D-030` superseded; the live unit of work is
`slice-01.md` (manual balance entry to a net-worth chart).

### Answered since the table was written

| Item | Answer | Source |
|---|---|---|
| Manual accounts in the first release | **Yes** - manual entry *is* the first release. `slice-01.md` has no connector and no credential; every balance is hand-entered, at `source_strength` = hand entry. | `D-030` |
| Aspire Compose publisher GA status | **The package is stable; the fidelity of what it emits is the real issue.** `Aspire.Hosting.Docker` 13.5.4 is non-prerelease and needs no `ASPIRECOMPUTE002` `NoWarn` (the only member carrying that attribute is one an AppHost never calls). The generated file differs from `deployment.md`'s topology in four ways: one flat network; external endpoints written with no host IP; health-based `depends_on` silently degraded to `service_started`; and an externally-bound Aspire dashboard service that no document listed. Base images can be digest-pinned via `WithImageSHA256`; only project images need a release-pipeline rewrite. | `SOURCE@microsoft/aspire@b477bdd` (round-2 committee, .NET/Aspire seat), `DOCS@2026-09-18` api.nuget.org. **Round 1's `S-25a` also prescribed `WithComposeServiceCustomization`, which does not exist, and a `NoWarn` that is not needed - both corrected here.** |

### Still open

| Item | Where | Blocks |
|---|---|---|
| Charting library | `research.md` | `slice-01.md` step 4 (Web) - the net-worth chart cannot be built without choosing one, and `M-24` wants the tabular equivalent decided alongside it |
| Raw payload retention period | `spec.md`, `research.md` | nothing in slice 01 - there is no connector, so there is no raw payload. Blocks the first ingestion slice |
| Duplicate-account conflict resolution | `spec.md` | nothing in slice 01 - one source cannot double-count. Slice 01 takes the `account_source` identity split (`M-10`) so the resolution policy can land later without a migration; blocks the first multi-source ingestion slice |
| Passkey versus password authentication | `research.md`, `security.md` | nothing in slice 01 - it has no authentication at all. Blocks the first slice that binds beyond loopback or holds real balances |

Nothing on either list blocks `slice-01.md` step 0 or step 1. One item -
the charting library - must be answered inside slice 01; the rest are named
against the later slice that needs them, and those slices may or may not be
commissioned (`D-030` makes the stop after slice 01 a gate).
