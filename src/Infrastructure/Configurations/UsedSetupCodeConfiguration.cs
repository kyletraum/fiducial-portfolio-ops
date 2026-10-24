using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

/// <summary>Migration 5: a spent setup code, keyed by its hash.</summary>
sealed class UsedSetupCodeConfiguration : IEntityTypeConfiguration<UsedSetupCode>
{
    public void Configure(EntityTypeBuilder<UsedSetupCode> e)
    {
        e.ToTable("used_setup_code", t => t.HasCheckConstraint("used_setup_code_sha256_hex", "sha256 ~ '^[0-9a-f]{64}$'"));
        e.HasKey(x => x.Sha256);
        e.Property(x => x.Sha256).HasColumnType("character(64)");
        e.Property(x => x.UsedAt).HasDefaultValueSql(Conventions.Now);
    }
}
