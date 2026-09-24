using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

sealed class AccountBalanceConfiguration : IEntityTypeConfiguration<AccountBalance>
{
    public void Configure(EntityTypeBuilder<AccountBalance> e)
    {
        e.ToTable("account_balance", t =>
            t.HasCheckConstraint("account_balance_single_currency", Conventions.SingleCurrency));
        e.Ignore(x => x.Amount);
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.Property(x => x.Balance).HasColumnType(Conventions.Money);
        e.Property(x => x.Currency).HasColumnType(Conventions.Currency);
        e.Property(x => x.CreatedAt).HasDefaultValueSql(Conventions.Now);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        // M-9 / R2-B3: the pointer is BACKWARD. A forward superseded_by would have to name a
        // row that does not exist yet, and aborts on the foreign key.
        e.HasOne<AccountBalance>().WithMany().HasForeignKey(x => x.Supersedes).OnDelete(DeleteBehavior.Restrict);

        // M-9: one live row per (account, date, source). Partial, so restatement history stays
        // queryable. It forces the write order: UPDATE the old row, THEN insert the new one.
        e.HasIndex(x => new { x.AccountId, x.AsOfDate, x.SourceSystem })
            .IsUnique()
            .HasFilter(Conventions.LiveBalance)
            .HasDatabaseName("account_balance_live_uq");

        // A row has at most one successor, or "the current value" stops being well defined.
        e.HasIndex(x => x.Supersedes)
            .IsUnique()
            .HasFilter("supersedes IS NOT NULL")
            .HasDatabaseName("account_balance_supersedes_uq");

        e.HasIndex(x => new { x.AccountId, x.AsOfDate })
            .IsDescending(false, true)
            .HasFilter(Conventions.LiveBalance)
            .HasDatabaseName("account_balance_series");
    }
}
