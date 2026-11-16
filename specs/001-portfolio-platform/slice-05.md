# Slice 05 — backup and restore, proved by a restore, over invented balances

## The commission

**Recorded here, in this repository, 2026-11-16.** Slices are commissioned and gated in
this repository (planning `D-043`, 2026-10-20), as slices 03 and 04 were.

**Commissioned by Kyle, 2026-11-16,** after slice 04's gate had decided STOP
([`slice-04-assessment.md`](slice-04-assessment.md) §6). Kyle reopened it for two reasons:
the backup is the largest item left before the first real balance (slice 03, "Before the
first real balance" 2, `D5`), and it is a piece any project started from this one would
need. Four rulings the same day, each chosen from options put to Kyle:

| Ruling | Kyle's choice | The options it was chosen from |
|---|---|---|
| How much | **On demand:** a backup command and a restore command, run by hand from the host, and the restore drill as a test. No schedule, no retention, no offsite copy | On demand; on demand and scheduled with retention; all of that and an offsite copy |
| What a backup holds | **The database and the key ring.** Sessions survive a restore. The file holds a signing key, so it is a credential | The database only; the database and the key ring; both and the deploy parameters |
| Encryption | **Encrypted with a passphrase Kyle holds.** A restore refuses a wrong one | Encrypted with a passphrase; plain for now, with encryption a gate before the first real balance; let the review decide |
| Format | **Logical, `pg_dump`**, restorable into a newer PostgreSQL major version | Logical; a physical copy of the stopped volume; both |

**Data, and who builds:** as slice 04. Invented data only; the agent builds, under the same
gate.

**Status: FIRST DRAFT, awaiting its design review.** Nothing below is built. Claims about
PostgreSQL, Docker, gpg and Aspire carry `D-029` markers; an `INFERRED` claim the design
rests on is a gate (below).

**What this slice is for, by the gate's rule** (a slice is for a technique, not because the
next feature is obvious). Two techniques, stated narrowly:

1. **A restore proved by a restore.** FR-9.2 and `testing.md` ask for a round trip; this
   slice makes it a drill that fails for each way a backup can be useless: a missing
   login, the wrong order, a key ring left behind, a truncated or altered file, a wrong
   passphrase. "Equal" is defined before it is tested: the catalog snapshot of slice 04,
   every table's rows by checksum, and a session from before the backup still signed in
   after the restore.
2. **Restoring a database whose security lives outside it.** The three logins, their
   attributes and passwords are cluster-wide, so a dump of the database does not carry
   them (`SCO-11`, carried from slice 04). The grants to them are in the dump, and fail
   without them (`INFERRED`, gate `G5-1`). So the restore is an ordered procedure, not one
   tool's run: the logins first, then the data, then the routine again.

A third is plausible: **authenticated encryption of an artifact, refused when altered.**
It sits close to slice 03's secrets work; the review should say whether it is new.

**Method:** `D-029`. **Built by:** the agent. **Estimate:** for the review to set. The
agent's guess, to be replaced: about **8 logged agent hours**. **Ends in:** a written
assessment and a full stop.

**Built against:** `spec.md` FR-9.1, FR-9.2; `deployment.md` "Operations";
`environments.md` "Backup and restore"; `testing.md` (the round trip); `security.md`
"Secrets"; slice 04's README runbook "Database logins".

---

## What the slice is

**Two host commands, one test that is the drill, and a runbook.**

### `backup`

One command, from the host, against the published stack while it runs:

1. **The database**, by `pg_dump --format=custom` as `postgres`, of `portfolio_dev` only. A
   custom-format dump is compressed and restorable selectively (`SOURCE`, the PostgreSQL 18
   documentation). It is taken inside one snapshot, so it is consistent while the API writes
   (`SOURCE`). It holds the migration history, so a restored database needs no migration.
