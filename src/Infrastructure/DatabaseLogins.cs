using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Portfolio.Infrastructure;

/// <summary>
/// slice-04.md "Provisioning" (step 1a): the two logins beside <c>postgres</c>, which the Migrator
/// keeps. The Migrator runs this after every migration run, pending or not, and the Integration
/// fixture runs it too, so the tests exercise the code production runs (TST-1).
/// </summary>
/// <remarks>
/// Every statement goes on a plain <see cref="NpgsqlCommand"/>, never through EF, whose command
/// logger wrote a password literal out in full (SRC-7). The passwords are sent as SCRAM verifiers:
/// a failed statement is copied into PostgreSQL's own log with its text (R2-DBS-2), and a verifier
/// there is not a login.
/// </remarks>
public static class DatabaseLogins
{
    public const string Api = "portfolio_api";
    public const string Reporting = "portfolio_reporting";

    /// <summary>
    /// Built-ins that write from inside a read-only transaction, revoked from PUBLIC by name, every
    /// overload (DBS-1, R2-DBS-1). Never wholesale: ordinary SQL needs the rest (SCO-13).
    /// </summary>
    public static readonly string[] RevokedFunctions = ["lo_create", "lo_creat", "lo_from_bytea", "pg_logical_emit_message"];

    /// <summary>
    /// The API login's grants, from its code paths (slice-04.md, the grant matrix). The existing
    /// suites, run as this login in step 1b, fail on a missing one; the catalog snapshot test fails
    /// on an extra one.
    /// </summary>
    static readonly string[] ApiGrants =
    [
        "SELECT, INSERT ON institution, account, account_source, ingest_run, audit_event, used_setup_code",
        // A restatement sets superseded_at before the insert (BalanceWrites); nothing else is updated.
        "SELECT, INSERT, UPDATE (superseded_at) ON account_balance",
        // Enrolment and every sign-in update the user, sign-out and revoke-sessions rotate its stamp,
        // and reset-owner deletes it; its passkeys, claims, logins and tokens go by ON DELETE CASCADE,
        // which needs no grant on them.
        "SELECT, INSERT, UPDATE, DELETE ON identity_user",
        "SELECT, INSERT, UPDATE ON identity_user_passkey",
        "SELECT ON identity_user_claim, identity_user_login, identity_user_token, app_setting, v_net_worth_daily",
    ];

    public static async Task EnsureAsync(NpgsqlConnection superuser, string apiPassword, string reportingPassword, CancellationToken ct = default)
    {
        foreach (var login in new[] { Api, Reporting })
            await CreateIfMissingAsync(superuser, login, ct);

        await using (var tx = await superuser.BeginTransactionAsync(ct))
        {
            foreach (var login in new[] { Api, Reporting })
                await RefuseWhatALoginMustNotBeAsync(superuser, login, ct);
            await ApplyGrantsAsync(superuser, ct);
            await tx.CommitAsync(ct);
        }

        await SetPasswordAsync(superuser, Api, apiPassword, ct);
        await SetPasswordAsync(superuser, Reporting, reportingPassword, ct);
    }

    /// <summary>
    /// Created WITHOUT a password, so the one statement carrying a secret is the one least likely to
    /// fail (R2-DBS-2). There is no IF NOT EXISTS form; 42710 is the role already there.
    /// </summary>
    static async Task CreateIfMissingAsync(NpgsqlConnection superuser, string login, CancellationToken ct)
    {
        try
        {
            await ExecuteAsync(superuser, $"CREATE ROLE {login} LOGIN", ct);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.DuplicateObject)
        {
        }
    }

