# Round 2 committee findings

**Date:** 2026-09-18 · **Seats:** 9 · **Raw findings:** ~157 · **Consolidated:** 46

Round 1 reviewed the specification set with fourteen seats and produced 89
findings, dispositioned 61 FIX / 14 DEFER / 13 CUT / 1 ACCEPT. Round 2 was
commissioned after `D-030` cut the work to one vertical slice
(`001-portfolio-platform/slice-01.md`) and after `D-029` established the
claim-strength markers.

---

## Method

Nine seats, chosen for what bites on a 50-80 hour solo slice: software
engineer, test automation, security, spec-driven development, red team, scope
realist, .NET/Aspire source-reader, accessibility, and a **builder** seat new
this round whose only charge was hours and evenings.

Three deliberate choices shaped what came back.

**No seat could read `committee-findings.md`.** Fresh eyes were the explicit
instruction, so a finding rediscovered independently is evidence that it is
real and reachable, not wasted effort. Five findings were reached by three or
more seats with no contact between them; those are recorded with their
confirmation count and are not treated as matters of judgement.

**Every external-system claim had to carry a strength marker**, and unmarked
ones were discarded at the broker. Seats were told plainly that in round 1
thirteen of fourteen reviewers missed the real defects by reasoning from
internally-consistent documents, and were instructed to look things up. They
did: this round contains a PostgreSQL 16.13 instance that was actually stood up
and loaded, a clone of `microsoft/aspire` at a pinned commit re-checked against
`release/13.5`, npm tarballs of two charting libraries read as source, and the
axe-core package downloaded and grepped.

**The two axes were kept separate.** Defects carry severity; buildability and
teaching-value judgements do not, and are collected in their own section. A
"this is too ambitious" never got averaged with a "this is wrong".

---

## The three headlines

### 1. Built as written, the slice does not run - and the defects are in SQL

The software-engineer seat stood up PostgreSQL 16.13 and ran the slice's
schema, its prescribed write order, its view sketch and an NFR-2-scale load.
Three failures inside twenty minutes, all `EXECUTED`:

- the restatement write order aborts on a foreign-key violation;
- the net-worth view sketch returns one row per account, not a daily series,
  and the naive completion blew a 90-second statement timeout against a
  2-second budget (the `LATERAL` form: 283 ms);
- `sum()` ignores NULLs, so an in-window account with no observation is
  silently zero - measured at `150.0000` on a day when 2 of 4 accounts had no
  verified value.

The meta-finding matters more than any of the three. `slice-01.md`'s schema
section carries nine load-bearing SQL claims, two `DOCS@` markers and not one
`EXECUTED`. **The three spikes all point at .NET Aspire; every defect above is
in PostgreSQL** - the one part of this stack a `docker run postgres` and a
heredoc would have spiked in ten minutes, at any point while the document was
being written. The method is right and was applied to the wrong half of the
stack.

### 2. Two fixes round 1 prescribed were themselves wrong

The .NET/Aspire seat cloned `microsoft/aspire` at `b477bdd` and re-checked
every load-bearing claim on the shipped `release/13.5` branch.

- **`WithComposeServiceCustomization(...)` does not exist.** Repo-wide grep at
  the pinned commit: zero hits; absent from the public API surface on the
  release branch too. The real API is `PublishAsDockerComposeService<T>(this
  IResourceBuilder<T>, Action<DockerComposeServiceResource, Service>)`.
- **The `ASPIRECOMPUTE002` `NoWarn` is unnecessary.** The attribute claim is
  true; the conclusion drawn from it is false. The only member in
  `Aspire.Hosting.Docker` carrying it is
  `DockerComposeEnvironmentResource.GetHostAddressExpression`, which an AppHost
  never calls, and `[Experimental]` diagnoses at the call site of the marked
  member. The diagnostic this slice does hit is `ASPIREJAVASCRIPT001`.

Both had been landed into four documents carrying a `SOURCE@` marker they had
not earned. **A review finding is an external-system claim too.** `D-029`
applies to it, and did not reach it. Corrected in `faf168f`.

### 3. The estimate is out by roughly a factor of two

The builder seat re-costed every step for someone competent with C# but new to
Aspire, working in 2-3 hour evening blocks. **100-160 hours, not 50-80** - at
three evenings a week, 13-21 weeks. The error is never in the happy path: it is
the toolchain nobody budgeted, `IDesignTimeDbContextFactory` and `HasFilter`
drift in step 2, four pieces of yak-shaving compressed into one sentence in
step 3, and above all step 6, where the work is not YAML but the edit-push-wait
loop. At 5-8 minutes a run, a two-hour evening buys about twelve attempts.

Separately and independently, three seats found the document's own arithmetic
wrong: the seven steps sum to 48-80 h and step 0's spike day is not in the
total, so its own numbers give **56-88 h** against a 50-80 h headline.

---

## Blocking - the slice cannot be built or demonstrated as written

| ID | Finding | Seats | Evidence |
|---|---|---|---|
| **R2-B1** | **Nothing creates an account.** Four endpoints, none of them `POST /accounts`; no seeder in any step; `:33` "Generated data only" contradicts `:18` "a user types a balance"; DoD 2 and the E2E both require an account to exist. A `psql INSERT` workaround breaks Constitution XI. **Fix:** add `POST /api/v1/accounts` and strike "generated data only", which is a leftover from the pre-`D-030` scope. | **4** (scope, builder, SDD, red team) | Document-internal |
| **R2-B2** | **Constitution III was never amended.** `:183` says "Constitution III is amended to permit exactly this"; the constitution carries Amendments 1 and 2 only, neither touching III, which still reads "never carries the last balance forward". `data-model.md:272` and `traceability.md:64` still ban gap-fill outright. The slice's only visible output is specified as a violation, and the constitution's own clause says a principle weakened this way "has not been amended; it has been broken". **Fix:** write Amendment 3 before step 4 - fill *within* an active window permitted, fill *past* a series end still forbidden. | **5** (scope, test automation, SDD, software eng, red team) | Document-internal |
| **R2-B3** | **The restatement write order does not run.** `UPDATE old SET superseded_by = <new id>` before the `INSERT` points a FK at a row that does not exist: `violates foreign key constraint … is not present in table "account_balance"`. **Fix (preferred):** invert the pointer - `superseded_at timestamptz` as the discriminator plus a backward `supersedes` FK on the new row. No forward reference, no deferral, partial index still holds. Alternatives: `DEFERRABLE INITIALLY DEFERRED` FK, or no FK. | **4** (software eng `EXECUTED`, test automation, red team, .NET) | `EXECUTED 2026-09-18` PG 16.13 |
| **R2-B4** | **The document's stated dichotomy is false.** It reasons correctly that a partial unique *index* cannot be `DEFERRABLE`, then concludes the three-statement order is forced. `EXCLUDE USING gist (…) WHERE (…) DEFERRABLE INITIALLY DEFERRED` gives partial **and** deferrable uniqueness (needs `btree_gist`). Take it or state why not. | 1 | `EXECUTED 2026-09-18` |
| **R2-B5** | **The net-worth view sketch is wrong, and the obvious completion is 300x over budget.** `DISTINCT ON (account_id) … ORDER BY as_of_date DESC` is rejected outright by PostgreSQL (the `DISTINCT ON` expressions must match the leftmost `ORDER BY`), and semantically yields one row *per account*, not a daily series. Completing it with a `LEFT JOIN … ON b.as_of_date <= c.d` is a triangular join: at the spec's own scale (25 accounts x 10 years) it exceeded a 90-second timeout. The `LATERAL … LIMIT 1` form ran it in **283 ms** (independently re-measured at 235 ms). **The `DISTINCT ON` half is Blocking; the performance half is not** (`ADV-2`): the 90-second blowout reproduces **only at a daily cadence** (91,250 rows), and slice-01 is hand-entered. At a monthly cadence (3,000 rows) the naive form is 2.59 s against a 2-second budget — 30% over, not 4,500%. The prescription survives on simplicity and an ~11x margin; the "300x" headline does not. **Use `LEFT JOIN LATERAL … ON true`, never `CROSS JOIN`** — see `ADV-1`. | **2** (software eng `EXECUTED`, .NET `SOURCE@`) | `EXECUTED` (two seats, independently); `SOURCE@postgres@REL_18_STABLE select.sgml:1224` |
| **R2-B6** | **Constitution II is violated inside the headline number, invisibly.** An in-window account with no observation contributes `NULL`, and `sum()` ignores NULLs, so a missing account is a silent zero. The view carries `accounts_carried` and `max_staleness_days` but no unverified count, and `api.md:136`'s `coverage.accounts_missing_data` is not in it. The chart looks right. **Fix:** `count(*) - count(balance) AS accounts_unverified`, surfaced, and refuse or badge a point where it is non-zero — **over a `LEFT JOIN LATERAL … ON true`, which `ADV-1` establishes is load-bearing**: composed with `CROSS JOIN LATERAL` this expression is identically zero and reproduces the defect it exists to catch. | 1, measured | `EXECUTED 2026-09-18`: `150.0000` with 2 of 4 unverified |
| **R2-B7** | **The most recent day's net worth is undefined and both readings are wrong.** "Carry-forward within each account's active window" never defines the window's end for an *open* account. If it ends at the last observation, an account that did not report today drops out of today's total - measured 25 instead of 135, which is the `M-1` defect reappearing at the right-hand edge. If it ends at "today", a silently-stopped account is forward-filled forever, which is Constitution III's forbidden case. **Fix:** name the rule and a staleness cutoff. | 1, measured | `EXECUTED 2026-09-18` |
| **R2-B8** | **No endpoint carries the net-worth series.** The four endpoints are accounts, per-account balances, POST balance, health. So the chart must either be summed in React - putting carry-forward, window and conditional-money logic in the client, breaking Constitution XI and FR-5.5 and making `v_net_worth_daily` dead code - or needs a fifth endpoint nobody scoped. The tabular equivalent DoD 7 requires has no transport for `accounts_carried` / `max_staleness_days` either. | **2** (accessibility, software eng) | Document-internal |
| **R2-B9** | **The spikes cannot run before step 1.** Spike A needs an AppHost; Spike B needs an AppHost + Vite app + a published compose stack, which *is* step 7's machinery; Spike C needs an AppHost + Postgres + a Migrator. This contradicts `process.md:84-86` ("the step whose only purpose is to stand the platform up goes **first**, and the load-bearing mechanisms are spiked inside it") and Constitution XIII. **Fix:** delete step 0 as a separate step; make the three claims the acceptance observations of step 1, keeping the gate language on each dependent step. | 1 | Document-internal, against the tree's own method doc |

