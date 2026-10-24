using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Domain;

namespace Portfolio.Integration;

/// <summary>
/// Slice 03, migration 5: what the database itself holds for authentication and audit. The
/// endpoints that write these rows come in step 2; these are the schema's own guarantees.
/// </summary>
public class AuthSchemaTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task The_migrated_database_has_Identitys_passkey_table()
    {
        // The design-time factory and the Migrator have no Identity services; without the schema
        // supplied to them they model Identity Version1, which has no passkey table (SRC-4).
        var tables = await pg.ScalarAsync<string[]>("""
            SELECT array_agg(table_name::text ORDER BY table_name) FROM information_schema.tables
             WHERE table_schema = 'public' AND table_name LIKE 'identity_user%'
            """);

        Assert.Equal(["identity_user", "identity_user_claim", "identity_user_login", "identity_user_passkey", "identity_user_token"], tables);
    }

    [Fact]
    public async Task A_passkey_round_trips_through_its_json_column()
    {
        // Spike H stored passkeys without the platform's snake_case naming convention, and found the
        // EF in-memory provider could not read the JSON-mapped data back at all. Here: Npgsql, with
        // the convention, every field.
        var user = new IdentityUser("passkey-owner");
        // Whole seconds: the JSON column keeps microseconds, as a timestamp does, and drops the last
        // tick of a DateTimeOffset (EXECUTED: 32.2323065 came back 32.2323060). Harmless for a
        // passkey's creation time; the test compares every field exactly.
        var created = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
        var data = new IdentityPasskeyData
        {
            PublicKey = [1, 2, 3, 4],
            Name = "Laptop",
            CreatedAt = created,
            SignCount = 7,
            Transports = ["internal", "hybrid"],
            IsUserVerified = true,
            IsBackupEligible = true,
            IsBackedUp = false,
            AttestationObject = [5, 6],
            ClientDataJson = [7, 8, 9],
        };
        await using (var db = pg.CreateContext())
        {
            db.Users.Add(user);
            db.UserPasskeys.Add(new IdentityUserPasskey<string> { CredentialId = [42, 43], UserId = user.Id, Data = data });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = pg.CreateContext();
        var passkey = await read.UserPasskeys.SingleAsync(p => p.UserId == user.Id, TestContext.Current.CancellationToken);

        Assert.Equal([42, 43], passkey.CredentialId);
        Assert.Equivalent(data, passkey.Data, strict: true);
    }

    [Theory]
    [InlineData("UPDATE audit_event SET actor = 'rewritten'")]
    [InlineData("DELETE FROM audit_event")]
    [InlineData("TRUNCATE audit_event")]
    public async Task An_audit_event_cannot_be_changed_or_removed(string sql)
    {
        await pg.ExecuteAsync("INSERT INTO audit_event (kind, actor, environment) VALUES ('sign_in_failed', 'unknown', 'Test')");

        var error = await Assert.ThrowsAsync<PostgresException>(() => pg.ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Contains("audit_event is append-only", error.MessageText);
    }

    [Theory]
    [InlineData("UPDATE used_setup_code SET used_at = now()")]
    [InlineData("DELETE FROM used_setup_code")]
    [InlineData("TRUNCATE used_setup_code")]
    public async Task A_spent_setup_code_cannot_be_changed_or_removed(string sql)
    {
        // What makes a code single-use across reset-owner: clearing the user leaves this table alone.
        await pg.ExecuteAsync($"INSERT INTO used_setup_code (sha256) VALUES ('{SetupCode.Sha256Hex(Guid.NewGuid().ToString())}')");

        var error = await Assert.ThrowsAsync<PostgresException>(() => pg.ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Contains("used_setup_code is append-only", error.MessageText);
    }

    [Fact]
    public async Task A_setup_code_is_recorded_as_spent_once()
    {
        var hash = SetupCode.Sha256Hex("the same code");
        await using (var db = pg.CreateContext())
        {
            db.UsedSetupCodes.Add(new UsedSetupCode { Sha256 = hash });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var again = pg.CreateContext();
        again.UsedSetupCodes.Add(new UsedSetupCode { Sha256 = SetupCode.Sha256Hex("THE-SAME code") }); // normalises to the same
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task An_audit_event_round_trips_with_its_kind()
    {
        // The enum's labels are multi-word: the declared labels and MapEnum's translator must agree.
        await using (var db = pg.CreateContext())
        {
            db.AuditEvents.Add(new AuditEvent { Kind = AuditEventKind.SessionsRevoked, Actor = "owner", Environment = "Test", ConnectionAddress = "::1" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = pg.CreateContext();
        var row = await read.AuditEvents.SingleAsync(e => e.Kind == AuditEventKind.SessionsRevoked, TestContext.Current.CancellationToken);

        Assert.Equal(("owner", "::1"), (row.Actor, row.ConnectionAddress));
        Assert.Equal("sessions_revoked", await pg.ScalarAsync<string>("SELECT kind::text FROM audit_event WHERE kind = 'sessions_revoked'"));
        Assert.True(row.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-5), "occurred_at defaults to now()");
    }
}
