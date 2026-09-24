# Slice 01 — the assessment

**Status: DRAFT. The facts are filled in; the decision is not.** Written 2026-09-24,
against `main` at `dec25df` (PR #13 merged). `slice-01.md` "Then stop" requires this
document before anything else happens, and says **the default at the gate is STOP**.

**Who wrote which part.** Sections 1–4 are facts assembled by the agent session that did
the build. Each one is checkable against a commit, a CI run or `LOG.md`, and they carry no
recommendation. Sections 5 and 6 are **Kyle's**: the gate asks what this taught *Kyle*,
which no one else can answer. The agent laid out candidates and evidence there and decided
nothing.

---

## 1. Definition of done

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | All three spikes run; claims restated at `EXECUTED` with command and date | **Met** | Spikes A `e44d991`, B `a845d93`, C `608a528`, D `a60f19c`. Each has a `RESULTS.md` and an `EXECUTED 2026-09-24` note in `slice-01.md` step 0 |
| 2 | `aspire run` brings the stack up; a balance entered in the browser appears in the chart | **Met** | Driven in headless Edge (step 4, `d0012cf`), and asserted by the E2E test (`f208875`) |
| 3 | `docker compose up` from the published artifact does the same, API on loopback, database port unpublished | **Met** | Step 7, `b59cd7b`. The Api answered on `127.0.0.1:8080` and was **refused on the LAN address**. Postgres was `expose`-only. The browser flow worked against the compose stack. Data survived `down`/`up` |
| 4 | Four tests, all green in CI on a pull request | **Met as the middle cut reads it; not as the item is worded** | Unit, Integration and Contract (plus Architecture) are green in CI from PR #11 onward. The fourth layer, the E2E, is green **locally** only, because the middle cut removed "the E2E in CI". DoD 6 was reworded for the middle cut; **DoD 4 never was**. See §4 |
| 5 | The architecture test passes | **Met** | 46 cases, green in CI. Every rule was seen failing when its invariant was broken |
| 6 | The committed OpenAPI document regenerates identically; the TypeScript client compiles | **Met** | The document generated on the Linux runner is byte-identical to the one committed from Windows (the first CI run on PR #11). The web job runs `tsc -b` |
| 7 | The chart has a tabular equivalent and is keyboard-reachable | **Met** | The E2E asserts both, and one axe call finds 0 WCAG 2.2 AA violations, light and dark |
| 8 | `LICENSE` present; no personal data anywhere in the repo | **Met** | `LICENSE` is present, with the non-affiliation line in `README.md`. `hygiene.py --all` reports clean. Its 14 advisories are spec prose naming public data providers as integration candidates |
| 9 | The eight live findings fixed **or** recorded as not-fixed with a reason | **Met: all ten fixed** | The findings table in `slice-01.md` lists **ten**, not eight (another count the text never reconciled). All ten are fixed, listed below |

**DoD 9, finding by finding:**

| Finding | Fixed in | How |
|---|---|---|
| **M-1** carry-forward within an active window | `9c79690`, `f208875` | `v_net_worth_daily` carries `accounts_carried`. The integration test checks carry, the 90-day expiry and `closed_on` |
| **M-3** environment selection off launch profiles | `e44d991` | Spike A: selection is configuration. The slice has one environment. Volume opt-in is configuration (`f208875`) |
| **M-4** same-origin, no configured origin | `a845d93`, `5332901` | A relative `/api` everywhere: a Vite proxy in run mode, the Api serving `wwwroot` when published |
| **M-5** a separate one-shot Migrator | `608a528`, `5332901`, `b59cd7b` | `WaitForCompletion`; in compose `service_completed_successfully`, and now `service_healthy` on Postgres |
| **M-9** the partial unique index and the write order | `9c79690`, `92841ae` | The pointer is inverted. `BalanceRecorder` runs UPDATE then INSERT inside the execution strategy. The reverse order is shown refused |
| **M-10** `account` identity split from `account_source` | `9c79690` | Migration 2, applied to a database with rows |
| **M-11** single currency, one CHECK | `9c79690` | A per-table literal CHECK. A second currency is shown refused |
| **M-24** semantic table, keyboard, contrast, tabular equivalent | `d0012cf`, `f208875` | axe 0 violations. The contrast ratios are recorded in `index.css` |
| **M-26** `LICENSE` and non-affiliation line | `2acda5f` | In the first commit |
| **M-27** `contents: read`, actions pinned by SHA | `9bb8f5a` | Both workflows, including `hygiene.yml`'s checkout, which had been on a tag. Dependabot keeps the pins current |

Supporting findings, briefly: S-11 (a Domain-only coverage floor, 70% against 72.2%), S-14
(corrected, and confirmed across operating systems), S-17 (triggers, not skipped), S-24,
S-25b (asserted on the app model), S-25c (an architecture rule), S-27, S-33 and S-34 are
all done. S-25a's network split was not taken; the flat network remains. S-34's resource
limits are an explicit DEFER in the slice.

---

## 2. What it cost

| | Hours | How it was measured |
|---|---|---|
| **Kyle** | **0.5** | `LOG.md`, the `Kyle h` column. Repository setup, 2026-09-18 |
| **Agent**, as logged | 9.7 | `LOG.md`, the `Agent h` column: 0.9 before this session (Spike D, the cutoff and the hours ruling, 2026-09-18 to 09-20), 8.8 for steps 0 to 7 |
| **Agent**, as measured | **≈ 5.1** for steps 0 to 7 | Commit timestamps: `1b783a4` 06:14 to `b59cd7b` 11:19 on 2026-09-24. That span includes time waiting on merges |

**The logged agent figure is overstated.** The per-step rows were the agent's own estimates,
written as it went, and they total 8.8 hours for a span that measures about 5. Take the
measured figure; the log column is what the agent believed at the time.

**Against the estimates:** the original 50–80 hours, and the ruled ~135 (band 105–170),
were estimates of **Kyle-evenings**. Neither applies to an agent session, and nothing here
says what the slice would have cost Kyle by hand.

**Against the 200-hour threshold:** it is **not tripped, and that tells us nothing.** The
2026-09-20 ruling (`5afc256`) counts only Kyle's hours toward it, and warned in advance
that "the assessment must not read an untripped threshold as evidence that the slice was
cheap". With 0.5 Kyle-hours, the threshold limb cannot decide anything. **The gate rests
entirely on the techniques limb, §5.**

---

## 3. The spikes' claims, and what else in `specs/` is suspect

| Spike | Claim | Result |
|---|---|---|
| A | `aspire run --launch-profile prd\|dev` runs two stacks at once | **Wrong, in more ways than the evidence predicted.** A second run from the same directory stops the first. **`--isolated` does not help**, despite its help text. `aspire run` has **no `--launch-profile` option**, and profiles cannot select an environment at all, so `plan.md`'s banner line "launch profiles still work for selection" was also wrong, and is struck |
| B | Service discovery gives the browser the API's address | **Wrong.** Client code never sees it. The template's proxy-then-`wwwroot` route answers M-4 **without** the experimental `PublishAsStaticWebsite` the slice named |
| C | A long-running Worker owns migrations and the Api waits | **Wrong as written; the fix works.** A one-shot Migrator with `WaitForCompletion` holds the Api and survives publish. The Migrator's own wait on Postgres degrades to `service_started` |
| D | The schema and the view | **Right, and incomplete.** Every assertion held on the EF migrations. But the view's date spine **stopped** after the last expiry, and the spike had deferred that edge to "the endpoint" (below) |

**Still suspect as a result:** `environments.md` in full (launch-profile selection, and
simultaneity by profile), `deployment.md`'s topology and simultaneity claims, and `plan.md`'s
Worker, launch-profile and health-ordering lines. All of these now carry corrected banners.
None was rewritten, because none has been commissioned.

### Defects the build found that no review did

Nine. Each one was found by running something, not by reading.

| Defect | Found by | Commit |
|---|---|---|
| The hygiene scanner **silently skipped** any file with non-cp1252 bytes on Windows, and still reported "clean" | A decode error during step 0 | `cab129f` |
| Renaming an Aspire resource renames the variables Vite receives, so the proxy **silently 502'd** | The first browser check | `5332901` |
| Npgsql's `MapEnum` emitted enum labels **alphabetically**, which put `'statement' > 'manual'` and reversed Constitution IV | Reading the generated migration | `9c79690` |
| A private design-time package split EF Core 10.0.11 from 10.0.12 (CS1705) | The Migrator build | `9c79690` |
| The snake_case convention applies to `SqlQuery` result types, so aliased columns returned a 500 | The first live call | `92841ae` |
| With `dot={false}`, **a lone value between unverified days draws nothing**, so the first balance after a gap was invisible | A screenshot, not an assertion | `d0012cf` |
| **The date spine stopped instead of saying UNVERIFIED** once every carried value expired (Constitution II) | The view integration test | `f208875` (migration 3) |
| **Nothing creates the database in compose.** The Migrator couldn't tell that from "server down" and reported "not reachable" | The first deploy | `b59cd7b` |
| Line endings: `autocrlf` would break a byte-level contract test on a Windows checkout | Reading git's warnings in step 3 | `f208875` |

---

## 4. Where the spec set was wrong, over-built, or useful

**Wrong: contradictions inside the live documents, each resolved by a ruling or a note.**

- **The endpoint list.** Four were listed, but the series endpoint was required by
  step 3's own preface and step 4, and **R2-B1's fix (`POST /accounts`) was never applied**.
  Kyle ruled six endpoints, 2026-09-24.
- **The architecture invariant's one exception named the wrong project.** It named the
  integration project, but Testcontainers took that project's role, and the E2E is what
  starts the app model. Kyle moved it, 2026-09-24.
- **Step 6 kept "E2E stays in the workflow"** after the middle cut removed it. The ruling
  won, and the step now carries a note.
- **DoD 4 still says "four tests… green in CI"**, and was never reworded for the middle cut
  as DoD 6 was.
- **DoD 9 says "eight live findings"; the table lists ten.**
- **S-14's advice against `.gitattributes`** was right about determinism and silent about
  checkout. One `eol=lf` line was needed for the reason S-14 had not considered.

**Over-built.** The nine frozen documents were consulted only through their banners, and
the banners did the work. The banners' `SOURCE@` predictions (the dashboard exposure,
`service_started`, the flat network) were **all confirmed by execution**, so the review
rounds' source reading was accurate where it was specific. What the build needed was
`slice-01.md`, the constitution, and the spikes.

**Useful.** The spikes were the highest-value hours, as the slice predicted: A and B
changed the app model before it was written. Spike D's assertion file was **re-run
unchanged against the EF migrations** (`run-on-migrations.sh`) and is still in use. The
claim markers (`EXECUTED`, `SOURCE@`, `INFERRED`) made it clear which statements to test
first. The two rulings made mid-build, on the endpoints and the E2E exception, were
needed because the document had drifted, not because it was vague.

---

## 5. Techniques — **Kyle to complete**

The gate's words: *"three techniques this taught that Kyle did not already have — named
specifically, with the commit that demonstrates each. 'Learned Aspire' does not count."*

**The fact that frames this section:** Kyle spent 0.5 hours, and an agent session did the
build. What follows are techniques the build **demonstrates**, each with its commit. Whether
any of them is now **Kyle's** — understood well enough to use without the agent — is the
question only Kyle can answer. **A technique that is merely present in the repository does
not satisfy this limb.**

Tick the ones that are genuinely yours; strike the rest. **Fewer than three means STOP.**

- [ ] **The AppHost is not the production runtime, and the generated compose file proves
      it.** `aspire run` created the database, ordered startup by health, and bound
      loopback. The published file did none of those until the app model said so.
      `b59cd7b`, and `f208875` for S-25b. *This is the slice's own worked example of a
      technique that counts.*
- [ ] **A partial unique index fixes the write order, and a retrying execution strategy
      must own the transaction.** The inverted pointer, UPDATE before INSERT, the reverse
      order shown refused, and the transaction run inside `CreateExecutionStrategy()`.
      `9c79690`, `92841ae`, `f208875`.
- [ ] **Carry-forward over a date spine, where "unverified" is a first-class state.**
      `LEFT JOIN LATERAL … ON true` rather than `CROSS JOIN`, NULL rather than zero, a stored
      cutoff, and a spine that runs to today, with each rule pinned by a test that fails
      without it. `9c79690`, `f208875`.
- [ ] **A hermetic end-to-end test of the whole app model.** `DistributedApplicationTestingBuilder`
      with .NET Playwright and axe, and the no-volume rule asserted on the model rather
      than assumed. `f208875`.
- [ ] **A contract that is generated, committed, and byte-stable across operating systems.**
      Build-time OpenAPI generation, its incremental cache, and a typed client generated
      from it. `92841ae`, `f208875`, and the first CI run on PR #11.
- [ ] **Verifying a claim before building on it.** Spikes as single-mechanism experiments,
      and results restated with strength markers. `e44d991`, `a845d93`, `608a528`.

Kyle's own, if different: ________________________________________________

---

## 6. The decision — **Kyle to complete**

`slice-01.md`: *"Two things are explicitly NOT reasons to continue: that the slice works,
and that the next slice is obvious."* Both are true now.

- [ ] **STOP.** The default.
- [ ] **A second slice**, which requires **a new decision file, written and dated**, in the
      planning repository. It is not a continuation of this one. If chosen: which
      technique is it for?

If a second slice is ever commissioned, these are its known starting obligations, each
already named with a trigger in `slice-01.md` or a PR:

- idempotency on `POST /balances` (the first connector slice);
- the pre-commit hook (the first real balance);
- resource limits via `Service.Deploy` (the first ingest batch);
- per-account-type cutoffs and value-weighted coverage (the first connector slice, C-389);
- the internal/front network split (S-25a);
- the 629 kB bundle;
- keeping `@types/node` on the runtime's major version (Dependabot PR #12).
