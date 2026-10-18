# Spike F — how large a file gets through, and what refuses it?

**Ran:** 2026-10-18. **Command:** `./run.sh` (direct to Kestrel and through the Vite
dev proxy), then `./run.sh container` [`EXECUTED 2026-10-18`].
**Against:** a five-endpoint spike API on .NET 10 (SDK 10.0.402). It ran on Windows,
and in a Linux container built by `dotnet publish -t:PublishContainer` on
`mcr.microsoft.com/dotnet/aspnet:10.0`, bound to loopback. Vite 8.3.4, the copy in
`web/`, was configured with only the `/api` proxy, as `web/vite.config.ts` configures
it. Uploads were filler bytes generated in a temp directory; no data.
**Result:** all three claims hold. Three further findings decide step 3: **the upload
is the raw request body, not a multipart form.**

---

## 1. The claims

| Claim (`slice-02.md` Spike F) | Verdict | Measured |
|---|---|---|
| **(1)** Kestrel refuses a body over 30,000,000 bytes by default; the multipart reader has its own, larger limit | **HOLDS**, first half | Raw body of 30,000,000 bytes: `200`, and `IHttpMaxRequestBodySizeFeature` reports 30,000,000. 30,000,001: `413`. A 30,000,000-byte **file** in a multipart form is refused `413` too, because the form's boundaries push the body over Kestrel's limit before the form reader's own limit is reached. The form reader's limit was **not** reached, so its size is not measured |
| **(2)** A minimal-API endpoint binding `IFormFile` carries antiforgery metadata and fails at request time without antiforgery middleware or an opt-out | **HOLDS** | `500`, `InvalidOperationException: Endpoint HTTP: POST /api/a-iformfile contains anti-forgery metadata, but a middleware was not found that supports anti-forgery`, thrown from `EndpointMiddleware.ThrowMissingAntiforgeryMiddlewareException`. With `.DisableAntiforgery()`: `200` |
| **(3)** The Vite dev proxy forwards a multipart body unchanged | **HOLDS, within limits** | Multipart 1 KiB, 200 KiB and 1 MiB, and raw 1 MiB, arrive at the same byte count through Vite as direct. **Over a limit it does not**: see finding 3 |

## 2. Every case

Status codes. Direct, Vite and container agree except where marked.

