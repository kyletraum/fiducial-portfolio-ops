# Quickstart — a reusable kit extracted from this repository

## The commission

**Status: DRAFT, 2026-11-15. Not commissioned.** Nothing in this document is decided.
Every question for Kyle is marked **OPEN** and listed under "Open questions". No option
is chosen here. Where a recommendation is given, it is labelled as one, and there is at
most one per question.

**Recorded here, in this repository, 2026-11-15**, as slices 03 and 04 were (planning
`D-043`, 2026-10-20).

**Asked for by Kyle, 2026-11-15,** after slice 04's gate had decided STOP
([`slice-04-assessment.md`](../001-portfolio-platform/slice-04-assessment.md) §6). The
request was for a commission draft and an inventory, with nothing extracted or built.
This is not a fifth slice of the platform, and it adds no feature to it.

**Rulings so far: none.** When Kyle rules, each ruling goes in this table with the
options it was chosen from, as `slice-04.md` records its four.

| Ruling | Kyle's choice | The options it was chosen from |
|---|---|---|
| — | — | See "Open questions", OPEN-1 to OPEN-14 |

**Not reviewed.** No seat has read this draft. Whether it gets a review is OPEN-13.

**What it is for.** Kyle wants to start future projects with Claude from what this
repository already worked out, without re-deriving it. Two halves:

1. **The process kit.** Specs and a constitution written before code, with adversarial
   review. A commission per slice, and a gate whose default is STOP. A build log with
   separate columns for Kyle's hours and agent hours. BUILT notes with mutations seen
   failing and claims marked `EXECUTED` or argued. An assessment where the agent writes
   the facts and Kyle makes the decisions.
2. **The starting code.** The Aspire AppHost, Migrator, API and React layout. The
   committed OpenAPI document and generated TypeScript client. CI, the hygiene scanner
   and the pre-commit hook. The security tier. The split database logins and the catalog
   snapshot. The test fixtures.

**Method:** `D-029`, as every slice: claim markers, and an `INFERRED` claim the design
rests on is a gate. **Built by:** not stated in the request. Slices 03 and 04 were built
by the agent under the gate. **Estimate:** none. There is no measured basis for one:
`LOG.md` holds hours for building features, and none for extracting them. A review seat
could supply one, as SCO did for slice 04 (`SCO-15`). **Ends in:** a written assessment
and a full stop.

**Built against:** [`inventory.md`](inventory.md), written the same day against `main`
at `b48b7ac`. Its counts: 7 GENERAL, 7 GENERAL-OPTIONAL, 11 FINANCE, 25 ENTANGLED, of 50
pieces.

**Claims about external systems in this draft are `INFERRED`.** What a GitHub template
repository does and what a `dotnet new` template can do were written from memory. No
vendor documentation was read and nothing was run in this session. The ones the design
would rest on are named under "Gates".

---

## What the kit is

**Half 1, the process kit.** Blank forms and short rules, taken from the instances here:

| Piece | Taken from | Inventory |
|---|---|---|
| A constitution skeleton: the amendment rule, claim strength, the spike | `.specify/memory/constitution.md` | P-1, P-2 |
| The review process, and a seat brief, a themes table and a rulings table | `specs/review/process.md`; the seat files | P-4, P-6 |
| A commission form with the house headings | `slice-01.md` … `slice-04.md` | P-7 |
| The BUILT-note rule, with one worked example | `slice-04.md` step 1a | P-8 |
| The gate text, with its override limb as a blank | `slice-04.md` "The gate" | P-9 |
| An assessment form: facts by the agent, two sections for Kyle | `slice-04-assessment.md` | P-10 |
| A build log with two hour columns and the units note | `LOG.md` | P-11 |
| The "how to read these documents" page | `specs/README.md` | P-14 |

**Half 2, the starting code.** A solution that builds, runs under `aspire run`, passes
its own tests and deploys to local Docker Compose, with no finance in it:

| Piece | Inventory | A new project gets it |
|---|---|---|
| Solution, tool pins, the app model, the Migrator, service defaults, the API host, the web shell | C-1, C-3, C-4, C-5, C-6, C-21 | Always |
| The contract chain: committed OpenAPI, generated client, byte-compare test | C-7 | Always |
| CI, the hygiene scanner, the hook and its test | C-23, C-24, C-25 | Always |
| Security headers and the cross-site write refusal | C-17 | Always |
| The Testcontainers fixture, the architecture tests, the E2E harness with axe, the published-stack checks | C-26, C-29, C-30, C-31 | Always |
| Passkeys, sessions, CSRF, the rate limiter, the audit log, and their tests | C-10, C-13, C-14, C-27, C-28 | By opting in |
| Split database logins and the catalog snapshot | C-19, C-20 | By opting in |
| Bounded SQL reporting | C-15 | By opting in (whether it ships at all is OPEN-11) |

"Always" and "by opting in" follow the categories in `inventory.md`. Which optional
tiers the first version carries is OPEN-11.

**The work is mostly separation.** 25 of the 50 pieces are ENTANGLED: 8 of the 17
process pieces and 17 of the 33 code pieces. Each row of `inventory.md` says what would
have to be separated. The process forms (P-6 to P-11) exist only as filled-in instances.

**A sample, not a blank.** The security tests act on finance routes (C-28), and the
published-stack check drives the account and balance forms (C-31). With the finance
removed they have nothing to act on. So the starting code needs one neutral sample
resource, through every layer once, for the tests to use. That is new code, not
extraction.

---

## What the kit is not

- **Not a fifth slice.** It adds nothing to the platform, and it does not admit the
  first real balance. Both keep their own commissions (`slice-04-assessment.md` §6).
- **No finance.** The balance model, file import, the migrations, the finance pages and
  their tests stay here (C-8, C-9, C-10a, C-11, C-12, C-13a, C-16a). So do Principles
  I–VI as written (P-3), unless OPEN-4 rules otherwise.
- **No history.** The kit starts a new migration history and a new git history. This
  repository's review records (P-5) and frozen spec set (P-13) stay here, as the kit's
  evidence.
- **No new GitHub repository until Kyle creates one.** That is Kyle's call, after this
  commission is decided (Kyle, 2026-11-15).
- **No change to this repository's behaviour.** Separation done here, if any, must leave
  every test green (see "Drift", option D-3).
- **No fix to the six inconsistencies** `inventory.md` lists under "Already inconsistent".
  Whether they are fixed first is OPEN-12.
- **No Azure.** `deployment.md` describes Azure Container Apps; nothing built deploys
  there, and the kit carries only what was built.

---

## The delivery form — options, none chosen (OPEN-5)

The two halves need not take the same form. Three forms were named in the request.

| Form | What it is | For it | Against it |
|---|---|---|---|
| **A GitHub template repository** | A repository marked as a template. A new project is made from it with all its files and none of its history. [`INFERRED`] | The whole tree arrives at once: code, CI, hook, specs layout. Nothing to install. Works for any stack | No parameters: the name `Portfolio` and the optional tiers must be changed by hand or by a script in the template. No link back to the template afterwards. [`INFERRED`] Needs a new repository, which is Kyle's call |
| **A `dotnet new` template** | A template package with a `template.json`. `dotnet new <name> -n <Project>` writes the files, renaming as it goes, and can leave out content by option. [`INFERRED`] | The rename and the optional tiers become parameters. Versioned as a package. [`INFERRED`] | .NET only. The template itself must be built, tested and kept working: conditional content is a second thing to break. Files outside the solution (`.github/`, `.githooks/`, `web/`, `specs/`) need checking (gate GQ-2). Where the package is published or stored is undecided |
| **A plain docs kit** | A folder of Markdown forms and rules, copied into a project | Works for any stack and any tool. Nothing to maintain but text. It can live in this repository | It carries no code. Nothing runs, so nothing shows it is still right |

**How the forms fit the halves:**

| | Template repository | `dotnet new` template | Docs kit |
|---|---|---|---|
| Half 1, process | Fits: the forms are files in the tree | Fits poorly: the forms are not .NET | Fits |
| Half 2, code | Fits, without parameters | Fits, with parameters | Does not fit |