2. **The key ring**, the files under the API's `/home/app/keys` on `portfolio-dev-keys`, by
   `tar`. It changes rarely (a new key every 90 days, `INFERRED`, gate `G5-3`), so taking it a
   moment apart from the dump is accepted. The review should confirm that.
3. **One file**: both parts and a manifest in one archive, **encrypted with gpg's symmetric
   mode** and a passphrase. Its name carries the UTC time and the latest migration. It is
   written to a directory **outside the repository**, which the command takes as an argument
   and the README names (`environments.md`).
4. **The manifest** records: the PostgreSQL and `pg_dump` versions, the latest migration,
   each table's row count and checksum, the key ring's file names, the time, and the commit
   the API image was built from. It holds **no** password, no key and no row.

**Where it runs** is for the review. The postgres image ships `pg_dump`, `pg_restore`,
`gpg`, `openssl` and `tar`, and not `age` (`EXECUTED 2026-11-16`, `postgres:18.3`). Options:

- **a. A compose service** on the postgres image, run by `docker compose run --rm backup`.
  It reaches the database by the internal network, mounts the key ring read-only and the
  backup directory from the host. One command, as the host commands are. The AppHost
  declares it, so the compose file stays complete on its own (slice 01 step 7).
- **b. `docker compose exec postgres`**, piped to the host. No new service, but the key ring
  is on another container's volume, and the host needs gpg.
- **c. A .NET command in the Migrator's image**, which already holds the superuser's
  connection. It would need the PostgreSQL client tools added to the image, and its own
  encryption code.

The agent's recommendation is **a**: it needs nothing on the host but Docker.

**The passphrase** is never in `.env`, the deploy state, the compose file, a log, the
manifest or a process's arguments. It is read from the terminal, or from a file the command
is given, inside the container (gpg's `--passphrase-file` or a pinentry, `INFERRED`, gate
`G5-4`). A backup sitting beside `.env` must not be decryptable with what is in `.env`.

### `restore`

One command, from the host, into a stack that is **stopped and empty**: a fresh volume, or
the old one after `down -v`. It **refuses** a database that has any table in `public`, and
a key ring volume that has any file, and says which. Overwriting data is not a restore's
job; the review should say whether a `--replace` is wanted.

The order, each step logged by name:

1. **Decrypt and verify.** A wrong passphrase, a truncated file and an altered byte each
   refuse before anything is written (gpg's integrity check, `INFERRED`, gate `G5-2`).
2. **Read the manifest.** Refuse a dump from a newer PostgreSQL major version than the
   server's.
3. **Start PostgreSQL** on the empty volume. It is initialised with the `.env` file's
   `POSTGRES_PASSWORD`, which need not be the one the backup was taken under.
4. **Create the logins** by the Migrator's routine (`DatabaseLogins.EnsureAsync`), before
   any data, so the dump's grants have someone to name.
5. **`pg_restore`** as `postgres`, into the database the stack's environment names,
   `--exit-on-error`, in one transaction (`--single-transaction`, `SOURCE`).
6. **Run the Migrator** as a deploy does. It finds no migration pending, re-asserts every
   grant and sets both passwords from `.env`. **So a backup restores onto a new machine
   with new generated passwords.** The passwords are not in the backup and need not be
   (`INFERRED`, gate `G5-5`).
7. **Write the key ring** into the empty keys volume, owned by the API image's `app` user
   (UID 1654, slice 03 step 7).
8. **Check** the restored database against the manifest: the latest migration, each
   table's row count and checksum. Any difference is a failed restore, named.
9. **Start the API.** It serves the restored data. A session from before the backup is
   still signed in.

**What a restore does not need:** the setup code (the owner is restored, so enrolment stays
closed) and the old passwords. **What it needs:** the backup file, its passphrase, and the
repository's compose files.

### The audit

Both commands write to `audit_event`, as the host commands of slice 03 do: the backup after
the dump's snapshot is taken (so it is not in its own dump), the restore after step 8. Each
row records the kind, the time and the backup's name. **Never** the passphrase, the
directory or a row count of a table. The review should say whether this belongs here or with
`D17`.

### The drill: one test, red for each way a backup is useless

In Integration, on Testcontainers, so it runs in CI (FR-9.2: "verified by a test"):

- **Seed** invented data across every table: accounts, balances with restatements, imports,
  the owner with a passkey, audit rows, saved reports, a session.
- **Back up**, through the real command, on the image the compose file names.
- **Restore** into a second, empty PostgreSQL container, through the real command.
- **Assert equal:**
  - every table's rows, by an ordered checksum per table (`md5` of the rows' text form);
  - the migration history;
  - each login's catalog snapshot, by slice 04's `catalog-snapshot.sql`, equal to
    `ExpectedPrivileges`;
  - the sequences' current values, so the next insert does not collide;
  - the key ring: a cookie issued before the backup is accepted after the restore.

