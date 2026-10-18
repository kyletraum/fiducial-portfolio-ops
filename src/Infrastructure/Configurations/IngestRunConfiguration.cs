using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

/// <summary>
/// FR-4.6, migration 4. The immutability trigger is raw SQL in the migration; these CHECKs hold
/// the all-or-nothing rule on the row itself.
/// </summary>
sealed class IngestRunConfiguration : IEntityTypeConfiguration<IngestRun>
{
    public void Configure(EntityTypeBuilder<IngestRun> e)
    {
        e.ToTable("ingest_run", t =>
        {
            t.HasCheckConstraint("ingest_run_counts_nonnegative",
                "rows_read >= 0 AND created >= 0 AND restated >= 0 AND unchanged >= 0");
            // A failed run wrote nothing; a succeeded run accounts for every row it read.
            t.HasCheckConstraint("ingest_run_all_or_nothing",
                "(status = 'failed' AND created = 0 AND restated = 0 AND unchanged = 0 AND error_detail IS NOT NULL)"
                + " OR (status = 'succeeded' AND created + restated + unchanged = rows_read AND error_detail IS NULL)");
            t.HasCheckConstraint("ingest_run_finished_after_started", "finished_at >= started_at");
            t.HasCheckConstraint("ingest_run_sha256_hex", "file_sha256 ~ '^[0-9a-f]{64}$'");
        });
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.Property(x => x.SourceSystem).HasMaxLength(32);
        e.Property(x => x.FileName).HasMaxLength(255);
        e.Property(x => x.FileSha256).HasColumnType("character(64)");
        e.Property(x => x.ErrorDetail).HasColumnType("jsonb");
        e.HasIndex(x => x.StartedAt).IsDescending().HasDatabaseName("ingest_run_newest_first");
    }
}
