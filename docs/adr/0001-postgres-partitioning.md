# 0001 — Native Postgres declarative partitioning instead of TimescaleDB

- **Status:** Accepted
- **Date:** 2026-09-16
- **Supersedes:** —

## Context

`price_observation` is the first of several hourly time-series tables (weather, forecasts follow in later phases). docs/ARCHITECTURE.md §4 already commits to "plain Postgres, not TimescaleDB... native declarative partitioning by month plus BRIN indexes," reasoning that volume is small (five zones × hourly × a few years ≈ 200k rows for prices) and an extension is unjustified at this size. This ADR records that decision at the moment it was actually implemented (Phase 1's schema), per docs/ENGINEERING.md §5.

Two options considered:

1. **TimescaleDB** (hypertables, continuous aggregates, compression) — the purpose-built extension for exactly this workload.
2. **Plain Postgres 17 with native `PARTITION BY RANGE`**, monthly partitions, BRIN index on the partition key.

## Decision

Option 2. `price_observation` is created via raw SQL in the first EF Core migration (EF's fluent model can't express partitioning) as `PARTITION BY RANGE (hour_start_utc)`, with monthly partitions pre-created from 2022-09 (hvakosterstrommen's own archive start) through 2027-03. `ingestion_run` stays a plain table — it's an operational log, not one of "the large tables" this decision is about.

The BRIN index from docs/DATA.md §1's DDL (`CREATE INDEX ... USING brin (hour_start_utc)`) is created on the partitioned parent, which Postgres 11+ automatically propagates to every partition, existing and future. No change needed to that DDL for partitioning to work.

## Consequences

**Good**

- No new extension, no new operational surface (backup/restore, upgrade compatibility) on a single-node k3s deployment that's already deliberately minimal (docs/ARCHITECTURE.md §8).
- Monthly partitions still give the two things that matter at this volume: pruning on time-range queries, and eventually cheap bulk-drop of old partitions if retention policy ever needs it.
- The BRIN index — which the docs already specified independent of this decision — does real work either way; this ADR only changes what's underneath it.

**Bad / costs**

- Partitions must exist before a row can be inserted into them. The migration pre-creates a wide, fixed range (through 2027-03) rather than partitioning on demand. **No job creates future months' partitions** — docs/ARCHITECTURE.md §5's job table has no such entry. This is a real gap, not an oversight papered over: past 2027-03, inserts for new months will fail until either a partition-maintenance job is added or partitions are created manually.
- TimescaleDB's continuous aggregates and native compression are given up. Not missed yet at ~200k rows.
- Raw SQL in the migration (`migrationBuilder.Sql(...)`) instead of EF's fluent model means the schema-as-code isn't fully expressed through EF's own abstractions — anyone changing this table needs to hand-edit migrations, not just update `PriceObservationEntityConfiguration`.

**Revisit if**

- Row counts approach 50M, or range-scan queries need to be reliably sub-second at a volume where BRIN's lossy index stops being enough — the trigger point docs/ARCHITECTURE.md §4 itself names.
- The 2027-03 partition horizon approaches without a maintenance job existing yet — add one (a scheduled job creating the next N months, idempotently) before that date, or this becomes an outage rather than a design footnote.
