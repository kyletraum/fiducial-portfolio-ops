# Quickstart — inventory of this repository

**Written 2026-11-15, against `main` at `b48b7ac`.** It supports
[`commission.md`](commission.md). Nothing here is extracted, moved or changed.

## How to read it

Each row is one significant piece. Its category is one of:

| Category | Means |
|---|---|
| **GENERAL** | Reusable as is, apart from the name `Portfolio` (see "The name", below) |
| **GENERAL-OPTIONAL** | Reusable, but a new project should opt in |
| **FINANCE** | Specific to this platform |
| **ENTANGLED** | General in idea, but its code or text is mixed with finance specifics. The row says what would have to be separated |

**Built in** names the slice whose commit first added the file
(`git log --diff-filter=A`). `pre` means before slice 01's step 1 (2026-09-18 to
2026-09-24: the repository, the ported specs, spec-kit, Spikes A–D). `maint` means a
commit outside a slice's steps.

**How each row was classified.** Files named in "Read" below were read in this session.
Every other file was classified by its commit, its name, and a count of finance terms
(`balance`, `account`, `institution`, `net worth`, `ingest`, `import`, `ofx`, `csv`,
`report`). A row resting on the count alone says **"by grep"**.

**Read:** `README.md`, `specs/README.md`, `.specify/memory/constitution.md`, `LOG.md`,
`slice-04.md` (commission, "is not", step 0, step 1a's BUILT note, DoD, gates,
obligations, the gate), `slice-04-assessment.md` (header, §5–§7), the head of
`specs/review/process.md` and of `specs/review/slice-04-seats/sco.md`,
`src/AppHost/AppHost.cs`, `src/AppHost/AppHost.csproj`, `src/Migrator/Program.cs`,
`src/Api/Program.cs`, `scripts/hygiene.py` (lines 1–175), `.githooks/pre-commit`,
`scripts/install-hooks.sh`, `.github/dependabot.yml`, `web/package.json`,
`web/vite.config.ts`, `.gitattributes`, `.vscode/settings.json`, `global.json`,
`dotnet-tools.json`, `deploy/.env.example`, and the head of
`tests/Integration/PostgresFixture.cs`. `ci.yml` was read for its job and step names and
lines 40–52.

**The name.** Every project's namespace starts `Portfolio.`, the solution is
`Portfolio.slnx`, and the name is in cookie names (`__Host-portfolio`, `portfolio-csrf`),
volume names (`portfolio-dev-pgdata`, `portfolio-dev-keys`), login names (`portfolio_api`,
`portfolio_reporting`), the database name (`portfolio_dev`) and the OpenAPI file name.
A rename is owed by every row and is not counted as entanglement.

## Counts

| Category | Process kit | Starting code | Total |
|---|---|---|---|
| GENERAL | 2 | 5 | **7** |
| GENERAL-OPTIONAL | 3 | 4 | **7** |
| FINANCE | 4 | 7 | **11** |
| ENTANGLED | 8 | 17 | **25** |
| **Total** | 17 | 33 | **50** |

Ids are labels only. They are not in order and some carry a letter.

---