---

## Spikes: two of three are already answered

The .NET seat resolved from source what the spikes were commissioned to
discover. This is worth roughly a day back.

| Spike | Status | What is actually true |
|---|---|---|
| **A** - two stacks at once, environment selection | **Answered; the claim is false** | `aspire run` calls `FindAndStopRunningInstanceAsync` unconditionally, keyed on the AppHost **file path alone** - the launch profile is not part of the key, and `--isolated` only adds a warning. Aspire's own resource string: *"To run multiple isolated instances simultaneously, run from different directories such as git worktree directories."* Launch profiles **do** work for selection (`environmentVariables`, `applicationUrl` → `ASPNETCORE_URLS`), so the parameterisation survives; simultaneity needs two directories. `SOURCE@microsoft/aspire@b477bdd RunCommand.cs:334-346, AppHostSocketManager.cs:65-74, RunCommandStrings.resx:261-263` |
| **B** - how the browser learns the API's address | **Answered by a shipped template** | **It never does.** The `aspire-ts-cs-starter` template injects `SERVER_HTTP`/`SERVER_HTTPS` into the Vite *process*, `vite.config.ts` proxies `/api`, the component calls a **relative** path, and publish folds static assets into the API container via the non-experimental `PublishWithContainerFiles`. Service discovery feeds the proxy, not the client. `plan.md:95` is wrong in both modes. Also: under `aspire run` the dev-server proxy is a different mechanism from the publish-time YARP site, and only the latter was named. `SOURCE@microsoft/aspire@b477bdd` template files |
| **C** - how the API waits for migrations | **Second half answered; keep the run-mode half** | `WaitForCompletion` **does** emit `depends_on: {condition: service_completed_successfully}` and genuinely requires termination, so the one-shot Migrator is right and `plan.md:189`'s long-running worker is not. But `WaitFor` *health* does **not** survive publish (below), and because `WaitForCompletion` does, Spike C passing will mask that. |
| **D** - *proposed* | **Missing, and the cheapest of the four** | The schema, the restatement and the view against a throwaway Postgres, before step 2. Would have caught B3, B4, B5, B6 and B7. |

---

## Must fix, at the step named

| ID | Finding | Seats | Status |
|---|---|---|---|
| **R2-M1** | **`aspire publish` binds the API on every interface.** `AddPorts` writes an external endpoint as a bare `host:container` string with no host IP (and with no explicit port, as a bare container port, so the host port is random). The .NET base images also set `ASPNETCORE_HTTP_PORTS=8080`, so Kestrel binds `0.0.0.0` *inside* the container - loopback is purely a property of the publish string. DoD 3 requires loopback and the step-7 override list fixes three other things and not that one. Combined with the slice having no authentication, `docker compose up` publishes an unauthenticated finance API to the LAN. | **3** (security, red team, .NET) | **Corrected in tree** `faf168f` |
| **R2-M2** | **`WaitFor` health ordering silently disappears on publish.** The publisher's `service_healthy` branch is commented out with a REVIEW note; everything but `WaitForCompletion` falls through to `service_started`. On a cold volume the Migrator starts while Postgres is still running initdb. The `pg_isready` override is load-bearing, not polish - and DoD 3 should run `docker compose down -v && up`, not a warm stack. | **2** (security, .NET) | **Corrected in tree** |
| **R2-M3** | **Publish emits an Aspire dashboard service no document lists.** `AddDockerComposeEnvironment` constructs it unconditionally, `DashboardEnabled` defaults true, its endpoint is `IsExternal` so it lands in `ports:`, with `restart: always`. A telemetry UI carrying SQL and financial URLs, bound on all interfaces, absent from `deployment.md`'s topology table *and* its verification checklist. **Fix:** `.WithDashboard(false)`. | 1 | **Corrected in tree** |
| **R2-M4** | **The generated `.env` is an accumulator, not a regenerated file.** The publisher writes it keys-only, but `EnvFile.Load` reads any `.env` already at the output path and `SaveKeysOnly` preserves every non-empty value it found. So once the operator types the Postgres password in to make compose work, every later `aspire publish` rewrites it with that password intact. `deployment.md:70` describes this backwards. **Fix:** git-ignore the publish output **directory**, not the file. | 1 | `SOURCE@microsoft/aspire@main DockerComposePublishingContext.cs:151-163, EnvFile.cs:127-157` |
| **R2-M5** | **Constitution IX has no mechanical enforcement in the only work that will happen.** `slice-01.md` mentions neither `.gitignore` nor the data-hygiene job nor secret scanning; step 6 is "build → unit → integration → contract → E2E"; DoD 8 asserts "no personal data anywhere in the repo" with nothing behind it. This contradicts `plan.md:270` ("from the first commit") and `cicd.md:91` ("before the first real connector"). The repo is public from commit one, and git history is the one thing that cannot be retrofitted. | **3** (scope, test automation, security) | Open |
| **R2-M6** | **Push protection does not cover what Constitution IX is about**, and detection after a push is not recovery. Push protection matches provider *secret* patterns - not balances, not institution names. GitHub states rewritten commits stay reachable by SHA, in clones and forks, and through any PR referencing them. So push → CI red → force-push leaves the balance permanently retrievable. **Fix:** run the same pattern set as a `pre-commit`/`pre-push` hook, the only point at which it is prevention. | 1 | `DOCS@2026-09-18` github/docs |
| **R2-M7** | **`numeric(19,4)` will not exist unless explicitly configured.** Npgsql's EF Core provider maps CLR `decimal` to the *unconstrained* store type `"numeric"`, so a code-first migration emits `numeric` with no precision or scale - and `S-24`'s midpoint test then passes without exercising the disagreement it exists to catch. **Fix:** `HasPrecision(19,4)`, and assert the column type in the integration test. | 1 | `SOURCE@npgsql/efcore.pg@main NpgsqlDecimalTypeMapping.cs:29-43` |
| **R2-M8** | **`numeric(19,4)` silently rounds a user's input.** A hand-entered `1.00005` stores as `1.0001` - PostgreSQL rounds on scale overflow rather than erroring. The API returns a different number than was submitted, at `source_strength = manual`. A Constitution I/IV problem dressed as a storage detail. **Fix:** validate scale <= 4 at the DTO, return 422. | 1 | `EXECUTED 2026-09-18` |
| **R2-M9** | **`M-11`'s fix is the identical defect `S-17` caught two paragraphs earlier.** "One `CHECK` that every account shares the reporting currency" is a cross-row comparison: `ERROR: cannot use subquery in check constraint`. The only writable `CHECK` is `CHECK (currency = 'USD')`, a literal in the schema. Also unaddressed: `account.currency` and `account_balance.currency` are two sources of truth with nothing tying them. | 1 | `EXECUTED 2026-09-18` |
| **R2-M10** | **The step-2 index references two columns step 2 never declares** (`deleted_at`, `superseded_by`). The migration cannot create the index as written. | 1 | Document-internal |
| **R2-M11** | **The reporting role is not "SELECT on `reporting` and nothing else anywhere".** A fresh PostgreSQL role holds, by default, `CONNECT` **and `TEMPORARY`** on databases and `EXECUTE` on every function, granted to `PUBLIC`. Concrete: `CREATE TEMP TABLE t AS SELECT * FROM reporting.v_transaction` is a write on the read-only connection, which the privilege test never attempts; in a loop it fills the volume holding PRD history, and temp *tables* are explicitly exempt from `temp_file_limit`. No document names a single `REVOKE`. | **2** (security, red team - both source-pinned) | `SOURCE@postgres acldefault(), ddl.sgml:2162-2178` |
| **R2-M12** | **FR-6.3's statement timeout is set by the attacker.** `statement_timeout` is `PGC_USERSET`, so the user's own SQL may `SET statement_timeout = 0`; PostgreSQL states `GRANT SET ON PARAMETER` is "meaningless except for parameters that would normally require superuser privilege to set"; and Npgsql splits `CommandText` on semicolons, so `SET statement_timeout = 0; SELECT …` is one command. **Fix:** enforce client-side with `NpgsqlCommand.CommandTimeout` and a `CancellationToken`; keep the server-side `ALTER ROLE` as defence in depth; and make the timeout test include the disarm attempt. | **2** (security, red team) | `SOURCE@postgres guc_tables.c:2612`, `SOURCE@npgsql SqlQueryParser.cs:17-24` |
| **R2-M13** | **The row cap has no database-level mechanism at all.** PostgreSQL has no row-limit GUC. A row cap over user-authored SQL is an application-side `LIMIT` wrapper - the exact string manipulation `D5` rejects as unsound, presented under the argument "`GRANT` cannot be fooled". And it bounds the answer, not the work: `SELECT array_agg(i) FROM generate_series(1,1000000000) i` returns one row and OOM-kills the database. **Fix:** demote it to "best-effort application wrapper" and add `work_mem` / `temp_file_limit` on the reporting role, which the database does enforce. | **2** (.NET, red team) | `SOURCE@postgres config.sgml` |
| **R2-M14** | **"Three guards, each independently sufficient" is false twice.** Guards 1 and 2 both read the *same injected environment value* - one mis-set variable defeats both, so they are one guard with two spellings. Guard 3 rests on revoking `DROP`, which PostgreSQL does not have: "The right to modify or destroy an object is inherent in being the object's owner, and cannot be granted or revoked in itself." **Fix:** rewrite guard 3 as separate clusters, separate credentials, no network route; and say honestly that 1 and 2 are one guard. Constitution X is the principle the document itself calls least recoverable. | 1 | `SOURCE@postgres ddl.sgml:1850-1855` |
| **R2-M15** | **The OAuth callback has no `state` parameter.** PKCE binds the code to the *client*; `state` binds the callback to the *user's session*, and it appears nowhere in the tree. Attack: Kyle's browser hits his own callback carrying an attacker's code; his instance exchanges it and binds the **attacker's** account, after which attacker-authored merchant and description text flows into his database, dashboards and exports. **Fix:** opaque session-bound `state`, exact redirect-URI matching, single-use short-TTL requests, CSRF on the callback. | 1 | Document-internal + `grep` |
| **R2-M16** | **user-secrets does not satisfy FR-3.1.** Constitution VII is "secrets never rest in plaintext" and FR-3.1 is "credentials MUST be encrypted at rest"; `security.md:49` then names user-secrets as an `ISecretStore` implementation, and Microsoft's own docs say Secret Manager "doesn't encrypt the stored secrets". One of three implementations of the interface that satisfies FR-3.1 does not. **Fix:** amend Constitution VII by procedure, or state the inner loop holds sandbox credentials only and assert it at startup. | 1 | `DOCS@2026-09-18` app-secrets.md |
| **R2-M17** | **FR-4.3 is unsatisfiable by the only thing being built.** It requires every externally-sourced row to carry an originating sync run, with no exemption. Slice-01 is entirely hand-entered; a manual row has no sync run and never will. **Fix:** one clause - "for hand-entered rows, the actor and entry time in place of a run id". | 1 | Document-internal |
| **R2-M18** | **Traceability's Constitution coverage table stops at XI.** Amendment 1 added XII and XIII the same day the file was retargeted. Two further rows are now false: Principle I does not mention Amendment 2's working-assumption state, and Principle III names a `closed_on` CHECK constraint that `S-17` establishes cannot be built. | 1 | Document-internal |

