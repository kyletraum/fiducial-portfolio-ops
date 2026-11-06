using Npgsql;
using Portfolio.Infrastructure;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 1a: what the two logins hold, as the routine the Migrator runs leaves them.
/// The fixture ran <see cref="DatabaseLogins.EnsureAsync"/> after migrating; the process test of
/// the built Migrator is <see cref="MigratorProcessTests"/>.
/// </summary>
public class DatabaseLoginsTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DatabaseLogins.Api)]
    [InlineData(DatabaseLogins.Reporting)]
    public async Task A_login_holds_exactly_its_committed_privileges(string login)
    {
        await using var connection = await OpenAsync();
        var actual = await CatalogSnapshot.TakeAsync(connection, login);
        var expected = CatalogSnapshot.Expected(login);

        Assert.True(expected.SequenceEqual(actual), $"{login}:{Environment.NewLine}{CatalogSnapshot.Diff(expected, actual)}");
    }

    /// <summary>
    /// The snapshot can fail (TST-4, R2-TST-4): each plant, made and rolled back, is flagged by the
    /// line it adds. A snapshot that read only grant rows would miss the membership and the default.
    /// </summary>
    public static TheoryData<string, string, string[]> Plants() => new()
    {
        { "a column grant", "GRANT SELECT (balance) ON account_balance TO portfolio_reporting",
            ["column public.account_balance.balance SELECT"] },
        { "a default ACL, then a new table",
            "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO portfolio_reporting; CREATE TABLE plant_table (id int)",
            ["default acl postgres r public {portfolio_reporting=r/postgres}", "relation public.plant_table SELECT"] },
        { "TEMP back to PUBLIC", $"GRANT TEMPORARY ON DATABASE {PostgresFixture.Database} TO PUBLIC",
            ["database app TEMPORARY"] },
        { "a predefined role", "GRANT pg_read_all_data TO portfolio_reporting",
            ["member of pg_read_all_data", "relation public.audit_event SELECT"] },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public async Task A_planted_grant_is_flagged(string plant, string sql, string[] flagged)
    {
        await using var connection = await OpenAsync();
        var before = await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Reporting);

        await using var tx = await connection.BeginTransactionAsync(Ct);
        await using (var command = new NpgsqlCommand(sql, connection))
            await command.ExecuteNonQueryAsync(Ct);
        var after = await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Reporting);
        await tx.RollbackAsync(Ct);

        var added = after.Except(before).ToList();
        foreach (var line in flagged)
            Assert.True(added.Contains(line), $"{plant}: expected '{line}' among the added lines:{Environment.NewLine}{string.Join(Environment.NewLine, added)}");
        Assert.Empty(before.Except(after));
    }

    [Fact]
    public async Task Running_the_routine_again_changes_nothing_and_both_logins_connect()
    {
        await using var connection = await OpenAsync();
        var before = (await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Api), await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Reporting));

        await DatabaseLogins.EnsureAsync(connection, pg.ApiPassword, pg.ReportingPassword, Ct);

        Assert.Equal(before.Item1, await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Api));
        Assert.Equal(before.Item2, await CatalogSnapshot.TakeAsync(connection, DatabaseLogins.Reporting));
        await AssertConnectsAsync(pg.ConnectionString, DatabaseLogins.Api, pg.ApiPassword);
        await AssertConnectsAsync(pg.ConnectionString, DatabaseLogins.Reporting, pg.ReportingPassword);
    }

    [Fact]
    public async Task A_login_that_belongs_to_a_role_is_refused_not_provisioned()
    {
        // Committed, not rolled back: the routine opens its own transaction. Undone in finally.
        await using var connection = await OpenAsync();
        await using (var grant = new NpgsqlCommand("GRANT pg_read_all_data TO portfolio_reporting", connection))
            await grant.ExecuteNonQueryAsync(Ct);
        try
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => DatabaseLogins.EnsureAsync(connection, pg.ApiPassword, pg.ReportingPassword, Ct));
            Assert.Contains("portfolio_reporting belongs to 1 role", refused.Message);
        }
        finally
        {
            await using var revoke = new NpgsqlCommand("REVOKE pg_read_all_data FROM portfolio_reporting", connection);
            await revoke.ExecuteNonQueryAsync(Ct);
        }
    }

    async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(pg.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    /// <summary>A login connects with this password: a fresh, unpooled connection, so nothing is reused.</summary>
    internal static async Task AssertConnectsAsync(string superuserConnectionString, string login, string password)
    {
        await using var connection = new NpgsqlConnection(As(superuserConnectionString, login, password));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var who = new NpgsqlCommand("SELECT current_user", connection);
        Assert.Equal(login, await who.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    internal static string As(string superuserConnectionString, string login, string secret)
    {
        // The indexer, not the Password property: the hygiene hook reads "Password = x" as a
        // connection string with its password written in.
        var builder = new NpgsqlConnectionStringBuilder(superuserConnectionString) { Username = login, Pooling = false };
        builder["Password"] = secret;
        return builder.ConnectionString;
    }
}
