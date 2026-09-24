# Spike A — two stacks, and what selects the environment

**Ran:** 2026-09-24. [`EXECUTED 2026-09-24`]
**Against:** Aspire CLI `13.5.4+9c1b401dd67746739044f68959cbf4d3d7af93a6`
(`dotnet tool install -g Aspire.Cli --version 13.5.4`), `Aspire.AppHost.Sdk`
and `Aspire.Hosting.PostgreSQL` 13.5.4, .NET SDK 10.0.301, Docker 29.8.0,
Windows 11. **Not** the `b477bdd` the spec's `SOURCE@` evidence was read at —
the evidence was re-checked by running it, on a later build.

**Result:** the claim under test is **WRONG**, as the evidence predicted. One
AppHost directory runs one stack; a second `aspire run` of it stops the first,
and neither `--isolated` nor a launch profile changes that. **Two directories
run side by side**, with volume, database and container parameterised by
configuration. Launch profiles **cannot select** an environment through
`aspire run` at all.

This is a spike, not the skeleton. `AppHost/` is one Postgres resource with a
named data volume and one database, all three named from `STACK_ENV`, and
nothing else.

---

## The claim, and what happened

> `environments.md` says `aspire run --launch-profile prd|dev` runs two stacks
> simultaneously.

`aspire run --help` on 13.5.4 lists **no `--launch-profile` option**. It does list
`--isolated`: *"Run in isolated mode with randomized ports and isolated user
secrets, allowing multiple instances to run simultaneously."* That help text is
the second claim this spike ended up testing, and it is also wrong.

Every run used `aspire run [--isolated] [--detach] --non-interactive -- --STACK_ENV=<env>`
from `spikes/spike-a-two-stacks/AppHost/` unless the table says otherwise.
`--STACK_ENV=` is a command-line configuration source that
`DistributedApplication.CreateBuilder(args)` binds.

| # | What ran | Observed |
|---|---|---|
| 1 | Same directory, `--detach`, `dev` then `prd` | Second run's AppHost PID replaced the first in `aspire ps`; first PID gone. First stack's `pg-dev` container lingered ~20 s, then its own DCP removed it |
| 2 | Same directory, `--isolated --detach`, `dev` then `prd` | CLI printed `Stopping previous instance (AppHost PID: 17764, CLI PID: 23720)`. Ports were randomised; the first stack was still stopped |
| 3 | Same directory, `--isolated`, **foreground**, `dev` then `prd` | Same stop. Aspire's own warning: *"A running instance of this AppHost was found and will be stopped. To run multiple isolated instances simultaneously, run from different directories"* |
| 4 | **Two directories** (`AppHost/`, and a copy at `AppHost2/`), both plain `--detach` | First kept running. Second **crashed**: `Failed to bind to address https://127.0.0.1:22039: address already in use` — both copies carried the same fixed dashboard ports from `launchSettings.json` |
| 5 | Two directories, second with `--isolated` | **Both running.** `aspire ps` lists `AppHost\AppHost.csproj` and `AppHost2\AppHost.csproj` |
| 6 | `aspire run -- --launch-profile http`, `https` profile carrying `STACK_ENV=from-https-profile` | AppHost logged `STACK_ENV=from-https-profile`: the passthrough was ignored and the **first** profile applied |

### Run 5, separation measured

| Parameterised by `STACK_ENV` | dev | prd |
|---|---|---|
| Container | `pg-dev-dtrdutdt` | `pg-prd-gveqqrdp` |
| Volume (`docker inspect … .Mounts`) | `spike-a-pg-dev` | `spike-a-pg-prd` |
| Database (`pg_database`) | `portfolio-dev` | `portfolio-prd` |
| Host port | `127.0.0.1:52693` (random) | `127.0.0.1:52730` (random) |
| `create table marker` run in dev | present, 1 row | `to_regclass('marker') is null` → `t` |

Postgres host ports were random and loopback-bound in **every** run, with or
without `--isolated`. `--isolated` is what randomises the **dashboard and
resource-service** ports, which are the ones that collided in run 4.

---

## What this settles for the slice

1. **Simultaneity needs two directories.** Keyed on the AppHost path, as the
   `SOURCE@b477bdd` reading said; still true at 13.5.4. Slice 01 has one
   environment, so this is recorded rather than built.
2. **Environment selection is configuration, not a launch profile.** A
   command-line argument (`-- --STACK_ENV=prd`) reaches `builder.Configuration`
   and names the volume, database and container. `builder.AddParameter(...)`
   reads the same configuration and is the typed form for step 1.
   `slice-01.md` Spike A's *"If it fails as expected"* branch applies.
3. **Launch profiles do not select.** `plan.md`'s banner says *"Launch profiles
   still work for selection"*; on 13.5.4 through `aspire run` they do not.
   `aspire run` has no option for it and ignores one passed through; the first
   profile in `launchSettings.json` always applies. `dotnet run --launch-profile`
   against the AppHost project was **not** tested; it bypasses the `aspire` CLI
   and is not the command the spec names.
4. **A second directory also needs its own dashboard ports.** Either `--isolated`
   or distinct `launchSettings.json` ports. A plain copy of the directory fails
   on start.

## Not measured

- Whether `--isolated`'s "isolated user secrets" keeps secrets apart between two
  directories. Nothing here uses a secret.
- Compose project names after `aspire publish`. That is Spike C's ground and
  `deployment.md`'s banner already names it.

## Reproducing

```sh
cd spikes/spike-a-two-stacks
aspire run --detach --non-interactive --apphost AppHost/AppHost.csproj -- --STACK_ENV=dev
aspire run --detach --non-interactive --apphost AppHost/AppHost.csproj -- --STACK_ENV=prd   # stops dev
aspire ps

# two directories: copy AppHost/ to AppHost2/ (sources + Properties/), then
aspire run --detach --non-interactive --apphost AppHost/AppHost.csproj  -- --STACK_ENV=dev
aspire run --isolated --detach --non-interactive --apphost AppHost2/AppHost.csproj -- --STACK_ENV=prd
aspire ps                                   # both listed

aspire stop --apphost AppHost2/AppHost.csproj
aspire stop --apphost AppHost/AppHost.csproj
docker volume rm spike-a-pg-dev spike-a-pg-prd
```

The first `aspire run` pulls the Postgres image; allow for it before judging a
`Starting` resource.
