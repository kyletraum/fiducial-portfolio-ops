using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Portfolio.Domain;

namespace Portfolio.Infrastructure;

public class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountSource> AccountSources => Set<AccountSource>();
    public DbSet<AccountBalance> AccountBalances => Set<AccountBalance>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    /// <summary>
    /// The one place the provider is configured. The runtime composition roots and the
    /// design-time factory both call it, so migrations are generated against the same
    /// mapping the application runs with.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        options
            .UseNpgsql(connectionString, Npgsql)
            .UseSnakeCaseNamingConvention();

    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MapEnum<SourceStrength>("source_strength");

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Constitution IV: the label ORDER is the strength order, and PostgreSQL compares enum
        // values by it. MapEnum alone emits the labels alphabetically (api, export, manual,
        // scrape, statement), which would make 'statement' > 'manual'. Declare them in order.
        model.HasPostgresEnum("source_strength",
            Enum.GetNames<SourceStrength>().Select(n => n.ToLowerInvariant()).ToArray());

        model.ApplyConfigurationsFromAssembly(typeof(PortfolioDbContext).Assembly);
    }
}
