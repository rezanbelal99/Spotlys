// Manual, one-time activation -- Phase 3's models were exported with is_active=false on
// purpose (promotion was left manual, docs/FORECASTING.md §7: "Rollback = flip is_active").
// ARCHITECTURE.md §5's automatic gated-promotion job (RetrainModels) is later, separate
// work; this migration exists so Phase 4's forecast serving has something real to load,
// reviewed like any other data change rather than a manual UPDATE run once and forgotten.
//
// These models' walk-forward backtest skill was already reported to the user as
// mixed/marginal (Regime A loses to B0 at 0-24h lead, ties B3 at 24-48h; Regime B wins at
// 24-48h/48-96h/96-168h) with calibration coverage below nominal -- that stays true after
// this migration. Activating them is what lets the serving slice be tested end-to-end with
// a real model, not a claim that they're production-ready by some new bar.
//
// Picks, per (zone, regime, quantile), the row with the latest trained_at_utc -- the most
// recent of Phase 3's single training run, but written this way (not a bare id list) so a
// future re-run of the export script before RetrainModels exists still activates the
// newest row rather than a stale one.
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spotlys.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ActivateInitialModels : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE model_version
            SET is_active = true
            WHERE zone = 'NO2' AND regime IN ('A', 'B')
              AND trained_at_utc = (
                  SELECT MAX(m2.trained_at_utc)
                  FROM model_version m2
                  WHERE m2.zone = model_version.zone
                    AND m2.regime = model_version.regime
                    AND m2.quantile = model_version.quantile
              );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE model_version SET is_active = false WHERE zone = 'NO2' AND regime IN ('A', 'B');
            """);
    }
}
