# Constitution

These are the non-negotiable principles of the portfolio platform. Every other
document in this specification set assumes them. A design that violates one of
these is wrong even if it satisfies every functional requirement.

Most of them are not invented here. They are carried forward from a planning
system that has been operated under them, and that learned several of them the
expensive way.

---

## I. No recalled, estimated or model-generated figures

Every number the system stores originates in a source it can name: an API
response, an MCP tool result, a file import, or an explicit human entry. A
figure with no source does not get stored.

*Consequence:* every externally-sourced row carries its origin in the schema,
not in a comment. There is no "roughly" column.

## II. `null` means UNVERIFIED, never zero

A missing value is missing. It is not zero, not the last known value, and not a
sensible default.

*Consequence:* a projection given a null input **refuses to run** and names the
input it lacks. A refusal is a successful outcome of a validation step, not an
error to be smoothed over. This is the single rule most likely to be eroded
under delivery pressure, because a default always looks more convenient than a
refusal - and a plan built on a defaulted number is worse than no plan.

## III. Closed accounts stop at their last sync

When an account stops reporting, its series ends. The system never
forward-fills, never carries the last balance forward, and never interpolates
across a gap it did not observe.

*Consequence:* charts and aggregates must be able to render a series that
simply stops, and say why.

## IV. Provenance strength is recorded, not assumed

Sources are not equally strong. A statement or export is stronger than an API
balance, which is stronger than a scraped page, which is stronger than a hand
entry. The schema distinguishes them and the UI shows the distinction.

*Consequence:* a weaker source still beats a null. Recording the weakness is
what makes it acceptable to use.

## V. Reported-but-unverified money is conditional, and says so

Money that has been *reported* as moving but not yet *observed* in a balance is
a distinct state, not a completed transfer. Any aggregate that includes it is
conditional and is labelled conditional wherever it is displayed.

*Consequence:* a pending-transfer state is first-class in the data model, and
totals carry a flag rather than silently absorbing it.

## VI. Determinism and reproducibility

Given the same inputs, a projection produces the same output. Every projection
run records the hash of every input it consumed and the version of the engine
that produced it.

*Consequence:* a stored result whose inputs have since changed is detectably
stale, and can be shown as stale rather than quietly believed.

## VII. Secrets never rest in plaintext, and never enter the repository

Credentials are encrypted at rest, are never returned by any API response, are
never written into an image layer, a committed environment file, or a frontend
bundle, and are never logged.

*Consequence:* a single secret-store interface with one implementation per
environment, and an audit record for every use.

## VIII. Local-first

The system runs completely on a single machine with no cloud dependency for
storage or compute. Nothing binds to a non-loopback interface by default.
Cloud deployment is an option, never a requirement.

*Consequence:* every feature must have a local answer. A feature that only
works when hosted is not done.

## IX. The repository contains no personal data, ever

Not in seed data, not in fixtures, not in test snapshots, not in documentation,
not in a screenshot, not in a commit message. Real data exists only in a runtime
volume that the repository never sees.

*Consequence:* this is enforced by a CI job, not by good intentions. The
repository is public; a single careless fixture is permanent.

## X. DEV can never reach PRD data

The development and production environments have separate volumes, separate
databases and separate credentials. Destructive operations - seed, reset, wipe -
assert they are running against DEV and refuse otherwise.

*Consequence:* the refusal has its own test. Of everything in this document,
this is the property whose failure is least recoverable: the production
environment holds somebody's actual finances.

## XI. The API is the only way in

The frontend holds no business logic and no database access. Every capability
the UI offers is an API call that any other client could make.

*Consequence:* the API contract is the product boundary. If something is only
possible through the UI, it is in the wrong place.

---

## Amendment

These principles change by explicit decision, recorded with a date and a
rationale, superseding rather than editing the text above. A principle that is
quietly weakened to let a feature land has not been amended; it has been broken.

---

### Amendment 1 — 2026-09-18 — two principles added

**Decision:** `D-029` (external claim provenance). **Instance:** `C-374`.
**Rationale:** a fourteen-seat review found that three core mechanisms in `plan.md`
and `environments.md` were asserted from reasoning about .NET Aspire rather than
from running it, and all three were wrong. Thirteen of the fourteen reviewers
missed them, because the documents are internally consistent and read correctly.
Only the seat that opened the vendor's source at a pinned commit found them.

The principles above govern figures the *application* stores. Nothing governed
claims the *specification* makes about the platform it is built on — and a wrong
claim of that kind survives every review, because review reads documents.

#### XII. Every claim about an external system carries the strength of its evidence

A statement about how an API, CLI, framework or build tool behaves is marked
`EXECUTED` (this session ran it), `SOURCE@<ref>` (read from the implementation at
a pinned reference), `DOCS@<date>` (read from published vendor documentation), or
`INFERRED` (reasoned, not observed).

*Consequence:* this is Principle IV applied to the platform instead of to the
data. A weaker source still beats a null — what makes it usable is that the
weakness travels with it, on the page, next to the claim.

#### XIII. A mechanism the design rests on may not stay `INFERRED`

Where the design breaks or must be redesigned if a mechanism does not behave as
described, `INFERRED` is not a resting state. It is raised to `EXECUTED` by a
**spike** — the smallest runnable thing that exercises that mechanism and nothing
else — and until then it is named in the document as a **gate on the step that
depends on it**, not as a caveat in a preface.

*Consequence:* the test is not *is this important* but **would this design have to
change if the claim were false**. Ordinary facts — a package name, a default, a
flag in help text — stay at whatever strength they were read at, marked, and
nothing is owed. And the platform skeleton is built first, because the cheapest
possible instance of the platform is worth more than another page of reasoning
about it.

#### What Amendment 1 does not change

Principle I is **not** weakened. `D-029` covers claims about external systems;
Principle I covers figures the application stores, and a figure still may not be
stored without a source it can name.

---

### Amendment 2 — 2026-09-18 — Principle I reconciled with the constant states

**Decision:** `D-029`, carrying the review's `M-20`. **Rationale:** `data-model.md`
defines a third legal state for `app.constant` — a **working assumption**: a real
value, openly banner-marked as unsourced, gated on an action dated before any
irreversible step. Principle I as written forbids it outright (*"A figure with no
source does not get stored… There is no 'roughly' column"*), and the review found
the contradiction had been introduced without using this section.

The mechanism is right and the absolute phrasing was wrong: a `null` cannot be
planned against, and the alternative to a weak value is not a strong value but a
gap that goes invisible. Principle I is therefore amended, by this section rather
than by editing it, to admit exactly one exception:

> A figure with no source does not get stored **unless it is carried as an openly
> labelled working assumption**: a non-null value, an explicit assumption flag, a
> source field that opens by declaring itself unsourced, and a named gating action
> that is open and dated before the irreversible step. **No terminal action may
> depend on such a value.** Reaching for this twice in one change is the erosion
> signal, not a precedent.

The banner, the flag and the gate are what keep it from being a "roughly" column.
Principle II is untouched: a `null` still means unverified and still refuses.
