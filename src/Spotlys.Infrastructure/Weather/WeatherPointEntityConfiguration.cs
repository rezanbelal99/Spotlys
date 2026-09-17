using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherPointEntityConfiguration : IEntityTypeConfiguration<WeatherPointRow>
{
    public void Configure(EntityTypeBuilder<WeatherPointRow> builder)
    {
        builder.ToTable("weather_point");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id").HasMaxLength(64).IsRequired();
        builder.Property(p => p.Zone).HasColumnName("zone").HasMaxLength(3).IsRequired();
        builder.Property(p => p.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Lat).HasColumnName("lat").HasColumnType("numeric(7,4)").IsRequired();
        builder.Property(p => p.Lon).HasColumnName("lon").HasColumnType("numeric(7,4)").IsRequired();
        builder.Property(p => p.AltitudeM).HasColumnName("altitude_m").IsRequired();
    }
}
