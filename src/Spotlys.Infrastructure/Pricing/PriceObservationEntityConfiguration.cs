using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Spotlys.Domain.Pricing;

namespace Spotlys.Infrastructure.Pricing;

// Maps directly to docs/DATA.md §1's price_observation DDL. Column names are explicit
// snake_case rather than relying on a naming-convention package (none is named in the
// docs) -- one line of mapping per column is cheap and exact.
internal sealed class PriceObservationEntityConfiguration : IEntityTypeConfiguration<PriceObservation>
{
    public void Configure(EntityTypeBuilder<PriceObservation> builder)
    {
        builder.ToTable("price_observation");

        builder.HasKey(p => new { p.Zone, p.HourStartUtc, p.Source });

        builder.Property(p => p.Zone)
            .HasColumnName("zone")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(p => p.HourStartUtc)
            .HasColumnName("hour_start_utc")
            .IsRequired();

        builder.Property(p => p.PriceExVatOrePerKwh)
            .HasColumnName("price_ex_vat")
            .HasColumnType("numeric(10,4)")
            .IsRequired();

        builder.Property(p => p.CurrencyRate)
            .HasColumnName("currency_rate")
            .HasColumnType("numeric(10,6)");

        builder.Property(p => p.Source)
            .HasColumnName("source")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.ObservedAtUtc)
            .HasColumnName("observed_at_utc")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(p => p.HourStartUtc)
            .HasMethod("brin");
    }
}
