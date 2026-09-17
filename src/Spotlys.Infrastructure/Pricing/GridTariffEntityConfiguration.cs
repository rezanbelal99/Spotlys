using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Pricing;

internal sealed class GridTariffEntityConfiguration : IEntityTypeConfiguration<GridTariffRow>
{
    public void Configure(EntityTypeBuilder<GridTariffRow> builder)
    {
        builder.ToTable("grid_tariff");
        builder.HasKey(g => new { g.GridCompanyId, g.ValidFrom });

        builder.Property(g => g.GridCompanyId).HasColumnName("grid_company_id").HasMaxLength(50);
        builder.Property(g => g.ValidFrom).HasColumnName("valid_from");
        builder.Property(g => g.EnergyDayOre).HasColumnName("energy_day_ore").HasColumnType("numeric(8,4)");
        builder.Property(g => g.EnergyNightOre).HasColumnName("energy_night_ore").HasColumnType("numeric(8,4)");
        builder.Property(g => g.SourceUrl).HasColumnName("source_url").IsRequired();

        builder.Property(g => g.CapacitySteps)
            .HasColumnName("capacity_steps")
            .HasColumnType("jsonb")
            .HasConversion(
                steps => JsonSerializer.Serialize(steps, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<List<CapacityStepRow>>(json, JsonSerializerOptions.Web)
                    ?? new List<CapacityStepRow>(),
                new ValueComparer<List<CapacityStepRow>>(
                    (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                    steps => steps.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                    steps => steps.ToList()));
    }
}
