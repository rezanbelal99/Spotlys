using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spotlys.Infrastructure.Pricing;

namespace Spotlys.Infrastructure.Accounts;

internal sealed class MeterProfileEntityConfiguration : IEntityTypeConfiguration<MeterProfileRow>
{
    public void Configure(EntityTypeBuilder<MeterProfileRow> builder)
    {
        builder.ToTable("meter_profile");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.Zone).HasColumnName("zone").HasMaxLength(3).IsRequired();
        builder.Property(m => m.GridCompanyId).HasColumnName("grid_company_id").IsRequired();
        builder.Property(m => m.SupportScheme).HasColumnName("support_scheme").HasMaxLength(20).IsRequired();
        builder.Property(m => m.IsCabin).HasColumnName("is_cabin").HasDefaultValue(false).IsRequired();
        builder.Property(m => m.SupplierMarkupExVatOrePerKwh)
            .HasColumnName("supplier_markup_ore").HasColumnType("numeric(8,4)").HasDefaultValue(0m).IsRequired();
        builder.Property(m => m.SupplierMonthlyFeeExVatNok)
            .HasColumnName("supplier_monthly_fee_nok").HasColumnType("numeric(8,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(m => m.ConsumptionRetentionYears)
            .HasColumnName("consumption_retention_years").HasDefaultValue(3).IsRequired();
        builder.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(m => m.UserId);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GridCompanyRow>().WithMany().HasForeignKey(m => m.GridCompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
