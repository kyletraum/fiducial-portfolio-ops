using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Infrastructure;
using Testcontainers.PostgreSql;

namespace Portfolio.Integration;

/// <summary>
/// One throwaway PostgreSQL per test class, migrated by the real EF migrations. No volume:
/// the container's data dies with it (S-25b - no test process mounts a named volume).
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    // The image Aspire's AddPostgres runs (13.5.4, 13.6.1 and 13.6.4 all publish postgres:18.3),
    // so these tests exercise the server version the app gets.
    public const string Image = "postgres:18.3";

    /// <summary>
    /// The app's database is named as in production, not <c>postgres</c>: slice 04 revokes CONNECT on
    /// <c>postgres</c> from PUBLIC, and the two must be different objects here too (R2-TST-2).
    /// </summary>
    public const string Database = "portfolio_dev";

    readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).WithDatabase(Database).Build();

    public string ConnectionString => container.GetConnectionString();

    /// <summary>Slice 04: the logins' passwords, fresh per fixture, set by the same routine the Migrator runs.</summary>
    public string ApiPassword { get; } = NewPassword();
    public string ReportingPassword { get; } = NewPassword();

    public static string NewPassword() => Guid.NewGuid().ToString("N");

    public virtual async ValueTask InitializeAsync()
    {
        await StartAsync();
        await using (var db = CreateContext())
            await db.Database.MigrateAsync();
        await EnsureLoginsAsync();
        await SeedAsync();
    }

    /// <summary>What the Migrator does after migrating (slice-04.md step 1a): <see cref="DatabaseLogins.EnsureAsync"/>.</summary>
    protected async Task EnsureLoginsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await DatabaseLogins.EnsureAsync(connection, ApiPassword, ReportingPassword);
    }

    /// <summary>Starts the container only. A fixture that migrates to a chosen point calls this.</summary>
    protected Task StartAsync() => container.StartAsync();

    /// <summary>Runs once per fixture - per test CLASS - after migrating.</summary>
    protected virtual Task SeedAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => container.DisposeAsync();

    /// <summary>
    /// A context configured as the running app's is: the shared Configure, PLUS the retrying
    /// execution strategy Aspire's client integration adds. That strategy refuses a
    /// user-initiated transaction unless the caller runs inside it - which BalanceRecorder
    /// must, and these tests would not notice if they ran without it.
    /// </summary>
    public PortfolioDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>();
        // No host here, so Identity's schema version has to be supplied (slice 03, migration 5);
        // without it the model would differ from the snapshot and MigrateAsync would refuse.
        PortfolioDbContext.WithIdentitySchema(options);
        options.UseNpgsql(ConnectionString, npgsql =>
            {
                PortfolioDbContext.Npgsql(npgsql);
                npgsql.EnableRetryOnFailure();
            })
            .UseSnakeCaseNamingConvention();
        return new PortfolioDbContext(options.Options);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? default : (T)value;
    }
}
