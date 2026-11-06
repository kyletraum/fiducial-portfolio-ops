# Slice 04: framework and driver source reader seat (SRC)

**Seat:** framework and driver source reader. **Date:** 2026-11-06. **Under review:** `specs/001-portfolio-platform/slice-04.md` at `main` 644efe2 (first draft).

**Pins** (taken from `src/*/obj/project.assets.json`, not just the csproj): Npgsql **10.0.3** (resolved through Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3; Aspire.Npgsql.EntityFrameworkCore.PostgreSQL 13.6.4 does not raise it), EF Core 10.0.14, Aspire 13.6.4 (SDK and every `Aspire.*` package), ASP.NET Core Identity 10.0.14. The AppHost resolves Npgsql 8.0.3 for its own health checks. That copy is irrelevant here.

**Read** (raw.githubusercontent.com / `gh api`, pinned):
- `npgsql/npgsql@v10.0.3` (d376839): `src/Npgsql/NpgsqlCommand.cs`, `NpgsqlDataReader.cs`, `NpgsqlBatch.cs`, `NpgsqlBatchCommand.cs`, `SqlQueryParser.cs`, `NpgsqlConnectionStringBuilder.cs`, `Internal/NpgsqlConnector.cs`, `Internal/NpgsqlReadBuffer.cs`; `test/Npgsql.Tests/SqlQueryParserTests.cs`.
- `npgsql/efcore.pg@v10.0.3` (5e912bf): `Storage/Internal/NpgsqlSqlGenerationHelper.cs`, `Storage/Internal/NpgsqlDataSourceManager.cs`.
- `dotnet/efcore@v10.0.14`: `src/EFCore.Relational/Storage/RelationalSqlGenerationHelper.cs`.
- `dotnet/aspnetcore@v10.0.14` (0ef4bbf): `Identity/Extensions.Core/src/UserManager.cs`, `Identity/EntityFrameworkCore/src/UserOnlyStore.cs`, `UserStore.cs`.
- `dotnet/aspire@v13.6.4` (c893527): `Aspire.Hosting.PostgreSQL/{PostgresBuilderExtensions,PostgresServerResource,PostgresDatabaseResource}.cs`; `Aspire.Hosting/{ConnectionStringBuilderExtensions,ConnectionStringResource,ParameterResourceBuilderExtensions}.cs`, `ApplicationModel/{ParameterResource,ParameterDefault,ReferenceExpression,UserSecretsParameterDefault}.cs`, `Orchestrator/ParameterProcessor.cs`, `Pipelines/Internal/{FileDeploymentStateManager,DeploymentStateManagerBase}.cs`; `Aspire.Hosting.Docker/{DockerComposeServiceResource,DockerComposeServiceResourceExtensions,DockerComposeEnvironmentResource,DockerComposeEnvironmentContext,DockerComposePublishingContext}.cs`; `src/Shared/PasswordGenerator.cs`.
- Repo: `src/AppHost/AppHost.cs`, `src/Api/{Program,AuthEndpoints,OwnerCommands,Endpoints,ImportEndpoints}.cs`, `src/Infrastructure/{PortfolioDbContext,BalanceWrites,BalanceRecorder,ImportPipeline,NetWorthDay}.cs`, `src/Infrastructure/Migrations/*` (triggers, FKs, identity columns), `src/Migrator/Program.cs` (no `appsettings*.json` beside it), `tests/Integration/*Fixture.cs` (connection wiring only), `aspire-output/docker-compose.yaml` (gitignored, generated 2026-10-31; keys read, no secret values).