    /// <summary>
    /// Re-asserted on every run, so a role found already there, or altered since, is put back
    /// (R2-DBS-3). Owning anything, or belonging to any role, is refused rather than repaired: either
    /// would make the grants below a lower bound, not the whole of what the login holds.
    /// </summary>
    static async Task RefuseWhatALoginMustNotBeAsync(NpgsqlConnection superuser, string login, CancellationToken ct)
    {
        await ExecuteAsync(superuser, $"ALTER ROLE {login} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS", ct);

        await using var check = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pg_auth_members WHERE member = r.oid),
                   (SELECT count(*) FROM pg_shdepend WHERE refclassid = 'pg_authid'::regclass AND refobjid = r.oid AND deptype = 'o')
              FROM pg_roles r WHERE r.rolname = $1
            """, superuser) { Parameters = { new() { Value = login } } };
        await using var reader = await check.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var (memberships, owned) = (reader.GetInt64(0), reader.GetInt64(1));
        if (memberships > 0 || owned > 0)
            throw new InvalidOperationException($"{login} belongs to {memberships} role(s) and owns {owned} object(s); a login here must do neither.");
    }

    /// <summary>
    /// Idempotent and convergent: everything the two logins hold on the app's objects is revoked
    /// first and granted again, in one transaction, so a grant dropped from this list is removed from
    /// the database too, and the API never sees a moment without its grants.
    /// </summary>
    static async Task ApplyGrantsAsync(NpgsqlConnection superuser, CancellationToken ct)
    {
        const string both = $"{Api}, {Reporting}";
        await ExecuteAsync(superuser, $"""
            REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {both};
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {both};
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM {both};
            REVOKE ALL ON ALL TABLES IN SCHEMA reporting FROM {both};
            REVOKE ALL ON SCHEMA reporting FROM {both};
            """, ct);

        // CONNECT on the app's database granted by name, never left to PUBLIC (R2-TST-2); every
        // other database refused to PUBLIC, and TEMPORARY with it (DBS-1, DBS-8, R2-DBS-5).
        var app = await ScalarAsync<string>(superuser, "SELECT current_database()", ct);
        await ExecuteAsync(superuser, $"""
            REVOKE ALL ON DATABASE {Quote(app)} FROM {both};
            REVOKE TEMPORARY ON DATABASE {Quote(app)} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {Quote(app)} TO {both};
            REVOKE CONNECT, TEMPORARY ON DATABASE postgres FROM PUBLIC;
            REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;
            REVOKE USAGE ON LANGUAGE plpgsql FROM PUBLIC;
            """, ct);

        // Every overload as this server defines it: pg_logical_emit_message gained a parameter in 17.
        await using (var find = new NpgsqlCommand(
            "SELECT p.oid::regprocedure::text FROM pg_proc p WHERE p.pronamespace = 'pg_catalog'::regnamespace AND p.proname = ANY($1)",
            superuser) { Parameters = { new() { Value = RevokedFunctions } } })
        {
            var signatures = new List<string>();
            await using (var reader = await find.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    signatures.Add(reader.GetString(0));
            foreach (var signature in signatures)
                await ExecuteAsync(superuser, $"REVOKE EXECUTE ON FUNCTION {signature} FROM PUBLIC", ct);
        }

        foreach (var grant in ApiGrants)
            await ExecuteAsync(superuser, $"GRANT {grant} TO {Api}", ct);

        // The reporting login's whole grant (slice-04.md step 2): the schema and its views, which
        // read with their owner's rights, so nothing beneath them is granted (Spike L 4). Every
        // relation there, so a view a later migration adds is granted in the same run; the catalog
        // test refuses one without security_barrier.
        await ExecuteAsync(superuser, $"""
            GRANT USAGE ON SCHEMA reporting TO {Reporting};
            GRANT SELECT ON ALL TABLES IN SCHEMA reporting TO {Reporting};
            """, ct);
    }

    /// <summary>
    /// The one statement that carries a secret. Not a bind parameter (42601: a password cannot be
    /// one), so a literal, and that literal a SCRAM verifier, computed here, which PostgreSQL stores
    /// as given.
    /// </summary>
    public static Task SetPasswordAsync(NpgsqlConnection superuser, string login, string password, CancellationToken ct = default) =>
        ExecuteAsync(superuser, $"ALTER ROLE {login} PASSWORD '{ScramVerifier(password)}'", ct);

    /// <summary>
    /// RFC 5802's StoredKey and ServerKey, in PostgreSQL's <c>SCRAM-SHA-256$iterations:salt$StoredKey:ServerKey</c>
    /// form. The password is ASCII letters and digits (the AppHost generates it without specials),
    /// for which SASLprep changes nothing; anything else is refused rather than normalised.
    /// </summary>
    public static string ScramVerifier(string password, int iterations = 4096)
    {
        if (password.Length < 16 || !password.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("A database login's password must be at least 16 ASCII letters and digits.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(16);
        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        var storedKey = SHA256.HashData(HMACSHA256.HashData(salted, "Client Key"u8));
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        return $"SCRAM-SHA-256${iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }

    static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }
}
