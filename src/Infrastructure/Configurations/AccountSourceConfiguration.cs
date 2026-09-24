using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

/// <summary>
/// M-10, delivered as migration 2 on purpose (round 2): the same DDL as folding it into
/// migration 1, but a second migration applied to a database that already holds rows is
/// the one worth practising.
/// </summary>
sealed class AccountSourceConfiguration : IEntityTypeConfiguration<AccountSource>
{
    public void Configure(EntityTypeBuilder<AccountSource> e)
    {
        e.ToTable("account_source");
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.Property(x => x.CreatedAt).HasDefaultValueSql(Conventions.Now);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        // FULL, not partial: a source id once seen must never be re-bindable to a different
        // account by soft-deleting the first binding.
        e.HasIndex(x => new { x.SourceSystem, x.SourceId })
            .IsUnique()
            .HasDatabaseName("account_source_identity");
    }
}
