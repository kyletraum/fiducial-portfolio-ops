# `specs/` - specification set for the portfolio platform

This tree holds the **requirements and design documents** for this platform.
The code that implements them lives in the same repository, alongside it.

## Where these came from

These documents were written, and adversarially reviewed three times, **before
any code existed**. They were drafted in a separate planning repository -
a file-based system of record with registers, decision files and an acceptance
test - and **moved here** once this repository was created, so that the
specification and the code it describes live and change together. One tree, one
editor, no second hand-edited copy.

What stayed behind, deliberately, is everything that is *about the planning
system* rather than about this platform: the decision files that ruled on scope
and method, the finding register, and the operating contract those cite. Those
are that repository's system of record and have no meaning here. Where a
document below cites an identifier like `D-030` or `C-374`, it is citing that
record, not a file in this repository.

## What this platform generalises

The planning system it came from provides some of these capabilities today as
Python scripts over YAML files. The mapping is why the data model looks the way
it does:

| There, today | Here, eventually |
|---|---|
| `state/accounts.yaml` | `institution` / `account` tables |
| `state/history.yaml` | `account_balance` time series |
| `state/allocation.yaml` | derived allocation views |
| `policy/ips.yaml` | policy tables driving target-vs-actual |
| `knowledge/constants.yaml` | sourced-or-gated constants |
| `models/*.py` | `IProjectionModel` plugins |
| `scripts/gen_portfolio_dashboard.py` | the dashboard UI |

These are a **mapping of shapes, not a transfer of content.** The platform is
generic: it models *a* portfolio, not any particular person's. No balance,
account identifier, institution name or household detail from that system
appears anywhere under `specs/`, and none may be added. The repository's
hygiene scanner enforces the same rule mechanically on every commit.

## spec-kit

**Installed 2026-09-24**: spec-kit `0.14.3.dev0`, via
`specify init --here --force --integration claude --script sh --ignore-agent-tools`.
That added `.specify/` and the `speckit-*` skills under `.claude/skills/`. In this
version the commands are skills named `/speckit-plan`, `/speckit-tasks` and so on,
not `/speckit.plan`.

**Installed is not converted.** These documents were written in
[spec-kit](https://github.com/github/spec-kit)'s **general shapes** but not its
exact ones, and the difference is a conversion rather than a drop-in:

| spec-kit expects | this tree has |
|---|---|
| `FR-001` numbering | `FR-1`, `FR-1.1` |
| prioritised user stories (`P1`/`P2`/`P3`), each independently testable | one unprioritised primary story |
| a `## Success Criteria` section of `SC-###` measurable outcomes | none |
| an `## Assumptions` section | none |
| `## Governance` plus a `Version \| Ratified \| Last Amended` footer in the constitution | neither |
| `tasks.md`, `quickstart.md`, `checklists/` | none |

`.specify/templates/tasks-template.md` decomposes by those story priorities, so
`/speckit-tasks` has nothing to work from until the conversion is done.
Verified against `github/spec-kit@main`, 2026-09-18.

The constitution has moved to `.specify/memory/constitution.md`, where the skills
read it. `specs/constitution.md` is now a pointer, kept so existing links and the
review documents' citations still resolve. `specs/001-portfolio-platform/` keeps
its path.

## Layout

```
specs/
  README.md                     this file
  constitution.md               pointer: moved to .specify/memory/constitution.md
  001-portfolio-platform/
    spec.md                     WHAT: requirements and acceptance criteria
    plan.md                     HOW: architecture and the Aspire app model
    research.md                 decisions taken, alternatives rejected
    data-model.md               PostgreSQL schema and provenance model
    environments.md             PRD vs DEV, persistence, mock data
    security.md                 threat model and controls
    testing.md                  the test automation pyramid
    cicd.md                     CI/CD pipelines
    deployment.md               local Docker and Azure Container Apps
    contracts/api.md            the REST surface
    traceability.md             requirement -> spec section map
    slice-01.md                 THE LIVE UNIT OF WORK - build this
  review/
    process.md                  claim-strength markers and review dispositions
    committee-findings.md       round 1: 14 seats, 89 findings
    round-2-findings.md         round 2: 9 seats, plus a review of that review
```

Read the constitution (`.specify/memory/constitution.md`) first. It is short, and the rest of the set assumes it.
Then read **`001-portfolio-platform/slice-01.md`** - it is the only document
describing work that is actually being done.

## Status, and how to read the rest

There are **three kinds of document here** and the difference matters:

| | | |
|---|---|---|
| **LIVE — build this** | `001-portfolio-platform/slice-01.md` | the only unit of work. ~135 hours. |
| **LIVE, with known defects** | `constitution.md`, `spec.md`, `traceability.md` | governing documents. The last two carry a **KNOWN DEFECTS** note naming what the review found and was not repaired; `constitution.md` is current and carries three amendments. |
| **FROZEN REFERENCE** | the other nine in `001-portfolio-platform/` | describe work that may never be commissioned. Consulted, not executed. |

Each of the nine frozen documents opens with a banner naming, **by line**, the
claims three review rounds contradicted - so you do not have to guess which
paragraph is the wrong one. A frozen document is corrected **only when a slice is
commissioned against it**; until then the banner is the correction.

**Treat a paragraph the banner does not mention as unreviewed, not as verified.**
The banners name what was found wrong, which is not the same as certifying
everything else.

Version numbers throughout were originally written as *confirm at scaffold time*
because the session that drafted them had no .NET SDK and could not reach the
vendor sites. Several have since been checked at a pinned reference and carry a
strength marker inline (`EXECUTED`, `SOURCE@<ref>`, `DOCS@<date>`, `INFERRED`) -
see `review/process.md` for what those mean. **A claim without a marker has not
been verified**, and a load-bearing one is named as a gate on the step that
depends on it rather than as a caveat.
