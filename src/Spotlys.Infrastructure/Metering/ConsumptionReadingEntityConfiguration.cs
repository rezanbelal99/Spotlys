using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spotlys.Infrastructure.Accounts;

namespace Spotlys.Infrastructure.Metering;

internal sealed class ConsumptionReadingEntityConfiguration : IEntityTypeConfiguration<ConsumptionReadingRow>
{
    public void Configure(EntityTypeBuilder<ConsumptionReadingRow> builder)
    {
        // docs/DATA.md §4: "store per-meter series in a separate schema with its own
        // retention policy" -- a real schema boundary, not just a table naming convention.
        builder.ToTable("consumption_reading", schema: "metering");

        builder.HasKey(c => new { c.MeterProfileId, c.HourStartUtc });

        builder.Property(c => c.MeterProfileId).HasColumnName("meter_profile_id").IsRequired();
        builder.Property(c => c.HourStartUtc).HasColumnName("hour_start_utc").IsRequired();
        builder.Property(c => c.Kwh).HasColumnName("kwh").HasColumnType("numeric(10,4)").IsRequired();
        builder.Property(c => c.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(c => c.ImportedAtUtc).HasColumnName("imported_at_utc").IsRequired();

        builder.HasIndex(c => c.HourStartUtc).HasMethod("brin");

        builder.HasOne<MeterProfileRow>().WithMany().HasForeignKey(c => c.MeterProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}
