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
nothing here is a real account.

**The slice-01 gate is decided (2026-10-18): a second slice is wanted.** See
[`slice-01-assessment.md`](specs/001-portfolio-platform/slice-01-assessment.md). Per
[`slice-01.md`](specs/001-portfolio-platform/slice-01.md) "Then stop", no second slice starts
until its own decision file is written and dated in the planning repository.

**Slice 02 is built (`D-041`, 2026-10-18):** a file-import connector (CSV and OFX) over
invented balances. Files are linked to accounts by their own identifiers and imported all or
nothing, and the same file twice changes nothing. Every import is recorded, immutably. Cross-site
writes are refused by two tested layers. The dates rule (UTC data, the viewer's zone on the
page) is tested away from UTC. The definition is
[`slice-02.md`](specs/001-portfolio-platform/slice-02.md). **The slice-02 gate is decided
(2026-10-19): a third slice is wanted, for the security tier.** See
[`slice-02-assessment.md`](specs/001-portfolio-platform/slice-02-assessment.md).

**Slice 03 is commissioned (2026-10-21):** the security tier, over invented balances. That
means passkey login, sessions, CSRF, security headers and a strict CSP, an audit log of
logins, rate-limited login, secrets kept out of logs, and the pre-commit hook installed and
tested. After its design review (2026-10-24), the argon2id password fallback is deferred. The
commission and definition are
[`slice-03.md`](specs/001-portfolio-platform/slice-03.md); it opens with three spikes. Slices
are now commissioned and gated in this repository: the planning repository's `D-043`
(2026-10-20) decoupled the two projects, and this repository only reads it. No real balance
enters before this slice is built: Kyle's ruling of 2026-10-18, recorded in
[`slice-02.md`](specs/001-portfolio-platform/slice-02.md) ("What the slice is not").

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
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.<Environment> down
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.<Environment> up -d
```

`<project>` is the `aspire-compose-…` name shown by `docker ps`. `<Environment>` is
`Production` when you deploy from a terminal, and `Development` when you deploy from VS Code's
"Aspire: deploy to local Docker Compose", because that uses the AppHost's launch profile, which
sets the environment to Development. Use whichever `.env.*` file the deploy wrote. Every override the deployment
needs is in the app model ([`src/AppHost/AppHost.cs`](src/AppHost/AppHost.cs)), not in a
separate file:

- loopback binding;
- a Postgres healthcheck;
- the Migrator waiting for the healthcheck;
- no dashboard;
- restart policies;
- memory limits: 512M for the API and 256M for Postgres, about twice the measured peak of a
  10,000-row import (slice 02, step 7).

So the generated file is complete on its own. `aspire-output/` is git-ignored as a
directory, because the `.env.<Environment>` file there holds the generated database password. Never commit
it. [`deploy/.env.example`](deploy/.env.example) lists the keys.

**Signing in (slice 03).** Every page and endpoint but the health check needs a session.

- **The first run asks for a setup code.** Open the app as **`http://localhost`**, not
  `127.0.0.1`: passkeys only work at `localhost`.
  - Deployed, the code is `SETUP_CODE` in `aspire-output/.env.<Environment>`.
  - Under `aspire run`, it is the `setup-code` parameter on the dashboard.

  Enter the code, then a user name, then create a passkey. A code works for **one**
  enrolment, ever.
- **Sessions last 7 days after you last use the app.** They survive a redeploy and
  `docker compose … restart api`, because the key ring that signs them is on its own volume,
  `portfolio-dev-keys`.
- **`docker compose … down -v` destroys both volumes.** You lose the data, the key ring and
  the owner. Every session ends, and enrolment opens again, for a code not used before.
- **The compose project's name** comes from `aspire deploy`: `aspire-compose-` and a hash of
  the AppHost's path. Move the repository and the next deploy is a new project, with new
  volumes and an empty database. A `docker compose -p <name>` run by hand uses the name you
  give it.

**Recovering, from the host.** Both commands run in a throwaway container, need no browser,
and are written to the audit log:

```sh
# A session you do not trust (a copied cookie): every browser must sign in again.
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.<Environment> run --rm api revoke-sessions
# A lost passkey: removes the owner and their passkeys, and opens enrolment again.
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.<Environment> run --rm api reset-owner
```

After `reset-owner`, enrolment needs a setup code that has never been used. **A redeploy does
not give you one.** `aspire deploy` keeps the code it generated in its deployment state,
`~/.aspire/deployments/<hash>/`, and writes the same one every time. An explicit
`Parameters__setup-code` does not override it either. Instead, put a new value in `SETUP_CODE`
in `aspire-output/.env.<Environment>`: 29 lower-case letters, without `i`, `l` or `o`. Then
recreate the API:

```sh
docker compose -p <project> -f aspire-output/docker-compose.yaml --env-file aspire-output/.env.<Environment> up -d api
```

The next `aspire deploy` writes the old, spent code back. That is harmless, since a spent code
cannot enrol anyone, but enrol before you redeploy. **Never use `aspire deploy --clear-cache`
on a stack with data.** It regenerates every parameter, the database password among them, and
the existing volume only accepts the password it was created with.

**Logs** are in `docker logs <api container>`. They are kept by Docker's `local` log driver
(about 100 MB per container), and are lost when the container is recreated. The request lines
that would carry a URL's query string are switched off in code, so a secret pasted into a URL
is not written down.

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
