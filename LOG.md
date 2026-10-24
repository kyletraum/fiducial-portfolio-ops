# Build log

One line per work session. **Write it as you go, not at the gate.**

This exists because `D-030` §2's stop gate asks *"what did it actually cost?"*,
and a figure reconstructed months later is a recalled figure — which the planning
system that commissioned this project forbids outright as a source. Hours are the
one input the assessment needs that **cannot be recovered afterwards**. Everything
else (what shipped, what broke, what is green) is in git.

Keep it boring. Date, step, hours, one clause. The value is the column of numbers
at the bottom, not the prose.

**Two hour columns, and only one of them gates.** `Kyle h` is evenings — the unit
the 200-hour abandon threshold counts. `Agent h` is wall-clock of an agent
session. **Agent hours do NOT count toward the threshold** (Kyle, 2026-09-20).
They are still recorded, because a cost nobody wrote down cannot be argued about
later; they are simply not the number the gate reads.

| Date | Step | Kyle h | Agent h | What actually happened |
|---|---|---|---|---|
| 2026-09-18 | 0 · repo | 0.5 | — | Repo created, Constitution IX controls landed, specs ported |
| 2026-09-18 | D · spike | — | 0.2 | Spike D: schema, supersession order, `v_net_worth_daily`; all assertions green |
| 2026-09-20 | D · spike | — | 0.3 | Staleness-cutoff sensitivity sweep; 45d proposed with a derivation rather than a guess |
| 2026-09-20 | 2 · data | — | 0.3 | Cadence + batch-entry modelling moved the cutoff 45d -> 90d; `D-034` ruled it; `slice-01.md` step 2 now names it |
| 2026-09-20 | 0 · gate | — | 0.1 | Kyle ruled agent hours out of the threshold; `LOG.md` split into two columns and the threshold reworded |
| 2026-09-24 | 0 · tooling | — | 0.2 | spec-kit 0.14.3.dev0 installed; constitution moved to `.specify/memory/`; stranded ruling commit merged as #4 |
| 2026-09-24 | A · spike | — | 0.5 | Spike A on Aspire 13.5.4: same directory stops the first stack even with `--isolated`; two directories work; no `--launch-profile`, selection is configuration |
| 2026-09-24 | C · spike | — | 0.4 | Spike C: one-shot Migrator + `WaitForCompletion` holds the API, fails it on exit 1, publishes as `service_completed_successfully`; Migrator's wait on pg degrades to `service_started` |
| 2026-09-24 | B · spike | — | 0.6 | Spike B: relative `/api` works in run (Vite proxy) and deployed compose (frontend in server `wwwroot`); no API address in client env; step 0 complete |
| 2026-09-24 | 1 · skeleton | — | 0.8 | AppHost (pg -> Migrator -> Api -> web), ServiceDefaults project, architecture test (20, each rule seen failing); page reaches `/api/health`; OpenApi + 6 npm advisories cleared |
| 2026-09-24 | 1 · skeleton | — | 0.2 | Tried `AspireUseCliBundle=true` (works); stayed on NuGet for CI-pinned versions, ASPIRE010 suppressed with the reason |
| 2026-09-24 | 1 · skeleton | — | 0.3 | Packages current: ServiceDefaults 10.10 / OTel 1.19; xunit v3 on Microsoft.Testing.Platform (`global.json`), VSTest packages dropped |
| 2026-09-24 | 2 · data | — | 1.0 | Domain + Money, Infrastructure (EF Core 10 / Npgsql), migrations `Initial` + `AccountSource` (M2 on rows), Migrator waits then migrates; Spike D passes on the migrated schema; enum-order defect caught |
| 2026-09-24 | 3 · API | — | 1.2 | Six endpoints (POST /accounts per R2-B1, ruled today), money as decimal strings, restatement via execution strategy, OpenAPI committed + deterministic, openapi-fetch client, AllowedHosts + test; snake_case-in-SqlQuery bug found live |
| 2026-09-24 | 4 · web | — | 0.9 | Recharts chart (gaps, labelled bands, filterNull=false) + tabular equivalent, accounts table and two forms on TanStack Query; driven in headless Edge, axe 0 violations light/dark; invisible-isolated-point bug found |
| 2026-09-24 | 5 · tests | — | 1.3 | Integration (Testcontainers), Contract (byte compare), E2E (Aspire test builder + Playwright + axe), S-25b volume opt-in; spine-stops-early defect found -> migration 3; 77 tests green |
| 2026-09-24 | 6 · CI | — | 0.6 | ci.yml (build, unit + Domain floor 70%, architecture, integration; contract advisory; web), actions SHA-pinned incl. hygiene, Dependabot; E2E kept out per middle cut |
| 2026-09-24 | 7 · deploy | — | 0.8 | Four compose overrides in the app model; aspire deploy + plain compose: Api loopback-only (LAN refused), pg unpublished, browser flow, data persists; missing-database defect found in Migrator |
| 2026-09-24 | gate · assessment | — | 0.3 | DoD walked (all met; DoD 4 wording vs middle cut noted); assessment drafted with facts only - techniques and decision left to Kyle; agent-hour column found overstated vs commit timestamps |
| 2026-10-17 | maint · Aspire | — | 0.6 | Aspire 13.5.4 -> 13.6.1 incl. the AppHost SDK Dependabot cannot bump; MessagePack pinned past GHSA-qhrr-8q5h-9q3h; corrupt NuGet cache entries cleared; Docker Desktop restarted; 77/77 |
| 2026-10-18 | gate · decision | — | — | Kyle: a second slice is wanted; assessment final; it needs its own decision file in the planning repo |
| 2026-10-20 | maint · records | — | 0.2 | Planning PR #122 (D-041 Amendment 1) was closed unmerged; Kyle chose not to revive it. Every reference here to that amendment now cites Kyle's 2026-10-18 ruling, recorded in slice-02.md |
| 2026-10-20 | maint · tooling | — | 0.3 | Debug-console stack traces from Aspire's AuxiliaryBackchannelService diagnosed: the extension's `aspire ps --follow` drops its connection; benign. Upstream fixes merged, unreleased (aspire #20542, #20763); check at the next Aspire bump (note in AppHost.csproj). Nothing filed upstream, by Kyle's rule |
| **Total at the gate** | | **0.5** | **10.6** | Agent h as logged. They were the agent's own estimates and overstate: commit timestamps measure ~5.1 h for steps 0-7 (assessment, section 2) |

<!-- Add a row per session. Total at the gate; do not total as you go. Slice 01 totalled at its gate, 2026-10-18. -->

### Slice 02 (`D-041`)

| Date | Step | Kyle h | Agent h | What actually happened |
|---|---|---|---|---|
| 2026-10-18 | 0 · definition | — | 0.4 | `slice-02.md` written: file import (CSV/OFX), Spikes E-G; the view's same-day pick across sources found unspecified |
| 2026-10-18 | G · spike | — | 0.4 | Spike G on pg 18.3 + real migrations: unfixed view returns 100 or 200 by WRITE ORDER; Kyle ruled the tie-break; fixed view 7/7, seen failing unfixed |
| 2026-10-18 | E · spike | — | 0.6 | Spike E: own ~150-line reader reads OFX 1.0.2 SGML + 2.2 XML; OfxNet 1.8.1 rejected - comma-decimal and de-DE culture read balances 100x, DTDs processed, investment statements dropped silently |
| 2026-10-18 | maint · hygiene | — | 0.4 | CI hygiene now scans every commit in a range at its own content; a value added then removed passed before, fails now (shown on a throwaway repo); all of main's history passes |
| 2026-10-18 | F · spike | — | 0.7 | Spike F direct, via Vite and in a container: Kestrel 30,000,000 default; IFormFile needs an antiforgery decision; multipart spills to disk; over-limit via proxy is 502 not 413; upload becomes the raw body, text/csv or application/x-ofx only; step 0 complete |
| 2026-10-18 | 1 · boundary | — | 0.9 | `src/Ingestion` (Domain only): CSV + OFX 1.x/2.x parsers, all-or-nothing; MoneyText -> Domain; architecture rule seen failing; 109 unit tests incl. de-DE and DTASOF cases; a UTF-8 BOM check that could never match caught by its test |
| 2026-10-18 | 2 · data | — | 1.1 | Migration 4 (ingest_run immutable + CHECKs, ingest_run_id, source_as_of, DB timezone UTC, Spike G ORDER BY) on existing rows and reversed; ImportPipeline + one shared write rule; POST /balances 200 when unchanged; 43 integration tests, the tie-break and idempotency each seen failing |
| 2026-10-18 | 3 · API | — | 1.0 | Bind/list sources (409 on re-bind), raw-body POST /imports (415/413/422 before a run; limit held twice), GET /imports; Sec-Fetch-Site middleware; routing's Accepts metadata found to enforce 415 itself; client types binary as string, so step 4 uploads with fetch; 74 integration tests |
| 2026-10-18 | 4 · web | — | 0.9 | Identifier column + link form, upload form (size/type checked before sending, fetch with the type set by extension), history table in the viewer's zone; balance form tells unchanged from recorded; import E2E in Chromium + axe; it caught a toLocaleString option clash that blanked the page |
| 2026-10-18 | 4 · fix | — | 0.3 | SectionBoundary per section: a render error costs one section, not the page; E2E forces a real throw (history response replaced), seen failing without the boundary, axe 0 on the degraded page |
| 2026-10-18 | 5 · tests | — | 1.4 | DoD 11 in Chromium from attacker.localhost and another port: found Vite's default CORS let a cross-site page READ the API through the dev proxy (fixed, cors:false); DoD 10: Chicago at 23:30 either side of DST, UTC+14/-11 rendering, pinned server clock, CI TZ matrix with a self-check; E2E 14 |
| 2026-10-18 | 7 · deploy | — | 0.8 | Peaks measured on the published compose copy (10k import, re-import, restate, widest series): api 234.5, pg 126.3 MiB; limits 512M/256M via Service.Deploy; same load passes under them (50%/49%, no OOM); 1 MiB+1 is 413 in the published stack; loopback, unpublished pg, restart persistence hold |
| 2026-10-18 | gate · assessment | — | 0.5 | DoD walked, all 11 met (DoD 2 OFX in the browser and DoD 7 keyboard added; DoD 3 CSV+OFX on the published stack, via the API); assessment drafted with facts only - techniques and decision left to Kyle; LOG rows 1-7 found in slice 01's table and moved |
| 2026-10-19 | gate · decision | — | — | Kyle: seven techniques ticked ([c] confirmed a tick); a third slice is wanted, for the security tier; it needs its own decision file in the planning repo |

| **Total at the gate** | | **0** | **9.4** | Agent h as logged, and overstated again: merge timestamps measure ~5.2 h for the definition through step 7 (assessment, section 2) |

<!-- Slice 02 rows above. Total at its gate. Rows 1 to 7 (PRs #54-#60) were first written into slice 01's
     table by a script that anchored on text both tables contain ("step 0 complete"); moved here 2026-10-18. -->

### Slice 03 (commissioned here, 2026-10-21)

| Date | Step | Kyle h | Agent h | What actually happened |
|---|---|---|---|---|
| 2026-10-21 | 0 · commission | — | 0.4 | Planning `D-043` decoupled the projects, so slices are commissioned here; `slice-03.md` written with Kyle's 2026-10-19 rulings as its commission, Spikes H-K; docs pointing at a planning decision file updated |
| 2026-10-21 | H · spike | — | 0.9 | Identity's in-box passkeys end to end in Chromium (CDP virtual authenticator) on pg 18.3: register, sign in, /me 401->200; 127.0.0.1 refused by the browser; EF in-memory can't hold passkeys; argon2id: Geralt 108 ms (139 ms Linux @512 MB) at 64 MiB t=3, chosen over Sodium.Core and Konscious |
| 2026-10-24 | 0 · review | — | 1.5 | Committee review dispositioned: Kyle ruled D1-D17 (password fallback deferred; middle cut adopted) and confirmed three D7 consequences; 44 FIX / 6 CUT / 3 DEFER; `slice-03.md` and `security.md` rewritten to the rulings, Spike K cut. Agent hours estimated, no timer |
| 2026-10-24 | 0 · review | — | 0.8 | Round 2 on the rewrite, three seats in parallel (SRC, TST, SCO; ~21 min wall-clock): 60 findings -> 31; Kyle ruled R1-R4; all applied to `slice-03.md`/`security.md` (build order, pipeline order, persistent cookie, 10-min challenge cookie, Version3 override, SoftAuthenticator). Agent hours estimated |
| 2026-10-24 | I · spike | — | 0.4 | G-5: passkey registers and signs in through the Vite proxy with step 2's config (ServerDomain pinned, ValidateOrigin from config, __Host-/Strict/Secure, persistent 7-day session, 10-min challenge); a second Vite port and the API's own port refused by ValidateOrigin; a refused registration leaves an orphan passkey (C3-3 confirmed). Agent hours estimated |
| 2026-10-24 | J · spike | — | 0.5 | The slice's antiforgery filter refuses JSON/PUT/DELETE/raw upload without a token (403, nothing written); the framework's own UseAntiforgery + metadata lets all of them write (C3-1 observed, incl. a working same-site attack); token binds to the user (G-20 holds); G-19 false: Playwright sees only the status of an opaque response, no body/headers/cookie, so DoD 4 reads the API's record (G-21). Agent hours estimated |
| 2026-10-24 | 1 · data | — | 0.8 | Migration 5: Identity store at Version3 (round 2's SchemaVersion override found ineffective: Identity reads IdentityOptions only, so every host registers it), tables snake_cased, audit_event + used_setup_code append-only by trigger; SetupCode in Domain; AppHost generates the 29-char setup code (GT-W raised). Unit, Architecture, Integration and all 15 E2E cases green. Agent hours estimated |
| 2026-10-24 | 2 · auth API | — | 1.5 | Seven /api/v1/auth routes (enrol by setup code, passkey sign-in/out, state, session), registered not enforced; cookies __Host-/Strict/Secure, persistent 7d, 10-min challenge, stamp rotated on sign-out; SoftAuthenticator in C# (GT-3 raised); TestAuth header-only scheme + guards; four HTTP classes signed in. 24 auth cases; Integration 114, Architecture 60, E2E 15 green. Agent hours estimated |
| 2026-10-24 | 3 · CSRF, limiter, headers | — | 1.0 | Pipeline order written into Program.cs; SecurityHeaders in OnStarting on every status (CSP, nosniff, no-referrer, DENY, Permissions-Policy allowing passkeys; no HSTS); CsrfTokens filter + token route (sign-out guarded now, the group in step 4); sign-in limiter 10/min on the four ceremony routes, 429 + Retry-After; no CORS. Integration 133, Architecture 60, Unit, Contract green; E2E 15 (one Vite-startup flake, passed twice alone). Agent hours estimated |
| 2026-10-24 | 4 · web + switch | — | 2.0 | FallbackPolicy and the group CSRF filter on; DoD 2 allow-list test (seen failing); web: apiFetch (token, one retry, one signed-out state), AuthGate with set-up/sign-in/expiry dialog/sign-out, PasskeyButton; E2E fixture enrols once and keeps state + passkey, existing tests moved, 10 new browser tests. E2E 25, Integration 137, Architecture 60, Unit, Contract green. Agent hours estimated |

<!-- Slice 03 rows above. Total at its gate. -->

<!--
UNITS NOTE -- RAISED 2026-09-18, RULED 2026-09-20. The 200-hour abandon
threshold assumes Kyle-evenings. Agent rows are wall-clock of an agent session
and are NOT the same unit; summing one column across both would have given the
gate a number that means nothing. KYLE'S RULING: agent hours do not count
toward the threshold. Implemented as two columns rather than by dropping the
agent figure, so the cost still exists to be argued about even though it does
not gate.

WHAT THE RULING DOES, STATED PLAINLY BECAUSE IT IS NOT OBVIOUS: the 200-hour
threshold no longer bounds what the PROJECT costs. It bounds what KYLE spends.
If most of the work is done by agent sessions, the threshold cannot trip no
matter how long the slice runs, and the stop gate then rests entirely on its
other limb -- three named techniques with the commit that demonstrates each.
That is a coherent position (the threshold was always about whether Kyle's
evenings were well spent) and it is Kyle's call; it is written down here so the
assessment does not later mistake an untripped threshold for evidence of a
cheap project.
-->
