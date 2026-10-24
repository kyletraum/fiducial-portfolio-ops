# Spike H — what does it take to do WebAuthn and argon2id in .NET 10?

**Ran:** 2026-10-21. **Commands:** `./run.sh` (passkeys: a throwaway `postgres:18.3`,
`SpikeH.Web` on `http://localhost:5299`, `SpikeH.Driver` in Chromium), and
`dotnet run -c Release` in `SpikeH.Argon2` (argon2id). The argon2id quick mode was also
run in `mcr.microsoft.com/dotnet/sdk:10.0` under `--memory 512m`. [`EXECUTED 2026-10-21`]
**Against:** .NET SDK 10.0 (runtime 10.0.13), Windows, 12 logical CPUs; Linux 6.6 in
Docker Desktop for the container run. Invented user, invented password.
**Result:**
- **Passkeys: ASP.NET Core 10 Identity's in-box support, with no library.** It registered a
  passkey and signed in with it end to end, in a real browser, against Postgres.
- **argon2id: Geralt 4.4.0, at 64 MiB, three passes, one lane.**

---

## 1. The three claims

| Claim (`slice-03.md` Spike H) | Verdict |
|---|---|
| **(1)** ASP.NET Core 10's Identity has built-in passkey support | **HOLDS.** `SignInManager` has `MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`, `MakePasskeyRequestOptionsAsync` and `PasskeySignInAsync`. `UserManager` has `AddOrUpdatePasskeyAsync`, `GetPasskeysAsync`, `FindByPasskeyIdAsync` and `RemovePasskeyAsync`. `IdentityPasskeyOptions` covers the server domain, user verification, resident keys, attestation, the allowed algorithms, and origin and attestation-statement validation [`SOURCE@Microsoft.AspNetCore.App.Ref 10.0.13`, the reference documentation]. **Run end to end**, in §2 |
| **(2)** A maintained library (`Fido2NetLib`) is the alternative if Identity's is unfit | **Not needed, and not run.** Fido2 4.3.1 (2026-10-16, net10.0) is maintained [`EXECUTED` — the nuget.org registration index]. Identity's handler passed, and a dependency needs a reason. Slice 02's OFX decision used the same test: take one only on evidence |
| **(3)** .NET has no argon2id in the box | **HOLDS.** No `Argon2` type appears anywhere in the 10.0.13 reference documentation for `Microsoft.NETCore.App` [`SOURCE@Microsoft.NETCore.App.Ref 10.0.13`]. Identity's default password hasher is PBKDF2 |

## 2. Passkeys, end to end (`run.sh`)

Chromium (Playwright 1.63.0) with a **CDP virtual authenticator**: CTAP2, internal
transport, resident key, user verification. The page calls `navigator.credentials.create()`
and `get()`, and the authenticator answers them. Identity's EF store, schema
`IdentitySchemaVersions.Version3`, which carries passkeys, ran on a throwaway PostgreSQL 18.3.

| Origin | Register | `/me`, signed out | Sign in | `/me`, signed in |
|---|---|---|---|---|
| `http://localhost:5299` | `200`, 32-byte credential stored | **`401`** | **`200`** | **`200`**, `spike-user` |
| `http://127.0.0.1:5299` | **`SecurityError: This is an invalid domain.`**, from the browser | `401` | **the same `SecurityError`** | `401` |

`window.isSecureContext` was `true` on both origins. Two Spike I claims were observed early,
and Spike I still owns them: `http://localhost` is a secure context for WebAuthn, and **an IP
address cannot be the relying party**, so the browser refuses before any request is sent.

**The virtual authenticator works.** The slice-03 E2E can drive passkeys in CI-able,
headless Chromium with no device.

### Found on the way

- **The EF in-memory provider cannot hold Identity's passkeys.** The first run used it, and
  registration succeeded. But reading the passkey back threw `NullReferenceException` in
  `IdentityUserPasskeyExtensions.ToUserPasskeyInfo`, and sign-in returned `Failed`. The
  passkey's data is JSON-mapped, and the in-memory provider does not read it back.
  **Integration tests must use real Postgres**, as this repository's already do.