**Seen failing**, each by a mutation of the restore or the file:

| Mutation | Must fail with |
|---|---|
| Skip step 4, the logins | `pg_restore`'s error on the first grant, and nothing restored |
| Run step 6 before step 5 | A named failure, not a database the Migrator re-created empty |
| Skip step 7, the key ring | The old cookie refused |
| Truncate the file by one byte | Refused at step 1, nothing written |
| Flip one byte of the file | Refused at step 1, nothing written |
| A wrong passphrase | Refused at step 1, nothing written |
| A non-empty target | Refused before step 3, the target unchanged |
| A table left out of the dump | Step 8 names the table |

**Published, by hand:** a backup of the running stack, `down -v`, a restore from the file,
the saved session still signed in, and slice 04's check script passing. Then the same file
restored with a **different** `.env` (new passwords), to show step 6.

### The runbook

README "Backup and restore": both commands, where the file goes, that it is a credential,
where the passphrase lives and that losing it loses the backup, what a restore needs, the
refusal over existing data, and that `down -v` is the one routine command that destroys data
(`deployment.md`).

---

## What the slice is not

- **No real data** (Kyle, 2026-11-16). Invented balances only. Admitting the first real
  balance stays its own commission.
- **No schedule, no retention, no offsite copy** (Kyle, 2026-11-16). The command can be
  scheduled by the host; nothing here does it. Trigger: the first real balance.
- **No deploy parameters in the backup** (Kyle, 2026-11-16). Step 6 makes them unneeded.
- **No physical backup and no point-in-time recovery** (Kyle, 2026-11-16).
- **No PostgreSQL major upgrade.** A logical dump makes one possible; doing it is its own
  slice.
- **No export** (FR-9.4, FR-6.5). A backup is for this application to read back, not a format
  for the user to take elsewhere.
- **No restore over existing data** unless the review rules a `--replace` in.

---

## Step 0 — the spikes

Each answers a gate below, on throwaway containers, before the step that needs it.

| Spike | Question | Gate |
|---|---|---|
| O 1 | `pg_restore` of a dump whose grants name a missing role: what fails, and does `--single-transaction` leave nothing behind? | `G5-1` |
| O 2 | gpg symmetric: does a truncated file, an altered byte and a wrong passphrase each fail with a non-zero exit before any plaintext is written to the pipe? | `G5-2` |
| O 3 | The key ring: how often does Data Protection add a key, and does a restored ring accept a cookie issued before the backup? | `G5-3` |
| O 4 | The passphrase: can gpg in a `docker compose run` container read it from a mounted file or the terminal, with nothing in `ps`, the environment or a log? | `G5-4` |
| O 5 | A restore under new passwords: does the Migrator's routine, run after `pg_restore`, leave both logins working and the snapshot equal? | `G5-5` |

---

## The build

Each step merges green, as slices 01–04 did. A first cut, for the review to reorder:

1. **The spikes** (step 0).
2. **`backup`**: the compose service in the AppHost, the dump, the key ring, the manifest,
   the encryption. Tests: the file decrypts with its passphrase, holds both parts and a
   manifest without a secret in it, and is consistent under concurrent writes.
