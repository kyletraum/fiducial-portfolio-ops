# Seat: Operator

Slice 03 (the security tier), branch `slice-03-spike-h` at `f3b5c2b`, read 2026-10-21. The
subject is the tier **as run**: `aspire run` on this Windows machine (Docker Desktop, WSL2
backend, Vite proxying `/api`) and the published Compose stack (`aspire deploy -o
aspire-output`: postgres + one-shot migrator + the API serving the SPA on `127.0.0.1:8080`,
no TLS, memory limits 512M / 256M, `restart: unless-stopped`).

## Method

- Read, in full: `specs/001-portfolio-platform/slice-03.md`; `security.md` (all sections, and
  its banner); `deployment.md` and `environments.md` (both carry the FROZEN REFERENCE banner:
  "consulted, not executed", corrected only when a slice is commissioned against them, and
  **slice 03 is not commissioned against either**, so what they say about backups, roles and
  topology remains unbuilt, and I treat it as intent, not fact); `src/AppHost/AppHost.cs`;
  `src/Api/Program.cs`, `Endpoints.cs`, `CrossSiteWrites.cs`, `appsettings*.json`;
  `src/Migrator/Program.cs`; `src/ServiceDefaults/Extensions.cs`;
  `src/Infrastructure/PortfolioDbContext.cs` and the `IngestRun` migration's trigger block;
  `aspire-output/docker-compose.yaml`; `README.md` "Running it"; `deploy/.env.example`;
  `spikes/spike-f-upload/RESULTS.md`; `spikes/spike-h-auth/RESULTS.md`.
- Did not read anything under `specs/review/` or any other seat's output.
- `aspire-output/.env*`: listed file names and **key names only** (`cut -d= -f1`); no value
  was read or printed. The AppHost's user-secrets file: existence and key names only.
- Executed, read-only: `docker ps -a` (nothing running), `docker images`, `docker volume ls`,
  `docker info` (logging driver), `docker image inspect api:latest`, and one throwaway
  `docker run --rm --network none --entrypoint sh api:latest` to read `id`, `$HOME` and
  whether `/home/app` is writable. `net localgroup docker-users`. No `aspire run`, no deploy,
  no compose up, no test suite.
- External claims carry `EXECUTED` / `SOURCE@` / `DOCS@date URL` / `INFERRED`. Every
  load-bearing `INFERRED` is listed under Gates.

Facts established, used throughout:

