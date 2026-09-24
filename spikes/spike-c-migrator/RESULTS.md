# Spike C — how the API waits for migrations

**Ran:** 2026-09-24. [`EXECUTED 2026-09-24`]
**Against:** Aspire CLI `13.5.4+9c1b401`, `Aspire.AppHost.Sdk`,
`Aspire.Hosting.PostgreSQL` and `Aspire.Hosting.Docker` 13.5.4, .NET SDK
10.0.301, Docker 29.8.0, Windows 11. Postgres image as published:
`docker.io/library/postgres:18.3`.

**Result:** the claim under test is **WRONG as written and the fix works**. A
one-shot Migrator that exits, with `api.WaitForCompletion(migrator)`, holds the
API until the Migrator exits 0, refuses to start it on a non-zero exit, and is
published to compose as `depends_on: { migrator: { condition:
service_completed_successfully } }`. **But** the Migrator's own wait on Postgres
is published as `service_started`, not `service_healthy`, so in compose the
Migrator can start before Postgres accepts connections.

This is a spike, not step 2. `Migrator/` sleeps and exits with a chosen code;
it stands in for "apply EF Core migrations, then exit". `Api/` logs its start
time and nothing else.

---

## The claim, and what happened

> `plan.md` gives migrations to a long-running Worker and says the API waits.

The app model, `AppHost/AppHost.cs`:

```
pg        = AddPostgres("pg");  db = pg.AddDatabase("portfolio")
migrator  = AddProject<Migrator>  .WithReference(db) .WaitFor(db)
api       = AddProject<Api>       .WithReference(db) .WaitForCompletion(migrator)
```

A long-running Worker was not built: `WaitForCompletion` on a process that never
exits never completes, which is the evidence's point and needs no run to show.

### Run mode — `aspire run --detach`

| # | Migrator | Observed (`aspire logs <resource> --timestamps`) |
|---|---|---|
| 1 | exits **0** after 8 s | api `Waiting for resource 'migrator' to complete` at 11:13:30.997 · migrator `exit … code 0` at 11:13:47.471 · api `Finished waiting` 11:13:48.086 · api process started 11:13:48.133 · `[api] start` 11:13:50.111. `aspire describe`: migrator `Finished`, api `Running`/`Healthy` |
| 2 | exits **1** after 8 s (`-- --MIGRATOR_EXIT=1`) | migrator `exit … code 1` at 11:14:38.796 · api 11:14:39.380 `Resource 'migrator' has entered the 'Finished' state with exit code '1' expected '0'` · `Failed to create resource api`. `aspire describe`: api **`FailedToStart`**; its process never ran |

Both runs also show the Migrator waiting for `pg` and `portfolio` to be
**healthy** before it starts. That is run mode only — see below.

### Publish — `aspire publish -o <scratch> --non-interactive`

Published outside the repository on purpose; `.gitignore` explains why a
publish directory must never be committed.

| Service | `depends_on` in `docker-compose.yaml` |
|---|---|
| `api` | `migrator: condition: "service_completed_successfully"` ✔ |
| `migrator` | `pg: condition: "service_started"` ✘ — **not** `service_healthy` |
| `pg` | no `healthcheck` block (`docker compose config`: none) |

Changing the Migrator to `.WaitFor(pg)` (the container rather than the
database) and republishing gives the same `service_started`. So health-based
ordering does not survive publish either way, confirming `plan.md`'s banner
(`:122`, *TRUE IN RUN MODE ONLY*) by execution rather than by reading.

### Also in the published file, not asked but measured

- **A dashboard service nobody asked for**: `compose-dashboard`, image
  `mcr.microsoft.com/dotnet/nightly/aspire-dashboard:13.5`, `restart: "always"`,
  `ports: - "18888"`. `docker compose config` resolves that to
  `{"mode":"ingress","target":18888}` with **no host IP**: a random host port
  on every interface. `deployment.md`'s banner predicted this from source.
- **Postgres is `expose`-only**, not published. The API is `expose`-only too:
  DoD 3's "API on loopback" needs a published port with `127.0.0.1`, which the
  generated file does not have.
- **`.env` is keys-only on a fresh publish**: `API_IMAGE`, `API_PORT`,
  `MIGRATOR_IMAGE`, `PG_PASSWORD`, all empty. Matches the `.gitignore` note.
  The *accumulating* behaviour that note warns about was not re-tested.
- **Project images are not built by `publish`**: `MIGRATOR_IMAGE` and
  `API_IMAGE` are variables.

---

## What this settles for the slice

1. **The Migrator is a separate one-shot project that exits.** Non-zero exit
   stops the API from starting, in run mode. In compose,
   `service_completed_successfully` means the same thing.
2. **The Migrator must tolerate Postgres not being ready yet.** In compose it
   starts on `service_started`. Either it retries its connection, or step 7's
   override adds a `pg_isready` healthcheck to `pg` and `service_healthy` to the
   Migrator. `deployment.md` already calls for the override; this makes it a
   step-2 concern too, because the Migrator's connect behaviour is written there.
   [`INFERRED` — the published file says `service_started`; compose not run, and
   this Migrator does not connect, so the race was not observed.]
3. **Step 7 needs `.WithDashboard(false)` or an override** for the dashboard
   service, and a `127.0.0.1` port for the API. Both named in `deployment.md`'s
   banner; now `EXECUTED`.

## Not measured

- `docker compose up` of the published file. Images are not built by `publish`;
  that is step 7.
- The start-up race in point 2 (see its marker).
- `.env` accumulation across repeated publishes.
- Build warning `ASPIRE010` (`AspireUseCliBundle=false`) on every build. Not
  investigated; it did not stop anything here.

## Reproducing

```sh
cd spikes/spike-c-migrator
aspire run --detach --non-interactive --apphost AppHost/AppHost.csproj
aspire wait api --apphost AppHost/AppHost.csproj --timeout 240
aspire logs api --apphost AppHost/AppHost.csproj --timestamps
aspire stop --apphost AppHost/AppHost.csproj

aspire run --detach --non-interactive --apphost AppHost/AppHost.csproj -- --MIGRATOR_EXIT=1
aspire describe --apphost AppHost/AppHost.csproj      # api: FailedToStart
aspire stop --apphost AppHost/AppHost.csproj

aspire publish --apphost AppHost/AppHost.csproj -o <somewhere outside the repo>
```
