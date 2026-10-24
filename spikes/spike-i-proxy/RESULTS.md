# Spike I remainder — gate G-5: a passkey through the Vite dev proxy

**Ran:** 2026-10-24. **Command:** `./run.sh` [`EXECUTED 2026-10-24`].

**What it started:**
- a throwaway `postgres:18.3`;
- `SpikeI.Web` on `http://localhost:5297`;
- two Vite 8 dev servers (the copy installed in `web/`), each proxying `/api` as
  `web/vite.config.ts` does (`changeOrigin: true`, `cors: false`):
  - A on 5295, the **only** origin on the API's `Spike:AllowedOrigins`;
  - B on 5296.

`SpikeI.Driver` drove all three origins in Chromium (Playwright 1.63.0), with a CDP virtual
authenticator on one page.

**Against:** .NET 10.0.13, Windows. Invented users, no data.

**Configured as `slice-03.md` step 2 specifies:**
- `ServerDomain = "localhost"`;
- `ValidateOrigin` comparing against configured origins and rejecting `crossOrigin`;
- `AuthenticatorTimeout`, and the `TwoFactorUserId` challenge cookie, both 10 minutes;
- every Identity cookie `__Host-`, `Strict`, `Secure`-always and `HttpOnly`;
- the session cookie made persistent in `OnSigningIn`.

**Result: G-5 is raised to EXECUTED.** The passkey ceremony passes Identity's origin check
through the Vite proxy, and the check is live: every origin not on the list was refused by it.

---

## 1. What happened at each origin

| Origin | Register | `/me` before | Sign in | `/me` after |
|---|---|---|---|---|
| **A** `http://localhost:5295`, through Vite, allowed | **`200`** | `401` | **`200`** | **`200`**, `spike-a` |
| **B** `http://localhost:5296`, through Vite, not allowed | **`400`** "The authenticator response had an invalid origin 'http://localhost:5296'" | `401` | **`401`**, refused by `ValidateOrigin` | `401` |
| The API's own port `http://localhost:5297`, direct, not allowed | (not attempted) | — | **`401`**, refused by `ValidateOrigin` | `401` |

`window.isSecureContext` was `true` at all three.

## 2. What the API saw

These are from the API's own log, which records each request's `Origin` and `Host`, each
`ValidateOrigin` verdict, and each `Set-Cookie` without its value.

- **The proxy forwards `Origin` unchanged and rewrites `Host`.** A's requests arrived with
  `Origin=http://localhost:5295` and `Host=localhost:5297`. With `ServerDomain` pinned, the
  rewritten `Host` has no effect on the RP ID. That confirms the committee's browser-side F4
  from the server side.
- **`ValidateOrigin` saw `clientDataJSON`'s origin**, not the proxy's target: `5295` →
  accept; `5296` → REJECT (sign-in and registration); `5297` → REJECT. `crossOrigin` was
  `false` and `topOrigin` absent in every case.
- **The session cookie:**
  `__Host-portfolio; expires=<now + 7 days>; path=/; secure; samesite=strict; httponly`.
  The browser stored it as `secure`, `httpOnly`, `sameSite=Strict`, expiring in 7 days. So
  `OnSigningIn` setting `IsPersistent` does make the passkey session persistent (round 2
  `SRC-2`), and `__Host-` with `Secure` is accepted over `http://localhost` through the proxy.
- **The challenge cookie:** `__Host-portfolio-challenge; path=/; secure; samesite=strict;
  httponly`. It was set on each options call and expired on each finish call. It has no
  `expires`, so it is a browser-session cookie, and the 10-minute limit is the ticket's
  lifetime inside it.
- **No exception** was logged in any run.

## 3. Found on the way

**A refused registration still leaves a credential in the authenticator.** The first full
run registered at B before signing in on the API's own port. That sign-in was refused before
`ValidateOrigin` ran, with "The provided credential does not belong to the specified user."

The browser had kept the passkey from B's refused registration and offered it first.
Spike H's options endpoint had already created user `spike-b` for it. So the server held a
user with no passkey, and the authenticator held a passkey with no server record.

That is the defect round 1's `C3-3` names, observed rather than argued: **nothing may exist
before the ceremony succeeds.** The spec's enrolment order creates the user and stores the
passkey together, inside one transaction, only after a valid attestation. The run order in
§1 puts B's registration last, so it cannot mask the other cases. Which check Identity runs
first, ownership or origin, was not read [`INFERRED`]; nothing in the design depends on it.

For the platform, a refused enrolment can still leave a passkey on the user's device that
the server does not know. The sign-in page should therefore treat "does not belong" as
"this passkey is not registered here", not as an error. That text is step 4's.

## 4. What this does not show

- **The published port (`G-12`)** needs the published stack, and runs in step 7.
- **Whether Aspire gives Vite the same port on every run (`SRC-12`)** was not tested here.
  The ports were fixed by `run.sh`. Step 2 takes the allowed origins from configuration, so
  the design does not depend on it.
- **Firefox:** there is no virtual authenticator there (`D14`, `C3-49`).