| Fact | Strength |
|---|---|
| The compose api service has `ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"`, no `ASPNETCORE_ENVIRONMENT` (so Production), no `OTEL_EXPORTER_OTLP_ENDPOINT`, and the same `postgres` superuser credentials as the migrator | `EXECUTED` (file read) |
| ASP.NET's `ForwardedHeaders_Enabled` setup sets `XForwardedFor \| XForwardedProto` and **clears `KnownIPNetworks` and `KnownProxies`** ("Only loopback proxies are allowed by default. Clear that restriction because forwarders are being enabled by explicit configuration.") | `SOURCE@dotnet/aspnetcore main src/DefaultBuilder/src/ForwardedHeadersOptionsSetup.cs` |
| Aspire sets that variable only in publish mode, only for projects with endpoints, and **not** when `.DisableForwardedHeaders()` has been called on the project | `SOURCE@dotnet/aspire main src/Aspire.Hosting/ProjectResourceBuilderExtensions.cs` |
| Data-protection key directory on Linux: `LOCALAPPDATA` env if set, else `Environment.SpecialFolder.UserProfile` + `.aspnet/DataProtection-Keys`; `null` (keys in memory, lost at exit) if neither resolves | `SOURCE@dotnet/aspnetcore main src/DataProtection/DataProtection/src/Repositories/DefaultKeyStorageDirectories.cs` |
| In `api:latest`: user `app` (uid 1654), `HOME=/home/app`, the directory exists, is writable, and has no `.aspnet` yet; entrypoint `dotnet /app/Api.dll`; Ubuntu 24.04 base | `EXECUTED 2026-10-21` |
| Keys are encrypted at rest with DPAPI **only on Windows**; in Docker "keys should be persisted in a folder that's a Docker volume"; otherwise "when the process shuts down, all generated keys are lost"; key lifetime 90 days | `DOCS@2026-10-21 https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings` |
| Docker daemon logging driver here is `local`: defaults `max-size 20m`, `max-file 5`, compressed, "preserves 100MB of log messages per container" | `EXECUTED` (`docker info`) + `DOCS@2026-10-21 https://docs.docker.com/engine/logging/drivers/local/` |
| `unless-stopped`: "Similar to `always`" = "Always restart the container if it stops"; backoff "double the previous delay, starting at 100 milliseconds", cap 1 minute, reset after 10 s of running | `DOCS@2026-10-21 https://docs.docker.com/reference/cli/docker/container/run/` |
| .NET in a container: GC heap hard limit defaults to 75% of the container limit (384 MiB of 512M) | `DOCS@2026-10-21 https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector` |
| Compose project name is `aspire-{environment resource name}-{first 8 hex of AppHost:PathSha256}` → here `aspire-compose-075c33a6`, deterministic from the AppHost **path** | `SOURCE@dotnet/aspire main src/Aspire.Hosting.Docker/DockerComposeEnvironmentResource.cs` + `EXECUTED` (`docker volume ls` shows `aspire-compose-075c33a6_portfolio-dev-pgdata`) |
| Identity `SecurityStampValidatorOptions.ValidationInterval` "Defaults to 30 minutes" | `DOCS@2026-10-21 https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.securitystampvalidatoroptions.validationinterval` |
| Hosting's "Request starting" log line includes `{QueryString}` | `SOURCE@dotnet/aspnetcore main src/Hosting/Hosting/src/Internal/HostingRequestStartingLog.cs` |
| Non-Development `UseExceptionHandler` + `AddProblemDetails` returns type/title/status/traceId only | `DOCS@2026-10-21 https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling` |
| HSTS "applies to HTTP over any TCP port of an HSTS Host"; over insecure transport "the UA MUST ignore any present STS header field(s)" | `DOCS@2026-10-21 https://www.rfc-editor.org/rfc/rfc6797` §8.3, §8.1 |
| `Secure` cookie: "sent ... only when a request is made with the `https:` scheme (except on localhost)"; "The `https:` requirements are ignored when the `Secure` attribute is set by localhost" | `DOCS@2026-10-21 https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Set-Cookie` |
| Rate limiting: the concurrency limiter "limits only the number of concurrent requests"; partitioning "by using client IP addresses" makes the app "vulnerable to Denial of Service attacks that use IP Source Address Spoofing"; each distinct partition key "creates and caches its own limiter, so partitioning on unbounded user-controlled input can exhaust memory"; default rejection is 503, settable to 429 | `DOCS@2026-10-21 https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit` |
| `docker-users` on this machine has exactly one member, `kylet`, which is the account the stack is run from; the active context is the `dockerDesktopLinuxEngine` named pipe | `EXECUTED 2026-10-21` |
| No `pg_dump`/`pg_restore`/backup script exists anywhere in the repository (only prose in the frozen references) | `EXECUTED` (grep) |
| No `DataProtection`, `PersistKeysTo`, `EnableSensitiveDataLogging`, `UseHsts`, `UseHttpsRedirection` anywhere in `src/` | `EXECUTED` (grep) |

---

## Findings