---

## Should fix

Condensed; full text in the seat reports.

**Tests.** The slice's unit test is the trivially-passing one - its midpoint
assertion needs a database, so at the unit layer it tests `System.Decimal`
(TA-2). **Nothing in the four tests touches `v_net_worth_daily`**, the only
novel logic the slice builds (TA-3). The provenance-forgery guard `S-27` has no
test at any layer, though the exactly-analogous credential test already exists
(TA-13). DoD 7's accessibility claim has no verification (A11Y-12). The
OpenAPI drift gate is **not deterministic**: the framework sorts tags but not
paths; generation launches the app's entrypoint so the document differs by
`ASPNETCORE_ENVIRONMENT` (the `/dev/*` routes); schema reference IDs changed
shape between .NET 9 and 10 (TA-4). "The generated client compiles against the
document" is tautological - the test that catches something is `tsc --noEmit`
over the frontend's own call sites (TA-5). No coverage collector, fixture-
sharing strategy, parallelism policy or quarantine lane is named anywhere
(TA-8, TA-9). Playwright for .NET does not capture traces on failure by
configuration - xUnit gives teardown no access to the outcome (TA-11).

**Order and time.** Step 6 (CI) depends on step 7 (deploy): the E2E job needs a
running stack, which is either `aspire run` on a headless Linux runner or the
compose artifact step 7 produces (BLD-10). The architecture test is one clause
in step 1 but is the first test infrastructure in the repository (BLD-12). No
step installs anything - .NET 10 SDK, Aspire CLI, container runtime, Node,
dev-cert trust, and `pwsh` for Playwright's browser install, which this machine
does not have per `C-283` (BLD-3). The client generator is unnamed, though the
choice dictates how step 4's code is written (BLD-6). `AddViteApp` resources
were reported build-only and unpublishable in the vendor tracker, and `aspire
publish` with a Vite app threw `KeyNotFoundException` on 13.2 - a fourth
load-bearing claim (BLD-5); note the .NET seat's template reading suggests the
supported path is `PublishWithContainerFiles`, so **verify which applies to the
installed version before step 7**.

**Accessibility.** The floor lives only in `slice-01.md` with no FR behind it
(A11Y-1). "Visibly carried" is a purely visual guarantee for a distinction
Constitution I and IV exist to preserve - it needs shape (not colour), the
fields in the tooltip payload, and columns in the table (A11Y-3). The form is
missing from the floor entirely, including a `role="alert"` region for the 422
refusal, which is Constitution II's only wire-level appearance (A11Y-7). The
environment banner is a safety control conveyed by colour (A11Y-5). "A tabular
equivalent" of a 3,650-row daily series is a wall, not an equivalent - it needs
a caption, a summary, and one column per visual encoding (A11Y-13).

**Data and API.** `data-model.md` contradicts the live slice in three places,
and its unique index would reject the supersession its own next paragraph
describes (SR-9, SE-3, RT-17d). `POST /balances` has no idempotency and no way
to say "this is a correction" (SE-11). The float ban stops at the JSON boundary
- money is a decimal string and every chart library takes `number` (SE-12).
The drift check catches drift, not breakage: a PR that deletes a response field
and regenerates the document passes (SE-21). Pagination is specified on
principle and omitted where it matters (SE-23). No isolation level, no
database-error-to-HTTP mapping, no cancellation, no app-connection statement
timeout, no 503-vs-500 rule (SE-24).

**Process and spec hygiene.** Three documents discharge the claim-strength
obligation with a **blanket preface**, the one form `process.md` and
Constitution XIII explicitly forbid (SDD-3). The three falsified `C-374`
mechanisms are still asserted unmarked at the paragraphs a builder opens
(SDD-2, SR-8). `README.md` does not know `slice-01.md` exists (SDD-13).
**Spec-kit compatibility is overstated**: the flags check out, the shapes do not
- spec-kit numbers `FR-001`, requires prioritised P1/P2/P3 stories, an `SC-###`
Success Criteria section and an Assumptions section, and its constitution
template wants a Governance section and a version footer (SDD-12). Three
behaviours the slice builds have no FR at all: series continuation,
supersession, accessibility (SDD-7). Authentication exists only in design
documents, behind a bare pointer that cannot be marked satisfied (SDD-8). A
dangling `[NEEDS CLARIFICATION]` contradicts `D-030` (SDD-9, SR-10).
"Configure .NET's rounding explicitly" names no mechanism because there is none
- `MidpointRounding` is per-call-site (SDD-10, SE-14).

**Solo-repo reality.** "Required review before merge" cannot work on a
one-person repository - a PR author cannot approve their own PR, so the setting
either blocks every merge or is bypassed every time, training the bypass habit
(SR-11, BLD-8, NET-13). Compose scopes containers, networks and volumes by
**project name**, defaulting to the directory name, so one directory is one
project and "two stacks simultaneously" fails on a namespace nobody set; and
`portfolio_dev` contains `portfolio`, making the DEV assertion a substring test
waiting to be written backwards (RT-16).

**Reference-document security.** Reporting views are not `security_barrier`
(SEC-12, NET-12). Webhook verification is asserted but never specified - raw
body, timestamp window, replay cache, constant-time compare - and the endpoint
contradicts Constitution VIII's loopback default (RT-11). Cursor poisoning is
undetectable by construction (RT-12). CSV export is where provider-authored
text leaves the escaping boundary: `=HYPERLINK(...)` executes on open (RT-10).
The query string is logged verbatim by ASP.NET Core and an OAuth code has no
distinguishing shape, so the redaction filter and its shape-based test both
pass while the credential sits in the log (SEC-16). Revocation is never
communicated to the provider (SEC-17). Backups are a plaintext `pg_dump` with
one sentence of protection, and the obvious default path is inside OneDrive,
iCloud Drive or Time Machine (RT-14). The envelope-encryption argument defends
volume exfiltration, not the stolen laptop it names as primary - a Compose
`file:` secret is a plaintext file on the same disk (SEC-15, RT-13). The
secret-store scheme has **no table** for its data keys, so KEK rotation cannot
record which KEK wrapped which key (SEC-15).

