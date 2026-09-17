using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Weather;

internal sealed class WeatherFetchCacheEntityConfiguration : IEntityTypeConfiguration<WeatherFetchCacheRow>
{
    public void Configure(EntityTypeBuilder<WeatherFetchCacheRow> builder)
    {
        builder.ToTable("weather_fetch_cache");

        builder.HasKey(c => c.PointId);

        builder.Property(c => c.PointId).HasColumnName("point_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.LastModified).HasColumnName("last_modified");
        builder.Property(c => c.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(c => c.FetchedAtUtc).HasColumnName("fetched_at_utc").IsRequired();

        builder.HasOne<WeatherPointRow>()
            .WithMany()
            .HasForeignKey(c => c.PointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