| ID | Severity | Finding | Rests on | What to do | Where |
|---|---|---|---|---|---|
| OPS-1 | **MUST FIX** (before step 3, and before step 5 records an actor IP) | **The published API believes any `X-Forwarded-For` / `X-Forwarded-Proto` a client sends.** Aspire writes `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` for the api service, and ASP.NET's handling of that flag clears the known-proxies restriction. There is no proxy in the stack; the only thing in front of Kestrel is Docker's port map. So every local caller chooses its own `RemoteIpAddress` and its own `Request.Scheme`. Consequences for this slice: (a) a login limiter partitioned by IP is keyed by attacker-chosen text, and each new value allocates and caches a limiter; (b) any "is this request from loopback" check is a check of a header; (c) `X-Forwarded-Proto: https` makes `Request.IsHttps` true, which changes what `CookieSecurePolicy.SameAsRequest`, any HTTPS redirect, and any HSTS middleware do; (d) the audit log's "actor" IP is whatever the caller wrote. Under `aspire run` the variable is absent, so none of this reproduces in development, which is exactly the "works locally, differs in production" shape `deployment.md` warns about | `EXECUTED` compose file; `SOURCE@` ForwardedHeadersOptionsSetup.cs; `SOURCE@` Aspire ProjectResourceBuilderExtensions.cs; `DOCS@` rate-limit page (spoofing, partition-cache growth) | Call `.DisableForwardedHeaders()` on the `api` project in `AppHost.cs` (it exists for this). Add a published-stack check to step 7: a request carrying `X-Forwarded-For: 203.0.113.9` is recorded in `audit_event` with the real connection address, not that value. Never key a limiter by `RemoteIpAddress` here (see OPS-2) | `src/AppHost/AppHost.cs` api resource; `aspire-output/docker-compose.yaml:52`; step 3 limiter; step 5 audit actor |
| OPS-2 | **MUST FIX** (before step 3) | **A login flood is an OOM kill, and an OOM kill is a restart loop.** One argon2id verify holds 64 MiB (Spike H), in libsodium's native memory, which the GC's 75% cap does not govern; the cgroup does. The API's measured peak under the import load is 234.5 MiB (AppHost.cs comment) against 512M. So somewhere between four and seven **concurrent** password verifications, fewer during an import, is the end of the container, exit 137, and `unless-stopped` brings it back with a 100 ms-doubling-to-1 min backoff, and the attacker's loop resumes. A fixed-window limit on *requests per minute* does not bound *hashes in flight*; a limiter partitioned by IP (OPS-1) bounds nothing. Note also the three places a hash runs: password login, enrolment/password-set, and the `NeedsRehash` → `SuccessRehashNeeded` path, which hashes twice | Spike H `EXECUTED` (64 MiB, 139 ms in a 512m container, one at a time); `DOCS@` GC heap limit; `DOCS@` Docker restart backoff; `INFERRED` that libsodium's argon2 buffer is native (Gate G1 measures it either way); `INFERRED` that an OOM kill counts as "stops" for the restart policy (Gate G2) | Step 3: a **global** `ConcurrencyLimiter` (not partitioned) on every endpoint that can run the hasher, `PermitLimit` 1 or 2, `QueueLimit` small, rejection 429; plus a global fixed/sliding window on failed logins for the throttle DoD 5 asks for. Then **measure** in the 512m container: N parallel password logins until the container dies, and set the permit from that number, with the import's peak subtracted (Gate G1). Record the number in `AppHost.cs` next to the memory-limit comment, which already says "revisit when ... changes" | step 3; `src/AppHost/AppHost.cs` S-34 comment; Spike H §3 last paragraph ("Step 3 sets the number with that in mind") |
| OPS-3 | **MUST FIX** (before step 7; design decision at step 2) | **Session cookies and antiforgery tokens die on every redeploy.** Nothing configures data protection, so in the container the key ring is written to `/home/app/.aspnet/DataProtection-Keys` (user profile resolves; the directory is writable), **unencrypted** (no DPAPI off Windows), in the container's **writable layer**. That layer survives `docker restart` and an OOM restart (same container) and is destroyed by `docker compose up` after `down`, by `--force-recreate`, and by every `aspire deploy` (new image → new container). Effect: all sessions invalid (user signed out), all antiforgery tokens held by open tabs invalid (writes fail with 400 until the page fetches a new token). With `README.md`'s documented stop/start pair, the owner is signed out on every restart of the stack. A volume thief: the postgres volume does not contain the keys, but the Docker Desktop VM disk contains both; `security.md`'s own banner says full-disk encryption is the answer to that threat, not the app | `SOURCE@` DefaultKeyStorageDirectories.cs; `EXECUTED` image probe (`HOME=/home/app`, writable, no `.aspnet` yet); `DOCS@` data-protection default-settings; `EXECUTED` README stop/start commands | Decide where keys live (Decision D3): a named volume via `PersistKeysToFileSystem` and a compose volume added in `PublishAsDockerComposeService`, or `PersistKeysToDbContext<PortfolioDbContext>`. Whichever: step 4's "expired session without losing the page" must also treat an antiforgery 400 as "refetch token, retry once, then show login", because a redeploy produces exactly that in an open tab. Add to the runbook what a redeploy does to sessions | step 2 (Identity setup), step 4, step 7; `src/AppHost/AppHost.cs` api `PublishAsDockerComposeService`; `README.md` "Running it" |
| OPS-4 | **MUST FIX** (before step 1; it is the open design question the slice names) | **"A one-time setup code shown in the AppHost's console" has nowhere to appear in the published stack, and wherever it appears, every local process can read it.** Candidates and their readers: ILogger → stdout → `docker logs <api>`; a file in the container → `docker exec`/`docker cp`; an Aspire secret parameter → `aspire-output/.env.Production` on disk and `docker inspect` env. Every one of those is readable by any member of `docker-users`, which on this machine is exactly the one account everything runs as; so "any local process" (the slice's own phrase) can read the code by construction. The window cannot be closed by secrecy on a single-user machine. It can be **bounded in time** (code single-use, short TTL, generated at start, regenerated on restart while no user exists) and **attested** (enrolment is an `audit_event` with time and connection address, shown on first login, so a claim the owner did not make is visible and the answer is wipe and redeploy). Two further restart facts: the code must not be persisted anywhere that survives enrolment; and after `docker compose down -v` (or `docker volume rm`) the database is empty, no user exists, and enrolment **silently reopens**, which the API must present as "no owner enrolled" rather than a login page. One more reopening nobody will expect: the compose project name is a hash of the **AppHost path**, so moving or renaming the repository directory and deploying again creates `aspire-compose-<newhash>_portfolio-dev-pgdata`, an empty database, and a fresh enrolment, with the old data orphaned in the old volume | `EXECUTED` `net localgroup docker-users`; `EXECUTED` compose file (no console; `.env.*` keys present); `SOURCE@` DockerComposeEnvironmentResource.cs project-name derivation; `EXECUTED` volume listing | Reword the slice's requirement from "must not leave a window in which any local process can claim the account" to "the window is bounded to [TTL] from first start, the code is single-use, and the claim is attested in `audit_event` and shown to the owner". Then choose the mechanism (Decision D2). Add to the runbook: the three events that reopen enrolment (fresh volume, `down -v`, moved repository), and what the owner should see when it has | step 1 design; `slice-03.md` "The build" item 1; `README.md` "Running it" |
| OPS-5 | SHOULD FIX (step 2) | **No way to end a session the owner does not hold.** If the cookie carries the ticket (Identity's default), a stolen cookie is valid until expiry; "sign out everywhere" via the security stamp is honoured at the next validation, every **30 minutes** by default. If the slice's `sessions` table is server-side (`ITicketStore` keyed by it, the cookie holding only an id), revocation is one row and immediate. Either way the runbook needs a revoke-all action that does **not** require a browser session, because the scenario is "someone else has the session" | `DOCS@` ValidationInterval default; slice-03.md data row (sessions table named, shape unstated) | Decision D4. Whichever shape: a `docker compose run --rm api revoke-sessions` (or equivalent) that bumps the security stamp and/or truncates sessions, written as an audit event; if stateless, set `ValidationInterval` low (minutes) and say so | step 2; runbook |
| OPS-6 | SHOULD FIX (step 1 schema; step 6/7 runbook) | **Lockout with one user and no email: lose the passkey and forget the password, and there is no path back.** The only recoveries are Docker-side: a CLI mode in the Api image (`docker compose run --rm api reset-owner`: the entrypoint is `dotnet /app/Api.dll`, arguments pass through) that clears credentials and reopens enrolment with an audit event; or `docker exec postgres psql` deleting the user (needs `POSTGRES_PASSWORD` from `.env.Production`; `--auth-local=scram-sha-256` means even the socket asks). **Schema constraint:** if `audit_event` references the user row with a `RESTRICT` foreign key, the user cannot be deleted while any audit event exists, so the psql path is closed; record the actor as a value, as `ingest_run` records its data, not as an FK into a deletable row. Last resort is the volume wipe, which is the financial record | `EXECUTED` image entrypoint; `EXECUTED` compose `POSTGRES_INITDB_ARGS`; slice-03.md step 1 ("append-only `audit_event`") | Design `audit_event.actor` as a value; write the reset path as a command and test it (a recovery nobody has run is OPS's version of "a guard nobody has watched fail", `environments.md`). Note: reset reopens enrolment, OPS-4 applies | step 1; step 7 runbook |
| OPS-7 | SHOULD FIX (step 5) | **Where logs go, how long they live, and what the redaction filter must cover.** Published stack: console provider → stdout → Docker `local` driver, 100 MB rotating per container, destroyed with the container (every recreate), readable by `docker-users`. No OTLP endpoint is set, so the OpenTelemetry exporter is not registered and nothing leaves the container; under `aspire run` the OTel logging provider **is** exporting to the dashboard, so the filter must sit on `ILogger` (all providers), not on the console formatter. Coverage the filter and DoD 7's test must name: (i) hosting's "Request starting" line logs the **query string** verbatim; it is suppressed today only because `Microsoft.AspNetCore` is at `Warning`, so a test must pin that an `Information` override still leaks nothing secret, and no auth endpoint may take a secret in a query string (the `security.md` banner's own missing item); (ii) EF Core's `Executed DbCommand` at `Information` masks parameter values unless `EnableSensitiveDataLogging` is on (it is not, anywhere); assert it is off; (iii) Production returns ProblemDetails with no exception detail, `Development` returns the developer page: DoD 7's capture must run against **Production** configuration; (iv) OTel traces under `aspire run` carry request attributes including the query (Gate G7); (v) the rate limiter's `OnRejected` log must not echo the partition key or body; (vi) a logged setup code (OPS-4) stays in `docker logs` until rotation or recreate, which is fine only if it is single-use | `EXECUTED` `docker info`, compose env, grep; `DOCS@` local driver; `SOURCE@` HostingRequestStartingLog.cs; `DOCS@` error-handling; `INFERRED` (ii), (iv) | Step 5: name the six surfaces in the redaction test; run the injection test with `ASPNETCORE_ENVIRONMENT=Production`; pin `Microsoft.AspNetCore` level in a test; add "logs are in `docker logs`, 100 MB, lost on recreate" to the runbook | step 5; `src/ServiceDefaults/Extensions.cs`; `src/Api/appsettings.json` |
| OPS-8 | SHOULD FIX (step 7) | **Migration 5 on the existing volume, and the roles it runs as.** The migrator applies every pending migration at every `up` (fine: slice-02 data stays, Identity tables are added). But both migrator and API connect as the `postgres` **superuser**, so the `audit_event` triggers (the `ingest_run` pattern) refuse `UPDATE`/`DELETE`/`TRUNCATE` from the application's code paths only; the same role may `ALTER TABLE audit_event DISABLE TRIGGER ALL`. `security.md` "Database" specifies a non-superuser app role; it is unbuilt and this slice is not commissioned against that section. The DoD 7 wording "refuses UPDATE/DELETE/TRUNCATE" is true as a trigger and false as a boundary. Rollback: Migration 5's `Down` drops the credential tables; the upgrade path `deployment.md` describes (back up, migrate) has no backup command to run | `EXECUTED` compose env (same `postgres` user everywhere); `EXECUTED` IngestRun migration trigger; `EXECUTED` grep (no backup tooling); `security.md` "Database" | Either build the app role now (Decision D6) or state in the assessment that immutability is against code, not against the connection string. Before migration 5 touches a volume the owner cares about, there must be a `pg_dump` command that works against this stack (Decision D5) | step 1 migration; step 7; `slice-03.md` DoD 7 |
| OPS-9 | SHOULD FIX (step 7 runbook) | **A backup now contains credentials and sessions.** Post-slice, `pg_dump` output holds: the argon2id hash (offline-crackable at 64 MiB/t=3 if the password is weak; the parameters are the only defence), passkey public keys and sign counters (not secret), the `sessions` rows (if server-side tokens are stored raw, a backup is a set of live sessions: store a hash of the session id), and `audit_event` (login times and addresses). `environments.md` already requires backups outside the repository and git-ignored (`*.dump`, `*.sql.gz`, `*.bak` are); restoring an older backup resets nothing about the key ring (OPS-3) so sessions are dead after restore regardless | `EXECUTED` `.gitignore`; slice-03.md data row | Store session identifiers hashed; say in the runbook that a dump is now a credential file; if sessions are server-side, "revoke all" after any restore | step 1 schema; runbook |
| OPS-10 | NOTE (step 3 headers; step 7) | **TLS and HSTS on `http://localhost`.** Spike H already observed WebAuthn succeed on `http://localhost` and refuse on `http://127.0.0.1` ("invalid domain"), so the published stack is reachable **only** as `localhost`; `AllowedHosts` still admits `127.0.0.1` and `[::1]`, and a user who types `127.0.0.1:8080` gets a login that cannot work. The compose port map is `127.0.0.1:8080` (IPv4 only); a browser resolving `localhost` to `::1` first must fall back (Gate G5; slice 02's published-stack check worked, per README). **HSTS must never be emitted for `localhost`**: a UA ignores it over http today, but the moment TLS is ever added, one accepted `Strict-Transport-Security` on `localhost` pins **every port** of `localhost` (Vite, the Aspire dashboard, every spike) to https for `max-age`. `security.md` says "HSTS where TLS terminates"; nowhere here, so the header is simply absent, and the CSP/header test should assert its **absence** on the published stack | Spike H `EXECUTED`; `EXECUTED` compose ports, `appsettings.json`; `DOCS@` RFC 6797 §8.3/§8.1; `DOCS@` MDN Secure/localhost | Drop `127.0.0.1;[::1]` from `AllowedHosts` or redirect them to `localhost` (Spike I decides); assert no HSTS header on the published stack; Decision D1 for TLS itself | step 3; `src/Api/appsettings.json:8`; Spike I |
| OPS-11 | NOTE | **Where the database password rests** after this slice: `aspire-output/.env.Production` and `.env.Development` (both present), AppHost user-secrets `Parameters:postgres-password` (unencrypted; `security.md` banner `:71`), and the environment of all three containers (`docker inspect`). All readable by the same account that holds Docker access, which can also `docker exec` into postgres, read the key ring (OPS-3) and read `docker logs`. The honest statement of what the tier defends against on this machine: **the browser** (other sites, extensions, XSS, CSRF) and a process without Docker access; **not** a process running as the owner with Docker access, which the threat model already excludes ("a compromised host with an active attacker at the keyboard"). The slice's "any local process" should say which processes | `EXECUTED` (file and key listings; no values); `EXECUTED` `docker-users` | State the boundary in the slice and the assessment: Docker-daemon access is root-equivalent for this stack. Nothing to build | `slice-03.md` "What the slice is" |
| OPS-12 | NOTE | **No `/health` in the published stack, and no container healthcheck.** `MapDefaultEndpoints` maps `/health` and `/alive` only in Development; the compose container runs as Production; the publisher writes no healthcheck for the api. `/api/v1/system/health` exists in every environment. DoD 2's anonymous "health" must name which path(s); the published stack's only sign of an OPS-2 restart loop is `docker ps` STATUS. Separately: the published environment is **Production** by name while the database is `portfolio_dev` on `portfolio-dev-pgdata`; `environments.md` wants the environment on the login screen before authentication, and this stack will say "Production" over DEV data | `EXECUTED` `Extensions.cs`, `Endpoints.cs`, compose file | Name the anonymous health path(s) in DoD 2; consider a compose healthcheck on the api (the image has no curl/wget; a `dotnet`-based probe or a tiny endpoint hit from the host is the cheap way); decide what the login screen's environment label says for this stack | step 2 DoD 2; step 4 login page; `src/AppHost/AppHost.cs` |

---

## Decisions for the owner

### D1. TLS for the published stack: require it, or not

What the slice assumes today, and what holds it up: WebAuthn needs a secure context, and
`http://localhost` is one (Spike H observed `isSecureContext === true` and both ceremonies
passing; Spike I owns the claim); a `Secure` cookie is accepted over `http://localhost` (MDN
states the https requirement is ignored for localhost; Spike I proves it in both browsers);
HSTS is absent (OPS-10). Against the threat model, loopback traffic crosses no network, so
TLS buys nothing against any listed threat; it buys parity with `deployment.md`'s "exposing
it beyond localhost" path, and independence from browsers' localhost carve-outs.

| Option | What it costs | What it changes |
|---|---|---|
| **(a) No TLS** (the slice as written) | Nothing. | Depends on two browser carve-outs for `localhost` (Spike I). The app is reachable only as `localhost`, never by IP. HSTS never. |
| **(b) Kestrel with the dev certificate in the container** | `dotnet dev-certs https -ep <pfx> -p <pw>` once; the PFX bind-mounted or copied in; `ASPNETCORE_Kestrel__Certificates__Default__Path/Password` in the container env, so **a second secret in `.env.Production`**; cert is per machine and expires yearly; the browser trusts it only where `--trust` was run; port 8443 mapping; HTTP→HTTPS redirect or a closed 8080. | Real `Secure` semantics; `isSecureContext` without the carve-out; the HSTS trap (OPS-10) becomes live. |
| **(c) mkcert or an owner-made CA** | As (b) plus one more tool and a root in the Windows store; cert renewal by hand. | As (b), with a cert that can name `127.0.0.1` too (WebAuthn still needs `localhost`). |
| **(d) A reverse proxy container (Caddy `tls internal`, nginx)** | One more image to pin by digest, memory-limit and restart; Caddy's root CA exported from the container and installed by hand; now forwarded headers are genuinely needed, with `KnownProxies` set to that one container (OPS-1 then becomes "configure it correctly" rather than "turn it off"); two hops to debug. | The shape `deployment.md` prescribes for LAN exposure, rehearsed early. |

My mark, as operator: (a) for this slice, with Spike I's proof recorded and the HSTS
absence asserted. (d) is the right shape for the day the API leaves loopback, which is its
own commission.

### D2. Where the first-run setup code appears, and what "no window" means

All four mechanisms are readable by the same account (OPS-4); they differ in window length
and in whether an unowned API ever serves a browser.

| Option | Cost | Window |
|---|---|---|
| **(a) Code in the log** (`docker logs api`, and the dashboard under `aspire run`) | Smallest: one log line, a TTL, single-use, an audit event on use. | From first start until the owner enrols or the TTL expires; regenerated at each restart while no user exists. Stays in `docker logs` until rotation. |
| **(b) An Aspire secret parameter** (`AddParameter("setup-code", secret: true)`) | Lands in user-secrets and `.env.Production`, i.e. persisted; must be invalidated after use or it is a permanent second credential. | Same readers, longer life. Worse. |
| **(c) Enrolment by command, not by browser**: `docker compose run --rm api enrol` sets the password (and prints nothing); the passkey is added from the browser after the first login | A CLI mode in the Api (`Program.cs` branching on `args`), tests for it; the published stack's "first run" becomes two commands. Passkey-first enrolment is not possible this way (the ceremony needs the browser). | **None in the browser**: the API never serves with no owner. The window moves to "the password set at the command line", which only Docker access can do. |
| **(d) A bind-mounted file the owner creates** | Compose volume for a host path; Windows path quoting in the generated file; one more thing `aspire deploy` must preserve. | Same as (a). |

Whatever is chosen, the reopening events (fresh volume, `down -v`, moved repository) need
the same answer, and the UI must say "no owner enrolled" rather than show a login.

### D3. Where the data-protection key ring lives

| Option | Cost | Consequence |
|---|---|---|
| **(a) Status quo: container writable layer** | Nothing. | Signed out and CSRF-stale on every redeploy and every `down`/`up` (OPS-3). Keys unencrypted in the VM disk. |
| **(b) A named volume** (`PersistKeysToFileSystem("/keys")`, `service.Volumes` added in `PublishAsDockerComposeService`, plus the same path under `aspire run` or the default Windows DPAPI-protected profile there) | A second named volume to document (`down -v` destroys it too), one line of configuration, no encryption at rest off Windows unless an `X509` protector is added (another secret). | Sessions survive redeploys. A disk-image thief has keys and database; without the running API on the owner's loopback the keys forge nothing usable. |
| **(c) In Postgres** (`PersistKeysToDbContext<PortfolioDbContext>`; one more table in migration 5) | One table; a backup now carries the key ring (OPS-9); the Migrator and tests see one more entity. | One volume to back up, restore and lose. Keys travel with every dump. |

### D4. Sessions: stateless cookie or the `sessions` table

| Option | Cost | Consequence |
|---|---|---|
| **Stateless** (Identity default) | None beyond Identity. | Revocation waits for the security-stamp interval (30 min default; settable). The `sessions` table in the slice's data row then holds nothing the cookie does not. |
| **Server-side** (`ITicketStore` on the `sessions` table, cookie holds an id) | A store implementation (~100 lines), a DB read per request, expiry sweep. | Immediate revocation; "sign out everywhere" is a `DELETE`; the table has a job; store the id hashed (OPS-9). |

### D5. A backup command before migration 5 runs on a volume that matters

`environments.md` and `deployment.md` require one; none exists. With invented data only, the
cost of not having it is a re-import. With the first real balance (its own commission) it is
the whole record. Options: write `pg_dump`/`pg_restore` commands against this stack now (an
hour, plus the restore test `environments.md` asks for), or record it as owed by the real-data
commission. The slice's data row adds credentials to what a dump contains either way.

### D6. The non-superuser application role

`security.md` "Database" specifies it; the compose stack connects as `postgres`. Building it
in this slice means a second role and password (one more generated secret in `.env.*`),
`GRANT`s in migration 5, and the Migrator keeping the superuser. Not building it means the
`audit_event` immutability DoD is a statement about code paths and should say so.

---

## Gates

Load-bearing claims at `INFERRED`; each names the check that raises it.

- **G1. Concurrent argon2id verifications versus the 512M limit.** Spike H measured one hash
  in a 512m container, never N at once, and whether libsodium's buffer is native (outside the
  GC cap) or managed changes only the arithmetic, not the cgroup. **Check:** in the published
  container, fire N parallel password logins for N = 2, 4, 6, 8 and watch `docker stats` /
  `memory.peak`; record where it dies; set OPS-2's permit from that with the import peak
  subtracted. Owner: step 3.
- **G2. An OOM kill is restarted by `unless-stopped`.** Docker's text says "if it stops";
  exit 137 is a stop, but it is not stated. **Check:** after G1's kill, `docker inspect
  --format '{{.State.OOMKilled}} {{.RestartCount}}' <api>`; expect `true` and a count > 0.
  Owner: step 3/7.
- **G3. All host-originated requests reach the API from one source address** (Docker
  Desktop's bridge gateway), so a per-IP partition is one bucket for every local client even
  with OPS-1 fixed. **Check:** one request from the host, read the connection address the
  audit event records. Owner: step 5. (If wrong, OPS-2's "global, not partitioned" still
  stands; only the explanation changes.)
- **G4. EF Core masks parameter values when `EnableSensitiveDataLogging` is off.**
  **Check:** DoD 7's injection test captures the `Executed DbCommand` line for the password
  insert. Owner: step 5.
- **G5. `http://localhost:8080` reaches a port published on `127.0.0.1` only** when the
  browser resolves `localhost` to `::1` first. Worked for slice 02's published-stack check
  (README); not asserted for the login flow. **Check:** Spike I runs its browser cases against
  the published port, not only Kestrel direct. Owner: Spike I.
- **G6. `aspire deploy` resolves `POSTGRES_PASSWORD` from the AppHost's user-secrets, so it
  is stable across deploys and still matches the password baked into the existing volume at
  `initdb`.** Evidence: the key `Parameters:postgres-password` exists in user-secrets (mtime
  2026-10-20); two deploys on 2026-10-18 share one compose volume. If wrong, a redeploy breaks
  database authentication against the existing volume and the migrator fails, which is the
  "API never starts" path. **Check:** read `DockerComposeEnvironmentResource`'s deploy step
  for where parameter values come from, or deploy twice against the same volume and watch the
  migrator. Owner: step 7.
- **G7. OpenTelemetry's ASP.NET Core instrumentation records the request query on traces**
  (under `aspire run` these reach the dashboard; in the published stack nothing is exported).
  **Check:** include the OTLP stream in DoD 7's capture, or state that the published stack
  exports nothing and the dashboard is Development-only. Owner: step 5.
- **G8. The `local` log driver is what the published containers actually get.** `docker
  info` reports the daemon default; a compose `logging:` key would override it and the
  generated file has none. **Check:** `docker inspect --format '{{.HostConfig.LogConfig.Type}}'
  <api>` after the next deploy. Owner: step 7.

---

## Axis 2 — scope, buildability, teaching value

**Scope.** The slice's "Deploy" row, "the published stack runs the same tier, on
`http://localhost` with no TLS", is one line for what is, operationally, four changes to the
app model and a runbook: forwarded headers off (OPS-1), a home for the key ring (OPS-3), an
enrolment path that exists without a console (OPS-4), and the three recovery actions
(revoke sessions, reset owner, what reopens enrolment: OPS-5/6). None is large; each is the
kind that is discovered at step 7 if not named at step 1. I would add them to the build
outline as step 7's named items, and leave TLS (D1), the app role (D6) and the backup
command (D5) as recorded decisions rather than silent omissions.

**Buildability.** Everything in the Findings is cheap: one AppHost call, one data-protection
line plus a volume, a concurrency limiter, a CLI branch in `Program.cs`, a schema choice for
`audit_event.actor`. The one measurement (G1) is an afternoon. The expensive items are all in
Decisions, and all deferrable without weakening the tier on loopback.

**Teaching value.** Three things this slice can teach that the first two did not, each with
a commit to point at: (1) the forwarded-headers flag as the canonical "the published artifact
is not the thing you ran" defect: invisible under `aspire run`, decisive in Compose, and
Aspire ships the switch for it; (2) a security control that creates an availability hole:
argon2id's memory cost times concurrency times a restart policy equals a one-request-per-
second denial of service, and the fix is bounding **concurrency**, not **rate**; (3) the
data-protection key ring as the state nobody lists: where ASP.NET keeps the secret that every
cookie depends on, and what a container recreate does to it. The enrolment question (OPS-4)
teaches something less comfortable and worth writing down in the assessment: on a single-user
machine the security tier's real boundary is Docker-daemon access, and the slice should say
so rather than promise a window it cannot close.
