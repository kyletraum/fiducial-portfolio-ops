using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql.NameTranslation;
using Portfolio.Domain;

namespace Portfolio.Infrastructure;

/// <summary>
/// The platform's one context. Since slice 03 it is also Identity's user store
/// (<see cref="IdentityUserContext{TUser}"/>: users, claims, logins, tokens and passkeys; no
/// roles, one user).
/// </summary>
public class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : IdentityUserContext<IdentityUser>(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountSource> AccountSources => Set<AccountSource>();
    public DbSet<AccountBalance> AccountBalances => Set<AccountBalance>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UsedSetupCode> UsedSetupCodes => Set<UsedSetupCode>();

    /// <summary>
    /// Identity's schema version, which carries the passkey table. Identity's model building
    /// reads it ONLY from <c>IdentityOptions</c> in the context's application services: the
    /// virtual <c>SchemaVersion</c> property is not consulted, and the method that is, is
    /// internal [SOURCE@dotnet/aspnetcore@v10.0.13
    /// src/Identity/EntityFrameworkCore/src/IdentityUserContext.cs:135-180; EXECUTED, an override
    /// left the model without passkeys]. So every host registers it (<see cref="AddIdentitySchema"/>),
    /// and a context built with no application services gets <see cref="Standalone"/>.
    /// </summary>
    public static readonly Version IdentitySchema = IdentitySchemaVersions.Version3;

    public static IServiceCollection AddIdentitySchema(IServiceCollection services) =>
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchema);

    static readonly IServiceProvider Standalone = AddIdentitySchema(new ServiceCollection()).BuildServiceProvider();

    /// <summary>
    /// For a context built outside a host (the design-time factory, test fixtures): supplies the
    /// Identity schema as application services. Under AddDbContext the host's own services are
    /// already attached, before any options action runs, and are left alone.
    /// </summary>
    public static DbContextOptionsBuilder WithIdentitySchema(DbContextOptionsBuilder options) =>
        options.Options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider is null
            ? options.UseApplicationServiceProvider(Standalone)
            : options;

    /// <summary>
    /// The one place the provider is configured. The runtime composition roots and the
    /// design-time factory both call it, so migrations are generated against the same
    /// mapping the application runs with.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        WithIdentitySchema(options)
            .UseNpgsql(connectionString, Npgsql)
            .UseSnakeCaseNamingConvention();

    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MapEnum<SourceStrength>("source_strength")
              .MapEnum<IngestRunStatus>("ingest_run_status")
              .MapEnum<AuditEventKind>("audit_event_kind");

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Identity's tables, renamed to the platform's convention: the naming convention renames
        // columns and keys, but not a table name Identity sets explicitly ("AspNetUsers").
        base.OnModelCreating(model);
        model.Entity<IdentityUser>(e =>
        {
            e.ToTable("identity_user");
            e.HasIndex(u => u.NormalizedUserName).HasDatabaseName("identity_user_name_unique");
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("identity_user_email");
        });
        model.Entity<IdentityUserClaim<string>>().ToTable("identity_user_claim");
        model.Entity<IdentityUserLogin<string>>().ToTable("identity_user_login");
        model.Entity<IdentityUserToken<string>>().ToTable("identity_user_token");
        model.Entity<IdentityUserPasskey<string>>().ToTable("identity_user_passkey");

        // Constitution IV: the label ORDER is the strength order, and PostgreSQL compares enum
        // values by it. MapEnum alone emits the labels alphabetically (api, export, manual,
        // scrape, statement), which would make 'statement' > 'manual'. Declare them in order.
        model.HasPostgresEnum("source_strength",
            Enum.GetNames<SourceStrength>().Select(n => n.ToLowerInvariant()).ToArray());
        model.HasPostgresEnum("ingest_run_status",
            Enum.GetNames<IngestRunStatus>().Select(n => n.ToLowerInvariant()).ToArray());
        // Multi-word labels: MapEnum's default translator is snake_case (sign_in_succeeded), so the
        // declared labels must be too. The single-word enums above are unaffected.
        model.HasPostgresEnum("audit_event_kind",
            Enum.GetNames<AuditEventKind>().Select(new NpgsqlSnakeCaseNameTranslator().TranslateMemberName).ToArray());

        model.ApplyConfigurationsFromAssembly(typeof(PortfolioDbContext).Assembly);
    }
}
