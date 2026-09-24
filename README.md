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

The full set lives in [`.specify/memory/constitution.md`](.specify/memory/constitution.md), where spec-kit reads it.

## Status

**Slice 01 is built:** manual balance entry through to a net-worth chart, through every
layer once. That means the Aspire app model, EF Core migrations, the API with a committed
OpenAPI document and a generated TypeScript client, and the React page. It has one test per
layer plus an architecture test, CI, and a Docker Compose deployment. **Invented data only:**
nothing here is a real account. The slice ends at a written assessment and a full stop
([`slice-01.md`](specs/001-portfolio-platform/slice-01.md), "Then stop").

## Running it

Needs the .NET 10 SDK, Docker, Node 24, and the Aspire CLI (`dotnet tool install -g Aspire.Cli`).

```sh
aspire run                     # the whole stack; the dashboard link is printed
dotnet test --solution Portfolio.slnx   # every layer, including the E2E (about a minute)
```

`aspire run` keeps its data in the Docker volume `portfolio-dev-pgdata`. Tests never mount
it: the integration tests and the E2E each start a throwaway Postgres.

**Deploying locally with Docker Compose.** One command builds the images, writes the compose
file, and starts it:

```sh
aspire deploy -o aspire-output
```

The API is then at <http://localhost:8080>, on **loopback only**. Postgres is not published
at all. To stop the stack and start it again later from the same generated files, use plain
Compose:

```sh
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.Production down
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.Production up -d
```

`<project>` is the `aspire-compose-…` name shown by `docker ps`. Every override the deployment
needs is in the app model ([`src/AppHost/AppHost.cs`](src/AppHost/AppHost.cs)), not in a
separate file:

- loopback binding;
- a Postgres healthcheck;
- the Migrator waiting for the healthcheck;
- no dashboard;
- restart policies.

So the generated file is complete on its own. `aspire-output/` is git-ignored as a
directory, because `.env.Production` holds the generated database password. Never commit
it. [`deploy/.env.example`](deploy/.env.example) lists the keys.

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

In [`specs/`](specs/). The constitution, the system specification, the
architecture plan, the data model, the threat model and the first slice's build
plan were written and adversarially reviewed **before any code existed** — three
rounds, the last of which reviewed the reviews and found a prescription that
silently cancelled itself.

Start with [`.specify/memory/constitution.md`](.specify/memory/constitution.md), then
[`specs/001-portfolio-platform/slice-01.md`](specs/001-portfolio-platform/slice-01.md),
which is the only document describing work actually in progress. The rest are a
frozen reference and each carries a banner naming the claims the reviews
contradicted.

## Licence

MIT. See `LICENSE`.
