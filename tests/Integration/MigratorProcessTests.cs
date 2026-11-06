using System.Diagnostics;
using Npgsql;
using Portfolio.Infrastructure;
using Testcontainers.PostgreSql;
using static Portfolio.Integration.DatabaseLoginsTests;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 1a (R2-TST-3): the BUILT Migrator, run as a process against a database nothing
/// else has touched, so these pass only if the Migrator itself provisions the logins. The fixture's
/// own call to the same routine (<see cref="PostgresFixture"/>) cannot stand in for it.
/// </summary>
public class MigratorProcessTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Migrator_provisions_on_every_run_and_a_second_run_takes_new_passwords()
    {
        await using var container = await StartAsync();
        var (p1, r1, p2, r2) = (PostgresFixture.NewPassword(), PostgresFixture.NewPassword(), PostgresFixture.NewPassword(), PostgresFixture.NewPassword());

        var first = await RunMigratorAsync(container, p1, r1);
        Assert.True(first.ExitCode == 0, first.Output);
        await AssertSnapshotsAsync(container);
        await AssertConnectsAsync(container.GetConnectionString(), DatabaseLogins.Api, p1);
        await AssertConnectsAsync(container.GetConnectionString(), DatabaseLogins.Reporting, r1);

        // No migration pending this time: the routine must still run, and take the new passwords.
        var second = await RunMigratorAsync(container, p2, r2);
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Contains("Applying 0 migration(s)", second.Output);
        await AssertSnapshotsAsync(container);
        await AssertConnectsAsync(container.GetConnectionString(), DatabaseLogins.Api, p2);
        await AssertConnectsAsync(container.GetConnectionString(), DatabaseLogins.Reporting, r2);
        await AssertRefusedAsync(container, DatabaseLogins.Api, p1);
        await AssertRefusedAsync(container, DatabaseLogins.Reporting, r1);

        // Neither run wrote a password, through EF's command log or any other (SRC-7).
        foreach (var secret in new[] { p1, r1, p2, r2 })
            Assert.DoesNotContain(secret, first.Output + second.Output);
    }

    [Fact]
    public async Task The_Migrator_takes_a_login_that_already_exists()
    {
        await using var container = await StartAsync();
        await ExecuteAsync(container, "CREATE ROLE portfolio_reporting");

        var run = await RunMigratorAsync(container, PostgresFixture.NewPassword(), PostgresFixture.NewPassword());

        Assert.True(run.ExitCode == 0, run.Output);
        await AssertSnapshotsAsync(container);
    }

    /// <summary>
    /// R2-DBS-2: PostgreSQL copies a failed statement into its own log. The statement that carries
    /// the password is made to fail; the log must hold its verifier, which shows the statement was
    /// logged, and never the password.
    /// </summary>
    [Fact]
    public async Task A_failed_password_statement_leaves_no_password_in_the_servers_log()
    {
        await using var container = await StartAsync();
        var secret = PostgresFixture.NewPassword();
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(Ct);

        var failed = await Assert.ThrowsAsync<PostgresException>(() => DatabaseLogins.SetPasswordAsync(connection, "no_such_login", secret, Ct));
        Assert.Equal(PostgresErrorCodes.UndefinedObject, failed.SqlState);

        var (stdout, stderr) = await container.GetLogsAsync(ct: Ct);
        var log = stdout + stderr;
        Assert.Contains("ALTER ROLE no_such_login PASSWORD 'SCRAM-SHA-256$", log);
        Assert.DoesNotContain(secret, log);
    }

    static async Task<PostgreSqlContainer> StartAsync()
    {
        var container = new PostgreSqlBuilder(PostgresFixture.Image).WithDatabase(PostgresFixture.Database).Build();
        await container.StartAsync(Ct);
        return container;
    }

    static async Task AssertSnapshotsAsync(PostgreSqlContainer container)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(Ct);
        foreach (var login in new[] { DatabaseLogins.Api, DatabaseLogins.Reporting })
        {
            var actual = await CatalogSnapshot.TakeAsync(connection, login);
            var expected = CatalogSnapshot.Expected(login);
            Assert.True(expected.SequenceEqual(actual), $"{login}:{Environment.NewLine}{CatalogSnapshot.Diff(expected, actual)}");
        }
    }

    static async Task AssertRefusedAsync(PostgreSqlContainer container, string login, string password)
    {
        await using var connection = new NpgsqlConnection(As(container.GetConnectionString(), login, password));
        var refused = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync(Ct));
        Assert.Equal(PostgresErrorCodes.InvalidPassword, refused.SqlState);
    }

    static async Task ExecuteAsync(PostgreSqlContainer container, string sql)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>The Migrator as compose runs it: its own build output, configured only by environment.</summary>
    static async Task<(int ExitCode, string Output)> RunMigratorAsync(PostgreSqlContainer container, string apiPassword, string reportingPassword)
    {
        var start = new ProcessStartInfo("dotnet", [MigratorDll()])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["ConnectionStrings__portfolio"] = container.GetConnectionString(),
                ["DatabaseLogins__ApiPassword"] = apiPassword,
                ["DatabaseLogins__ReportingPassword"] = reportingPassword,
                ["DOTNET_ENVIRONMENT"] = "Production",
            },
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>The Migrator's own output, built first by the project reference, in this build's configuration.</summary>
    static string MigratorDll()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = here.Parent!.Name; // bin/<Configuration>/net10.0/
        var root = here;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Portfolio.slnx")))
            root = root.Parent;
        var dll = Path.Combine(root!.FullName, "src", "Migrator", "bin", configuration, here.Name, "Migrator.dll");
        Assert.True(File.Exists(dll), $"The Migrator is not built at {dll}.");
        return dll;
    }
}
