# fiducial-portfolio-ops

A self-hosted personal finance platform. .NET Aspire, PostgreSQL, React.

**This is not a product and not advice.** It is not affiliated with, endorsed by
or connected to any financial institution, and it provides no financial,
investment, tax or legal advice. *Fiducial* is a measurement term — a fiducial
marker is a known reference point that everything else is measured against — and
it is not a claim to fiduciary status of any kind.

---

## What makes it different

Most personal-finance software optimises for looking complete. This one optimises
for **never lying to you about where a number came from**. Five rules, enforced in
the schema and the tests rather than the documentation:

1. **Every stored number names its source.** An API response, a file import, or an
   explicit human entry. A figure with no source does not get stored.
2. **`null` means UNVERIFIED, never zero.** A projection given a missing input
   *refuses to run* and names what it lacks. A refusal is a successful outcome of
   validation, not an error to smooth over.
3. **Closed accounts stop at their last sync.** No forward-filling, no carrying the
   last balance, no interpolating across a gap nobody observed. A chart has to be
   able to render a series that simply stops — and say why.
4. **Provenance strength is recorded, not assumed.** A statement beats an API
   balance beats a hand entry, and the UI shows which one you are looking at. A
   weak source still beats a null; recording the weakness is what makes it usable.
5. **The repository contains no personal data, ever.** Not in seeds, not in
   fixtures, not in a screenshot. Enforced by a CI job and a pre-commit hook, not
   by good intentions.

The full set lives in `specs/constitution.md` (see *Where the specification lives*).

## Status

**Pre-implementation.** This repository currently contains its licence, its data-
hygiene controls, and this README. The first slice — manual balance entry through
to a net-worth chart, through every layer once — is specified and costed but not
yet built.

## Before you commit anything

```sh
./scripts/install-hooks.sh
```

This sets `core.hooksPath` so the hygiene scanner runs before every commit.
**It is local, untracked config — `git clone` does not set it, so run it after
every fresh clone.** The same scanner runs in CI on every push and every branch,
but that is *detection*: once a commit reaches a public repository, rewriting
history does not remove it. GitHub keeps the objects reachable by SHA, in clones
and forks, and through any pull request that references them. The hook is the last
point at which prevention is still possible.

To audit the whole tree rather than a diff:

```sh
python3 scripts/hygiene.py --all
```

False positives are expected. Add the path to `ALLOWLIST` in `scripts/hygiene.py`
with a reason. Never wildcard it.

## Where the specification lives

The constitution, the system specification, the architecture plan, the data model,
the threat model and the first slice's build plan were written and adversarially
reviewed before any code existed — three review rounds, including one that reviewed
the reviews. They live in the planning repository that commissioned this one and
will be ported here.

## Licence

MIT. See `LICENSE`.
