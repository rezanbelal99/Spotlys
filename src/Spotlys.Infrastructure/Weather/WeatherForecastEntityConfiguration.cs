using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Weather;

// Exactly docs/DATA.md §2's DDL: point_id, issued_at_utc, fetched_at_utc, valid_at_utc,
// then the five real-typed measurement columns.
internal sealed class WeatherForecastEntityConfiguration : IEntityTypeConfiguration<WeatherForecastRow>
{
    public void Configure(EntityTypeBuilder<WeatherForecastRow> builder)
    {
        builder.ToTable("weather_forecast");

        builder.HasKey(w => new { w.PointId, w.IssuedAtUtc, w.ValidAtUtc });

        builder.Property(w => w.PointId).HasColumnName("point_id").HasMaxLength(64).IsRequired();
        builder.Property(w => w.IssuedAtUtc).HasColumnName("issued_at_utc").IsRequired();
        builder.Property(w => w.FetchedAtUtc).HasColumnName("fetched_at_utc").IsRequired();
        builder.Property(w => w.ValidAtUtc).HasColumnName("valid_at_utc").IsRequired();
        builder.Property(w => w.TempC).HasColumnName("temp_c").HasColumnType("real");
        builder.Property(w => w.WindMs).HasColumnName("wind_ms").HasColumnType("real");
        builder.Property(w => w.WindDirDeg).HasColumnName("wind_dir_deg").HasColumnType("real");
        builder.Property(w => w.CloudFrac).HasColumnName("cloud_frac").HasColumnType("real");
        builder.Property(w => w.PrecipMm).HasColumnName("precip_mm").HasColumnType("real");

        builder.HasIndex(w => w.ValidAtUtc).HasMethod("brin");

        builder.HasOne<WeatherPointRow>()
            .WithMany()
            .HasForeignKey(w => w.PointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