| Endpoint | Case | Direct / container | Through Vite |
|---|---|---|---|
| A `IFormFile`, no antiforgery decision | 1 KiB | `500` | `500` |
| B `IFormFile`, antiforgery off | 1 KiB | `200` | `200` |
| B | 200 KiB | `200`, **spilled to disk** | same |
| B | 30,000,000-byte file | `413` | **`502`** |
| C `IFormFile` + body limit 1 MiB + 16 KiB + file-length check | file of exactly 1 MiB | `200`, **spilled** | same |
| C | file of 1 MiB + 1 | `413` (the handler's own check) | `413` |
| C | 30,000,000-byte file | `413` | **`502`** |
| **D raw body**, `text/csv` or `application/x-ofx` only, limit 1 MiB | exactly 1 MiB | `200`, no spill | `200` |
| D | 1 MiB + 1 | `413` | **`502` 19 times in 20**, `413` once |
| D | 1 MiB + 1, chunked (no `Content-Length`) | `413` | `413` |
| D | posted as a multipart form | **`415`** | `415` |
| E raw, server default | 30,000,000 | `200` | `200` |
| E | 30,000,001 | `413` | **`502` 20 times in 20** |

## 3. What the spike found that it was not asked

### Finding 1 — a multipart upload is written to disk

The form reader spills any file part over 64 KiB to an `ASPNETCORE_*.tmp` file in the
temp directory while the request runs. That was **observed**: the handler counted one
such file during the 200 KiB and 1 MiB uploads, and none for 1 KiB. Afterwards the file
is gone. In the container, `/tmp` held only the runtime's three diagnostic pipes before
and after. The raw body never spills: endpoint D read 1 MiB into memory and wrote
nothing.

**Why it matters:** `slice-02.md` says the uploaded file itself is never stored. A
spilled form part is storage, however brief. It lands in the container's writable
layer, and it would survive a crash mid-request. With invented data that costs nothing.
With the first real export it would be a bank file on disk that nothing in this design
accounts for. Raising `MemoryBufferThreshold` would avoid the spill but keep the rest
of the form machinery. The raw body avoids it by construction.

### Finding 2 — the content-type check is the cross-site defence

Endpoint B had to opt out of antiforgery to work at all, and that opt-out is exactly
what ASP.NET's error in claim (2) exists to prevent. A `multipart/form-data` POST is
what a plain HTML form on **any** website can send to `127.0.0.1` without a CORS
preflight. The API is on the viewer's own machine and has no authentication, so nothing
else stands in the way.

Endpoint D accepts only `text/csv` and `application/x-ofx`, and refused the multipart
post with `415`. Neither type is CORS-safelisted. A cross-origin `fetch` carrying one
triggers a preflight, which this API never answers with `Access-Control-Allow-Origin`,
so the browser never sends the POST. An HTML form cannot send either type at all.
[`INFERRED` from the Fetch standard's safelist, which was recalled and not read. The
`415` is `EXECUTED`; a cross-site page was **not** driven in a browser.] `POST
/balances` is already safe the same way, because it takes `application/json`.

### Finding 3 — through the dev proxy, "too large" usually arrives as `502`

Kestrel answers `413` and closes the connection while the client is still sending. A
proxy still forwarding the body sees the close and reports `502 Bad Gateway`. Measured
through Vite: 19 of 20 for a body 1 byte over 1 MiB, 20 of 20 for a body 1 byte over
30,000,000. A chunked body got `413`, because Kestrel ends it in the middle of the
stream. Direct and in the container, `413` every time.

In the published stack there is no proxy, so the user gets the `413`. In development
they get a `502` that says nothing about size. **The browser therefore checks
`File.size` before sending** and says "this file is larger than 1 MiB" itself. The
server limit stays authoritative; the browser check exists to give a useful message.

## 4. Memory, for `S-34`

The spike API in its container: **21.7 MiB idle**, **38.2 MiB** after 50 raw 1 MiB
uploads, **48.1 MiB** after 50 more as multipart. Upload handling at this size is not
what sizes the limit. The real Api also carries EF Core, Npgsql and OpenTelemetry, so
step 7 sets the compose `deploy.resources.limits` from **the real Api's measured
peak** during a 10,000-row import, with a stated margin. Nothing here is a number to
copy.

## 5. What step 3 inherits

- **`POST /api/v1/imports` takes the file as the raw request body.** `Content-Type:
  text/csv` or `application/x-ofx` decides the parser; anything else is `415`. The
  file name travels as a query parameter (`?fileName=`), length-bounded, and is stored
  as data (`FR-1.5`). This replaces the `multipart/form-data` in step 3, which was
  written before the spike.
- **The limit is endpoint metadata**: `RequestSizeLimitAttribute(1 MiB)`, honoured for
  minimal APIs. Shown refusing at 1 MiB + 1 with and without `Content-Length`, before
  the handler reads anything. The body is read into memory inside that bound; no
  temp file, no form reader.
- **No antiforgery is needed and none is disabled.** The endpoint binds no form, so it
  carries no antiforgery metadata. The content-type allowlist is the defence, and step
  5's integration test asserts the `415` for a multipart post.
- **The browser checks `File.size` before upload**, because of finding 3, and sends
  `fetch(url, { method: 'POST', headers: { 'Content-Type': ... }, body: file })`.
- **Not settled here, and a gate on step 3:** how a raw-body endpoint is described in
  the committed OpenAPI document, and whether `openapi-fetch` sends a `File` body
  unserialised. [`INFERRED` that `.Accepts<Stream>("text/csv", "application/x-ofx")`
  describes it and that `bodySerializer` passes it through; neither was run.]

## Running it

```sh
./run.sh             # direct and through Vite; needs .NET 10, and web/'s node_modules (npm ci in web/)
./run.sh container   # the API in a container on loopback; needs Docker
```

Both start and stop everything they use. The container image `spike-f-api:spike`
stays in the local Docker image store; `docker rmi spike-f-api:spike` removes it.
