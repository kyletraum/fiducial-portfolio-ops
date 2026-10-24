using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

/// <summary>
/// security.md "Audit", migration 5. The append-only trigger is raw SQL in the migration, as
/// ingest_run's is.
/// </summary>
sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> e)
    {
        e.ToTable("audit_event");
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.Property(x => x.OccurredAt).HasDefaultValueSql(Conventions.Now);
        e.Property(x => x.Actor).HasMaxLength(256);
        e.Property(x => x.Subject).HasMaxLength(256);
        e.Property(x => x.Environment).HasMaxLength(32);
        e.Property(x => x.ConnectionAddress).HasMaxLength(45); // the longest textual IPv6 address
        e.HasIndex(x => x.OccurredAt).IsDescending().HasDatabaseName("audit_event_newest_first");
    }
}
