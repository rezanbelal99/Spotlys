from __future__ import annotations

import pandas as pd

from spotlys_model.training.report import build_report_json


def test_build_report_json_produces_the_shape_the_dotnet_repository_parses():
    baseline_summary = pd.DataFrame(
        [{"lead_bucket": "0-24h", "baseline": "b1", "n": 100, "mae_ore_per_kwh": 12.5, "skill_vs_b1": 0.0}]
    )
    quantile_summary_by_regime = {
        "A": pd.DataFrame(
            [
                {
                    "lead_bucket": "0-24h",
                    "n": 100,
                    "mae_ore_per_kwh": 9.1,
                    "skill_vs_b1": 0.272,
                    "pinball_loss": 4.4,
                    "coverage_50": 0.48,
                    "coverage_90": 0.87,
                }
            ]
        ),
        "B": pd.DataFrame(
            [
                {
                    "lead_bucket": "0-24h",
                    "n": 80,
                    "mae_ore_per_kwh": 7.0,
                    "skill_vs_b1": 0.4,
                    "pinball_loss": 3.1,
                    "coverage_50": 0.49,
                    "coverage_90": 0.88,
                }
            ]
        ),
    }
    regret_summary = {
        "n_days": 42,
        "mean_regret_vs_perfect_foresight_nok": 3.2,
        "mean_regret_vs_charge_on_arrival_nok": -6.5,
        "mean_regret_vs_always_0200_nok": -1.1,
        "median_regret_vs_charge_on_arrival_nok": -5.9,
        "median_regret_vs_always_0200_nok": -0.8,
    }

    report = build_report_json(baseline_summary, quantile_summary_by_regime, regret_summary)

    # Exactly the field names Spotlys.Infrastructure.Forecasting.ModelBacktestReportRepository
    # parses -- a rename on either side has no compiler to catch it (ADR 0002), so this test
    # and that repository's own integration test are the closest thing to a contract check.
    assert report["quantile_model"]["A"][0]["lead_bucket"] == "0-24h"
    assert report["quantile_model"]["A"][0]["mae_ore_per_kwh"] == 9.1
    assert report["quantile_model"]["A"][0]["skill_vs_b1"] == 0.272
    assert report["quantile_model"]["A"][0]["coverage_90"] == 0.87
    assert "B" in report["quantile_model"]
    assert report["decision_regret"]["n_days"] == 42
    assert report["decision_regret"]["mean_regret_vs_charge_on_arrival_nok"] == -6.5
    assert report["baselines"][0]["baseline"] == "b1"
