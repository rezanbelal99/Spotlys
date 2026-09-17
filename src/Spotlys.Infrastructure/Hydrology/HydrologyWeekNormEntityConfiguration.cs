using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Hydrology;

internal sealed class HydrologyWeekNormEntityConfiguration : IEntityTypeConfiguration<HydrologyWeekNormRow>
{
    public void Configure(EntityTypeBuilder<HydrologyWeekNormRow> builder)
    {
        builder.ToTable("hydrology_week_norm");

        builder.HasKey(h => new { h.Zone, h.IsoWeek });

        builder.Property(h => h.Zone).HasColumnName("zone").HasMaxLength(3).IsRequired();
        builder.Property(h => h.IsoWeek).HasColumnName("iso_week").IsRequired();
        builder.Property(h => h.MinFillFraction).HasColumnName("min_fill_fraction").HasColumnType("real").IsRequired();
        builder.Property(h => h.MedianFillFraction).HasColumnName("median_fill_fraction").HasColumnType("real").IsRequired();
        builder.Property(h => h.MaxFillFraction).HasColumnName("max_fill_fraction").HasColumnType("real").IsRequired();
        builder.Property(h => h.FetchedAtUtc).HasColumnName("fetched_at_utc").IsRequired();
    }
}
