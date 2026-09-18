# Spec review process

How a specification in this tree is written and reviewed. Two rules, each from a
failure this project actually had. Ruling: `D-029`. Instance: `C-374`.

---

## 1. Claim provenance — mark what you did not run

### The failure

The `specs/` tree asserted three .NET Aspire mechanisms from reasoning about the
platform rather than from exercising it. All three were wrong:

| Claim | Reality |
|---|---|
| Two stacks run simultaneously on launch profiles | The CLI stops any running instance first, with no opt-out |
| Service discovery supplies the API address to the SPA | It reaches the dev-server process, never the browser bundle |
| The Worker owns migrations and the API waits for them | The wait primitive requires the dependency to terminate; the Worker never does |

**Thirteen of fourteen specialist reviewers missed all three.** Not through
carelessness — the documents are internally consistent and read correctly. They are
simply not derivable from the documents. Only the seat that opened the vendor's
source at a pinned commit found them.

That is what makes this different from every other defect class. A wrong claim
about a file is caught the moment someone opens the file. A wrong claim about a
platform survives review, and is caught at build time — after the design has
committed to it.

### The rule

Every statement about how an external system behaves carries a marker:

| Marker | Means | Example |
|---|---|---|
| `EXECUTED` | ran it here; the command is named | `EXECUTED 2026-09-18: aspire run --launch-profile dev` |
| `SOURCE@<ref>` | read from the implementation at a pinned reference | `SOURCE@microsoft/aspire@b477bdd RunCommand.cs#L338` |
| `DOCS@<date>` | read from published vendor documentation | `DOCS@2026-09-18 https://…` |
| `INFERRED` | reasoned, not observed | `INFERRED — no SDK available` |

Write it inline, next to the claim, not in a preface. A reader scanning one
paragraph must be able to tell what was checked.

```
- **App model:** a Postgres resource with a persistent named volume.
  [SOURCE@microsoft/aspire@b477bdd — WithDataVolume(name) exists and takes
  an explicit name; PostgresBuilderExtensions.cs#L456]
```

**`DOCS@` means the vendor's published documentation, never its support bot.**
A vendor chatbot is the weakest source in the stack and may be used to find a
question, never to answer one — see `C-229`, which this rule does not weaken.

### Load-bearing claims

A claim is **load-bearing** when the design breaks or must be redesigned if it is
false. Not *is this important* — **would this design have to change**.

A load-bearing claim may not rest at `INFERRED`. It is raised to `EXECUTED` by a
**spike**: the smallest runnable thing that exercises that mechanism and nothing
else. Not a prototype, not a vertical slice — one mechanism.

Until the spike runs, the claim is named in the document as a **gate on the step
that depends on it**:

```
> **GATE — step 1.** Two side-by-side stacks is INFERRED, not executed.
> Spike: two AppHost directories, `aspire run` in each, assert both stay up.
> If it fails, environments.md needs rewriting before anything is built on it.
```

A gate belongs where the work is planned, not only in a preface. A preface is read
once; a gate is read by whoever picks up that step.

### What is not owed a spike

Ordinary facts — a package name, a default value, a flag that appears in help text.
Mark them at the strength they were read at and move on. Spiking everything would
make the rule unusable, which is how rules get dropped.

### Sequencing

Where a plan has a step whose only purpose is to stand the platform up, that step
goes **first**, and the load-bearing mechanisms are spiked inside it. All three
`C-374` defects would have surfaced in one afternoon of step 1.

This is not a scope rule. It does not say build more. It says the cheapest possible
instance of the platform is worth more than another page of reasoning about it.

### Writing a spec with no environment

Entirely legitimate — the session that wrote this tree had no .NET SDK and could not
have spiked anything. What it may not do is present an unverified mechanism as
settled. Mark it `INFERRED`, gate the dependent step, and hand over a document whose
own text says which parts are load-bearing and unchecked.

---

## 2. Disposition — a finding is not done until it has one

### The failure

The fourteen-seat review produced 89 findings. Roughly half of the defective
surface sat in work a first release should not contain — the second data connector
and its webhooks, the cloud target, the release and deploy pipelines, saved reports,
four of six projection models. Cutting that surface removes those defects with it,
at no cost.

Nothing in the output said so. Specialists asked to find problems find problems;
every one of them was correct within its own lens, and the aggregate read as a
to-do list of 89 items against a project with an estimated 700–1,000 hours of
specified scope and one part-time developer.

**A flat list is not neutral. It is an implicit instruction to fix everything.**

### The rule

Before a review is handed over, every finding carries one of four dispositions:

| | Means | Requires |
|---|---|---|
| **FIX** | in scope, will be corrected | the release it lands in |
| **CUT** | dissolves because the surface is being removed | what is being cut, and where that is recorded |
| **ACCEPT** | real, and we are living with it | the reason, in writing |
| **DEFER** | not now | the **trigger** that reopens it — a date, an event, a threshold |

A `DEFER` with no trigger is a `FIX` nobody has scheduled. That is the disposition
most likely to be abused, so the trigger is what makes it legitimate.

A finding with no disposition is **not a finding yet** — it is an observation.

### The scope decision comes first

Dispositions cannot be assigned without one. So the review's first output is the
scope question, and the findings are read against it. Where the review itself
produces the scope evidence — as this one did — that section leads the document and
says so.

### Severity and disposition are different axes

Severity is *how bad is this if it ships*. Disposition is *what are we doing about
it*. A **Must Fix** finding can legitimately be `CUT`: the defect is severe and the
surface is not being built. Collapsing the two is what turns a review into a
backlog.

---

## Applying this to an existing document

`committee-findings.md` carries a disposition column, assigned against the scope
recommendation in its own `S-0`. The spec documents do not yet carry claim-provenance
markers — `C-374` stays OPEN until they do, and the three defective sections carry
their corrections.

The order that matters: **mark the claims, then spike the load-bearing ones, then
fix the findings whose disposition is FIX.** Fixing a section whose platform claim
is still unverified just moves the same error to a different paragraph.
