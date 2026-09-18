# Deployment

Three targets, one app model.

| Target | What runs | Purpose |
|---|---|---|
| **Inner loop** | Aspire AppHost: dependencies as containers, projects as processes | development |
| **Self-hosted Docker** | generated Compose file, everything containerised | **the primary target** |
| **Azure Container Apps** | managed containers and managed PostgreSQL | optional |

> **Version claims below are unverified.** No .NET SDK was available to the
> session that wrote this and the vendor documentation sites were unreachable
> from it. Confirm every package name, command and GA/preview status against
> `aspire --help` and current documentation at scaffold time.

---

## The distinction that matters

**The AppHost is a development orchestrator, not a production runtime.**

In the inner loop it starts containers for dependencies and runs the .NET
projects as local processes, with a dashboard attached. That is excellent for
development and is *not* what runs on a server.

For deployment, the app model is **published**: Aspire reads the same resource
description and emits a Compose file (or cloud infrastructure). The topology is
described once; the artifacts differ.

Teams get this wrong by assuming the thing they run locally is the thing that
ships. The symptom appears late, in production, as a behaviour nobody could
reproduce. Stating it here costs a paragraph.

---

## Inner loop

```sh
aspire run --launch-profile dev
aspire run --launch-profile prd
```

Both run **simultaneously** - separate volumes, databases and ports
(`environments.md`).

Gives: containerised PostgreSQL with a persistent volume; API and worker as
processes with injected connection strings; the Vite dev server with hot reload
and the API address supplied by service discovery; the dashboard showing logs,
traces and metrics across every resource.

**Requirements:** the .NET SDK, the Aspire CLI, Docker or Podman, Node. The CLI
installs via the vendor's install script (`README.md`).

---

## Self-hosted Docker - the primary target

The deployment Constitution VIII is written for: everything on one machine, no
cloud account, no external dependency.

### Generating the artifact

The Aspire Docker hosting package is added to the AppHost and a Compose
environment declared in the app model. Then:

```sh
aspire publish
```

emits a `docker-compose.yaml` and an `.env` carrying the resource configuration.

**Two things to be careful about:**

1. **The package is stable, and the generated file is not the topology
   described below.** `Aspire.Hosting.Docker` 13.5.4 is a non-prerelease package
   [`DOCS@2026-09-18`, api.nuget.org]. The publisher puts every service on one
   flat `bridge` network named `aspire`; writes an external endpoint as a bare
   `host:container` string with **no host IP**, so Docker binds it on every
   interface (and with no explicit port, as a bare container port, so the host
   port is random); and writes tag-based image names for the project images
   [`SOURCE@microsoft/aspire@b477bdd`, `DockerComposeServiceResource.cs:272-310`,
   `DockerComposePublishingContext.cs:68-71`]. Non-external endpoints go to
   `expose:`, so the database port is genuinely unpublished.

   Three further gaps, each verified at the same reference:

   - **`WaitFor` health ordering does not survive publish.** The publisher's
     `service_healthy` branch is commented out and everything that is not
     `WaitForCompletion` falls through to `service_started`
     [`DockerComposeServiceResource.cs:184-206`]. `WaitForCompletion` does
     survive, as `service_completed_successfully`. So a `pg_isready` healthcheck
     plus explicit `depends_on` conditions in the override is load-bearing, not
     polish.
   - **An Aspire dashboard service is emitted that this document never listed.**
     `AddDockerComposeEnvironment` constructs it unconditionally,
     `DashboardEnabled` defaults to `true`, and its endpoint is `IsExternal`, so
     it lands in `ports:` with `restart: always`
     [`DockerComposeEnvironmentExtensions.cs:74-102`,
     `DockerComposeEnvironmentResource.cs:41`]. A telemetry UI carrying SQL and
     financial URLs, bound on all interfaces. Pass `.WithDashboard(false)` unless
     it is wanted.
   - **Base images can be digest-pinned at source** - `WithImageSHA256(string)`
     exists and is mutually exclusive with `Tag`
     [`ContainerResourceBuilderExtensions.cs:481-492`]. Only the self-built
     project images need the release-pipeline rewrite.

   Reach the intended topology through **`PublishAsDockerComposeService<T>(this
   IResourceBuilder<T>, Action<DockerComposeServiceResource, Service>)`**
   [`Aspire.Hosting.Docker/api/Aspire.Hosting.Docker.cs:53`] or a committed
   override file. Confirm all of it by running `aspire publish` - `slice-01.md`
   step 7 is where that first happens.
