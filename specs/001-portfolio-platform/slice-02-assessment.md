# Slice 02 — the assessment

**Status: DECIDED, 2026-10-19 (Kyle): a third slice is wanted, for the security tier.**
Written 2026-10-18 against `main` at `a6bd187` (PR #60 merged), with the DoD evidence added
alongside it; Kyle's sections 5 and 6 recorded 2026-10-19. **Seven** techniques are ticked
(§5), against a limb that needs three. The first box was typed `[c]`; Kyle confirmed it is a
tick. Per `slice-02.md` "Then stop" and `D-041` §6, the third slice still needs **its own
decision file, written and dated, in the planning repository** before any work starts. It is
not a continuation of this one.

**Who wrote which part.** Sections 1–4 and 7 are facts, assembled by the agent session that
did the build. Each one can be checked against a commit, a CI run or `LOG.md`, and none
carries a recommendation. Sections 5 and 6 are **Kyle's**: the gate asks what this taught
*Kyle*, which no one else can answer. The agent laid out candidates and evidence there and
decided nothing; every box was left unticked for Kyle.

---

## 1. Definition of done

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | Spikes E, F, G run; claims restated at `EXECUTED` or replaced | **Met** | G `03b3cff`, E `fcaf458`, F `035a404`. Each has a `RESULTS.md` and a RAN note in `slice-02.md` step 0. §3 has what each found |
| 2 | Under `aspire run`: bind, upload a CSV and an OFX file, both appear in the chart; the same files again change nothing | **Met, under the Aspire test builder** | It starts the same app model `aspire run` does. `ImportAFileTests`: CSV `b4962e9`, OFX with this file. The CSV import draws the chart. Both files are `unchanged` when uploaded again |
| 3 | Under `docker compose up` from the published artifact: the same, with the resource limits in the compose file and the over-limit file refused | **Met, through the API rather than the browser** | Limits in `aspire-output/docker-compose.yaml` and in `HostConfig.Memory` (`a6bd187`). 1 MiB + 1 got `413` with and without `Content-Length`. A CSV and an OFX file were imported, then `unchanged` on repeat, and net worth read `100` then `250` across their dates. Run 2026-10-18 on a copy of the published compose file with its own volume |
| 4 | The tests in step 5 green; the CI jobs green on a pull request | **Met, as step 6 reads it** | Unit, architecture, integration and contract are green in CI on every PR from #54 to #60, and the time-zone matrix from PR #59, when it was added. The E2E (14 tests) is green **locally**: step 6 keeps it out of CI, as slice 01's middle cut did |
| 5 | The architecture test passes, including the new rule | **Met** | 52 cases. `IngestionBoundaryTests` was seen failing (51 of 52) with `Infrastructure` and EF Core added to `Ingestion` (`eb2ca94`) |
| 6 | The committed OpenAPI document regenerates identically, and the client compiles | **Met** | The Contract job is green on every PR, and the web job runs `tsc -b`. The client types a binary body as `string`, so the upload uses `fetch` (`8f683f8`) |
| 7 | The upload form and run history keyboard-reachable, labelled, axe 0 | **Met** | axe found 0 WCAG 2.2 AA violations, on the full page (`b4962e9`) and on the degraded page (`edb9504`). Tab reaches the link form, the file input, the import button and the history region (with this file) |
| 8 | No committed file is an export; hygiene green with no new allow-list entry | **Met** | No `.csv`, `.ofx`, `.qfx` or `.qif` file is tracked. `ALLOWLIST` is byte-identical to `fd6f4bb`. `hygiene.py --all` is clean; its 14 advisories are spec prose. Every fixture is generated in code |
| 9 | Each obligation done, or recorded as not done with a reason | **Met** | Below |
| 10 | The dates rule holds away from UTC | **Met** | `e7fc1b6`, detailed below |
| 11 | Cross-site writes refused, each layer alone and both together | **Met** | Each layer alone, on every write endpoint, in integration tests; layer 2 was seen failing without the middleware (`8f683f8`). Both together in Chromium, from two attacker origins against two targets, with whether each request reached the API recorded. This **found a real hole** (§3) (`e7fc1b6`) |

**DoD 10 in detail (`e7fc1b6`):**
- **Browser:** the page ran in `America/Chicago` with its clock pinned at 23:30 either side
  of the US change. Every default "today" was Chicago's date, and making "today" UTC failed
  both cases. A calendar date rendered as written in Chicago, UTC+14 and UTC−11.
- **Server:** with a pinned clock, "today" is the UTC date.
- **OFX:** the repeated 01:30 on 1 Nov is one date and two moments.
- **CI:** the matrix proved its own zone. Kiritimati logged `2026-10-19 07:51 +14` and Pago
  Pago `2026-10-18 06:51 SST −11`. Each ran 119 unit and 77 integration tests, 0 skipped.

**DoD 9, obligation by obligation:**

| From | Item | Outcome |
|---|---|---|
| `D-030` Amendment 1 | Idempotency on `POST /balances` | **Done** `522a9aa`. The same amount again is `200`, and nothing is written |
| `slice-01.md` (`S-34`) | Resource limits | **Done** `a6bd187`. Measured, then set (§2 of step 7) |
| `D-034` §5 | Per-account-type staleness cutoffs | **Deferred, by design.** Invented data has no entry history to tune against (`D-041` §4) |
| `C-389` | Coverage weighted by value | **Open question**, §4. Not built and not ruled |
| `D-030` Amendment 1 | Pre-commit hook | **Deferred.** Its trigger is the first real balance |
| slice-01 assessment | Network split (`S-25a`) | **Open.** Not this slice's technique |
| slice-01 assessment | The bundle size | **Open, and grown:** 629 kB to **638 kB** (189.6 kB gzipped) |
| Dependabot PR #12 | `@types/node` on Node's major | **Held** in `.github/dependabot.yml` |
| Kyle, 2026-10-18 | The dates rule | **Done**, DoD 10 |
| Kyle, 2026-10-18 | Cross-site writes refused by two layers | **Done**, DoD 11 |
| Kyle, 2026-10-18 (`slice-02.md`, "What the slice is not") | No real balance before the security tier | **Held.** Invented data only throughout |

---

## 2. What it cost

| | Hours | How it was measured |
|---|---|---|
| **Kyle** | **0** logged | `LOG.md`, the `Kyle h` column: every slice-02 row is `—`. Kyle's reviews, merges and rulings in the session were not logged as hours |
| **Agent**, as logged | **9.4** | `LOG.md`, the `Agent h` column: 13 rows, this assessment included |
| **Agent**, as measured | **≈ 5.2** for the definition through step 7 | Merge timestamps: PR #49 at 13:05 UTC to PR #60 at 18:14 UTC, 2026-10-18. That span includes time waiting on merges. Not in it: the `D-041` decision before (planning PR #119, merged 12:57 UTC), and this assessment after |

**The logged agent figure is overstated again, as in slice 01.** The rows are estimates the
agent wrote as it went. They total more than the span they fall inside.

**Work outside both columns:**
- the planning-repository PRs: `D-041` (#119, merged), and an amendment to it (#122, closed
  unmerged on 2026-10-20);
- the guard that keeps this project's sessions from writing to the planning repository, a
  hook outside both repositories;
- the Dependabot pass.

**Against the 200-hour threshold:** it is **not tripped, and that tells us nothing.** `D-041`
§3 said so in advance: with the agent building, this limb cannot trip however long the
slice runs. **The gate rests entirely on the techniques limb, §5.**

---

## 3. The spikes' claims, and what else in `specs/` is suspect

| Spike | Claim | Result |
|---|---|---|
| E | OFX's balance is `LEDGERBAL`/`BALAMT`/`DTASOF`; 1.x is SGML and 2.x XML; no maintained .NET library is worth depending on | **Held.** OfxNet 1.8.1, the one recent candidate, was run on the same files. It read amounts 100× too large, for a comma decimal and for a plain `.` decimal on a de-DE machine. It also processed DTDs, dropped an investment statement silently, and threw on `[0:GMT]`. **Not adopted.** The element names are corroborated by OfxNet's independent reading but were **not read from the OFX specification**, which stays `INFERRED` |
| F | Kestrel's default is 30,000,000 bytes; an `IFormFile` endpoint needs an antiforgery decision; Vite forwards bodies unchanged | **Held, all three.** Three further findings changed the design: a multipart upload **spills to disk**; taking a form means **turning antiforgery off**; and through the dev proxy, "too large" arrives as **`502`, not `413`**. The upload became the raw request body |
| G | The view's same-day pick across sources is unspecified | **Held, more strongly than claimed.** **Write order** decided net worth; the plan did not. "Changes between queries" was not observed. At build time the old view picked the manual row in both orders: still unspecified, resolved differently from the spike. Fixed by Kyle's ruled tie-break in migration 4 |

**Still suspect as a result.** None of these documents was rewritten, because `D-031` keeps
them frozen until a slice is commissioned against them:
- **`spec.md`** has no requirement for a file import: FR-1 is MCP and FR-2 is HTTP APIs.
  A correction to `D-041` was drafted (planning PR #122) and closed unmerged, so `D-041`
  still says FR-1/FR-2. The spec was not changed either.
- **`data-model.md`** describes an `ingest_run` that was not built: `connection_id`,
  `trigger`, `environment`, and a `partial` status this slice deliberately never produces.
  Its provenance-strength list is `statement, api, derived, scraped, manual`; the code's
  enum since slice 01 is `statement, export, api, scrape, manual`. **`export`, the strength
  both importers record, is not in the data model at all.**
- **`security.md`** requires "CORS restricted to the known frontend origin". It does not
  name the dev server as a CORS surface, and the dev server's default silently broke that
  rule (below).

### Defects the build found that no review did

Each one was found by running something, not by reading.

| Defect | Found by | Commit |
|---|---|---|
| **The net-worth view's same-day pick was decided by write order.** A manual entry and an import on one day gave 100 or 200 depending on which was saved first | Spike G | `03b3cff`; fixed `522a9aa` |
| **The only maintained OFX library misread money**: 100× on a comma decimal or a de-DE machine. It also expanded a DTD entity into an account id and dropped investment statements without an error | Spike E | `fcaf458` (not adopted) |
| **A multipart upload is written to disk**, and accepting one means turning antiforgery off | Spike F | `035a404` |
| **CI's hygiene scan passed a value that a later commit removed**, though the commit stayed reachable on a public repository | A refusal pushed past on PR #51 | `658d88a` |
| **The UTF-8 byte-order-mark check could never match.** A `"\xEF…"u8` literal encodes U+00EF as two bytes | Its unit test | `eb2ca94` |
| **"Record a balance" claimed a replacement** when an idempotent repeat changed nothing | Reading the step-2 contract against the form | `b4962e9` |
| **One `toLocaleString` option clash blanked the whole page.** There was no error boundary | The step-4 E2E | `b4962e9`; contained `edb9504` |
| **Vite's dev server granted CORS to any `localhost` or `*.localhost` page.** Through the proxy, an attacker page **read `/api/v1/accounts`**, and its preflighted writes reached the API, where `Sec-Fetch-Site` refused them | The DoD 11 browser test | `e7fc1b6` |

One discovery, not a defect: `.Accepts(...)` metadata makes **routing** return `415`. It was
found by disabling the handler's own content-type check (`8f683f8`).

**The session's own slips, each corrected unless marked:**
- a hygiene refusal masked by a pipe (PR #51);
- mixed line endings in `.gitattributes` (PR #50);
- three drafting errors in `D-041`. **Not corrected:** the corrections (planning PR #122)
  were closed unmerged, so the errors stand in `D-041`;
- CRLF in a planning file (`C-411`);
- `LOG.md` rows for steps 1–7 written into slice 01's table, moved with this file.

---

## 4. The three problems the slice was chosen for

`slice-02.md` named three problems that every later connector has. Which did it actually
exercise, and which only in form?

| Problem | Exercised? | Evidence |
|---|---|---|
| **Re-import without double counting** | **Yes, fully.** | The same file twice gives `unchanged = N` and no new row: in integration, in the browser, on the published stack, and at 10,000 rows. `POST /balances` follows the same rule |
| **A source that disagrees with what is stored** | **Yes, on invented values.** | Restatement at 10,000 rows. Manual against import on one day, and two sources of equal strength, under the ruled tie-break (G1–G3). Never a real institution's restatement behaviour |
| **Identity across sources** | **In form.** | One account bound under both CSV and OFX imports from each (integration, and on the published stack). The binding is manual and the ids are invented. Nothing matched or merged identities automatically, and the slice never had to |

**`C-389`, value-weighted coverage: did the slice make the case?** The facts:
- the status still counts accounts;
- no test here produced a status that value-weighting would have read differently;
- with invented balances, none could show a large account misreported as "verified".

The argument in `slice-02.md` stands as an argument. **Ruling it is Kyle's call.**

---

## 5. Techniques — **Kyle to complete**

The gate's words (`D-041` §6): *"three techniques this slice taught that Kyle did not
already have — each with the commit that demonstrates it, and **not** the six slice 01
already claimed."*

**The fact that frames this section:** Kyle logged no hours, and an agent session did the
build. What follows are techniques the build **demonstrates**, each with its commit. Whether
any is now **Kyle's** — understood well enough to use without the agent — only Kyle can
answer. **A technique merely present in the repository does not satisfy this limb.**

Tick the ones that are genuinely yours; strike the rest. **Fewer than three means STOP.**

- [x] **Idempotency by content, with one write rule for every path and the run record
      immutable at the database.** Unchanged, restated or created per (account, date,
      source); triggers refuse `UPDATE`/`DELETE`/`TRUNCATE`; CHECKs hold all-or-nothing on
      the row; the migration applied to rows and reversed. `522a9aa`.
- [x] **Defence in depth, tested from a real second origin in a real browser.** Content types
      that force a preflight, plus `Sec-Fetch-Site`, each shown alone and together. It caught
      the dev server's CORS default. `8f683f8`, `e7fc1b6`.
- [x] **Moments versus calendar dates.** UTC moments converted only at the edge; calendar
      dates never converted. Tested with pinned clocks, browser time zones, and a CI matrix
      that proves its own zone. `fcaf458` (the rule), `e7fc1b6`.
- [x] **Reading hostile input without a library, and declining a dependency on evidence.** An
      SGML and an XML front end over one tree; DTDs prohibited; invariant-culture amounts;
      all or nothing. `fcaf458`, `eb2ca94`.
- [x] **Raw-body uploads rather than multipart**, for what a form reader does to disk, to
      antiforgery and behind a proxy. `035a404`, `8f683f8`.
- [x] **Measuring before limiting.** cgroup `memory.peak`, and why an unconstrained .NET peak
      overstates its need. `a6bd187`.
- [x] **Proving a defence by breaking it.** Every new rule here was seen failing: the
      architecture rule, idempotency, the tie-break, both cross-site layers, the time-zone
      cases and the error boundary. `eb2ca94`, `522a9aa`, `8f683f8`, `e7fc1b6`, `edb9504`.
      *Closest to slice 01's "verifying a claim before building on it"; Kyle should decide
      whether it is a new technique or the same one.*

Kyle's own, if different: ________________________________________________

---

## 6. The decision — **Kyle to complete**

`slice-02.md`: *"Two things are explicitly NOT reasons to continue: that the slice works,
and that the next slice is obvious."*

- [ ] **STOP.** The default.
- [x] **A third slice**, which requires **a new decision file, written and dated**, in the
      planning repository. It is not a continuation of this one. If chosen: which technique
      is it for? **The security tier** (Kyle, 2026-10-19): `security.md`'s "API" section -
      authentication, `SameSite=Strict` session cookies, CSRF protection on every
      state-changing request, CORS restricted to the app's own origin - and the pre-commit
      hook. It is the slice Kyle's 2026-10-18 ruling requires before any real balance.

**Bound by Kyle's ruling of 2026-10-18 whatever is chosen** (recorded in `slice-02.md`, "What
the slice is not"): no real balance enters before a slice that builds `security.md`'s tier. A third slice that proposes real balances must be that
slice, or come after it.

If a third slice is commissioned, these are its known starting obligations, each already
named with a trigger or recorded in a PR:
- **the security tier** (Kyle, 2026-10-18), before any real balance;
- **the pre-commit hook** (`D-030` Amendment 1), the first real balance;
- **per-account-type cutoffs** (`D-034`) and **value-weighted coverage** (`C-389`), once
  real entry history exists;
- **the network split** (`S-25a`);
- **the bundle**, now 638 kB;
- **the two time-zone jobs as required checks** on `main` (Kyle's call, PR #59);
- **finer error boundaries and a web unit-test runner** (PR #58);
- **the import's plan-to-write race**, stated and not tested (PR #55);
- **the `dotnet-ef` tool** at 10.0.12 against EF Core 10.0.13 (PR #55).

---

## 7. Owed elsewhere

This project's sessions **cannot write to the planning repository** (Kyle, 2026-10-18; a
hook enforces it). These are owed there, by a planning-repository session:

- **The slice-02 gate, recorded once Kyle decides:** a new decision file if a third slice is
  wanted, as `D-041` was for slice 01's gate, or a dated note on `D-041` if STOP.
- **Kyle's 2026-10-18 ruling is recorded only in this repository.** Its amendment to `D-041`
  (planning PR #122) was closed unmerged on 2026-10-20, and Kyle chose not to revive it for
  now. Its three corrections to `D-041` went with it. The planning clone is still checked
  out on that PR's branch.
- **The project mirror** `claude/Portfolio_Current_State.md`, unsynced since PR #119.
- **`C-374` and `C-375`**: slice 01's executed spikes bear on both, and closing them is
  their own crossing (`D-041`, "What this does NOT do").
