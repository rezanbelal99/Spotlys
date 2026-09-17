using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Ingestion;

// Maps to docs/DATA.md §6's ingestion_run DDL, plus the surrogate Id -- see IngestionRunRow.
internal sealed class IngestionRunEntityConfiguration : IEntityTypeConfiguration<IngestionRunRow>
{
    public void Configure(EntityTypeBuilder<IngestionRunRow> builder)
    {
        builder.ToTable("ingestion_run");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(r => r.JobName).HasColumnName("job_name").HasMaxLength(100).IsRequired();
        builder.Property(r => r.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(r => r.FinishedAt).HasColumnName("finished_at");
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(r => r.Rows).HasColumnName("rows");
        builder.Property(r => r.WindowFrom).HasColumnName("window_from");
        builder.Property(r => r.WindowTo).HasColumnName("window_to");
        builder.Property(r => r.Error).HasColumnName("error");

        builder.HasIndex(r => new { r.JobName, r.StartedAt });
    }
}