2. **The generated `.env` is a deployment artifact, not source.** It is
   git-ignored from the first commit (`security.md`). It carries configuration
   and is the obvious place for a credential to end up in a repository.

The release pipeline generates this artifact per version and attaches it to the
release (`cicd.md`), so a self-hoster gets a file matching the images they are
running.

### Running it

```sh
docker compose up -d
```

Topology:

| Service | Notes |
|---|---|
| `db` | PostgreSQL, **named volume**, port **not published to the host** |
| `api` | published on loopback only by default |
| `web` | static frontend; the only service a browser reaches |
| `worker` | ingestion and migrations; no published port |

Networks: an internal network for the database, reachable only by `api` and
`worker`; a front network for `web`.

Images referenced **by digest**, never by tag.

### Operations

- **Migrations** run as an explicit step before the new version serves traffic -
  a separate `worker` invocation, not an API side effect.
- **Backup and restore** are single documented commands, and restore is verified
  by a test (`environments.md`, FR-9.2).
- **Upgrade:** pull the new digests, back up, run migrations, start. The release
  notes carry the rollback path.
- **`docker compose down -v` destroys PRD data.** It is the one routine command
  that does, and the operations documentation says so in those words.

### Exposing it beyond localhost

Binding to a network interface is a deliberate act with consequences, documented
rather than assumed:

- terminate TLS at a reverse proxy with a real certificate;
- keep application authentication enabled regardless of what sits in front;
- never publish the database port;
- understand that a finance application reachable from a network is a target -
  and that the local-only default exists for a reason.

---

## Azure Container Apps - optional

Optional, always. The system must remain fully functional with no cloud account
(Constitution VIII), and the self-hosted path may not degrade into a
second-class configuration.

```sh
aspire deploy
```

or the Azure Developer CLI, driven by the same app model.

| Local | Cloud |
|---|---|
| PostgreSQL container + volume | Azure Database for PostgreSQL, private networking |
| envelope encryption, KEK from a Docker secret | Key Vault with managed identity |
| loopback binding | platform ingress, TLS terminated by the platform |
| Aspire dashboard | Azure Monitor and Application Insights |
| Compose file | platform-managed container apps |

The **secret-store interface is unchanged** (D10) - a different implementation
behind the same contract, which is the whole reason the interface exists.

Deployment is manual with an approval gate (`cicd.md`), by digest, signature
verified, migrations explicit, smoke-tested, rollback documented.

**Not spec'd:** Kubernetes. Considered and rejected in `research.md` - more
operational surface than a single-user application can justify, with no capability
the other two targets lack.

---

## Configuration

The same build artifact runs everywhere (FR-10.5). Only inputs differ:

| Input | Inner loop | Self-hosted | Cloud |
|---|---|---|---|
| connection string | injected by AppHost | Compose `.env` | platform configuration |
| KEK / secret store | user-secrets | Docker secret or host keyring | Key Vault + managed identity |
| environment name | launch profile | `.env` | platform configuration |
| ports | launch profile | Compose | platform ingress |

No credential is ever baked into an image, committed, or logged.

---

## Verification

A deployment is good when:

1. `/system/health` reports ready.
2. `/system/version` reports the expected version.
3. `/system/environment` reports the expected environment.
4. Authentication succeeds.
5. One dashboard renders with data.
6. A sync runs and records an `ingest_run`.
7. The database port is **not** reachable from the host.
8. Response headers carry the expected security headers (`security.md`).

Steps 1-5 are the release pipeline's smoke test. Steps 6-8 are on the
self-hosted operator's first-run checklist, and 7 is the one most often assumed
rather than checked.