**Ran** (scratch `…\scratchpad\s4-src\`, nothing in the repo):
- `probe/`: a console app on Npgsql 10.0.3 + EFCore.PG 10.0.3 against a throwaway `postgres:18.3` container (`s4-src-pg`, removed afterwards). Probes P1 to P9, quoted per finding. Output is in `probe/run1.txt` and `run2.txt`.
- `apphost/`: a throwaway Aspire 13.6.4 AppHost. It has Postgres + `AddDatabase`, two `persist: true` generated secret parameters, a `busybox` "migrator" given the passwords by `WithEnvironment`, and a `busybox` "api" given (A) `WithEnvironment("ConnectionStrings__portfolio", ReferenceExpression)` and (B) `WithReference(AddConnectionString(...))`. I ran `aspire publish -o out` (CLI 13.6.4) and read the `docker-compose.yaml` and `.env` it wrote. I did not run `aspire deploy`.

---

## Findings

### SRC-1: a row cap read as "stop after N + 1, then dispose" bounds only memory. Dispose reads the whole result set
**MUST FIX (Spike N 2, step 3, DoD 3).** `EXECUTED` (P4a–c). `SOURCE@npgsql/npgsql@v10.0.3:src/Npgsql/NpgsqlDataReader.cs:956-1000` (`Consume` loops `NextResult` until the result sets are exhausted), `:1081-1110` (`Close` calls `Consume` whenever the reader is not already consumed); `src/Npgsql/NpgsqlCommand.cs:1424-1428` (ExecuteReader's token is scoped to the Execute call only).
**Where:** "Running a query … the rows up to a cap"; Spike N 2; DoD 3 "A result past the cap is cut".

**The defect:**
- Disposing a reader mid-result does **not** stop the query. Npgsql reads and discards every remaining row from the socket. P4a read 101 rows of `SELECT generate_series(1, 30000000)`, then `DisposeAsync` took **4,456 ms**. At that rate the draft's own example, 1e9 rows, is about 2.5 minutes of server and network work after the cap (`INFERRED`, linear extrapolation).
- `CommandTimeout` does not stop the drain, because rows keep arriving (SRC-2).
- Cancelling the `CancellationToken` that was passed to `ExecuteReaderAsync`, after it has returned, does nothing. P4c: still 4,562 ms. Npgsql scopes that token to the Execute call (`NpgsqlCommand.cs:1424-1428`).
- `cmd.Cancel()` before dispose works. P4b: 8 ms. `pg_stat_activity` no longer showed the query, and the same backend PID was reusable (`FullState=Open`).
- The cap cannot bound work done before the first row. `SELECT g FROM generate_series(1, 30000000) g` (a set-returning function in `FROM`, which PostgreSQL materialises) sent **no row** within 2 s (P4a, first run). Only the time bound covers that.

**What to do:** write the read loop as: read up to N + 1 rows, with the request token passed to every `ReadAsync`; then `cmd.Cancel()` before the reader is disposed whenever the cap was hit; then dispose. Add a test for it: the cap is hit on a 1e9-row series, the response returns in well under the time bound, and `pg_stat_activity` shows no row for that query afterwards.

### SRC-2: `CommandTimeout` is a per-read socket timeout, not a wall-clock bound
**MUST FIX (Spike N 1, Q2, DoD 3).** `EXECUTED` (P1–P3). `SOURCE@npgsql/npgsql@v10.0.3`:
- `src/Npgsql/Internal/NpgsqlConnector.cs:2899-2903`: the timeout is set as `ReadBuffer.Timeout` per user action.
- `src/Npgsql/Internal/NpgsqlReadBuffer.cs:309`: `RestartTimeoutWithoutReset` after every successful read.
- `:336-360`: on a timeout, send a PostgreSQL cancel request, then wait `CancellationTimeout`, else `Break`.
- `NpgsqlConnector.cs:2201-2229`: `PerformPostgresCancellation` opens a new connection and sends CancelRequest.
- `:1792-1800`: a `57014` after our cancel surfaces as a timeout or `OperationCanceledException`, and the connector is not broken.
- `NpgsqlConnectionStringBuilder.cs:980-981`: `CancellationTimeout` defaults to 2000 ms.

**Where:** Spike N 1; Q2 ("the role's defaults, the API's command timeout, or both"); DoD 3.

**The defect:**
- The answer to Spike N 1 is half yes:
  - On a timeout or a cancelled token, Npgsql **does** send a protocol cancel, and the server stops the query. P1: `SET statement_timeout=0; SELECT pg_sleep(30)` with `CommandTimeout=2` failed at 2,029 ms with `NpgsqlException(TimeoutException)`. P2: `CancelAfter(2s)` failed at 2,019 ms with `OperationCanceledException(PostgresException 57014)`. In both, `pg_stat_activity` no longer showed the query, and the connection stayed `Open` on the same PID.
  - But the timeout re-arms on every read. P3: six 20 kB rows, one a second, under `CommandTimeout=2`, **completed after 6,012 ms with no timeout**. The same query with small rows timed out only because PostgreSQL buffered them and nothing arrived within 2 s. A query that streams can therefore run without bound under `CommandTimeout` alone.
- If the cancel request goes unanswered within `CancellationTimeout`, Npgsql breaks the connection (`NpgsqlReadBuffer.cs:356-360`). A closed socket does not by itself stop the backend (`INFERRED`; DBS should say whether `client_connection_check_interval` is wanted).
- A `SET` made by user SQL stays on that physical connection after the timeout. After P1, `SHOW statement_timeout` on the same open connection returned `0`. It is reset only when the connection goes back to the pool: Npgsql prepends `DISCARD ALL` or `SET SESSION AUTHORIZATION DEFAULT;RESET ALL;…` (`NpgsqlConnector.cs:516-520,2672,2752-2766`). P5b: after close and reopen, the role default `1s` was back.

**What to do:** the time bound is a wall-clock `CancellationTokenSource.CancelAfter(bound)`, linked to the request token. Pass it to `ExecuteReaderAsync` **and** to every `ReadAsync`, and call `cmd.Cancel()` when it fires during dispose (SRC-1). Keep `ALTER ROLE portfolio_reporting SET statement_timeout` as the server-side second bound (see SRC-3 for why it holds). Open and close one pooled connection per request, never a held connection, so the reset runs between requests. Q2's answer is "both, and `CommandTimeout` is not the API's bound".

### SRC-3: multi-statement text is split client-side and every part runs, and that is how one request disarms the role's timeout. The protocol can refuse it without parsing
**MUST FIX (Spike N 3, Q5, step 3).** `EXECUTED` (P5, P5b). `SOURCE@npgsql/npgsql@v10.0.3`:
- `src/Npgsql/NpgsqlCommand.cs:931-941`: a command with no parameters is parsed like a named-parameter command when `EnableSqlRewriting` is on, which is the default (`:97`).
- `src/Npgsql/SqlQueryParser.cs:137-139`: a `;` at parenthesis depth 0 starts a new statement.
- `NpgsqlCommand.cs:907-929`: positional mode sends the text as is: "If the SQL contains a semicolon … PostgreSQL will error".
- `:931-935`: an `NpgsqlBatchCommand` with no parameters is sent unparsed.
- `SqlQueryParser.cs:471`: a batch command with named parameters and a `;` throws `NotSupportedException`.

**Where:** Spike N 3; Q5 ("A clear refusal is usability; the grants stay the boundary either way"); "No query parsing as a guard".

**The defect:**
- `new NpgsqlCommand("SELECT 1; SELECT 2")` returned **2 result sets** (`Statements.Count=2`, P5).
- As a role with `ALTER ROLE … SET statement_timeout='1s'`, `SET statement_timeout=0; SELECT pg_sleep(3)` **completed in 3,011 ms** (P5b).
- So if Q2 settles on the role default, multi-statement text breaks DoD 3, and Q5's "either way" is false. The grants bound reads and writes. They do not bound time.
- Without multi-statement text, the role default holds. A single statement `SELECT set_config('statement_timeout','0',false), pg_sleep(3)` was cancelled at 1,004 ms. The next statement on the same connection was also cancelled at 1,003 ms: the error rolled the `set_config` back (P5b).

**What to do:** run user SQL as **an `NpgsqlBatch` holding exactly one `NpgsqlBatchCommand`**, with positional parameters or none. Npgsql then sends the text as one Parse message, and **PostgreSQL itself** refuses more than one statement: `42601 cannot insert multiple commands into a prepared statement` (P5, for `SELECT 1; SELECT 2`, `SELECT 1; SET statement_timeout=0` and `SELECT $1; SELECT 2`). That is the server refusing, not a parser, so it satisfies "no query parsing as a guard". A trailing `;` and a trailing `-- comment` are still accepted (P5).

Map `42601` to a clear problem title. Its message is PostgreSQL's own, so passing it on is safe.

Two rejected alternatives:
- **Do not** set the `Npgsql.EnableSqlRewriting=false` AppContext switch. It is process-wide and `static readonly` (`NpgsqlCommand.cs:73-97`). EF Core emits `@name` placeholders: `dotnet/efcore@v10.0.14:src/EFCore.Relational/Storage/RelationalSqlGenerationHelper.cs:77,94`, and EFCore.PG does not override them (`NpgsqlSqlGenerationHelper.cs`). With the switch off, every EF command with a parameter would throw "Named parameters are not supported" (`NpgsqlCommand.cs:944-945`). `INFERRED` from source, not run.
- A positional `NpgsqlCommand` also refuses (P5), but only when the command has at least one parameter. A parameterless one is parsed and split.

Rewrite Q5 as decided ("one statement, refused by the protocol"), and DoD 3 as holding under both bounds.

### SRC-4: `@name` rewriting is literal-aware, but it also rewrites `:name`, and an unmatched placeholder runs as an operator. Positional `$n` is the safer contract
**SHOULD FIX (Spike N 4, step 4, DoD 4).** `EXECUTED` (P6). `SOURCE@npgsql/npgsql@v10.0.3:src/Npgsql/SqlQueryParser.cs`:
- `:115-128`: comments, quoted strings, `E''` strings and dollar quotes are skipped.
- `:129-136`: both `:` (unless it follows `:`) and `@` (unless it follows `@`) start a placeholder.
- `:197-207`: a placeholder that matches no parameter is left in the text.
- `:533-534`: parameter-name characters are letters, digits, `_` and `.`.

**Where:** Spike N 4; step 4 "typed parameters bound by the driver".

**The defect:**
- With parameter `x=42`, `@x` inside `'…'`, `$$…$$`, `$t$…$t$`, `E'\'@x'`, `"a@x"`, `/* */` and `--` was left alone, and the bare `@x` was bound (P6). The draft's worry about comments and literals is unfounded.
- Three real hazards, all shown in P6:
  - `:x` is also a placeholder.
  - `SELECT @y FROM (SELECT -5 AS y) t`, with no parameter `y`, passed through and **returned 5**: PostgreSQL's `@` is the absolute-value operator. A typo in a parameter name becomes a different query, not an error.
  - `(ARRAY[1,2,3])[1:x]`, where `x` is also a parameter name, was rewritten to `[1:$1]` and failed with `42601`.
- Named parameters also force the parser, and with it the client-side semicolon check, onto the SRC-3 path (`SqlQueryParser.cs:471`).

**What to do:** store saved reports with `$1 … $n` and an ordered, typed parameter list, and bind them as positional `NpgsqlParameter { NpgsqlDbType = … }` on the one-command batch of SRC-3. Npgsql does no rewriting there (P6: `SELECT $1, '@x'` gave `42 | @x`). Validate when a report is saved that its parameter count matches the highest `$n` in the text. A Parse with `Describe` would do that server-side, without parsing. DoD 4's `1; DROP TABLE account` test then exercises PostgreSQL's binding, not Npgsql's rewriter.

### SRC-5: the API updates and deletes rows today, so Q1's "no `UPDATE` at all" is false, and the grant list must name each path
**MUST FIX (Q1, step 1, DoD 1, the login table).** `SOURCE` + repo read, and `EXECUTED` for the grant mechanics (P8).
**Where:** Q1 ("Balances supersede rather than update … If none does, the API gets no `UPDATE` at all"); the login table ("Only the table rights the API's code paths use").

**The defect:** these API paths write by `UPDATE` or `DELETE`:

| Path | Statement | Source |
|---|---|---|
| Every restatement, from the API or an import | `UPDATE account_balance SET superseded_at` | `src/Infrastructure/BalanceWrites.cs:55-59` (`ExecuteUpdateAsync`) |
| Enrolment, and **every sign-in** | `UPDATE identity_user` (all columns, concurrency-stamp check), after `AddOrUpdatePasskeyAsync` | `SOURCE@dotnet/aspnetcore@v10.0.14:src/Identity/Extensions.Core/src/UserManager.cs:2624-2633`; `src/Identity/EntityFrameworkCore/src/UserOnlyStore.cs:208-219` (`Context.Update(user)`); repo `AuthEndpoints.cs:104,156` |
| Every sign-in | `UPDATE identity_user_passkey` (the sign count) | `UserOnlyStore.cs:618-637` |
| Sign-out, and the `revoke-sessions` host command | `UPDATE identity_user` (`UpdateSecurityStampAsync`) | `UserManager.cs:992-1002`; repo `AuthEndpoints.cs:170`, `OwnerCommands.cs:54` |
| The `reset-owner` host command | `DELETE identity_user`, cascading to `identity_user_{passkey,claim,login,token}` through `ON DELETE CASCADE` | `UserOnlyStore.cs:234-243`; `Migrations/20261024160637_AuthAndAudit.cs:98-161`; repo `OwnerCommands.cs:71` |
| This slice, FR-6.4 | `UPDATE` and `DELETE` on `saved_report` | slice-04.md step 4 |

Further facts that bear on the grant script:
- `testing.md`'s "inserts and links rather than overwriting" is true of the balance *value*, but the old row's `superseded_at` is updated in place.
- The host commands run in the **api** image (`OwnerCommands.cs` doc comment: `docker compose run --rm api reset-owner`), so they get exactly what `portfolio_api` holds. A no-`DELETE` API breaks `reset-owner`.
- Reads the grant script must cover:
  - `public.v_net_worth_daily` (`NetWorthDay.cs:30`) and `app_setting` (`Endpoints.cs:157`);
  - `EXECUTE` on `pg_advisory_xact_lock` (`AuthEndpoints.cs:97`), if Spike L revokes `PUBLIC`'s `EXECUTE` widely (DBS overlap).
- Mechanics, as a non-owner (P8):
  - `INSERT` into a `GENERATED BY DEFAULT AS IDENTITY` table (as `identity_user_claim` is, `AuthAndAudit.cs:89`) needed **no** sequence grant.
  - The FK cascade deleted child rows with **no** `DELETE` grant on the child.
  - `ALTER TABLE … DISABLE TRIGGER` was refused with `42501 must be owner of table`. That is `G-7`'s mechanism, confirmed.
- The API needs no DDL at startup. `Api/Program.cs` calls no `Migrate` or `EnsureCreated`. Migrations run only in the Migrator (`Migrator/Program.cs:39`), and the API never reads `__EFMigrationsHistory`.

**What to do:**
- Answer Q1 with the table above. Grant:
  - `UPDATE (superseded_at)` (column-level) on `account_balance`;
  - `UPDATE` on `identity_user` and `identity_user_passkey`;
  - `DELETE` on `identity_user` only;
  - `SELECT, INSERT, UPDATE, DELETE` on `saved_report`;
  - `INSERT, SELECT` on the rest.
- Decide whether `reset-owner` keeps running as the API login, or moves to the Migrator's credentials.
- Step 1's "every existing suite stays green" is the test that shows the grants are enough. It holds only if the Integration fixture runs as the new role (TST overlap).

### SRC-6: `WithReference(db)` must be removed from the API, not kept beside a new connection string. It hands the API the superuser password three more times
**MUST FIX (Spike M 2, step 1, DoD 1 and 8).** `EXECUTED` (the repo's `aspire-output/docker-compose.yaml:53-60`; the probe publish). `SOURCE@dotnet/aspire@v13.6.4`:
- `src/Aspire.Hosting.PostgreSQL/PostgresServerResource.cs:56-58,169-175` (the connection string and the connection properties Username, Password, Uri);
- `PostgresDatabaseResource.cs:31-40,71-74`;
- `src/Aspire.Hosting.Docker/DockerComposeServiceResourceExtensions.cs:31-90` (a parameter becomes a `${…}` placeholder, and an endpoint expression becomes the internal host and port).

**Where:** Spike M 2; step 1 "the API switches to it"; DoD 1.

**The defect:**
- Today the API's environment holds `ConnectionStrings__portfolio`, **and** `PORTFOLIO_USERNAME=postgres`, `PORTFOLIO_PASSWORD=${POSTGRES_PASSWORD}` and `PORTFOLIO_URI=postgresql://postgres:${POSTGRES_PASSWORD}@…`. Aspire 13 injects a resource's connection properties with `WithReference`.
- If the build only overrides the connection string and keeps `WithReference(db)`, for example for `WaitFor`, the API process can still log in as `postgres`. `G-7` would then close for the login the API happens to use, but not for the API process.

**What to do:** on the API, drop `.WithReference(db)` and set:

```csharp
.WithEnvironment("ConnectionStrings__portfolio", ReferenceExpression.Create(
    $"Host={postgres.Resource.Host};Port={postgres.Resource.Port};Username=portfolio_api;Password={apiPwd};Database={db.Resource.DatabaseName}"))
```

- Probe publish, variant A: this emitted `Host=postgres;Port=5432;Username=portfolio_api;Password=${API_DB_PASSWORD};Database=portfolio_dev`, wrote `API_DB_PASSWORD=` to `.env`, and emitted **no** `PORTFOLIO_*` variable.
- The key name is unchanged, so `Api/Program.cs:33` and every `UseSetting("ConnectionStrings:portfolio", …)` fixture are untouched.
- The API needs no `WaitFor(db)`: `WaitForCompletion(migrator)` already orders it.
- The reporting connection works the same way under a second key. Variant B, `AddConnectionString("portfolio-reporting", expr)` + `WithReference`, also survives publish, but emits **both** `ConnectionStrings__portfolio-reporting` and `ConnectionStrings__portfolio_reporting` (probe). It cannot be named `portfolio`, which `AddDatabase` already holds. Prefer A.
- Add an app-model assertion (E2E `AppModelTests`) that the API's resolved environment contains neither the `postgres` user nor `POSTGRES_PASSWORD`.
- Run mode is `INFERRED`: `Host` and `Port` resolve to the allocated host port, and this was not run under `aspire run`.

### SRC-7: role passwords: regenerated on every `aspire run` unless persisted, impossible to bind as a parameter, and logged by EF if set through it
**MUST FIX (Spike M 1, 3 and 4, step 1, DoD 8).** `EXECUTED` (P7, P9, probe publish). `SOURCE@dotnet/aspire@v13.6.4`:
- `src/Aspire.Hosting/ParameterResourceBuilderExtensions.cs:166-184`: with `persist: false`, the default, a `GenerateParameterDefault` is not wrapped in `UserSecretsParameterDefault`.
- `:446-461`: `CreateGeneratedParameter`, which `AddPostgres`'s password uses (`PostgresBuilderExtensions.cs:59`), always is in run mode.
- `src/Aspire.Hosting/Orchestrator/ParameterProcessor.cs:128-131,673-697`: in publish mode, every parameter value is saved to deployment state.
- `Pipelines/Internal/FileDeploymentStateManager.cs:65-83`: the state is keyed per AppHost and per `EnvironmentName`.
- `src/Shared/PasswordGenerator.cs:19`: the special characters are `-_.{}~()*+!`, which leaves out `;`, `=`, `'` and `$`.

**Where:** Spike M 1 ("Aspire secret parameters, generated, as the setup code is?"), M 3, M 4; step 6.

**The defect:**
- **"As the setup code is" is the wrong model.** `setup-code` is `AddParameter(…, GenerateParameterDefault, secret: true)` without `persist`, and `AppHost.cs` says it is regenerated on every `aspire run`. A role password made that way changes on every run, while `aspire run` keeps the database on the opt-in named volume (`Postgres:DataVolume`). From the second run on, the API login fails unless the Migrator resets the password every time. `INFERRED` from source; not run.
- **Across deploys**, the new parameters go through the same path as `postgres-password`, which slice 03 showed stable (`G-26`, `EXECUTED` 2026-10-31). The state is per environment, so the first deploy of each environment generates its own.
- **The Migrator receives the passwords cleanly.** `WithEnvironment("Roles__ApiPassword", apiPwd)` published as `${API_DB_PASSWORD}`, beside `${POSTGRES_PASSWORD}` (probe).
- **The password cannot be a bind parameter.** `ALTER ROLE postgres PASSWORD $1` failed with `42601` (P9). `CREATE ROLE IF NOT EXISTS` does not exist either: `42601` at `NOT` (P9), which confirms the draft's `INFERRED`. So the password goes into the SQL text as a literal.
- **Through EF, that text is logged.** `db.Database.ExecuteSqlRawAsync("ALTER ROLE … PASSWORD '…'")` logged `info: Microsoft.EntityFrameworkCore.Database.Command[20101] Executed DbCommand … ALTER ROLE rep_to PASSWORD 'SEKRET-PROBE-VALUE'` (P7).
  - The Migrator has no `appsettings.json`, so `Information` is the default.
  - It calls `AddServiceDefaults`, which exports logs over OTLP to the dashboard under `aspire run` (`INFERRED` from the ServiceDefaults template).
  - In compose, the line lands in `docker logs migrator`.

**What to do:**
- Declare both passwords with `AddParameter(name, new GenerateParameterDefault { MinLength = 22, Special = false }, secret: true, persist: true)`. The AppHost has a `UserSecretsId`, so run mode keeps them.
- Have the Migrator run, **every time**, after migrations: create the role if missing (DBS owns the `DO` or `duplicate_object` form), then `ALTER ROLE … PASSWORD` unconditionally. Either path then works: a reused volume with a new password, or a new volume.
- Send that statement on a plain `NpgsqlCommand`, outside EF's logger, as a quoted literal. Better, send a client-computed `SCRAM-SHA-256$…` verifier, which PostgreSQL stores as given (`DOCS@2026-11-06 https://www.postgresql.org/docs/18/sql-createrole.html`, PASSWORD). A leaked log line then is not a login.
- Add a test that the Migrator's captured log output does not contain either password.
- The Integration fixture needs the same role-provisioning code callable without the Migrator process (TST overlap).

### SRC-8: an unkeyed reporting `NpgsqlDataSource` in DI becomes EF's connection whenever the API's connection string is missing
**SHOULD FIX (step 3).** `SOURCE@npgsql/efcore.pg@v10.0.3:src/EFCore.PG/Storage/Internal/NpgsqlDataSourceManager.cs:70-77`: with no connection string, EF takes `applicationServiceProvider.GetService<NpgsqlDataSource>()`. The branch is checked **before** the `MapEnum` branch at `:80`.
**Where:** step 3, "an endpoint that runs one statement on the reporting login".

**The defect:**
- `PortfolioDbContext.Configure` passes `GetConnectionString("portfolio")`, which is null when the key is absent: a renamed key, or a fixture that sets only the reporting string. If the reporting data source were then registered unkeyed (`Npgsql.DependencyInjection`'s `AddNpgsqlDataSource`, already in the API's graph at 10.0.3), the API's whole EF context would run as `portfolio_reporting`.
- It would fail closed, since every write is refused, but with confusing errors. The reverse wiring mistake is the dangerous one.
- `INFERRED` from source; not run.

**What to do:** register the reporting source **keyed** (`AddNpgsqlDataSource(cs, serviceKey: "reporting")`), or build it privately in the endpoint's service. Have the API refuse to start when `ConnectionStrings:portfolio` is missing, as the setup-code check already does for its own value.

### SRC-9: the results reader for arbitrary SQL is unspecified
**NOTE (step 3).** `SOURCE@npgsql/npgsql@v10.0.3:src/Npgsql/NpgsqlCommand.cs:350,375`: `AllResultTypesAreUnknown` and `UnknownResultTypeList` exist on `NpgsqlCommand`, and are absent from `NpgsqlBatch.cs` and `NpgsqlBatchCommand.cs`.
**Where:** step 3 "returns the columns, the rows".

**The defect:**
- User SQL can return any type: enums mapped only on EF's data source, `numeric` beyond `decimal`, intervals, arrays. The draft does not say how a row becomes JSON.
- Reading every column as text would sidestep the type mapping. But that switch lives on `NpgsqlCommand`, while SRC-3's protocol-level single-statement refusal needs `NpgsqlBatch`. The two choices interact.
- `INFERRED` that `GetValue` throws on out-of-range `numeric`; not run.

**What to do:** add to Spike N: "read one row of each column type the views expose, plus `1e40::numeric`, on the batch path". Decide between typed `GetValue` with a per-type fallback and the text format. Cast enum columns to `text` in the views either way.

---

## Confirmed (do not re-check)

- Npgsql answers a command timeout or a cancelled token with a protocol CancelRequest on a new connection. The server's `57014` comes back as `NpgsqlException(TimeoutException)` or `OperationCanceledException`, and the connector stays usable. `EXECUTED` P1, P2. `SOURCE@npgsql/npgsql@v10.0.3:src/Npgsql/Internal/NpgsqlReadBuffer.cs:336-360`, `NpgsqlConnector.cs:1792-1800,2201-2229`.
- A pooled connection is reset on return (`DISCARD ALL`, or `SET SESSION AUTHORIZATION DEFAULT; RESET ALL; …` with prepared statements), which restores role-level GUC defaults. `NoResetOnClose` defaults to off. `EXECUTED` P5b. `NpgsqlConnector.cs:516-520,2672,2752-2766`.
- A role-level `statement_timeout` cannot be escaped from inside one statement. The `set_config` is rolled back with the cancelled statement. `EXECUTED` P5b.
- `CREATE ROLE IF NOT EXISTS` does not exist, and utility statements take no bind parameters. `EXECUTED` P9.
- A non-owner cannot `DISABLE TRIGGER` (`42501`), needs no sequence grant for an identity column, and needs no `DELETE` on the child for an FK cascade. `EXECUTED` P8.
- `aspire publish` 13.6.4 turns a `ReferenceExpression` over `postgres.Resource.Host`/`.Port` plus secret parameters into `Host=postgres;Port=5432;…;Password=${NAME}`, and lists each parameter in `.env`. `EXECUTED` probe. Generated parameters are saved to deployment state on publish (`ParameterProcessor.cs:128-131`).
- `WithCreationScript` on a Postgres database runs only under `aspire run`, on `ResourceReadyEvent`, against the default database (`PostgresBuilderExtensions.cs:75,593,785`). It is no route for creating roles in the published stack. `SOURCE`.
- The API process needs no DDL. Only the Migrator migrates. `SOURCE` (repo).
- The named-parameter parser skips string literals, `E''` strings, dollar quotes, quoted identifiers and both comment forms. `EXECUTED` P6.

## Answers to Spike M and N questions

- **M 1:** Generated secret parameters, but with `persist: true` (user secrets in run mode). Without it they regenerate on every run (`SOURCE` `ParameterResourceBuilderExtensions.cs:166-184`). Across deploys they are kept in deploy state like `postgres-password` (`SOURCE` `ParameterProcessor.cs:673-697`; `G-26` `EXECUTED`).
- **M 2:** A `ReferenceExpression` over `postgres.Resource.Host`/`.Port` plus the parameter, set as `ConnectionStrings__portfolio`, with `WithReference(db)` removed. It survives publish; `AddConnectionString` survives too, but names the variable twice (`EXECUTED` probe; SRC-6).
- **M 3:** The Migrator, after migrations, on every run. The passwords come from `WithEnvironment`. The password goes in as a literal (no bind, `EXECUTED` P9), not through EF's logger (`EXECUTED` P7), preferably as a SCRAM verifier (`DOCS`). Not a migration (SRC-7).
- **M 4:** There is no `IF NOT EXISTS` (`EXECUTED` P9). Create if missing, then `ALTER ROLE … PASSWORD` every run, so a reused volume takes a new password. The `DO` or `duplicate_object` form is DBS's call (`INFERRED`).
- **N 1:** Yes. Timeout or cancellation sends a server cancel, and the query leaves `pg_stat_activity` (`EXECUTED` P1, P2). But `CommandTimeout` re-arms on every read, so the bound must be a wall-clock token plus `cmd.Cancel()` (`EXECUTED` P3; SRC-2).
- **N 2:** Only if you `cmd.Cancel()` before dispose. Dispose alone drains the whole result: 4.5 s per 30M rows (`EXECUTED` P4a, P4b). Cancelling the `ExecuteReaderAsync` token after it returns does nothing (`EXECUTED` P4c).
- **N 3:** Yes, `NpgsqlCommand` splits and runs every part (`EXECUTED` P5). Refuse without parsing by using a one-command `NpgsqlBatch`: PostgreSQL returns `42601` (`EXECUTED` P5). Not the `EnableSqlRewriting` switch, which would break EF (`SOURCE`; SRC-3).
- **N 4:** Comments and literals are safe (`EXECUTED` P6). `:name`, unmatched `@name` (run as the `@` operator) and array slices are not (`EXECUTED` P6). Use positional `$n` on the batch path (SRC-4).
