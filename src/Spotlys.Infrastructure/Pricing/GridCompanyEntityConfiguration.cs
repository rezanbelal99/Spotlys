using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Pricing;

internal sealed class GridCompanyEntityConfiguration : IEntityTypeConfiguration<GridCompanyRow>
{
    public void Configure(EntityTypeBuilder<GridCompanyRow> builder)
    {
        builder.ToTable("grid_company");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").HasMaxLength(50);
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
    }
}