3. **`restore`**: the ordered procedure and its refusals. The drill and its mutations.
4. **The audit rows**, if the review keeps them here.
5. **The published stack and the runbook**, by hand, as above.
6. **The assessment.**

---

## Definition of done

Each item names the test that shows it and how that test was seen failing.

1. **A backup is one command** and writes one encrypted file outside the repository, holding
   the database, the key ring and a manifest with no secret in it.
2. **A restore is one command** into an empty stack, in the order above, and refuses a
   non-empty target.
3. **The drill passes in CI** and is seen failing for each mutation in its table.
4. **No secret leaks:** the passphrase in no log, argument, environment, manifest or file but
   the one the operator gave; the passwords in no backup. Shown by a canary passphrase, as
   slice 03's DoD 7 does for logs.
5. **Published:** the round trip by hand, on the same `.env` and on a new one.
6. **The runbook** is in the README, and every command in it was run as written.
7. **Nothing regressed.** Slices 01–04's DoD items hold. CI is green.
8. **Each obligation below is done, or recorded as not done with a reason.**

---

## Gates

`INFERRED` claims the build rests on, each with what raises it:

| Id | Claim | Raised by |
|---|---|---|
| G5-1 | A dump's grants fail to restore without their roles, and `--single-transaction` leaves nothing behind | Spike O 1 |
| G5-2 | gpg's symmetric mode refuses a truncated file, an altered byte and a wrong passphrase, before writing plaintext | Spike O 2 |
| G5-3 | The key ring changes rarely enough to be taken apart from the dump, and a restored ring accepts an old cookie | Spike O 3 |
| G5-4 | The passphrase can reach gpg in the container with nothing in `ps`, the environment or a log | Spike O 4 |
| G5-5 | The Migrator after `pg_restore` makes a backup restorable under new passwords | Spike O 5 |

---

## Obligations this slice carries

| From | Item | Here |
|---|---|---|
| slice 03, "Before the first real balance" 2 (`D5`) | The backup command | **Built here** (Kyle, 2026-11-16) |
| slice 04, "Obligations" (`SCO-11`) | The restore recreates the logins before the data | **Built here**: restore step 4 |
| slice 04 README, "A backup must bring the logins back before the data" | The runbook line | **Replaced** by the new runbook |
| slice 03, "Before the first real balance" 1, 4, 5, 6 | Commission, `D17`, the dev page (`D9`), `C-389` | **Carried** |
| `spec.md` FR-9.2 | Restore verified by a test | **Built here** |
| slice 04 assessment §7 | `G4-1`, the full E2E, the host commands' login | **Carried**, unchanged |

---

## The gate — slice 01's rule, unchanged

`LOG.md` carries on with both columns, one line per session, written as it goes.

> **The default at the end of slice 05 is STOP.** A sixth slice, or the first real balance,
> requires its own commission, **written and dated in this repository**, as this one is.
>
> The default is overridden only if the slice-05 assessment names **three techniques this
> slice taught that Kyle did not already have**. Each must come with the commit that
> demonstrates it, and must **not** be any of the thirty techniques slices 01 to 04 already
> claimed.
> **And** Kyle's hours must have stayed under **200** (`LOG.md`, `Kyle h`).
>
> **Explicitly NOT reasons to continue:** that the slice works, and that the next slice is
> obvious.

**Stated, not smoothed:** the agent builds, so the hours limb cannot trip. The gate rests
on the techniques limb and on Kyle's own judgement of what the slice taught.

Write `slice-05-assessment.md`. It answers:

- What did it cost, in both columns, against the estimate the review sets?
- Which of the gates were wrong once built?
- For each mutation in the drill, was it seen failing, and how?
- Which techniques did this slice teach that Kyle did not already have, each with its
  commit?
- Is a sixth slice wanted, or the first real balance?

## Then stop
