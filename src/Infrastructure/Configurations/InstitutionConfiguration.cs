using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

sealed class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> e)
    {
        e.ToTable("institution");
        e.Property(x => x.Id).HasDefaultValueSql(Conventions.NewUuid);
        e.Property(x => x.CreatedAt).HasDefaultValueSql(Conventions.Now);
    }
}
