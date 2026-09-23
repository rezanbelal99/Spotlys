"""
Assembles one zone's backtest report -- the baseline summary, the quantile model's own
per-regime summary, and decision regret (training.regret) -- into a single JSON document
and writes it to `model_backtest_report`. Read back by
`Spotlys.Application.Forecasting.IModelSkillRepository` for `GET /api/v1/model/skill/{zone}`
and the public `/model` page -- the same "Python writes, .NET reads" split ADR 0002 already
established for `model_version` (see `export/onnx_export.py`).

`build_report_json` is pure (no I/O), so the document shape is testable without a database;
`write_report` is the thin DB-write wrapper, exercised for real once `model.yml` actually
runs this end to end -- the same "not yet tested until exercised for real" note Phase 3
made about the model_version read-side repository before the export script existed.
"""

from __future__ import annotations

import json
from typing import Any

import pandas as pd

from spotlys_model.db import get_connection


def build_report_json(
    baseline_summary: pd.DataFrame,
    quantile_summary_by_regime: dict[str, pd.DataFrame],
    regret_summary: dict[str, float],
) -> dict[str, Any]:
    """`baseline_summary` is `training.backtest.summarize`'s own output;
    `quantile_summary_by_regime` maps 'A'/'B' to `training.lightgbm_model.summarize_quantile_model`'s
    output; `regret_summary` is `training.regret.summarize_regret`'s output. Records, not a
    DataFrame's own default JSON encoding, so the shape is stable and human-readable in the
    stored jsonb column."""
    return {
        "baselines": json.loads(baseline_summary.to_json(orient="records")),
        "quantile_model": {
            regime: json.loads(summary.to_json(orient="records"))
            for regime, summary in quantile_summary_by_regime.items()
        },
        "decision_regret": regret_summary,
    }


def write_report(zone: str, report: dict[str, Any]) -> int:
    """Inserts one report row and returns its id. Every run gets its own row rather than
    updating one in place (docs/FORECASTING.md §6: "the backtest emits ... a report per
    run"), so `GetLatestAsync`'s "most recent" ordering has real history to order over,
    matching how `model_version` accumulates rows instead of being overwritten."""
    generated_at = pd.Timestamp.now(tz="UTC").to_pydatetime()
    with get_connection() as conn:
        with conn.cursor() as cur:
            cur.execute(
                """
                INSERT INTO model_backtest_report (zone, generated_at_utc, report_json)
                VALUES (%s, %s, %s)
                RETURNING id
                """,
                (zone, generated_at, json.dumps(report)),
            )
            row = cur.fetchone()
        conn.commit()
    return row[0] if row else -1