## Half 1 — the process kit

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| P-1 | Constitution: the amendment rule, and Principles XII and XIII (claim strength; a mechanism may not stay `INFERRED`; the spike) | `.specify/memory/constitution.md` "Amendment", "Amendment 1" | pre | **GENERAL** | Cites planning IDs `D-029` and `C-374`, which do not resolve in a new project |
| P-2 | Constitution Principles VII (secrets), VIII (local-first), IX (no personal data), X (DEV never reaches PRD), XI (the API is the only way in) | same file | pre | **GENERAL-OPTIONAL** | Each is a choice. VIII and IX fix a local, public-repository project; X assumes two environments |
| P-3 | Constitution Principles I–VI and Amendments 2 and 3 | same file | pre | **FINANCE** | Figures, `null`, closed accounts, provenance strength, pending money, projection determinism. Whether I, II and IV generalise is `commission.md` OPEN-4 |
| P-4 | The review process: the four claim markers, the load-bearing test, review dispositions | `specs/review/process.md` | pre | **GENERAL** | Cites `D-029` and `C-374`. Its examples are this project's Aspire claims |
| P-5 | The review records | `specs/review/committee-findings.md`, `round-2-findings.md`, `slice-03-committee.md`, `slice-03-r2.md`, `slice-04-committee.md`, `slice-03-seats/`, `slice-03-r2-seats/`, `slice-04-seats/`, `slice-04-r2-seats/` | pre, S3, S4 | **FINANCE** | Instances. They are the evidence for P-6 |
| P-6 | The review method: seats in parallel, findings grouped into themes, Kyle's numbered rulings, a rewrite, a second round | Only in P-5 and in each commission's header (`slice-04.md` "Reviewed", "Reviewed again") | S3, S4 | **ENTANGLED** | No template is committed. To separate: a seat brief (a seat file opens with seat, date, commit under review and a "Read" list: `slice-04-seats/sco.md:1-20`), a findings-to-themes table, and a rulings table with the options each ruling was chosen from |
| P-7 | The commission and slice definition | `specs/001-portfolio-platform/slice-01.md` … `slice-04.md` | pre (01), S2, S3, S4 | **ENTANGLED** | The headings are the format: The commission; What the slice is; What the slice is not; Step 0 — the spikes; The build; Definition of done; Gates; Obligations; The gate; Then stop (`slice-04.md`). To separate: a blank file with those headings |
| P-8 | BUILT notes: a dated note under each build step, with mutations "seen failing" and claims marked `EXECUTED` | Inside P-7's files, e.g. `slice-04.md:286` | S1–S4 | **ENTANGLED** | A convention inside instances. To separate: one worked example and the rule that a DoD item names its test and how it was seen failing (`slice-04.md` "Definition of done") |
| P-9 | The gate: STOP by default, overridden only by named techniques with commits and an hours limit | `slice-04.md` "The gate"; first in `slice-01.md` | pre | **ENTANGLED** | The override limb is this project's purpose (techniques Kyle did not have), and the 200-hour limit is from planning `D-030`. To separate: the default and the "NOT reasons to continue" list are general; the override limb is a parameter |
| P-10 | The assessment: facts by the agent, sections 5 and 6 by Kyle, every box unticked | `slice-01-assessment.md` … `slice-04-assessment.md` | S1–S4 | **ENTANGLED** | To separate: the section list (DoD; cost; what the build corrected; shown or argued; techniques; decision; owed elsewhere) and the "Who wrote which part" paragraph |
| P-11 | The build log: one row per session, `Kyle h` and `Agent h`, totals at the gate, the units note | `LOG.md` | pre (`3fc01b5`), columns split `5afc256` | **ENTANGLED** | The header, the columns and the units note are general. The rows are this project's. The header cites `D-030` and the 200-hour threshold |
| P-12 | spec-kit | `.specify/`, `.claude/skills/speckit-*` (10 skills) | pre (`1b783a4`) | **GENERAL-OPTIONAL** | Installed, not converted: `/speckit-tasks` has nothing to work from (`specs/README.md` "spec-kit") |
| P-13 | The frozen specification set | `specs/001-portfolio-platform/spec.md`, `plan.md`, `research.md`, `data-model.md`, `environments.md`, `security.md`, `testing.md`, `cicd.md`, `deployment.md`, `traceability.md`, `contracts/api.md` | pre (`534e995`) | **FINANCE** | The convention they follow (a frozen document opens with a banner naming the claims a review contradicted) is described in P-14 |
| P-14 | The specs index: the layout, the three kinds of document, "a claim without a marker has not been verified" | `specs/README.md`, `specs/constitution.md` (a pointer) | pre | **ENTANGLED** | Mixed with the planning-system mapping table and this platform's file list. To separate: the "Status, and how to read the rest" section |
| P-15 | Spikes about the stack and the security tier: A (two stacks), B (API address), C (Migrator), F (upload), H (passkeys), I (proxy), J (CSRF) | `spikes/spike-a-two-stacks/` … each with `RESULTS.md` | pre (A–C), S2 (F), S3 (H–J) | **GENERAL-OPTIONAL** | Evidence, not code to copy. The form (the smallest runnable thing, a `run.sh`, a `RESULTS.md`) is the reusable part |
| P-16 | Spikes about the data: D (the SQL), E (OFX), G (same-day pick) | `spikes/spike-d-sql/`, `spike-e-ofx/`, `spike-g-same-day-pick/` | pre (D), S2 (E, G) | **FINANCE** | |
| P-17 | The README | `README.md` | pre, then every slice | **ENTANGLED** | "Before you commit anything" and the status-per-slice pattern are general. The five rules and the runbook are this platform's |