---

## Conflicts resolved

**`AddViteApp` publishability.** The builder seat found vendor issues stating
`AddViteApp` resources are hardcoded build-only containers that
`.PublishAsDockerFile()` cannot override, and a `KeyNotFoundException` from
`aspire publish` on 13.2. The SDD and .NET seats independently confirmed
`PublishAsStaticWebsite(apiPath:, apiTarget:)` exists and does what Spike B
proposes. **Both hold:** they describe different mechanisms, and the .NET seat
found a third - the shipped starter template uses the non-experimental
`PublishWithContainerFiles`. Resolution: this is a real gate on step 7 and the
supported path must be confirmed against the *installed* version before that
step, not assumed from any of the three.

**Digest pinning.** Round 1 said the publisher writes "tag-based image names
with no digest resolution". The security seat found `WithImageSHA256(string)`
in the app model, mutually exclusive with `Tag`. **Round 1 was too broad:**
base images can be digest-pinned at source; only self-built project images need
the release-pipeline rewrite. Corrected in tree.

**The write-order fix.** Test automation and the .NET seat both prescribed
`DEFERRABLE INITIALLY DEFERRED`. The software-engineer seat, having run it,
prefers inverting the pointer - no deferral, no forward reference, plain
immediate FK. **Take the inversion**; it is strictly simpler and does not
depend on EF Core emitting raw migration SQL.

**Is slice-01 over- or under-scoped?** The scope realist says it is correctly
scoped and 30 hours is unreachable without collapsing back into the horizontal
stub `D-030` escaped. The builder says it is a 100-160 hour job. **These agree:**
the scope is right and the *estimate* is wrong. Both name the same cut if one
is needed (E2E) and both want CI earlier.

---

## Axis 2 - buildability and teaching value

Kept separate from severity by design. Where the seats converge:

**The vertical slice was the right call and nobody argued otherwise.** The
spikes are repeatedly named as the best-designed thing in the tree - one
mechanism each, a stated fallback each, a named gate each. The complaint is not
that they exist but that they point at Aspire while the defects are in SQL, and
that two of the three were answerable from source without spending a day.

**CI last is backwards, and the document contains the argument against itself.**
`cicd.md` says the hygiene job is written "before the first real connector, not
after. A control added once it is needed is added after the first mistake." The
same logic applies to the whole workflow. A `dotnet build` job in commit two
costs twenty minutes and turns step 6 from a fourteen-hour wall into seven
twenty-minute increments, each small enough to finish in one sitting.

**The named quit risk** is the Sunday the chart first moves - because everything
after it is ten to eighteen hours of edit-push-wait, reproducing something that
already works locally, for an audience of one, immediately before a written hard
stop.

**Where the slice teaches the trivial version.** One migration, when the
instructive one is the *second* against a database with rows - and the
`account_source` split is sitting right there to be delivered as migration 2.
TanStack Query named without its reason (the POST → invalidate → refetch round
trip). The API step is the thinnest in the document. And the deployment step is
described as a formality when it is the richest part: a flat network, a random
published port, an unrequested dashboard, and `service_started` where health was
promised - four real gaps between a development orchestrator and a production
runtime, discovered by reading a file you generated. That is the lesson
`research.md` D2 says the project exists to teach, currently budgeted at four
hours and one sentence.

**The thinnest spot overall is error handling.** No isolation level, no
database-error-to-HTTP mapping, no cancellation, no readiness-versus-liveness
split. Concurrency control and failure mapping are where a .NET developer's
skill actually shows, they are cheap on a four-endpoint API, and the slice
currently teaches the happy path of every layer and the failure path of none.

**The security teaching gap is by omission, not error.** Every security control
is in the deferred half, so the developer finishes having learned nothing about
the part of the stack where mistakes are permanent - while producing a public
repository and a running unauthenticated finance app. Two controls would cost
under four hours combined and each teaches a real mechanism: a `Host`-header
allow-list (the control that makes "loopback default" mean what Constitution
VIII intends, since a visited web page can reach `127.0.0.1` same-origin by DNS
rebinding), and the `.gitignore` plus hygiene job.

**The reference documents are a maintenance tail in their current state.**
`D-030` says the value is that "the thinking is already done and adversarially
reviewed". The thinking is done - but nine documents carry specific, invisible
divergences from the live slice, and a stale reference consulted mid-build is
worse than no reference because the builder cannot see which paragraph is wrong.
The proposed fix is to **freeze** them: one banner per file naming the
contradicted claims and pointing at `slice-01.md`, plus the rule that a
reference document is corrected only when a slice is commissioned against it.
Five minutes a file, once, versus paying to re-find the same divergences every
round - which is precisely what this round did.

**The strategic question, from the red team.** Slice 01 builds a
manual-balance-entry app with no authentication, no PRD environment, and a
permanent "generated data" label - so the only way to answer the question the
project exists to answer, *is a chart of my own money worth 80 hours*, is to
type real balances into the one instance that is labelled fake, wiped by
`/dev/reset`, screenshotted by Playwright, and sitting in the working tree of a
public repository. **Decide that in writing now**, because at hour 80 it will be
decided by momentum.

---

## What round 2 caught that round 1 missed, and why

Round 1's own lesson was that thirteen of fourteen seats missed three wrong
Aspire claims because the documents were internally consistent. `D-029` was
written to fix that, and it half-worked.

**What it fixed:** every seat this round used markers, and the findings that
matter most all carry `EXECUTED` or `SOURCE@<pinned ref>`. The PostgreSQL
defects were found by running PostgreSQL. The Aspire defects were found by
cloning Aspire. The charting recommendation was found by reading two npm
tarballs. None of this was reachable by reading the specification set, and none
of it was reachable last round.

**What it did not fix, in two ways.**

First, **`D-029` was applied to the specs and not to the review**. Round 1's
findings were written into the tree carrying `SOURCE@` markers they had not
earned, and two of them were wrong. A finding is an external-system claim.
The rule needs to say so.