**Combinations that follow:**

- **F-1. One template repository for both halves.** One thing to copy. The process forms
  are tied to the .NET code.
- **F-2. A docs kit for the process, a `dotnet new` template for the code.** The process
  travels to any stack. Two things to maintain.
- **F-3. A docs kit for the process, a template repository for the code.** As F-2, with
  no parameters and no template engine.
- **F-4. A docs kit only, first.** Half 1 alone. Half 2 waits for its own commission.

OPEN-1, OPEN-2 and OPEN-3 bear on this choice, so they come first.

---

## Drift — options, none chosen (OPEN-6)

This repository will keep changing. Dependabot's update PRs alone account for eight
merges between 2026-10-25 and 2026-11-15 (`git log --author=dependabot`: #70, #71, #72,
#95, #100, #101, #111, #112). A kit copied once falls behind its source within weeks:
see `inventory.md` "Likely to go stale fast".

| Option | What it means | For it | Against it |
|---|---|---|---|
| **D-1. A snapshot, dated** | The kit records the commit it was taken from. No syncing. A new project updates its own pins | No upkeep. Honest about its age | Every fix made here after the snapshot is missed. Workarounds rot unseen |
| **D-2. The kit is the source** | General code moves to the kit. This repository takes changes from it | One place for each general piece | This repository must be refactored to consume it, against a gate that says STOP. There is no mechanism for it yet |
| **D-3. This repository is the source; the kit is generated** | Separation is done here, behind the tests. A script produces the kit from this tree, leaving the finance out | The kit is tested by this repository's CI before it is cut. Regenerating picks up every fix | The 25 ENTANGLED pieces must be separated here first, which changes this repository. The script is new code to keep |
| **D-4. A snapshot, with a refresh checklist** | D-1, plus a short list run by hand at each new project: pins, the inconsistencies, each workaround's cited version | Cheap. Makes the age visible at the moment it matters | Depends on someone running it |

**Whichever is chosen, the kit states its own age:** the source commit, the date, and
the pins table from `inventory.md`. That much is not optional under Principle XII: a
pinned version is a claim about an external system.

---

## What would prove it works

**A second, different project started from it.** Not a copy of this one. The proof is
that the kit carries a project nobody designed it for.

Proposed, for the review to tighten:

1. **The process half is shown** when the second project has, written from the kit's
   forms: a constitution, one commission, one BUILT note with a mutation seen failing,
   one log table with both columns, and one assessment whose decision sections are
   Kyle's.
2. **The code half is shown** when the second project, from a fresh clone, builds, runs
   under `aspire run`, passes the kit's own tests in CI, and deploys to local Compose,
   with its first feature built through every layer once.
3. **What had to be changed is recorded as it happens.** Every edit the second project
   made to a kit file, and every piece it deleted unused, goes in a list. That list is
   the assessment's main evidence.
4. **No finance arrives.** A search of the second project for the inventory's finance
   terms finds nothing, and the hygiene scanner passes.

What the second project is, and whether it is on the same stack, is OPEN-8. A second
.NET and Aspire project tests the code half. It tests the process half less, because a
form that fits two similar projects has not been shown to fit a different one.

---

## Step 0 — the spikes

Candidates only. Which run depends on OPEN-5.

| Spike | Question | Needed if |
|---|---|---|
| Q-A | Does a project made from a template repository get `.github/`, `.githooks/` with its mode bits, and `.gitattributes`, and is the hook still uninstalled until `install-hooks.sh` runs? | F-1 or F-3 |
| Q-B | Can one `dotnet new` template write files outside the solution folder (`.github/`, `.githooks/`, `web/`, `specs/`), rename `Portfolio` everywhere including file names, and leave out an optional tier cleanly? | F-2 |
| Q-C | With the finance removed and one sample resource added, does the whole test suite pass? Which tests had to change? | Any form with half 2 |
| Q-D | Does the committed OpenAPI document regenerate byte for byte after a rename? | Any form with half 2 |

---

## Definition of done — draft

Each item would name the check that shows it, as `slice-04.md`'s do.

1. Every GENERAL and chosen GENERAL-OPTIONAL piece of `inventory.md` is in the kit, or
   is listed as left out with a reason.
2. Every ENTANGLED piece taken has its separation done as its row describes, or the row
   is corrected.
3. The kit holds no finance term and no planning identifier that does not resolve
   (OPEN-10). **Seen failing:** a planted one is found.
4. The kit states its source commit, its date and its pins.
5. A second project exists and meets "What would prove it works".
6. The list of what the second project changed in the kit is written.
7. This repository's CI is green at the end, with every test it had at the start.

---

## Gates

`INFERRED` claims this draft rests on. Each is raised by a spike or by vendor
documentation read at a date, before the step that depends on it.

| Id | Claim | Raised by |
|---|---|---|
| GQ-1 | A repository made from a GitHub template carries every file, including dotfile directories, and no history | Q-A |
| GQ-2 | A `dotnet new` template can write outside the solution folder, rename in file names and contents, and exclude content by option | Q-B |
| GQ-3 | The security tests can act on a neutral sample route with no loss of what they show | Q-C |
| GQ-4 | The pieces marked "by grep" in `inventory.md` hold no finance logic | Reading each file before it is taken |

---

## Obligations this would carry

| From | Item | Here |
|---|---|---|
| `slice-04-assessment.md` §7 | `G4-1`, the full E2E at step 6's tree, the host commands' login argued not shown | **Carried, unchanged.** If the kit takes the logins (C-19), `G4-1` goes with them |
| `slice-04-assessment.md` §7 | Dependabot: a rule ignoring .NET majors, proposed and not written | **Carried.** The kit's Dependabot file would inherit the gap |
| slice 03, "Before the first real balance" | The backup, the wider audit, the dev page, `C-389` | **Not touched.** They bind the first real balance, not the kit |
| `inventory.md` "Already inconsistent" | Six items | OPEN-12 |

---

## The gate — slice 01's rule, with one limb open

`LOG.md` carries on with both columns, one line per session, written as it goes.

> **The default at the end of the quickstart is STOP.** A second version of the kit, a
> further project started from it, or any change to this repository to serve it,
> requires its own commission, **written and dated in this repository**, as this one is.
>
> **Explicitly NOT reasons to continue:** that the kit works, and that the next piece to
> extract is obvious.

**The override limb is not written, because it is Kyle's to set (OPEN-9).** Slices 01 to
04 could override the default only by naming three techniques the slice taught Kyle.
That limb asks what building something taught. An extraction builds nothing new, so the
limb as worded would never be met, and STOP would be certain before the work began.

**Stated, not smoothed:** if the agent builds, the hours limb cannot trip, as in slices
03 and 04.

The assessment would answer:

- What did it cost, in both columns?
- Which inventory rows were wrong once the separation was tried?
- What did the second project change in the kit, and what did it delete unused?
- Which `INFERRED` claims about the delivery form were wrong once run?
- Is anything further wanted?

## Then stop

---

## Open questions — all for Kyle

Each has its options. A recommendation, where given, is one line, labelled, and is the
agent's opinion, not a finding.

**OPEN-1. What kinds of projects?**
(a) Always .NET and Aspire. (b) Other stacks too.
*Fact:* half 2 is .NET 10, Aspire 13.6, PostgreSQL and React throughout. Half 1 names no
stack except in its examples (`specs/review/process.md`).
*Recommendation:* none. It turns on what Kyle expects to build.

**OPEN-2. Who uses it?**
(a) Kyle and Claude only. (b) Published for others.
*Fact:* this repository is public (Principle IX) and MIT-licensed. The kit's forms name
Kyle throughout ("Kyle to complete", "Kyle's hours"). Published, it needs a neutral
name for that role, a README for a stranger, and a view on support.
*Recommendation:* (a) first. Publishing later costs a rewrite of names; publishing first
costs that plus an audience before the kit has been used once.

**OPEN-3. Which half matters more?**
(a) The process. (b) The code. (c) Equal.
*Fact:* the process half is smaller to extract (8 forms, text only) and survives a
change of stack. The code half is 33 inventory pieces, 17 of them ENTANGLED, and goes
stale by its pins.
*Recommendation:* none. It decides the order of work and whether F-4 is enough.

**OPEN-4. Does "never lie about where data came from" go in the kit's constitution?**
(a) No: Principles I–V stay here as finance. (b) Yes, generalised: every stored value
names its source, `null` means unverified, source strength is recorded. (c) As an
optional section a project adopts if it stores facts about the world.
*Fact:* Principles XII and XIII are already that rule applied to claims about the
platform (Amendment 1: "this is Principle IV applied to the platform instead of to the
data"). Principles III and V speak of accounts and money and do not restate cleanly.
Amendments 2 and 3 exist because I and III, as first written, forbade something the
platform needed.
*Recommendation:* (c). It keeps the rule available without binding a project that has
no sourced data.

**OPEN-5. The delivery form for each half.**
F-1, F-2, F-3 or F-4, above.
*Recommendation:* none before OPEN-1 to OPEN-3 are ruled.

**OPEN-6. How drift is handled.**
D-1, D-2, D-3 or D-4, above.
*Recommendation:* D-4. It is the only option that needs no new mechanism and no change
to this repository.

**OPEN-7. Where the kit lives, and when.**
(a) A new repository, created by Kyle after this commission is decided. (b) A folder in
this repository. (c) Process forms here, code elsewhere.
*Fact:* a `dotnet new` template or a docs kit can live in a folder here. A GitHub
template repository cannot: it must be its own repository. [`INFERRED`]
*Recommendation:* none. It follows OPEN-5.

**OPEN-8. What is the second project, and who picks it?**
*Fact:* none is named. "Different" is not defined: a different domain on the same stack,
or a different stack.
*Recommendation:* a project Kyle already wants, not one invented to test the kit.

**OPEN-9. The gate's override limb.**
(a) No override: STOP is final and anything further is a new commission. (b) The second
project's change list is under a size Kyle sets. (c) Something else Kyle names.
*Recommendation:* (a). It matches four gates that each decided on Kyle's judgement, and
it needs no new measure.

**OPEN-10. Planning identifiers in the kit.**
The constitution, `process.md` and `LOG.md` cite `D-029`, `D-030` and `C-374`, which
resolve only in the planning repository (`specs/README.md` "Where these came from").
(a) Restate each rule in the kit with no identifier. (b) Keep the identifiers with a
note on where they resolve.
*Recommendation:* (a). A new project cannot open the planning repository.

**OPEN-11. Which optional tiers the first version carries.**
Passkeys and sessions (C-10, C-14); the audit log (C-13); split logins and the snapshot
(C-19, C-20); bounded SQL reporting (C-15). Each is: in, in by option, or left here.
*Fact:* the logins' grant matrix and the reporting views are the two most finance-bound
of these (`inventory.md` C-19, C-15).
*Recommendation:* none. It turns on what the second project needs.

**OPEN-12. The six inconsistencies found.**
(a) Fix them here first, in their own PR. (b) Carry them to the kit's refresh list.
(c) Leave them.
*Recommendation:* (a). They are small, and a kit cut from a tree with known errors
copies them.

**OPEN-13. Does this draft get a review, and by which seats?**
*Fact:* slices 03 and 04 were each reviewed twice before any build (`slice-04.md`
"Reviewed", "Reviewed again"). Slice 04's first review cost 2.0 agent hours and its
second 1.0 (`LOG.md`, 2026-11-06). No seat has read this draft.
*Recommendation:* one round, after OPEN-1 to OPEN-5 are ruled, so the seats review a
design and not a menu.

**OPEN-14. spec-kit.**
(a) The kit carries `.specify/` and the ten skills. (b) It carries neither. (c) It
carries only the constitution's location.
*Fact:* spec-kit is installed and not converted, and `/speckit-tasks` has nothing to
work from (`specs/README.md` "spec-kit"; P-12).
*Recommendation:* none.
