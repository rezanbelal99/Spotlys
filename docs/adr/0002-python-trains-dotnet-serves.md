# 0002 — Python trains the model, .NET serves it via ONNX

- **Status:** Accepted
- **Date:** 2026-09-15
- **Supersedes:** —

## Context

The forecasting core is gradient-boosted quantile regression. The natural training environment is Python (LightGBM, Optuna, pandas, the whole evaluation ecosystem). The natural serving environment is the existing ASP.NET Core API, which already holds the tariff engine, the optimizer and the database.

Three options were considered:

1. **Train and serve in Python** — a FastAPI sidecar the .NET API calls over HTTP.
2. **Train and serve in .NET** — ML.NET's FastTree.
3. **Train in Python, export to ONNX, serve in-process from .NET.**

Constraints that mattered: a single small VPS under 150 kr/month; a solo developer; forecast workload of 5 zones × 168 hours × 5 quantiles, recomputed twice a day and cached.

## Decision

Option 3. LightGBM models are exported with `onnxmltools` at a pinned opset and loaded by `Microsoft.ML.OnnxRuntime` inside the API process.

## Consequences

**Good**

- No second runtime in production, no extra container, no network hop, no sidecar to keep alive or secure. Inference is single-digit milliseconds in-process.
- The full Python evaluation ecosystem stays available for training, tuning and backtesting, where it is genuinely better than anything in .NET.
- Models become versioned artefacts with a SHA and provenance (`model_version` table), which makes rollback a database flag rather than a redeploy.
- The pattern transfers directly to the machine-vision work, where the same export-and-serve shape applies.

**Bad / costs**

- Two languages in one repo: two toolchains, two CI paths, two sets of dependency updates.
- ONNX conversion is a real source of silent divergence, particularly around categorical features and missing-value handling. **Mitigated by a mandatory parity test** (1 000 random rows, max abs diff < 1e-5) that runs in CI on every export.
- The feature builder must produce identical output in training and serving. Mitigated by keeping one implementation in Python and shipping the computed feature vector to .NET at inference time — the .NET side assembles features from the database using a generated schema contract, and a cross-language fixture test asserts equality for a fixed `as_of`.
- ONNX Runtime adds ~40 MB to the container image.

**Revisit if**

- Inference latency ever exceeds 50 ms, or the model family changes to something ONNX handles poorly (custom loss, unusual layers). At that point a Python sidecar becomes justified and this ADR should be superseded rather than quietly ignored.