Second, **the marker rule does not tell you which mechanisms to spike**. Three
spikes were commissioned against Aspire; the SQL - nine load-bearing claims in
the same document - got two `DOCS@` markers and no spike, and it is where the
slice actually breaks. The load-bearing test ("would the design change if this
were false") was applied correctly and the *inventory* of load-bearing claims
was incomplete. A cheap fix: before writing a gate, list every external system
the step touches, not just the unfamiliar one.

---

# DISPOSITION

Assigned 2026-09-18, against the scope `D-030` already settled (one vertical
slice, then a hard stop) plus three calls taken at handover:

| Call | Ruling |
|---|---|
| **Azure Container Apps** | **DEFER**, not CUT. It is the target Kyle actually works against, so the practice value is the point; the maintenance cost that motivated the cut proposal is answered by the freeze instead. Trigger: a slice that commissions cloud deployment. |
| **E2E / Playwright** | **Keep one, and make it earn its place.** It asserts on the tabular equivalent rather than chart pixels (deterministic, non-visual), carries DoD 7's keyboard and table assertions, and includes one `AxeBuilder` call. This answers the scope realist and the accessibility seat at once rather than averaging them. |
| **Reference documents** | **Freeze with banners.** One banner per file, and the rule that a reference document is corrected only when a slice is commissioned against it. |

Per `process.md`: severity and disposition are different axes. Several **Must
Fix** findings below are `DEFER` — the defect is severe and the surface is not
being built yet. A `DEFER` with no trigger would be a `FIX` nobody scheduled, so
every one names its trigger.

**Totals, counted mechanically over the IDs this document carries (`ADV-10`):**
**81 FIX · 31 DEFER · 4 CUT · 4 ACCEPT**, over **119 distinct finding IDs**.
The header's "~157 raw / 46 consolidated" describes the seats' output before
merging, not what is dispositioned here; the 63/34 figures first published were
wrong and are corrected above.

---

## The freeze, mechanically

Kyle chose banners over corrections, so **the banner must do the work the
correction would have done**. A generic "this document is frozen" notice does
not; the builder's problem is not knowing a document is stale, it is not knowing
*which paragraph*. So each banner **enumerates the claims this review
contradicted**, by line, and points at `slice-01.md`.

The claims each banner must name:

| Document | Contradicted claims the banner names |
|---|---|
| `plan.md` | service discovery supplies the SPA's API address (`:95` — it never does; the browser uses a relative path); two simultaneous stacks by launch profile (`:101` — one AppHost path, one instance); the Worker owns migrations as a long-running service (`:189` — it must terminate); `IProjectionModel`'s split is a type-system property (`:149` — it is not; the refusal tests are the enforcement); `Projections` references nothing while `Validate` resolves inputs (`:56` vs `:144` — resolution is data access, the two cannot both hold) |
| `environments.md` | `:3`, `:16-23` simultaneity; the three guards being independently sufficient (`:60-65` — guards 1 and 2 read the same value, and guard 3 rests on revoking `DROP`, which PostgreSQL does not have) |
| `deployment.md` | inner-loop simultaneity (`:38-43`); `.env` described as regenerated (`:70` — it is an accumulator) |
| `security.md` | the reporting role holds "nothing else anywhere" (`:87` — `CONNECT`, `TEMPORARY` and `EXECUTE` are `PUBLIC` defaults); the statement timeout as a boundary (`:90` — it is `PGC_USERSET` and the user can disarm it); user-secrets as an `ISecretStore` satisfying FR-3.1 (`:49` — it does not encrypt); the KEK/stolen-laptop argument (`:63` — it defends volume exfiltration, not the threat it names) |
| `data-model.md` | the unique index that would reject the supersession the next paragraph describes (`:99` vs `:100-101`); the `closed_on` `CHECK` (`:89`, `:102` — impossible, it references another table); `v_balance_daily` versus the slice's carry-forward view (`:258`); no `account_source` table; **CUT** — `is_estimate` (`:97`), struck as a second semantically undefined channel for the weakness `source_strength` already carries (`SR-13`) |
| `testing.md` | the DEV-assertion test's layer (`:95`); the conformance properties asserted at `:125-128` while `:54-56` places three of the same behaviours at the frontend unit layer; "the generated client compiles against the document" (`:120` — tautological); **CUT** — the ~800/~150/~30/~10 test counts (`:13-16`) and the five coverage floors (`:165-173`), struck as unsourced numbers (`SR-12`); **CUT** — E2E journey 10, reset-and-reseed DEV (`:156`), struck as order-dependent against a shared stack (`TA-10b`) |
| `cicd.md` | coverage floors attached to a job that cannot execute those assemblies (`:30`); required review on a one-person repository (`:149`); image signature verification existing only on the optional target (`:128`) |
| `contracts/api.md` | no account-creation endpoint; no `POST /accounts/{id}/balances`; no `/transfers` or constants write path, so two constitutional states are reachable only through `/dev/seed` |
| `research.md` | D5's argument that the guarantee is a database privilege rather than query parsing — true of the timeout's *intent* and false as built: the row cap has no database mechanism at all and is the application-side string wrapper D5 rejects |

Writing those nine banners is itself a `FIX`, listed below. It is the single
highest-leverage item in this disposition: it converts nine stale documents from
a liability into a usable reference for about an hour's work, and it is what
makes every `DEFER` below safe to leave alone.

---

## FIX — before step 1

Corrections to the tree itself. None requires a toolchain; all are writing.

| Finding | What lands | Where |
|---|---|---|
| **R2-B2** Constitution III never amended | **Amendment 3** — carry-forward permitted *within* an account's active window, forbidden *past* a series end, with `accounts_carried` and `max_staleness_days` mandatory on every carried row | `constitution.md` |
| **R2-M16** user-secrets does not satisfy FR-3.1 | **Amendment 4**, or a stated carve-out: the inner loop holds sandbox credentials only, asserted at startup | `constitution.md`, `security.md` |
| **R2-B9** spikes cannot run before step 1 | Delete step 0 as a separate step. The three claims become the acceptance observations of step 1, each keeping its gate on the dependent step | `slice-01.md` |
| **Spikes A, B, C** answered from source | A: cut to a 20-minute confirmation, and record that simultaneity needs two directories. B: read the `aspire-ts-cs-starter` template instead — the browser uses a relative path in both modes. C: cut to the run-mode half; `WaitForCompletion` is confirmed | `slice-01.md` |
| **Spike D** proposed | **Add it, and run it first.** Schema, restatement and view against a throwaway Postgres, before step 2. The cheapest of the four and it covers B3-B7 | `slice-01.md` |
| **R2-M17** FR-4.3 unsatisfiable by manual rows | One clause: for hand-entered rows, the actor and entry time in place of a run id | `spec.md` |
| **SDD-7 / A11Y-1** three built behaviours have no FR | New FRs for series continuation, supersession, and the accessibility floor — the floor currently dies with the slice | `spec.md`, `traceability.md` |
| **SDD-9 / SR-10** dangling `[NEEDS CLARIFICATION]` | Delete; `D-030` answered it | `spec.md` |
| **SDD-3** blanket prefaces | Delete the three prefaces or add inline markers beside the claims. This is the one form `process.md` and Constitution XIII explicitly forbid | `plan.md`, `deployment.md`, `README.md` |
| **SDD-2 / SR-8** the three `C-374` mechanisms still unmarked | Covered by the nine banners above. **This is what closes `C-374`** | nine documents |
| **SDD-13** README does not know slice-01 exists | One paragraph: which document is the live unit of work, and that the rest are consulted rather than executed | `README.md` |
| **SDD-12** spec-kit compatibility overstated | Say "hand-portable, with a conversion step", and list the conversion: `FR-001` numbering, prioritised P1/P2/P3 stories, an `SC-###` Success Criteria section, an Assumptions section, and the constitution's Governance section and version footer | `README.md` |
| **R2-M18** traceability's Constitution table stops at XI | Add XII and XIII; correct the Principle I and III rows | `traceability.md` |
| **A11Y-9** charting library | **Recharts.** `accessibilityLayer` defaults true, keyboard traversal of data points, an announced value, `connectNulls` false, `prefers-reduced-motion` honoured — against ECharts' canvas default, opt-in `aria`, and a label truncated to 10 data points. Record the ten-point checklist so the criteria outlive the choice | `research.md`, `traceability.md` |
| **SR-5 / SDD-14 / RT-17** estimate arithmetic | Restate: 56-88 h by the document's own step sums, and **a realistic band of 100-160 h** including toolchain, CI iteration and the publish path. See ACCEPT below | `slice-01.md`, `plan.md` |
| **BLD-2 / SDD-14 / RT-17** DoD says "eight", the list has ten | "the ten live `M-` findings and the ten supporting `S-` items" | `slice-01.md` |
| **SR-6** the gate's first question is unanswerable | New DoD item: one line per work session in a `LOG.md` — date, step, hours. The only gate input that cannot be reconstructed afterwards without breaking ground rule 1 | `slice-01.md` |
| **SR-7** the stop gate names no condition for "no" | Write the abandon threshold into the assessment: a cost ceiling, and "the assessment must name three techniques this taught that Kyle did not already have" | `slice-01.md` |
| **BLD-3** nothing installs anything | Add a prerequisites step with named versions: .NET 10 SDK, Aspire CLI, container runtime, Node, dev-cert trust, and `pwsh` for Playwright's browser install — which this machine does not have per `C-283` | `slice-01.md` |
| **BLD-10** step 6 depends on step 7 | Reorder, or name the E2E host mechanism in step 6. Also: a `dotnet build` workflow in **commit two**, before the AppHost exists | `slice-01.md` |
| **SR-11 / BLD-8 / NET-13** required review on a solo repo | Required status checks, linear history, no force-push; **not** required approvals, with the reason written down so it is not silently re-added | `cicd.md` |
| **RT-16b** `portfolio_dev` contains `portfolio` | Rename to `portfolio_prd` / `portfolio_dev`. A substring test waiting to be written backwards, and it is free now | `environments.md` |
| **Nine freeze banners** | As tabulated above | nine documents |

## FIX — at a named step

**Step 1 — Skeleton.** `.gitignore` and the data-hygiene job in **commit one**
(R2-M5), with the pattern set also installed as a `pre-commit` hook, which is
the only point at which it is prevention rather than detection (R2-M6). The
architecture test asserts over project files or `GetUsedAssemblyReferences`, not
the compiled assembly's reference table, which the compiler prunes (SE-19); its
scaffolding cost goes into step 1's budget (BLD-12).

**Step 2 — Data.** Invert the supersession pointer: `superseded_at` plus a
backward `supersedes` FK (R2-B3), and strike the false dichotomy about
deferrable uniqueness (R2-B4). The view takes the **`LEFT JOIN LATERAL … ON
true`** form (R2-B5) and carries `accounts_unverified` (R2-B6) — **not `CROSS
JOIN LATERAL`, which silently cancels R2-B6** (`ADV-1`): under `CROSS JOIN` an
account with no observation produces no row, so `count(*)` counts only observed
accounts and the unverified count is identically zero. Measured on R2-B6's own
example: `CROSS JOIN` gives `150.0000 / 0`, `LEFT JOIN … ON true` gives
`150.0000 / 2`. The window-end rule and a
staleness cutoff are named before it is written (R2-B7). `HasPrecision(19,4)`,
asserted in the integration test (R2-M7). The currency constraint is a trigger
or a literal, not a cross-row `CHECK` (R2-M9). Declare `deleted_at` and
`superseded_at` (R2-M10). A `Money` value object carrying an explicit
`MidpointRounding`, since there is no global knob (SDD-10, SE-14). State the
Npgsql UTC-`Kind` invariant (BLD-7, SE-26). `HasFilter` drift and
`IDesignTimeDbContextFactory` named as gates (BLD-13). **Deliver
`account_source` as migration 2, not folded into migration 1** — same DDL, and
the second migration against a database with rows is the one worth practising.

**Step 3 — API.** `POST /api/v1/accounts` (R2-B1) and a net-worth series
endpoint carrying `accounts_carried` and `max_staleness_days` (R2-B8) — five
endpoints, not four. Scale validation returning 422 (R2-M8). Idempotency and
explicit correction semantics on the balance POST (SE-11). Bound the balances
collection (SE-23). Name the TypeScript client generator (BLD-6). An RFC 9457
422 refusal shape — the constitution's only appearance on the wire (SDD-15,
SE-20). Drift-gate determinism: `global.json`, an explicitly pinned generation
environment, and canonicalisation as a **named script**, not an adjective
(TA-4). A `Host`-header allow-list, about ten lines, which is what makes
"loopback default" mean what Constitution VIII intends against DNS rebinding
(SEC-7).

