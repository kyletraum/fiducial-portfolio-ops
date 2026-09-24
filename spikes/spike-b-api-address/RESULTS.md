# Spike B — how the browser learns the API's address

**Ran:** 2026-09-24. [`EXECUTED 2026-09-24`]
**Against:** Aspire CLI `13.5.4+9c1b401`, `aspire-ts-cs-starter` template
13.5.4 with `Aspire.AppHost.Sdk`, `Aspire.Hosting.JavaScript` and
`Aspire.Hosting.Docker` pinned to 13.5.4, .NET SDK 10.0.301, Node 24.16.0,
Docker 29.8.0 (buildx 0.37.1), headless Microsoft Edge, Windows 11.

**Result:** the claim under test is **WRONG**. Service discovery never reaches
the browser. The template's mechanism works in both modes **without** the
experimental API the slice named as the candidate fix: the page calls a
**relative** `/api` path, and something on the page's own origin forwards it.

| Mode | Page origin | Who answers `/api` | Browser result |
|---|---|---|---|
| `aspire run` | Vite dev server, `http://localhost:52844` | Vite's `server.proxy['/api']`, targeting `SERVER_HTTPS \|\| SERVER_HTTP` | `200 server ok` |
| `aspire deploy` → compose | the **server** container, `http://localhost:54455` | the server itself; the built frontend is in its `wwwroot` | `200 server ok` |

`import.meta.env` keys visible to client code in **both** modes:
`BASE_URL, DEV, MODE, PROD, SSR`. No API address, under any name.

This is a spike, not step 4. The app is the template with its weather page
replaced by one that reports the page origin, the result of
`fetch('/api/health')`, and the keys of `import.meta.env`. The server gained one
endpoint, `/api/health`, which echoes the `Host` header it received.

---

## The claim, and what happened

> `plan.md` says service discovery supplies it, so no environment hardcodes an
> origin.

The AppHost is the template's, plus `AddDockerComposeEnvironment("compose")`:

```
server      = AddProject<Server>("server") .WithHttpHealthCheck("/health") .WithExternalHttpEndpoints()
webfrontend = AddViteApp("webfrontend", "../frontend") .WithReference(server) .WaitFor(server)
server.PublishWithContainerFiles(webfrontend, "wwwroot")
```

### Run mode — `aspire run --detach`

- **The address exists only in the Vite process.** `aspire describe webfrontend
  --format Json` shows `SERVER_HTTP`, `SERVER_HTTPS`, `services__server__http__0`
  and `services__server__https__0` in its environment. None is `VITE_`-prefixed,
  so none reaches client code; the rendered page lists only Vite's five built-ins.
- **The page works anyway.** Headless Edge
  (`--headless=new --virtual-time-budget=10000 --dump-dom`) rendered
  `origin: http://localhost:52844` and `api: 200 server ok; host header
  localhost:7304`. The request went to Vite's origin; `changeOrigin: true`
  rewrote the `Host` header to the server's.
- `curl -i http://localhost:52844/api/health` gives the same `200`, which
  confirms it is the dev server's proxy and not the browser doing anything clever.

### Publish — `aspire publish`, then `aspire deploy`

`aspire publish` writes **no `webfrontend` service**. There are two services,
`server` and `compose-dashboard`, and the `.env` has two keys, `SERVER_IMAGE` and
`SERVER_PORT`. The frontend is built in a throwaway image and its output copied
into the server image. `ls /app/wwwroot` in the running container lists
`index.html`, `assets`, `Aspire.png` and `github.svg`.

`aspire deploy -o <scratch>` builds both images and runs `docker compose up`.
Its `push-server` step **only tags locally** (`server:aspire-deploy-<timestamp>`)
when no registry is configured.

- Headless Edge against the published server rendered
  `origin: http://localhost:54455` and `api: 200 server ok; host header
  localhost:54455`. Page and API are **one origin**, so no proxy and no CORS.
- **Every published port binds every interface.** `docker ps`:
  `0.0.0.0:54455->8080/tcp, [::]:54455->8080/tcp` for `server`, and the same
  shape for `compose-dashboard`. `WithExternalHttpEndpoints()` writes
  `ports: - "${SERVER_PORT}"` with no host IP.

---

## What this settles for the slice

1. **Use the template's mechanism, not `PublishAsStaticWebsite`.** The browser
   uses a relative `/api` path everywhere. In run mode Vite proxies it; in
   compose the API serves the page. Nothing configures an origin in either mode,
   which is what `M-4` asks for. It needs **no experimental API**:
   `ASPIREJAVASCRIPT001` does not appear. `PublishAsStaticWebsite` was **not
   run**, because the template's route answered the question first.
2. **API routes must live under `/api`.** The Vite proxy matches that prefix
   only, and in compose the API also serves the page, so a route outside `/api`
   collides with a frontend path. `/health` and `/alive` from
   `MapDefaultEndpoints` sit outside it and are not reachable through the dev
   proxy. That is fine for Aspire's own health probes; the page must not call them.
   [`INFERRED` from `vite.config.ts`'s single `/api` proxy key; not requested.]
3. **DoD 3 needs a step-7 override.** "API on loopback" is not what Aspire
   writes: the published server port binds `0.0.0.0` and `[::]`. So does the
   dashboard. Both are already in `deployment.md`'s banner; now measured on a
   running stack.

## Environment trap met on the way

`aspire deploy` failed first with `ERROR: failed to build: resolve : Access is
denied.` Aspire writes the generated frontend Dockerfile under
`Path.GetTempPath()`, which prefers `TMP`, and in the shell that ran this `TMP`
was `C:\WINDOWS\TEMP`, which that user cannot read. With `TMP` set to the user's
own temp folder it built. This is a property of that shell, not of Aspire;
it is recorded because the error names neither the path nor the variable.

## Not measured

- `PublishAsStaticWebsite` (see point 1).
- HTTPS in compose. The published server listens on HTTP 8080 only.
- A production build under `aspire run`. Run mode uses the Vite dev server.

## Reproducing

```sh
cd spikes/spike-b-api-address/frontend && npm ci && cd ..
aspire run --detach --non-interactive --apphost SpikeB.AppHost/SpikeB.AppHost.csproj
aspire describe --apphost SpikeB.AppHost/SpikeB.AppHost.csproj   # webfrontend URL
msedge --headless=new --user-data-dir=<tmp> --virtual-time-budget=10000 --dump-dom <webfrontend URL>
aspire stop --apphost SpikeB.AppHost/SpikeB.AppHost.csproj

aspire deploy --apphost SpikeB.AppHost/SpikeB.AppHost.csproj -o <outside the repo>
msedge --headless=new --user-data-dir=<tmp> --virtual-time-budget=10000 --dump-dom <server URL>
docker compose -p <project from docker ps> down --rmi local
```
