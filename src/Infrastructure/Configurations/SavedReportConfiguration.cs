using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

/// <summary>Migration 7: a saved report. Insert and delete only; the API login holds no UPDATE on it.</summary>
sealed class SavedReportConfiguration : IEntityTypeConfiguration<SavedReport>
{
    public void Configure(EntityTypeBuilder<SavedReport> e)
    {
        e.ToTable("saved_report", t =>
        {
            t.HasCheckConstraint("saved_report_name_present", "length(btrim(name)) BETWEEN 1 AND 200");
            t.HasCheckConstraint("saved_report_parameters_array", "jsonb_typeof(parameters) = 'array'");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.HasIndex(x => x.Name).IsUnique();
        e.Property(x => x.SqlText).HasColumnName("sql_text");
        e.Property(x => x.Parameters).HasColumnType("jsonb");
        e.Property(x => x.CreatedAt).HasDefaultValueSql(Conventions.Now);
    }
}