**Step 4 — Web.** Recharts, with its three wrong-for-this-application defaults
handled: `filterNull={false}` so a null is not silently dropped from the
readout, an explicit accessible name, and `polite` rather than the hardcoded
`assertive` (A11Y-10). Non-visual channels for a carried point — shape not
colour, the fields in the tooltip payload, columns in the table (A11Y-3). Form
accessibility including a `role="alert"` region for the 422 (A11Y-7). The
tabular equivalent specified: caption, summary, one column per visual encoding,
stated granularity (A11Y-13). A committed palette as CSS custom properties, and
contrast written as "4.5:1 text, 3:1 chart series, axis and focus ring" — axe
has no non-text-contrast rule, so tooling will not catch it (A11Y-8). A
persistent "Generated data" text marker (A11Y-6). The decimal-string-to-number
rule (SE-12). The `vite.config.ts` proxy named explicitly (BLD-4). TanStack
Query's reason for existing written down — the POST → invalidate → refetch round
trip.

**Step 5 — Tests.** The unit test becomes currency-mismatch rejection and the
liability sign convention; the midpoint assertion moves to integration where the
database is (TA-2). The integration test covers `v_net_worth_daily` — the
carry-forward window, `accounts_carried`, the stop at `closed_on` (TA-3). A
contract test asserting no *request* schema exposes `source_strength`,
`source_system`, `ingest_run_id` or `superseded_at` (TA-13). The client check is
`tsc --noEmit` over the frontend's own call sites, not the tautology (TA-5).
Playwright tracing budgeted or set always-on, since xUnit gives teardown no
access to the outcome (TA-11). The one E2E carries DoD 7's assertions and one
`AxeBuilder` call (A11Y-11, A11Y-12). State the fixture-sharing strategy
(TA-9). Add the failure path the slice otherwise never teaches: database-error
to HTTP mapping and `CancellationToken` (SE-24).

**Step 6 — CI.** The hygiene job and the architecture test in the workflow
(TA-16). `retention-days` on the E2E artifact, since artifacts on a public
repository are downloadable by any signed-in user (RT-18).

**Step 7 — Deploy.** The override supplies `127.0.0.1:` and DoD 3 **proves** it
with `ss -ltn` rather than asserting it (R2-M1); a `pg_isready` healthcheck and
explicit `depends_on`, tested cold with `down -v && up` (R2-M2);
`.WithDashboard(false)` (R2-M3); the publish output **directory** git-ignored,
not the `.env` file (R2-M4). `AddViteApp` publishability is a **gate**, not an
assumption — three sources disagree about which path works, so confirm against
the installed version before the step (BLD-5). Re-check the package version
rather than trusting one written months earlier (BLD-11).

## DEFER — each with its trigger

| Trigger | Findings |
|---|---|
| **The slice that commissions the SQL reporting surface** | R2-M11 `PUBLIC` defaults and the missing `REVOKE` DDL · R2-M12 `statement_timeout` enforced client-side · R2-M13 the row cap has no database mechanism, and D5's argument corrected with it · SEC-12/NET-12 `security_barrier` · RT-7 `work_mem` and `temp_file_limit` · RT-8 catalog metadata is readable · RT-10 CSV formula injection on export |
| **The first connector slice** | R2-M15 the OAuth `state` parameter · RT-11 webhook verification over the raw body, timestamp window, replay cache, and the ingress Constitution VIII does not allow for · RT-12 cursor poisoning and reconciliation · SEC-16 redaction by parameter name, since the query string is logged verbatim and an OAuth code has no shape · SEC-17 provider-side revocation · TA-14 the generator's clock |
| **The slice that commissions the second environment** | R2-M14 the three guards, restated honestly · RT-16a `COMPOSE_PROJECT_NAME` · A11Y-5 the environment banner as a full safety control · TA-7 the DEV-assertion test's layer |
| **The first slice holding real balances** | RT-14 encrypted backups and a path outside every sync root · SEC-15/RT-13 the KEK threat model restated · SEC-15b the secret-store key tables and `kek_version` · SEC-3 the generated-versus-real tripwire made mechanical |
| **The slice that commissions authentication** | SDD-8 authentication has no FR behind its pointer |
| **The slice that commissions cloud deployment** | The whole Azure block — `deployment.md`'s third target, `cicd.md`'s deploy pipeline, `security.md`'s Azure column, `research.md` D9, the third `ISecretStore` · SEC-18a signature verification is the self-hoster's manual step |
| **The first released version** | SE-21 breaking-change detection against the last release, not just drift |
| **The slice that commissions a real test suite** | TA-8 coverage floors attached to jobs that execute those assemblies · TA-6 the conformance properties placed once · TA-10 journeys 5 and 10 · TA-15 the missing write paths for transfers and constants |
| **The audit slice** | SE-22 immutability and append-only given a mechanism rather than a comment |

## CUT

| Finding | What is removed | Recorded |
|---|---|---|
| **SR-12** | `testing.md`'s ~800 / ~150 / ~30 / ~10 test counts and its five coverage floors. Unsourced numbers that get quoted back as commitments, and `slice-01.md` already overrode them (`S-11`). The pyramid shape and the order-of-magnitude argument stay — that is the part that teaches | `testing.md`, banner |
| **SR-13** | `is_estimate` on `account_balance`. Two undefined-relative-to-each-other ways to say "this number is soft"; `source_strength` already carries `derived` and `manual`, and Constitution IV exists to keep weakness in one channel | `data-model.md`, banner |
| **TA-10b** | E2E journey 10 (reset and reseed DEV). Order-dependent by construction against a shared seeded stack, which `testing.md:190` forbids outright. Replaced by a unit test on the banner component | `testing.md`, banner |
| **S-24 at the unit layer** | The midpoint rounding test as a *unit* test. The slice performs no arithmetic that rounds, so it would assert `System.Decimal`'s behaviour — which `testing.md:200` lists as deliberately not tested. The assertion survives, at integration | `slice-01.md` |

## ACCEPT

| Finding | Why we are living with it |
|---|---|
| **The 100-160 hour estimate** | Accept the number; do not cut scope to fit it. The scope realist and the builder appear to disagree and do not: the scope is right and the estimate was wrong. Cutting to reach 50-80 collapses back into the horizontal stub `D-030` was written to escape. What changes is the calendar and the CI-first ordering, not the contents |
| **NET-13** the solo required-review claim | Three seats agree from reasoning; two could not reach `docs.github.com` to confirm, so it rests at `INFERRED`. Accepted at that strength with one probe at step 6 rather than blocking on it — the fix (drop required approvals, keep status checks) is right either way |
| **RT-18** CI artifacts are public on a public repository | Accepted while the data is invented, with `retention-days` as the cheap mitigation. Re-opens the moment a real balance exists, under the real-balances trigger above |
| **SEC-8 / RT-18b** Playwright traces carry full network and DOM | Same reasoning. The unguarded path is a trace generated *locally* against real data and then committed, which the hygiene job cannot see inside — so `test-results/`, `playwright-report/` and `*.zip` go in `.gitignore` (a FIX) and the residual is accepted |

---

## What this disposition does to the slice

Nine blocking findings, all `FIX`. Most of the `Must Fix` security work is
`DEFER` with a named trigger, because the surface it defends is not being built
— which is the disposition rule working as intended rather than a softening.

The four items that change the shape of the work, rather than correcting it:

1. **Spike D goes first**, and step 0 folds into step 1. Two of the three
   original spikes are already answered, so this costs nothing net and it covers
   the five defects a running PostgreSQL found in twenty minutes.
2. **CI moves to commit two.** A `dotnet build` job and nothing else, before the
   AppHost exists — turning step 6 from a fourteen-hour wall into increments
   that fit an evening, and defusing the named quit risk.
3. **The nine banners get written before anything is built.** One hour, and it
   is what makes every `DEFER` above safe to leave alone.
4. **The estimate is restated, and the scope IS amended — the two together are
   Kyle's call, not this document's** (`ADV-17`, `ADV-18`). The builder seat's
   100-160 hours was costed against the **un-amended** slice. This disposition
   then adds roughly twenty items: a fifth and sixth endpoint, a second
   migration, a `Host`-header allow-list, a pre-commit hook and hygiene job in
   commit one, a `Money` value object, RFC 9457 problem details, idempotency on
   the balance POST, pagination, error-to-HTTP mapping and cancellation, a
   rewritten architecture test, a canonicalisation script, a committed palette, a
   specified tabular equivalent, a `role="alert"` region, a custom tooltip
   component, a prerequisites step, a `LOG.md`, an abandon threshold and nine
   banners. **An earlier version of this section asserted that "what changes is
   the calendar and the CI-first ordering, not the contents". That was false**,
   and it made accepting the doubled cost look free. The amended slice is being
   re-costed and the keep-or-cut decision is Kyle's against that figure.

