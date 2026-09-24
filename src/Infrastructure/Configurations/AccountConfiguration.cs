using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> e)
    {
        e.ToTable("account", t =>
        {
            t.HasCheckConstraint("account_window_ordered", "closed_on IS NULL OR closed_on >= opened_on");
            t.HasCheckConstraint("account_single_currency", Conventions.SingleCurrency);
        });
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.Property(x => x.Currency).HasColumnType(Conventions.Currency);
        e.Property(x => x.CreatedAt).HasDefaultValueSql(Conventions.Now);
        e.HasOne<Institution>().WithMany().HasForeignKey(x => x.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
