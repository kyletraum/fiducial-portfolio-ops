using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain;

namespace Portfolio.Infrastructure.Configurations;

sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> e)
    {
        e.ToTable("app_setting", t =>
        {
            t.HasCheckConstraint("app_setting_single_row", "id");
            t.HasCheckConstraint("app_setting_staleness_positive", "balance_staleness_days > 0");
        });
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasData(new AppSetting { Id = true, BalanceStalenessDays = AppSetting.DefaultBalanceStalenessDays });
    }
}