---

# ADVERSARIAL REVIEW OF THIS DOCUMENT — 2026-09-18

One seat, charged with reviewing the review under `D-031` §1: does each
*prescription* carry the strength its finding claims, and does the disposition
hold. It stood up PostgreSQL 16.13, re-read `microsoft/aspire` at the same pin,
downloaded `recharts@3.10.1`, `axe-core@4.13.0` and the spec-kit templates, and
reached `github/docs`. **Its verdict was that this document could not be built
from as written.** Corrections applied above; the rest recorded here.

## What it confirmed

Recorded first, because a clean result is a result. Re-run or re-read
independently and sound: every SQL defect (`R2-B3`, `B4`, `B5`'s `DISTINCT ON`
half, `M-7`, `M-8`, `M-9`, `M-11`, `M-12`, `M-14`); the preferred supersession
redesign, **and both stated alternatives**; every `faf168f` Aspire correction;
the `aspire-ts-cs-starter` reading, exactly; `WithImageSHA256`; every Recharts
claim including `prefers-reduced-motion`, which it went looking to falsify and
could not (`isAnimationActive` defaults to `'auto'`); axe-core's 105 rules with
no non-text-contrast rule; every spec-kit shape in `SDD-12`; the GitHub
force-push language verbatim; `Aspire.Hosting.Docker` 13.5.4; the estimate
arithmetic. Of 24 line anchors in the freeze table, 21 were exact.

It also closed one open item itself: **`S-25b` holds** —
`DistributedApplicationTestingBuilder` sets `DcpPublisher:RandomizePorts` and has
no volume handling, so the real named volume is mounted
[`SOURCE@microsoft/aspire@b477bdd DistributedApplicationFactory.cs:202`]. One
imprecision: it also sets `DisableDashboard`, so "randomises ports and nothing
else" is wrong. Not load-bearing.

## Prescriptions corrected

| ID | Correction |
|---|---|
| **ADV-1** | **The blocker, fixed above.** `CROSS JOIN LATERAL` + `count(*) - count(balance)` cancel: the composed pair returns `accounts_unverified = 0` on `R2-B6`'s own example. Both halves were measured; the composition never was. |
| **ADV-2** | **Fixed above.** `R2-B5`'s "300x" rests on an unstated daily cadence. At the slice's real cadence the naive form is 30% over budget, not 4,500%. |
| **ADV-3** | `R2-M13`/`RT-7` prescribe `work_mem` and `temp_file_limit` "which the database does enforce" — against an `array_agg` example neither bounds. Measured: `work_mem='64kB'`, `temp_file_limit='1MB'`, a plain aggregate accumulated **7,813 kB in 222 ms**, because a non-grouped aggregate's transition state is not charged to `work_mem` and never spills. Both knobs are still worth setting (they bound sorts, hashes and spill files); the enforcement claim is struck. Related: `R2-M12`'s client-side timeout **is** correct and genuinely un-disarmable [`SOURCE@npgsql/npgsql@main NpgsqlConnector.cs:2116-2121`], but it bounds *the API's wait, not the server's work*. Taken together the two DEFERs leave server-side resource exhaustion with **no prescribed control**, which neither finding notices. |
| **ADV-4** | `NET-13` was ACCEPTed at `INFERRED` for want of a doc read; the seat reached it in one request. `DOCS@2026-09-18 github/docs about-protected-branches.md`: *"By default, the restrictions of a branch protection rule don't apply to people with admin permissions"*, and bypass lists require an organization. So on a **personal** repo required approvals is silently bypassed by default — not "blocks every merge or trains the bypass habit". The prescription (status checks, linear history, no force-push; not required approvals) is right either way. **The ACCEPT is withdrawn; this is a FIX with a verified reason.** |
| **ADV-5** | Step 4's `polite`-not-`assertive` is **not a prop**. The attributes are spread last from a literal and nothing feeds them [`SOURCE@recharts@3.10.1 DefaultTooltipContent.js:140-147`]; the only route is a custom `content` component. Budget a component, not a line. |
| **ADV-6** | `R2-M4`'s fix (git-ignore the publish output directory) stops the secret reaching git but **does not touch the accumulator** the finding describes — a rotated password keeps its stale value across every later `aspire publish`. Second-order: if the `S-34` override file lives in that directory, git-ignoring it un-commits the override. **The disposition must say where the override lives.** |
| **ADV-7** | `R2-M6`'s pre-commit hook has **no install path**. `.git/hooks` is untracked and not created by `git clone`, so a prevention control that vanishes on re-clone is not prevention. Name `core.hooksPath` with a committed hook directory, or a committed installer script, and put it in Step 1's budget. |

## Presentation corrected

**ADV-8 — seat counts.** The five three-plus-seat findings are arithmetically
honest (no padding, no near-miss merged in), but two of them attach the count to
the wrong limb. `R2-B3`: four seats reached the *defect*; one ran it, and the
four **split three ways on the remedy**, which the Blocking table's bold **4**
reads as consensus on. `R2-M1`: three seats reached "unauthenticated API on the
LAN"; one could read `AddPorts`. **Rule going forward: state a seat count only
for the limb each seat actually reached, and never put one seat's `EXECUTED` in
an Evidence cell shared by four.**

**ADV-9 — the `AddViteApp` conflict is less open than stated.** At the pin the
three sources do not disagree: the pipeline validator throws on an unconsumed
build-only container and **names the supported paths in its own error message**
— *"Reference them from another resource, for example using
`PublishWithContainerFiles` or `PublishWithStaticFiles`"*
[`SOURCE@microsoft/aspire@b477bdd DistributedApplicationPipeline.cs:402-406`] —
the template does exactly that, `PublishAsStaticWebsite` is a different
(experimental, YARP) shape rather than a contradiction, and the
`KeyNotFoundException` is a version-specific bug report. **Restated:** the
supported path at `b477bdd` is `PublishWithContainerFiles`; step 7 confirms the
installed version exposes it. The gate stands; the manufactured uncertainty does
not. One consequence nothing had drawn: on that path **the API container serves
the SPA**, so there is no second published service and `slice-01.md`'s topology
is wrong in a way no banner names.

## Integrity defects

**ADV-11 — forty IDs are dispositioned but stated as findings nowhere, and the
seat reports they point to were never committed.** That was this session's
choice: the nine reports were held in context and not written to disk. It cost
their text. Eleven of the forty are DEFERs whose entire surviving content was a
half-clause, and a disposition with no finding is a label, not a decision. Their
text, reconstructed and now recoverable:

- **`RT-7`** — the row cap bounds the answer, not the work; a single-row
  aggregate can exhaust backend memory (see `ADV-3` for what does *not* bound it).
- **`RT-8`** — catalog metadata is readable by the reporting role:
  `pg_views.definition` exposes every view body including the soft-delete
  predicates, `pg_class`/`pg_attribute` every table and column name,
  `pg_class.reltuples` row counts. Stop treating schema shape as confidential and
  pin `search_path`.
- **`SEC-3`** — "generated data only" is an instruction to a person, not a
  control; nothing distinguishes a generated row from a real one, so the tripwire
  needs to be mechanical.
- **`SEC-15b`** — the envelope-encryption scheme has **no table** for its data
  keys, so KEK rotation cannot record which KEK wrapped which key and a crash
  mid-rotation is unrecoverable.
- **`SEC-18a`** — image signature verification exists only in the Azure deploy
  pipeline; the primary target is a self-hoster running `docker compose up`, which
  verifies nothing. Publish the `cosign verify` command in the release notes.
- **`SE-22`** — `ingest_run` and `audit_event` are declared immutable and
  append-only with no mechanism; the app role holds rights over the whole schema.
  Needs `REVOKE UPDATE, DELETE` plus a raising trigger.
- **`TA-6`** — the five client conformance properties are asserted at the
  contract layer while three of the same behaviours sit at the frontend unit
  layer; state them once, at Layer 1, and let the contract layer assert only the
  wire shape.
- **`TA-7`** — the DEV-assertion test is placed at integration though it is a
  pure function of two injected values, and testing it there requires
  manufacturing a PRD-named database inside the test environment.
- **`TA-10`** — E2E journeys 5 and 10 break the suite's own rules: 5 duplicates
  a privilege check already covered at integration, 10 is order-dependent against
  a shared stack.
- **`TA-14`** — the deterministic generator has **no pinned clock**; if the
  series anchors to today, the same seed yields different dates tomorrow and every
  date-keyed fixture drifts. It needs `(seed, asOfDate)`.
- **`TA-15`** — there is no API write path for pending transfers or constants,
  so two constitutional states are reachable only through `/dev/seed` and a PRD
  user cannot record the state Constitution V calls first-class.

