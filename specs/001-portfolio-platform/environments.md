# Environments

Two environments run **simultaneously** on one machine (FR-10.1):

| | PRD | DEV |
|---|---|---|
| Purpose | the user's real financial data | development, testing, demonstration |
| Data | real, from live connections | generated, from a seed |
| Volume | `portfolio-prd-data` | `portfolio-dev-data` |
| Database | `portfolio` | `portfolio_dev` |
| Ports | base range | base range + offset |
| Credentials | real, in the secret store | none required |
| Destructive operations | **refused** | permitted |
| Retention | permanent, backed up | disposable |

Selected by launch profile:

```sh
aspire run --launch-profile prd
aspire run --launch-profile dev
```

Same build artifact, same app model, different inputs (FR-10.5).

---

## Isolation

Separation is by **construction**, not configuration discipline:

1. **Different volumes.** Distinct named volumes. Neither container mounts the
   other's.
2. **Different databases**, with **different roles and passwords**. The DEV
   role has no privilege on the PRD database. Even a connection string
   accidentally carried from one to the other fails to authenticate.
3. **Different ports.** Both can run at once, so nothing tempts anyone to stop
   PRD to test something.
4. **Different secret scopes.** DEV cannot read PRD's secret store.

Points 1 and 2 are independent: point 1 alone is defeated by a copied
connection string; point 2 alone is defeated by a mounted volume. Both together
mean a single mistake is not enough.

---

## The DEV assertion

Every destructive operation - seed, reset, wipe, truncate - begins by asserting
it is running in DEV, and **throws otherwise** (FR-10.3, Constitution X):

```
if (environment is not DEV) throw;
```

The assertion reads the environment from injected configuration, verifies the
connection string names the DEV database, and **refuses if either check fails or
if either is absent**. An unset environment is not a permissive default - a
missing answer means refuse.

Three guards, each independently sufficient:

1. DEV-only routes are **not registered** in PRD - absent, not forbidden.
2. The service-level assertion above.
3. The DEV database role lacks `DROP` and `TRUNCATE` on the PRD database
   entirely.

This is more defence than most features warrant. It is warranted here: PRD holds
someone's actual financial history, restoring from backup after an accidental
wipe is at best a bad day, and the operation that would do it is one command
that looks identical in both environments.

**FR-13.4 requires a test proving the refusal fires in PRD.** A guard nobody has
watched fail is a guard nobody knows works.

---

## Visibility

The active environment appears (FR-10.4):

- in the UI header, permanently, with a distinct colour per environment;
- on the login screen, **before authentication** - a user must never enter a
  real credential into a DEV instance believing it is PRD;
- in every structured log record, as an `environment` field;
- on every `ingest_run` and `audit_event` row;
- in the page title, so a browser tab is unambiguous.

When data is generated, the banner says so explicitly (FR-11.4). A screenshot of
this application should never be ambiguous about whether the numbers are real -
both for the user's own sake and because screenshots of a public project end up
in issues and documentation.

---

## Persistence

| Operation | PRD | DEV |
|---|---|---|
| container restart | data survives | survives |
| image upgrade | survives | survives |
| `compose down` | survives | survives |
| `compose down -v` | **destroyed** | destroyed |
| `POST /dev/reset` | refused | data replaced |

`docker compose down -v` deletes named volumes. It is the one routine command
that destroys PRD data, and it must be called out in the operations
documentation rather than left for someone to discover.

### Backup and restore

Backup is a single documented command producing a compressed dump, and it must
be automatable. Restore is a single documented command.

**Restore is verified by a test** (FR-9.2), not merely documented: CI backs up a
seeded database, restores into a fresh one, and asserts the contents match. An
untested backup is an assumption, and the moment you need it is the worst moment
to discover the assumption was wrong.

Backups contain real financial data. They are written to a path outside the
repository, the documentation says so unambiguously, and the backup directory is
git-ignored from the first commit (Constitution IX).

---

## Mock data

DEV must be fully exercisable with **zero real credentials** (FR-11.3). Three
layers:

### 1. The deterministic generator

Given a seed, it produces identical data every time (FR-11.1) - so a failing
test is reproducible, and a demo looks the same on every machine.

It generates invented institutions, a realistic spread of account types,
multi-year daily balance series with plausible drift and volatility, holdings
with prices, and categorised transactions with recurring patterns (salary, rent,
subscriptions) and irregular ones.

**It deliberately generates the awkward states** (FR-11.2). This is the part
that matters, and the part a naive generator omits:

| State | Why it is generated |
|---|---|
| an account that closes mid-series | proves nothing forward-fills (Constitution III) |
| a transfer reported but never landed | proves conditional money is flagged (Constitution V) |
| a constant with no verified value | proves a projection refuses rather than defaulting (Constitution II) |
| an account with balances but no transactions | a real and commonly mishandled case |
| an account with transactions but no balances | the reverse |
| a restated balance for a date already recorded | proves supersession works |
| a transaction amended upstream after ingestion | proves updates do not duplicate |
| a period of no activity | proves charts handle gaps without inventing data |
| a second source reporting the same account | proves duplicate detection |
| a negative-balance credit account | sign conventions |
| an account in a second currency | proves currency is never assumed |

These are exactly the states the constitution's rules exist to handle, so the
generator doubles as fixture material for the test suite. Nothing else in the
project would produce them so cheaply.

Generated values must not resemble any real person's finances, and institution
names are invented (FR-11.5).

### 2. The stub MCP server

A small local MCP server exposing the same read-only tool surface as the real
one, backed by generated data. It lets the MCP connector be developed and tested
end to end with no account and no credential, and it gives the test suite a
place to simulate failures the real server will not produce on demand: a timeout,
a partial page, a malformed record, an expired token.

Its tool schemas are **contract-tested against the real server's published
schemas**, so the stub cannot quietly drift into testing a fiction.

### 3. The provider sandbox

The HTTP provider's sandbox environment exercises the real client code, the real
token exchange and the real webhook path, with credentials that are not secret
and data that is not anyone's (FR-2.5).

### The demo

These three layers are also the demo: someone clones the public repository, runs
one command, and has a working application with data and no credentials
(NFR-4). The work was required for testing regardless; the demo is free.
