using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Pricing;

internal sealed class SchemeParameterEntityConfiguration : IEntityTypeConfiguration<SchemeParameterRow>
{
    public void Configure(EntityTypeBuilder<SchemeParameterRow> builder)
    {
        builder.ToTable("scheme_parameter");
        builder.HasKey(p => new { p.Scheme, p.Parameter, p.ValidFrom });

        builder.Property(p => p.Scheme).HasColumnName("scheme").HasMaxLength(50);
        builder.Property(p => p.Parameter).HasColumnName("parameter").HasMaxLength(50);
        builder.Property(p => p.ValidFrom).HasColumnName("valid_from");
        builder.Property(p => p.ValidTo).HasColumnName("valid_to");
        builder.Property(p => p.Value).HasColumnName("value").HasColumnType("numeric(12,4)");
        builder.Property(p => p.SourceUrl).HasColumnName("source_url").IsRequired();
    }
}