---

## Half 2 — the starting code

### Layout, app model and hosts

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-1 | Solution and tool pins | `Portfolio.slnx`, `global.json`, `dotnet-tools.json`, `aspire.config.json`, `.gitignore`, `.gitattributes`, `LICENSE` | pre, S1 | **GENERAL** | `.gitattributes` names two generated files by path |
| C-2 | Editor settings | `.vscode/` (4 files) | maint (`e06c2ae`, after slice 01's assessment) | **GENERAL-OPTIONAL** | `settings.json` names generated files by path |
| C-3 | The app model: Postgres → one-shot Migrator → Api → web; the opt-in data volume; the Compose overrides (healthcheck, loopback port, restart, memory limits, no dashboard) | `src/AppHost/AppHost.cs`, `AppHost.csproj`, `appsettings*.json`, `Properties/` | S1 (step 1, step 7) | **ENTANGLED** | Mixed in, in one file: the setup-code parameter and the key-ring volume (S3), the two login passwords and two connection strings (S4), memory limits measured on a 10,000-row import (S2). To separate: the S3 and S4 blocks behind the options they belong to (C-10, C-14) |
| C-4 | The Migrator: wait for the server, migrate, exit non-zero on failure | `src/Migrator/Program.cs`, `Migrator.csproj` | S1 | **ENTANGLED** | Mixed in: `AddIdentitySchema` (S3) and the `DatabaseLogins.EnsureAsync` call with `ReportingLimits` (S4) |
| C-5 | Service defaults | `src/ServiceDefaults/Extensions.cs`, `ServiceDefaults.csproj` | pre (Spike B, `a845d93`) | **GENERAL** | By grep: no finance term |
| C-6 | The API host: the builder choice, the log filter, the pipeline order, the build-time OpenAPI document, the refusal to start without a connection string | `src/Api/Program.cs`, `Api.csproj`, `appsettings*.json`, `Properties/` | S1, S3, S4 | **ENTANGLED** | Mixed in: the report runner's registration, `MapPortfolioApi`, the document's description, `OwnerCommands` dispatch. To separate: the report runner block (C-15) and the auth registrations (C-10) |
| C-7 | The contract chain: a committed OpenAPI document, a generated TypeScript client, a byte-compare test, an advisory CI job | `src/Api/openapi/portfolio-api.json`, `web/src/api/schema.d.ts`, `web/src/api/client.ts`, `gen:api` in `web/package.json`, `tests/Contract/`, two rules in `.gitattributes` | S1 (steps 3, 5) | **ENTANGLED** | The mechanism is general. The two committed files are this API's content and must be regenerated, not copied |

### Finance

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-8 | The balance model | `src/Domain/` except `AuditEvent.cs`, `SetupCode.cs`, `UsedSetupCode.cs`, `SavedReport.cs`; `src/Infrastructure/BalanceRecorder.cs`, `BalanceWrites.cs`, `NetWorthDay.cs`; `Configurations/` except `AuditEventConfiguration.cs`, `UsedSetupCodeConfiguration.cs`, `SavedReportConfiguration.cs` | S1, S2 | **FINANCE** | `Conventions.cs` holds the money type and the live-balance filter |
| C-9 | File import | `src/Ingestion/`, `src/Infrastructure/ImportPipeline.cs`, `src/Domain/IngestRun.cs`, `src/Api/ImportEndpoints.cs` | S2 | **FINANCE** | |
| C-10a | The balance and account API | `src/Api/Endpoints.cs`, `Contracts.cs` | S1 | **FINANCE** | `Contracts.cs` also holds later slices' request and response types (not read) |
| C-11 | The migrations | `src/Infrastructure/Migrations/` (7 migrations and the snapshot) | S1 (3), S2 (1), S3 (1), S4 (2) | **FINANCE** | A new project starts a new history. Migration 5 (`AuthAndAudit`) holds the Identity and audit tables a new project would want from C-10 and C-13 |
| C-12 | The finance pages | `web/src/App.tsx`, `web/src/api/queries.ts`, `web/src/money.ts`, `web/src/components/Accounts.tsx`, `Imports.tsx`, `NetWorthChart.tsx`, `NetWorthTable.tsx` | S1, S2 | **FINANCE** | `App.tsx` composes every section, including the general ones. `recharts` in `package.json` belongs here |
| C-13a | The finance tests | Unit: all but `SetupCodeTests.cs`, `ProcessTimeZoneTests.cs`. Integration: `BalanceIdempotencyTests`, `ImportApiTests`, `ImportPipelineTests`, `ImportSupport`, `MigrationOnRowsTests`, `NetWorthViewTests`, `RestatementTests`, `SameDayPickTests`. E2E: `EnterABalanceTests`, `ImportAFileTests`. Architecture: `IngestionBoundaryTests` | S1, S2 | **FINANCE** | `MigrationOnRowsTests` is the pattern "run a migration over existing rows, down and up" |
| C-16a | The dates rule and its tests: UTC data, the viewer's zone, a CI matrix that proves its own zone | `tests/Integration/ClockTests.cs`, `tests/E2E/TimeZoneTests.cs`, `tests/Unit/ProcessTimeZoneTests.cs`, the "Time zone" job in `ci.yml` | S2 (step 5) | **FINANCE** | `ProcessTimeZoneTests.cs` and the CI job have no finance term (by grep). The other two test balances (10 and 11 hits) |

### The security tier (slice 03)

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-10 | Passkeys, sessions, enrolment by setup code, the host recovery commands | `src/Api/AuthEndpoints.cs`, `AuthServices.cs`, `OwnerCommands.cs`; `src/Domain/SetupCode.cs`, `UsedSetupCode.cs`; `web/src/api/passkey.ts`, `session.ts`; `web/src/components/Auth.tsx` | S3 | **GENERAL-OPTIONAL** | By grep: the only finance-term hits are the name `Portfolio` and the words "account bar". It assumes one owner: one enrolment per setup code (`README.md` "Signing in") |
| C-14 | The CSRF filter and the sign-in rate limiter | `src/Api/CsrfTokens.cs`, `SignInRateLimit.cs` | S3 (step 3) | **GENERAL-OPTIONAL** | Both need C-10's session. By grep |
| C-17 | Security headers with a strict CSP; the cross-site write refusal | `src/Api/SecurityHeaders.cs` (S3), `CrossSiteWrites.cs` (S2) | S2, S3 | **GENERAL** | By grep. The Permissions-Policy allows passkeys (`LOG.md`, 2026-10-24, step 3), which matters only with C-10 |
| C-13 | The audit log: append-only by trigger; a refusal line per layer | `src/Domain/AuditEvent.cs`, `Configurations/AuditEventConfiguration.cs`, `src/Api/Refusals.cs`; the trigger DDL inside migration `20261024160637_AuthAndAudit.cs` | S3 | **ENTANGLED** | The kinds are "the six authentication event kinds" (`AuditEvent.cs:32`). The trigger lives in a migration with the Identity tables. To separate: the `append_only()` function and trigger as their own migration, and the kinds as the project's own |
| C-18 | The database context | `src/Infrastructure/PortfolioDbContext.cs`, `DesignTimeDbContextFactory.cs` | S1, S3 | **ENTANGLED** | One context holds Identity's store and six of this platform's `DbSet`s (`PortfolioDbContext.cs:19-24`), and maps a finance enum. To separate: the `Configure` and `AddIdentitySchema` methods from the sets |

### The database tier (slice 04)

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-19 | Split database logins, provisioned on every Migrator run | `src/Infrastructure/DatabaseLogins.cs` | S4 (step 1a) | **ENTANGLED** | The routine is general and a new project would opt in. The grant matrix names this schema's tables (`DatabaseLogins.cs:36-47`), and the two logins are fixed, one of them for reporting. To separate: the matrix as data the project supplies |
| C-20 | The catalog snapshot: effective privileges against committed files, with plants | `tests/Integration/CatalogSnapshot.cs`, `catalog-snapshot.sql`, `ExpectedPrivileges/portfolio_api.txt`, `portfolio_reporting.txt`, `DatabaseLoginsTests.cs`, `MigratorProcessTests.cs`, `ApiLoginTests.cs` | S4 (1a, 1b, 6) | **ENTANGLED** | The snapshot query and the comparer are general (by grep: only the name). The two expected files are this schema's. `ApiLoginTests.cs` has 8 finance-term hits |
| C-15 | SQL reporting, bounded from outside the query | `src/Infrastructure/ReportRunner.cs`, `ReportCells.cs`, `ReportParameters.cs`, `ReportingLimits.cs`; `src/Api/ReportEndpoints.cs`, `SavedReportEndpoints.cs`; `src/Domain/SavedReport.cs`; migrations `Reporting`, `SavedReports`; `web/src/components/Reports.tsx`; `tests/Integration/ReportApiTests.cs`, `ReportingSchemaTests.cs`, `SavedReportTests.cs`; `tests/E2E/ReportsPageTests.cs` | S4 (steps 2–5) | **ENTANGLED** | The runner's six-part boundary does not depend on what the views hold (`slice-04.md` "Running a query"). The four views are finance (`slice-04.md` "The reporting schema"). To separate: the runner and its refusal tests from the view definitions. A new project would opt in |

### Web shell

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-21 | The Vite and React shell | `web/package.json`, `package-lock.json`, `vite.config.ts`, `eslint.config.js`, `tsconfig*.json`, `index.html`, `src/main.tsx`, `src/vite-env.d.ts`, `.dockerignore`, `.gitignore` | pre (Spike B), S1 | **GENERAL** | `vite.config.ts` requires the AppHost resource to be named `api` |
| C-22 | A render error costs one section, not the page | `web/src/components/SectionBoundary.tsx`, `tests/E2E/SectionFailureTests.cs` | S2 (`edb9504`) | **ENTANGLED** | The component is general (by grep: 1 hit). Its test forces the throw through the import history response (9 hits). `web/src/index.css` was not read |

### CI, hygiene and the hook

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-23 | CI | `.github/workflows/ci.yml`, `scripts/coverage_floor.py`, `.github/dependabot.yml` | S1 (step 6), later maint | **ENTANGLED** | The job shape and SHA-pinned actions are general. The coverage floors name `Domain 85` and `Ingestion 80`, measured here (`ci.yml:42-51`). The Dependabot holds are for this dependency set. To separate: the floors and the holds as the project's own |
| C-24 | The hygiene scanner | `scripts/hygiene.py` | pre (`2acda5f`) | **ENTANGLED** | General: the three modes, forbidden paths, the allow-list, the credential rules (private key, JWT, key prefixes, connection-string password). Finance: the account, routing, card and SSN shapes and the institution list (`hygiene.py:44-106`). To separate: the rule table into a general set and a project set |
| C-25 | The hook, its installer, the CI workflow and the hook's test | `.githooks/pre-commit`, `scripts/install-hooks.sh`, `.github/workflows/hygiene.yml`, `tests/Architecture/PreCommitHookTests.cs` | pre; the test S3 (step 6) | **GENERAL** | `PreCommitHookTests.cs` has 4 finance-term hits (by grep, not read): it may plant a finance-shaped value to see the hook refuse |

### Test fixtures

| Id | Piece | Paths | Built in | Category | Notes |
|---|---|---|---|---|---|
| C-26 | The Testcontainers fixture: one throwaway Postgres per class, migrated by the real migrations, no volume | `tests/Integration/PostgresFixture.cs`, `Integration.csproj` | S1 (step 5), S4 | **ENTANGLED** | Since S4 it runs `DatabaseLogins` and hands out three connection strings (`slice-04.md` "Provisioning"; `LOG.md` 2026-11-06, step 1b). To separate: the three-login part, with C-19 |
| C-27 | Auth test doubles and the secret canary | `tests/Integration/AuthFixture.cs`, `TestAuth.cs`, `SoftAuthenticator.cs`, `SecretCapture.cs`, `AuditShape.cs`; `tests/E2E/VirtualAuthenticator.cs`; `tests/Unit/SetupCodeTests.cs` | S3; `AuditShape` maint (`e9275e6`) | **GENERAL-OPTIONAL** | They go with C-10. By grep. `AuthFixture.cs` imports `ImportSupport` (line 10) |
| C-28 | The security tests | Integration: `AllowListTests`, `AuthApiTests`, `AuthSchemaTests`, `AuthSchemeGuardTests`, `CorsTests`, `CrossSiteWriteTests`, `CsrfFilterTests`, `HostFilteringTests`, `SecurityHeadersTests`, `SignInRateLimitTests`, `SecretsStayOutTests`, `OwnerCommandsTests`, `RefusalLogTests`. E2E: `AuthBrowserTests`, `SetUpBrowserTests`, `CrossSiteBrowserTests`. Architecture: `NoCredentialInResponsesTests` | S1 (`HostFilteringTests`), S2, S3 | **ENTANGLED** | Several use finance routes as their subject: `AllowListTests` (11 hits), `CrossSiteWriteTests` (12), `CrossSiteBrowserTests` (26), `SecretsStayOutTests` (calls `/api/v1/accounts`, line 105). To separate: a neutral sample route for the tests to act on |
| C-29 | The architecture tests | `tests/Architecture/AspireBoundaryTests.cs`, `TestBoundaryTests.cs` | S1, S3 | **ENTANGLED** | Both hold the project list as literals that include `Ingestion` (`AspireBoundaryTests.cs:23`, `TestBoundaryTests.cs:11`). The separation is those two lists |
| C-30 | The E2E harness: the whole app model on a throwaway Postgres, Playwright, axe | `tests/E2E/AppFixture.cs`, `Axe.cs`, `E2E.csproj` | S1 (step 5), S3 | **ENTANGLED** | `Axe.cs` is general (by grep: only the name). `AppFixture.cs` sets the login parameters (line 56) and, since S3, enrols once and keeps the passkey (`LOG.md` 2026-10-24, step 4). To separate: the enrolment, with C-10 |
| C-31 | The published-stack checks and the app-model test | `tests/E2E/PublishedStackTests.cs`, `AppModelTests.cs`; `deploy/.env.example` | S1 (`AppModelTests`), S3 (step 7b), S4 (step 6) | **ENTANGLED** | `PublishedStackTests.cs` drives the account and balance forms (lines 90–100). `AppModelTests.cs` asserts the two login strings (lines 70–77). To separate: the Compose-shape assertions from the page flow |

---

## Likely to go stale fast

### Pins, as of `b48b7ac`

| What | Where | Value |
|---|---|---|
| .NET | every `.csproj` (`net10.0`); `ci.yml` (`10.0.x`) | 10 |
| Aspire SDK | `src/AppHost/AppHost.csproj` | `Aspire.AppHost.Sdk/13.6.4`, bumped by hand: Dependabot does not bump it |
| Aspire packages | `AppHost.csproj`, `Infrastructure.csproj`, `E2E.csproj` | 13.6.5 |
| EF Core, Identity, OpenAPI | `src/*/*.csproj` | 10.0.15 |
| `dotnet-ef` tool | `dotnet-tools.json` | 10.0.13, `rollForward: false` |
| Npgsql | `Infrastructure.csproj` | 10.0.3 |
| PostgreSQL image in tests | `tests/Integration/PostgresFixture.cs:16` | `postgres:18.3` |
| xunit, Playwright, axe, Testcontainers | `tests/*/*.csproj` | 4.0.3, 1.64.1, 4.14.0, 4.16.0 |
| Node | `ci.yml` | 24 |
| React, Vite, TypeScript | `web/package.json` | ^19.3.1, ^8.3.7, ~5.9.3 |
| `openapi-typescript`, `openapi-fetch` | `web/package.json` | 7.13.0, 0.17.0 (exact) |
| GitHub Actions | `ci.yml`, `hygiene.yml` | pinned by commit SHA |
| spec-kit | `specs/README.md` | 0.14.3.dev0 |

### Already inconsistent at `b48b7ac`

Facts found while reading. None is fixed by this PR.

1. **The Aspire SDK and packages differ.** The SDK is 13.6.4 and the packages are 13.6.5.
   The comment in `AppHost.csproj` says to keep them on the same version.
2. **`dotnet-ef` is behind EF Core.** The tool is 10.0.13 and the packages are 10.0.15.
   Commit `6b5c32d` bumped the tool "to match EF Core".
3. **A Dependabot hold outlived its pin.** `dependabot.yml` ignores MessagePack majors
   because "MessagePack is pinned". Commit `13658f4` removed the pin from the project
   files.
4. **`deploy/.env.example` lists five keys.** It lacks `API_DB_PASSWORD` and
   `REPORTING_DB_PASSWORD`, which `README.md` "Database logins" names.
5. **The test image's comment stops at 13.6.4.** `PostgresFixture.cs:14` says Aspire
   13.5.4, 13.6.1 and 13.6.4 publish `postgres:18.3`. It is not checked for 13.6.5.
6. **`specs/README.md` "Layout" and "Status" name slice 01 as the only live unit.** Four
   slices are built.

### Workarounds for specific framework behaviour

Each is a comment in the file named. Each is a claim about a version, and none is
re-checked here.

| Workaround | Where | About |
|---|---|---|
| A healthcheck written by hand, because the Compose publisher writes none and degrades `WaitFor` to `service_started` | `AppHost.cs` (postgres, migrator) | Aspire's Docker publisher |
| Ports rewritten to `127.0.0.1:…`, because the publisher writes no host IP | `AppHost.cs` (api) | Aspire's Docker publisher |
| `WithDashboard(false)` | `AppHost.cs` | Aspire's Docker publisher |
| `DisableForwardedHeaders()` | `AppHost.cs` | Aspire's publish default |
| The key ring mounted at `/home/app`, as a mount annotation | `AppHost.cs` | The image's `app` user (UID 1654); the publisher's volume declaration |
| A connection string built as a `ReferenceExpression`, not `WithReference(db)` | `AppHost.cs` | `PostgresServerResource.ConnectionString`, cited at v13.6.4 |
| `persist: true` on generated parameters; the setup code's 29-character alphabet | `AppHost.cs` | Aspire's `PasswordGenerator`, cited at v13.6.1 |
| The data volume opt-in, because the testing builder randomises ports only | `AppHost.cs` | `DistributedApplicationTestingBuilder` |
| `ASPIRE010` suppressed; DCP and the dashboard from NuGet | `AppHost.csproj` | Revisit per microsoft/aspire#16386 |
| A note to check the next bump for microsoft/aspire PR #20542 | `AppHost.csproj` | Debug-console noise |
| `3D000` treated as "server up", because nothing creates the database in Compose | `Migrator/Program.cs` | Aspire's `AddDatabase`; the Postgres image |
| An explicit `Microsoft.EntityFrameworkCore.Relational` reference, against CS1705 | `Infrastructure.csproj` | EF, Npgsql and Aspire version resolution |
| Identity's schema `Version3` registered in every host | `Migrator/Program.cs`; `LOG.md` 2026-10-24, step 1 | ASP.NET Core Identity |
| `WebApplication.CreateBuilder`, for host filtering | `Api/Program.cs` | The slim and empty builders |
| Request log lines off below Warning | `Api/Program.cs` | `Hosting.Diagnostics` logs the query string |
| The `GetDocument.Insider` exception | `Api/Program.cs` | The build-time OpenAPI tool runs the entry point |
| Authentication and authorization called explicitly, in a stated order | `Api/Program.cs` | `WebApplication`'s automatic middleware |
| `cors: false` | `web/vite.config.ts` | Vite's dev-server CORS default |
| The proxy target read from `API_HTTPS` / `API_HTTP` | `web/vite.config.ts` | Aspire's variable naming, tied to the resource name `api` |
| xunit v3 on Microsoft.Testing.Platform; test projects as executables | `global.json`, `tests/*/*.csproj` | The test platform |
| TypeScript held at major 5; `@types/node` at 24; React and React DOM together | `dependabot.yml` | `openapi-typescript`; Node; React's peer check |
| `eol=lf` on generated files, shell scripts and the hook | `.gitattributes` | `core.autocrlf` on Windows |
| The hook picks its Python by running it | `.githooks/pre-commit` | The Microsoft Store `python3` stub |
| UTF-8 decoding stated explicitly | `scripts/hygiene.py:164-173` | cp1252 on Windows |
| E2E waits until Vite answers, not until it is healthy | `tests/E2E/AppFixture.cs:210` | Aspire reports a resource with no health check as healthy |