**ADV-12 — three findings carry no disposition.** `SR-9`, `SE-3` and `RT-17d`
appear once and never again. The `data-model.md` banner covers the substance;
they are **FIX, folded into that banner**, and are recorded as such here so they
are not observations.

**ADV-13 — fixed above.** Three CUTs named `testing.md`/`data-model.md` as their
recording location, which `D-031` had just frozen, and did not appear in those
files' banner enumerations. They are now in the banners, which is the only place
they can legally be recorded.

**ADV-14 — two findings dispositioned twice, and ACCEPT used for two things.**
`RT-18` and `NET-13` each appear as both FIX and ACCEPT. `NET-13`'s ACCEPT is
withdrawn (`ADV-4`); `RT-18` is a **FIX** (`retention-days`) with an explicitly
accepted residual, matching how `SEC-8` was already written. Separately,
`process.md` defines ACCEPT as a statement about the **defect**; two rows used it
about the **evidence** or about a cost. **The 100-160 hour row is not a
disposition at all** — it is a decision, and it has gone back to Kyle.

**ADV-16 — the Definition of Done was never updated for this disposition.**
`BLD-2` fixes DoD 9 to "the ten live `M-` findings and the ten supporting `S-`
items". After this document, what is live is ~81 round-2 FIX items. As it stands
a builder can satisfy every DoD item and implement none of the corrections here.
DoD 9 needs one more clause.

## What this seat found that round 2 missed

**ADV-20 — six of the ten round-1 `S-` prescriptions still inside `slice-01.md`
were never re-examined.** `C-375` is a *class*, and its population is **every
round-1 prescription still sitting in the live document**: `S-14`, `S-25a`,
`S-25b`, `S-25c`, `S-33`, `S-34`. Four are platform claims carrying
`SOURCE@microsoft/aspire@b477bdd`. The .NET seat re-checked the claims in the
*prose*, not the prescriptions in the *findings table* — which is the distinction
`C-375` is about. One (`S-25b`) is now closed above; five remain. **This is the
highest-value unfinished work in the round.**

**ADV-21 — round 2 reasoned about `/dev/*` routes that `slice-01.md` does not
have.** `grep -n "/dev" slice-01.md` returns nothing; those routes live in
`contracts/api.md`, a frozen reference. Consequences: one of `TA-4`'s three
stated causes of drift-gate non-determinism does not exist in the slice, and that
limb of its fix addresses nothing (the other two causes stand, and the
canonicalisation limb still earns its place); the red team's "wiped by
`/dev/reset`" is wrong; and `TA-10b`'s reasoning reads as though the journey were
in scope. **This is the review reasoning from documents it had just declared
unreliable** — the failure the freeze exists to stop.

**ADV-22 — three charter gaps.** No **frontend engineering** seat: step 4 is the
second-largest step by hours and was reached only through the accessibility and
software-engineering lenses. No **operations/observability** seat: `ServiceDefaults`,
OpenTelemetry and readiness-versus-liveness are named once, by nobody in
particular, and step 1 builds `ServiceDefaults` "for telemetry and health" with
nothing verifying it. And **nobody re-read `spec.md` as a requirements document**
— its three findings were all reached from the slice side looking back.

**ADV-23 — every DEFER is contingent on a slice `D-030` says may never exist.**
Eight of nine triggers are "the slice that commissions X", and `D-030` §2 makes
the stop a gate whose live outcome includes *no second slice*. If the assessment
declines one, all 31 deferred findings become permanent — and `process.md` says a
permanent removal is a CUT with a recorded location, not a DEFER. **Stated
plainly rather than discovered at the gate: if slice 2 is declined, these convert
to accepted-and-permanent, and the banners are their record.**

## The structural finding

The concrete defects above — wrong totals, forty findings with no text, CUTs
recorded nowhere, three wrong anchors, and a prescription pair that cancels — are
all downstream of one thing: **the disposition was written by the seat-broker and
read by nobody.** Round 1 was reviewed by round 2; round 2's *disposition*, the
artifact that actually decides what gets built, was reviewed by nobody until this
seat. That is `C-375` one level up again, and it is the argument for making an
adversarial pass over the disposition a standing part of the method rather than
something commissioned once.

---

# THE LAST FIVE — `C-375`'s remaining population, verified 2026-09-18

`ADV-20` observed that `C-375` is a **class**, and its population is *every
round-1 prescription still sitting in the live build document* — not just the two
already caught. Ten sit in `slice-01.md`. Round 2 re-examined four, `ADV` closed
`S-25b`, and five had never been checked by anyone. One seat took them, with a
`microsoft/aspire` clone at `b477bdd` and `raw.githubusercontent.com` reads of
`dotnet/aspnetcore` and `microsoft/OpenAPI.NET` at pinned refs. No .NET SDK and no
Docker daemon were available, and nothing is marked `EXECUTED` that was not run.

**Two more were wrong. The hit rate on unverified round-1 prescriptions is now
four of seven.**

| | | |
|---|---|---|
| **`S-14`** | **PARTLY WRONG** | Three of four limbs. "Sorted keys" is already done upstream (`Components.Schemas` is `OrderBy`'d ordinally; `paths` is registration-ordered and deterministic). "Invariant culture" is already satisfied — but only on the build-time path, so the limb was never about culture, it was about *which generator you use*. "LF via `.gitattributes`" is the wrong target and counterproductive: `Writer.NewLine` is `"\n"` unconditionally, and `.gitattributes` would **hide** a CRLF from `git diff` while an in-process compare still failed. Only the failure-message limb survives, and the command it needed — `dotnet build -p:OpenApiDocumentsDirectory=<dir>` — was the missing piece. |
| **`S-25a`** | **VERIFIED** | All three limbs exact. One understatement corrected: `ConfigureComposeFile` and `WithProperties` also exist, and the per-service callback runs *after* the network assignment — so the internal/front split **is** expressible in committed C#. |
| **`S-25c`** | **PARTLY WRONG** | Premise verified; the prescribed wording **breaks step 5 of its own document**. `DistributedApplicationTestingBuilder` ships in `Aspire.Hosting.Testing`, which matches the banned prefix, and step 5 requires it. Both official templates reference it directly. An unexceptioned invariant fails the moment the integration-test project exists — and would then be quietly rewritten to exclude it. |
| **`S-33`** | **WRONG** | Names the wrong file *and* prescribes work the publisher already does. `aspire publish` writes a keys-only `.env` — that **is** the example file, under the name Compose reads. The file carrying resolved secrets is `.env.<environment>`, written by *deploy*. "Never a populated `.env`" pointed at the one variant that is never populated. And a root `.env.example` is read by nothing and, in the publish directory, is un-committed by the rule protecting the `.env`. |
| **`S-34`** | **PARTLY WRONG — and its GATE resolves YES without a spike** | All four overrides are expressible in the app model: `Service` carries `Healthcheck`, `DependsOn` with `Condition`, `Restart`, `Ports` and `Deploy`; the callback runs after the network assignment so it can overwrite `service_started`; the constraint is `IComputeResource`, which both `ContainerResource` and `ProjectResource` satisfy. **But the gate was pointed at the wrong limb.** The *fallback* is what fails: Compose keys `ports` on `{ip, target, published, protocol}`, so an override adding `127.0.0.1:` **appends** rather than replaces — both bindings, a bind conflict, DoD 3 silently unmet — and no override file can delete a service, so `.WithDashboard(false)` has no file route. Also: one limb of the original, **resource limits**, had been silently dropped with no disposition; restored as an explicit DEFER. |

## Two corrections to this document's own earlier findings

**`ADV-6`/`R2-M4`'s accumulator reasoning was wrong about which file holds the
secret.** "A rotated password keeps its stale value across every later `aspire
publish`" is true only of values a human typed into `.env` by hand — and `.env`
never receives a generated secret in the first place (`value: null`,
`includeValues: false`). The secret-bearing `.env.<environment>` is written by
*deploy* with `Create` + `includeValues: true`, **rebuilt from scratch each run**,
so a rotation does propagate. **The git-ignore fix stands; the reasoning behind it
did not.** Corrected in `deployment.md`'s banner and in `.gitignore`.

**`S-14` sat in a three-way contradiction nothing had caught**, because it is not
a platform claim and falls in the seam between the cut and the step: the middle
cut removes drift-gate determinism work, the step-3 preface repeats the cut, line
241 prescribed exactly that work, and DoD 6 stated it as a pass/fail gate. The cut
stands and DoD 6 is reworded.

## What this says about the method

Four of seven unverified round-1 prescriptions were wrong, and **none of the four
was catchable by reading the documents** — each fell out in minutes from a clone
already on disk. The two failures found here have the same shape as the two that
created `C-375`: *the defect is real, and the remedy names an artifact that either
does not do the job or breaks something else in the same document.*

`D-031` §1 already binds review output to `D-029`'s markers. What this round adds
is narrower and worth stating: **a prescription that names a file, a package, a
method or a flag is a claim about that artifact, and the cheapest possible check
is to look at the artifact.** Three of the four failures would have been caught by
one `grep` against a clone.

`S-34`'s gate is the other lesson. It was written to be answered by a **spike** —
running the thing — and it was answerable by reading four small model classes and
one ordering in the publisher. A gate that could have been closed by reading is a
gate that cost the schedule a day for nothing.
