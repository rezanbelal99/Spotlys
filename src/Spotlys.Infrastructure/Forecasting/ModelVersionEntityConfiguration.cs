using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Spotlys.Infrastructure.Forecasting;

// Maps to docs/FORECASTING.md §7's model_version DDL, plus the regime/onnx_path columns
// documented in Spotlys.Application.Forecasting.ModelVersionEntry's own XML doc.
internal sealed class ModelVersionEntityConfiguration : IEntityTypeConfiguration<ModelVersionRow>
{
    public void Configure(EntityTypeBuilder<ModelVersionRow> builder)
    {
        builder.ToTable("model_version");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(m => m.Zone).HasColumnName("zone").HasMaxLength(3).IsRequired();
        builder.Property(m => m.Regime).HasColumnName("regime").HasMaxLength(1).IsRequired();
        builder.Property(m => m.Quantile).HasColumnName("quantile").HasColumnType("numeric(3,2)").IsRequired();
        builder.Property(m => m.TrainedAtUtc).HasColumnName("trained_at_utc").IsRequired();
        builder.Property(m => m.TrainDataToUtc).HasColumnName("train_data_to").IsRequired();
        builder.Property(m => m.GitSha).HasColumnName("git_sha").HasMaxLength(40).IsRequired();
        builder.Property(m => m.OnnxSha256).HasColumnName("onnx_sha256").HasMaxLength(64).IsRequired();
        builder.Property(m => m.OnnxPath).HasColumnName("onnx_path").IsRequired();
        builder.Property(m => m.BacktestMae).HasColumnName("backtest_mae").HasColumnType("numeric(10,4)");
        builder.Property(m => m.BacktestSkill).HasColumnName("backtest_skill").HasColumnType("numeric(6,4)");
        builder.Property(m => m.IsActive).HasColumnName("is_active").HasDefaultValue(false).IsRequired();

        builder.HasIndex(m => new { m.Zone, m.Regime, m.Quantile, m.TrainedAtUtc });

        // At most one active model per zone/regime/quantile (docs/FORECASTING.md §7:
        // "Rollback = flip is_active" only makes sense if "the active one" is unambiguous).
        builder.HasIndex(m => new { m.Zone, m.Regime, m.Quantile })
            .IsUnique()
            .HasFilter("is_active");
    }
}
