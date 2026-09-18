# CI/CD

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
> - **`:51` — UNMEASURABLE.** Coverage floors are attached to the unit-test job, but three of the five name assemblies unit tests never load. No collector or cross-job merge is named either.
> - **`:170` — CANNOT WORK SOLO.** *Required review before merge.* A PR author cannot approve their own PR, and on a **personal** repository the owner is an admin, so the rule is silently bypassed by default rather than enforced. Keep required status checks, linear history and no force-push; drop required approvals. [`DOCS@2026-09-18` github/docs]
> - **`:149` — PROTECTS THE OPTIONAL TARGET.** Image signature verification exists only in the Azure deploy pipeline. The **primary** target is a self-hoster running `docker compose up`, which verifies nothing. Publish the verify command in the release notes.
> - **Not prevention.** Push protection matches provider *secret* patterns — not balances, not institution names, which is what Constitution IX is about. And a force-push does not remove a pushed commit. The pattern set must also run as a pre-commit hook.
>
> Anything *not* listed above is **unreviewed, not verified**. Where a claim
> about an external system carries a strength marker (`EXECUTED`,
> `SOURCE@<ref>`, `DOCS@<date>`, `INFERRED` — see `../review/process.md`), that
> marker is the claim's real weight. An unmarked external claim has not been
> checked.

GitHub Actions. Described as pipelines and their guarantees rather than as YAML,
so the intent survives the syntax changing.

Five workflows:

| Workflow | Trigger | Guarantee |
|---|---|---|
| **PR** | pull request | nothing merges that does not build, pass and conform |
| **Security** | PR, and weekly | no known-vulnerable dependency, no leaked secret, no vulnerable image |
| **Data hygiene** | every push | no personal data enters the repository |
| **Release** | tag | versioned, signed, reproducible artifacts |
| **Deploy** | manual | a reviewed change reaches the cloud environment |

---

## PR pipeline

Runs on every pull request. **All jobs are required checks**; merge is blocked on
failure (FR-13.5).

Jobs, parallel where they can be:

1. **Build** - solution and frontend, warnings as errors. Fail fast; nothing else
   is worth running if this fails.
2. **Lint and format** - .NET analysers and formatting, ESLint, Prettier,
   TypeScript strict. Formatting is checked, never auto-fixed on a PR: a bot
   commit on someone's branch is a poor trade for a one-line fix they can make.
3. **Unit tests** - with coverage against the floors in `testing.md`.
4. **Integration tests** - Testcontainers, real PostgreSQL. The slowest required
   job, and the one that catches the most.
5. **Contract tests** - regenerate the OpenAPI document and **fail on any diff
   against the committed one** (FR-12.3); regenerate and compile the client.
6. **E2E tests** - build images, start a DEV stack, seed with a fixed seed, run
   Playwright. Traces and screenshots uploaded on failure - an E2E failure
   without a trace costs more to diagnose than the test saved.
7. **Architecture tests** - the Aspire boundary and dependency-direction
   invariants (`plan.md`). Cheap, and they catch a structural regression that no
   behavioural test would.

**Concurrency:** a new push cancels the previous run on the same branch. There is
no value in finishing a run against superseded code.

**Caching:** NuGet and npm caches keyed on lockfiles. Never cache test results -
a cached pass is not a pass.

---

## Security pipeline

On pull requests and weekly on a schedule. Weekly matters: a dependency
advisory published today affects code that has not changed.

1. **Secret scanning** with **push protection enabled** - the only control here
   that prevents rather than detects.
2. **Dependency review** - fails on a PR introducing a known-vulnerable package.
3. **Static analysis** (CodeQL) for C# and TypeScript.
4. **Container scanning** of built images; fails on high or critical
   vulnerabilities with a fix available.
5. **License check** - flags a dependency whose licence is incompatible with the
   project's.

Findings go to the repository's security tab. A public repository holding a
finance application will be scanned by others; better to find things first.

---

## Data hygiene pipeline

Constitution IX, mechanically (FR-13.6). Runs on **every push**, including to
branches, because a secret or a real balance in an unmerged branch is still
published.

Scans the diff for:

- account-number and routing-number shapes;
- credential-shaped strings - API key prefixes, JWT structure, private key
  headers, connection strings with passwords;
- real financial institution names in seed data, fixtures or documentation;
- balance-shaped values in fixtures that fall outside the generator's declared
  ranges;
- files that should never be committed: `.env`, backup dumps, local settings,
  database volumes.

Fails the build on a hit. **False positives are expected and acceptable**: the
cost of one is a minute of annoyance and an allow-list entry with a comment; the
cost of one miss on a public repository is permanent. The allow-list is reviewed,
never wildcarded.

This job is written **before the first real connector**, not after. A control
added once it is needed is added after the first mistake.

---

## Release pipeline

On a version tag.

1. Re-run the full PR pipeline. A tag does not inherit trust from a green PR -
   the merge commit is not the commit that was tested.
2. Derive the version from the tag; stamp it into assemblies, images and the
   `/system/version` endpoint.
3. Build the API and web images **separately** (FR-12.1), multi-architecture.
4. Push to the registry, tagged with the version and **referenced by digest**
   everywhere downstream.
5. Generate the Compose deployment artifact from the Aspire app model
   (`deployment.md`) and attach it to the release, so a self-hoster gets a file
   matching that exact version rather than a snapshot of `main`.
6. Generate and publish an **SBOM** per image.
7. **Sign** images and attach provenance attestation.
8. Publish release notes with the upgrade path and any migration requiring
   attention.

**Never published:** a `.env` file, a database dump, or anything the data
hygiene pipeline would reject.

---

## Deploy pipeline

Manual dispatch only, with an environment approval gate. Nothing deploys
automatically; a single-user application has no need for continuous deployment
and every reason to prefer a deliberate act.

1. Select a released version by digest - never a tag, never `latest`. A tag can
   move; a digest cannot.
2. Verify the image signature before deploying.
3. Apply migrations as a **separate, explicit step** that completes before the
   new version serves traffic (`plan.md`).
4. Deploy to the cloud target.
5. Smoke test: health, version, authentication, one dashboard.
6. Roll back on failure - to the previous digest, with the migration rollback
   path documented per release.

**Self-hosted deployment is not automated.** The operator downloads the release
artifact and runs it. Their machine, their timing, their call - and an
auto-updating application holding someone's financial data is a worse idea than
the convenience it offers.

---

## Repository settings

These make the pipelines binding rather than advisory:

- Branch protection on `main`: required status checks, no force-push, no
  deletion.
- Required review before merge.
- Linear history, so a bisect means something.
- Secret scanning with push protection: **on**.
- Dependency alerts and automated security updates: **on**.
- Actions restricted to verified and explicitly allow-listed publishers,
  **pinned by commit SHA** rather than tag - a third-party action pinned to a
  mutable tag is arbitrary code execution with repository credentials.
- Deploy environments require approval and hold their own scoped secrets.

---

## What CI does not do

- **It does not touch real financial data.** No job holds a real credential for a
  financial provider. Sandboxes and stubs only (`testing.md`).
- **It does not auto-merge.** Not even dependency updates - a merged dependency
  bump nobody looked at is how supply-chain attacks land.
- **It does not deploy on merge.** Release and deploy are deliberate.
- **It does not skip on "trivial" changes.** There is no reliable way to know a
  change is trivial before testing it.
