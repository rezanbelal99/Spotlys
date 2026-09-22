using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Metering;

internal sealed class ConsumptionRetentionPurgeEntityConfiguration : IEntityTypeConfiguration<ConsumptionRetentionPurgeRow>
{
    public void Configure(EntityTypeBuilder<ConsumptionRetentionPurgeRow> builder)
    {
        builder.ToTable("consumption_retention_purge");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(p => p.MeterProfileId).HasColumnName("meter_profile_id").IsRequired();
        builder.Property(p => p.PurgedThroughUtc).HasColumnName("purged_through_utc").IsRequired();
        builder.Property(p => p.RowsDeleted).HasColumnName("rows_deleted").IsRequired();
        builder.Property(p => p.RunAtUtc).HasColumnName("run_at_utc").IsRequired();
    }
}
