using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Hydrology;

internal sealed class HydrologyObservationEntityConfiguration : IEntityTypeConfiguration<HydrologyObservationRow>
{
    public void Configure(EntityTypeBuilder<HydrologyObservationRow> builder)
    {
        builder.ToTable("hydrology_observation");

        builder.HasKey(h => new { h.Zone, h.WeekStartDate, h.Source });

        builder.Property(h => h.Zone).HasColumnName("zone").HasMaxLength(3).IsRequired();
        builder.Property(h => h.WeekStartDate).HasColumnName("week_start_date").IsRequired();
        builder.Property(h => h.FetchedAtUtc).HasColumnName("fetched_at_utc").IsRequired();
        builder.Property(h => h.FillFraction).HasColumnName("fill_fraction").HasColumnType("real").IsRequired();
        builder.Property(h => h.CapacityTwh).HasColumnName("capacity_twh").HasColumnType("real").IsRequired();
        builder.Property(h => h.FillTwh).HasColumnName("fill_twh").HasColumnType("real").IsRequired();
        builder.Property(h => h.NextPublicationUtc).HasColumnName("next_publication_utc");
        builder.Property(h => h.Source).HasColumnName("source").HasMaxLength(30).IsRequired();
    }
}
