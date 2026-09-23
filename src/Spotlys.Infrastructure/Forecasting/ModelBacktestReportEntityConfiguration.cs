using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Forecasting;

// docs/ARCHITECTURE.md §5's plan for Phase 4 Part 6: "a new small table
// model_backtest_report ... written by the Python model.yml pipeline and read by a new
// IModelSkillRepository -- same 'Python writes, .NET reads' pattern as model_version
// itself". Every run inserts a new row rather than updating one in place, so the endpoint
// can always report the most recent report's own generated_at_utc honestly.
internal sealed class ModelBacktestReportEntityConfiguration : IEntityTypeConfiguration<ModelBacktestReportRow>
{
    public void Configure(EntityTypeBuilder<ModelBacktestReportRow> builder)
    {
        builder.ToTable("model_backtest_report");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(r => r.Zone).HasColumnName("zone").HasMaxLength(3).IsRequired();
        builder.Property(r => r.GeneratedAtUtc).HasColumnName("generated_at_utc").IsRequired();
        builder.Property(r => r.ReportJson).HasColumnName("report_json").HasColumnType("jsonb").IsRequired();

        builder.HasIndex(r => new { r.Zone, r.GeneratedAtUtc });
    }
}
