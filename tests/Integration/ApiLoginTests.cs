using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Portfolio.Infrastructure;
using static Portfolio.Integration.AuthFixture;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 1b and DoD 1: the Api runs as <c>portfolio_api</c>, and that login cannot remove
/// the audit trail. Each refusal has two outcomes: the owner, in a rolled-back transaction, gets past
/// the grant and meets the trigger or succeeds outright, while the Api's own connection is refused by
/// the grant. The SQLSTATE that differs is the evidence that the grant refuses (R2-TST-1). This closes
/// gate G-7, which slice 03 left open (EXECUTED 2026-10-31: postgres could disable the trigger).
/// </summary>
public class ApiLoginTests(AuthFixture db) : IClassFixture<AuthFixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The trigger's refusal: append_only() and ingest_run_immutable() raise restrict_violation.</summary>
    const string Trigger = PostgresErrorCodes.RestrictViolation;
    const string Succeeds = "succeeds";

    // Rows for the row-level triggers to meet; inserted inside the owner's transaction, rolled back with it.
    const string Rows = $"""
        INSERT INTO audit_event (kind, actor, environment) VALUES ('sign_in_failed', 'unknown', 'Test');
        INSERT INTO used_setup_code (sha256) VALUES (repeat('a', 64));
        INSERT INTO ingest_run (source_system, file_name, file_sha256, started_at, finished_at, status, rows_read, created, restated, unchanged, error_detail)
        VALUES ('import:csv', 'g7.csv', repeat('0', 64), now(), now(), 'failed', 0, 0, 0, 0, '[]');
        """;

    public static TheoryData<string, string> Refusals() => new()
    {
        { "ALTER TABLE audit_event DISABLE TRIGGER audit_event_append_only", Succeeds },
        { "DROP TRIGGER audit_event_append_only ON audit_event", Succeeds },
        { "DROP TABLE audit_event", Succeeds },
        { "DROP FUNCTION append_only() CASCADE", Succeeds },
        { "DROP FUNCTION ingest_run_immutable() CASCADE", Succeeds },
        { $"ALTER DATABASE {PostgresFixture.Database} SET work_mem = '8MB'", Succeeds },
        // The second way past an append-only trigger without disabling it: the owner's UPDATE goes through.
        { "SET LOCAL session_replication_role = replica; UPDATE audit_event SET actor = actor", Succeeds },
        { "UPDATE audit_event SET actor = 'rewritten'", Trigger },
        { "DELETE FROM audit_event", Trigger },
        { "TRUNCATE audit_event", Trigger },
        { "UPDATE used_setup_code SET used_at = now()", Trigger },
        { "DELETE FROM used_setup_code", Trigger },
        { "TRUNCATE used_setup_code", Trigger },
        { "UPDATE ingest_run SET rows_read = 1", Trigger },
        { "DELETE FROM ingest_run", Trigger },
        // CASCADE: without it the owner is refused first by account_balance's foreign key (0A000),
        // and the row would show that rule, not the trigger.
        { "TRUNCATE ingest_run CASCADE", Trigger },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task The_owner_gets_past_the_grant_and_the_Api_does_not(string sql, string asOwner)
    {
        // As the owner: the control. Without it, a 42501 below could be any refusal.
        await using (var owner = new NpgsqlConnection(db.SuperuserConnectionString))
        {
            await owner.OpenAsync(Ct);
            await using var tx = await owner.BeginTransactionAsync(Ct);
            await ExecuteAsync(owner, Rows);
            if (asOwner == Succeeds)
                await ExecuteAsync(owner, sql);
            else
                Assert.Equal(asOwner, (await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(owner, sql))).SqlState);
            await tx.RollbackAsync(Ct);
        }

        // On the Api's own connection, from its own services.
        await using var api = db.Api(NewSetupCode());
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using (var tx = await connection.BeginTransactionAsync(Ct))
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
            await tx.RollbackAsync(Ct);
        }
        Assert.Equal(DatabaseLogins.Api, await WhoAmIAsync(connection));
    }

    /// <summary>R2-TST-5: who the app is, through its own context, for each kind of factory the suite builds.</summary>
    [Fact]
    public async Task Every_kind_of_test_factory_runs_the_Api_as_its_own_login()
    {
        await using var auth = db.Api(NewSetupCode());
        await using var import = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", db.ApiConnectionString));

        foreach (var factory in new WebApplicationFactory<Program>[] { auth, import })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var connection = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.GetDbConnection();
            await connection.OpenAsync(Ct);
            Assert.Equal(DatabaseLogins.Api, await WhoAmIAsync(connection));
            await using var super = new NpgsqlCommand("SELECT rolsuper FROM pg_roles WHERE rolname = current_user", connection);
            Assert.Equal(false, await super.ExecuteScalarAsync(Ct));
        }
    }

    /// <summary>SRC-8: with no connection string the Api does not start, rather than fall back to anything.</summary>
    [Fact]
    public void The_Api_refuses_to_start_without_its_connection_string()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").UseSetting("ConnectionStrings:portfolio", ""));

        var refused = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:portfolio is not set", refused.GetBaseException().Message);
    }

    static async Task<string> WhoAmIAsync(NpgsqlConnection connection)
    {
        await using var who = new NpgsqlCommand("SELECT current_user", connection);
        return (string)(await who.ExecuteScalarAsync(Ct))!;
    }

    static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
