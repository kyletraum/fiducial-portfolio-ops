# Spike J — CSRF tokens in a single-page app, beside slice 02's two layers

**Ran:** 2026-10-24. **Command:** `./run.sh` [`EXECUTED 2026-10-24`].

**What it ran:**
- `SpikeJ.Web` on `http://localhost:5293` (the app's page and API) and `:5294` (an attacker
  page, the same site);
- `SpikeJ.Driver` in Chromium (Playwright 1.63.0).

**Against:** .NET 10.0.13, Windows. No database: the token binds to any authenticated
principal, so a spike-only route signs in a named cookie principal. The passkey ceremony is
Spikes H and I.

**Configured as `slice-03.md` step 3 specifies:**
- headers in `OnStarting`;
- explicit `UseAuthentication`/`UseAuthorization`;
- the slice's endpoint filter on `/api`, calling `IAntiforgery.ValidateRequestAsync` for
  every method but `GET`/`HEAD`/`OPTIONS`/`TRACE` and refusing `403` with its own title;
- the anonymous sign-in route exempt (round 2 `R1`);
- `HeaderName = "X-CSRF-TOKEN"`, `SuppressReadingTokenFromFormBody`,
  `SuppressXFrameOptionsHeader`;
- the antiforgery cookie not `__Host-`.

**The control:** a `/fw` group with only the framework's `UseAntiforgery()` and
`RequireAntiforgeryToken` metadata.

**Each case** reads the API's write counters before and after, so "nothing written" is a
number.

**Result:**
- **Claims 1 and 2 hold, as reworded:** validation *and refusal* cover every unsafe method,
  the raw upload and `DELETE` included, but only through the slice's own filter. **C3-1 is
  observed:** the framework's own middleware lets every one of those writes run.
- **Claim 3 is confirmed from the server side.**
- **G-20 holds.**
- **G-19 does not:** the test cannot read an opaque response's body. That changes DoD 4's
  oracle.

---

## 1. The framework alone (`/fw`): validates, never refuses (`C3-1`)

| Case | Status | Written |
|---|---|---|
| `POST` JSON, no token, anonymous | `200` | **yes** |
| `DELETE`, no token, anonymous | `200` | **yes** |
| `POST` raw `text/csv`, no token, anonymous | `200` | **yes** |
| `POST` JSON, **wrong** token, signed in | `200` | **yes** |
| The same-site attack page's `no-cors` upload, signed in | `200` | **yes** |

The last row is a working CSRF attack against an endpoint "protected" by the framework's
metadata. That is exactly what round 1's blocking finding predicted from the source.

## 2. The slice's filter (`/api`)

| Case | Status | Written |
|---|---|---|
| Token endpoint, no session | `401` | — |
| `POST` JSON, no token, anonymous | **`403`** | no |
| Sign-in (exempt), no token | `200` | — |
| `POST` JSON: no token / wrong token / token | **`403` / `403`** / `200` | no / no / yes |
| `PUT` JSON: no token / token | **`403`** / `200` | no / yes |
| `DELETE`: no token / token | **`403`** / `200` | no / yes |
| `POST` raw `text/csv`: no token / token | **`403`** / `200` (7 bytes intact) | no / yes |
| `POST` form-urlencoded, token in the header | `200` | yes |
| `POST` form-urlencoded, token **only in the form body** | **`403`** | no |
| `GET`, no token (safe method) | `200` | — |
| Signed in as Bob, **Alice's** token | **`403`**: "meant for a different claims-based user" | no |
| Bob's own token | `200` | yes |
| Sign-out: no token / token | **`403`** / `200` | — |
| After sign-out, Bob's old token | **`403`** | no |

Every `403` carried the title "CSRF token missing or invalid". The API logged the
framework's reason for each:
- cookie not present;
- header not present;
- token could not be decrypted;
- meant for a different user.

That last reason is the token binding to the claim UID (`C3-22`): the page must fetch a
token after every sign-in. A token issued after sign-in validated with the filter placed
after authentication, which **raises `G-20`**.

## 3. The same-site attacker, and what a test can see (`G-19`)

The attacker page is `http://localhost:5294`, while Alice is signed in at `:5293`. It sends a
`no-cors` `POST`, `text/plain`, to the upload endpoints.

| | `/api/upload` (the filter) | `/fw/upload` (the framework) |
|---|---|---|
| What the page saw | `type=opaque`, `status=0` | the same |
| **Server:** session cookie arrived | **true**, `Sec-Fetch-Site: same-site` | **true** |
| Playwright `response.Status` | `403` | `200` |
| Playwright `response.TextAsync()` | **threw**: "No data found for resource with given identifier" | **threw**, the same |
| Playwright response headers | the filter's `X-Refused-By` header **absent** | — |
| Playwright request headers | `content-type`, `referer`, `sec-ch-ua*`, `user-agent`: **no `Cookie`, `Origin` or `Sec-Fetch-*`** | the same |
| Written | no | **yes** |

**What follows:**
- **Claim 3 is confirmed from the server.** The `Strict` session cookie crosses `localhost`
  ports and authenticates the attacker's request, as the committee's browser probe (BRW F7)
  showed from the browser side.
- **`G-19` is false.** Playwright cannot read the body of a response the page cannot read,
  so a `ProblemDetails.title` cannot identify the refusing layer in the browser test.
- **`GT-6` is false for this case.** Playwright does not report the cookie Chromium sent on
  such a request, so "the cookie is proven sent" cannot be read from Playwright either.
- **DoD 4's browser half reads both from the API.** Each refusal logs its layer and whether
  the request authenticated, and the E2E reads that through the test builder's resource log.
  That makes `G-21` load-bearing for DoD 4. Playwright's status is still asserted. The
  `X-Refused-By` header was a probe, not a recommendation.

## 4. What this does not show

- **Slice 02's layers beside the filter.** This spike has no content-type allowlist and no
  `Sec-Fetch-Site` refusal, so the filter is the first refusal every case meets. In the
  platform, layer 2 refuses the same-site attack first (D12); the integration half of DoD 4
  shows the filter alone.
- **The Identity application cookie.** A plain cookie scheme stands in for it. The token's
  binding is to the `NameIdentifier` claim, which Identity's principal carries too.
- **Whether `G-21` holds** (`ResourceLoggerService.WatchAsync` yields the API's console
  lines under the test builder). It is owed at step 4, where DoD 4's E2E is written.