- **Cookie authentication redirects by default.** An anonymous `/me` was `404`: a redirect
  to `/Account/Login`, which does not exist. An API needs `OnRedirectToLogin` answering
  `401`. The second run had it.
- `AddIdentityCore` with `IdentityDbContext` creates the roles tables as well.
  `IdentityUserContext<TUser>` is the smaller shape, for one user with no roles. **Which
  shape the platform takes is step 1's decision.** Only `EnsureCreated` was run here; an EF
  migration of Identity's tables on Npgsql was not.

## 3. argon2id (`SpikeH.Argon2`)

All three hashed, verified the right password and refused a wrong one. Median of five
(three in the container), for one hash:

| Parameters | Geralt 4.4.0 | Sodium.Core 1.4.1 | Konscious 1.3.1 |
|---|---|---|---|
| 19 MiB, t=2 (OWASP's floor) | 21 ms | **refused**: "has to be at least 3" passes | 118 ms |
| 64 MiB, t=2 (libsodium INTERACTIVE) | 67 ms | **refused** | 212 ms |
| **64 MiB, t=3** | **108 ms** | 118 ms | 309 ms |
| 256 MiB, t=3 (libsodium MODERATE) | 825 ms | 837 ms | 2145 ms |
| **64 MiB, t=3, Linux, `--memory 512m`** | **139 ms** | 143 ms | 446 ms |

- **Geralt and Sodium.Core both wrap libsodium**, and both write the standard
  `$argon2id$v=19$m=…,t=…,p=…$salt$hash` string. **Each verifies the other's hashes**, on
  Windows and on Linux. Their native library loaded in the Linux container.
- **Sodium.Core refuses fewer than three passes.** That limit is the wrapper's, not
  argon2id's.
- **Konscious** is managed code, 2.5–3× slower. It has no encoded string, so the salt and
  parameters would be ours to store and parse. Its last release was 2024-06.
- **Not run:** Isopoh (last release 2023-08) and NSec. NSec is libsodium again, by Sodium.Core's
  author, and adds nothing the two wrappers do not.

**Geralt, because:**
- it accepts any valid parameters;
- it has `NeedsRehash(hash, iterations, memorySize)`, which maps directly onto Identity's
  `PasswordVerificationResult.SuccessRehashNeeded`, so raising the cost later upgrades each
  hash at the next login;
- it is span-based, so the password need not become an interned string;
- it was released 2026-07 and builds for .NET 10.

**64 MiB, t=3, p=1** costs about 110 ms here and 140 ms in a container held to the
published API's 512 MB limit. Each hash in flight holds 64 MiB, so the login rate limit is
also what keeps concurrent hashes inside that limit. Step 3 sets the number with that in
mind.

## 4. What step 1 inherits

- **Identity's in-box passkeys:** `SignInManager`/`UserManager` with schema `Version3`. No
  WebAuthn library.
- **A custom `IPasswordHasher<TUser>` on Geralt,** at m=64 MiB, t=3, p=1, with
  `NeedsRehash` wired to `SuccessRehashNeeded`. Identity's PBKDF2 default is replaced, not
  kept beside it.
- **Cookie authentication answering `401`/`403`,** never a redirect.
- **The Identity store shape is open:** `IdentityUserContext<TUser>` inside
  `PortfolioDbContext`, or `IUserPasskeyStore`/`IUserPasswordStore` on the platform's own
  tables. Either needs an EF migration on Npgsql, which this spike did not run.
- **E2E passkeys** through a CDP virtual authenticator, shown working here.
- **The app is reached as `localhost`, never `127.0.0.1`.** That is Spike I's to settle with
  `AllowedHosts`.

## Running it

```sh
./run.sh                                   # passkeys; needs Docker and Playwright's Chromium
cd SpikeH.Argon2 && dotnet run -c Release   # argon2id
```

None of the three projects is in `Portfolio.slnx`, and nothing in `src/` references them.
