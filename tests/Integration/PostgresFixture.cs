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
    const string Image = "postgres:18.3";

    readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => container.GetConnectionString();

    public virtual async ValueTask InitializeAsync()
    {
        await StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await SeedAsync();
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
