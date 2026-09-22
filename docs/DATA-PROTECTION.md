# DATA-PROTECTION.md — lawful basis, retention, and data subject rights

Written at the point personal data first enters the system (Phase 4), not retrofitted —
docs/ARCHITECTURE.md §9 requires this document to exist in the same change as the accounts
feature, not after.

---

## 1. What personal data Spotlys holds

| Data | Table | Why it's collected |
|---|---|---|
| Email address, password hash | `app_user` (ASP.NET Core Identity's own store) | Account identification and authentication |
| Zone, grid company, support scheme, tariff details | `meter_profile` | Needed to compute *this household's* bill (docs/DOMAIN.md §5) — a raw spot price is meaningless without it |
| Hourly metered consumption | `metering.consumption_reading` | Peak tracking (docs/DOMAIN.md §4a), bill simulation, and the regime advisor |

No other personal data is collected. Anonymous, demo-mode use of the product (today's
prices, the seeded demo household's bill) requires no account and stores nothing about the
visitor.

## 2. Lawful basis

Processing is on the basis of **contract** (GDPR Art. 6(1)(b)): the data is collected
because the household explicitly asked Spotlys to compute their bill, track their peaks,
or advise on their tariff, and none of those functions work without it. There is no
processing for advertising, profiling, or any purpose the user did not directly ask for.

## 3. Where the data lives

Entirely self-hosted (docs/DEVOPS.md §1): one Postgres instance, one droplet. No
third-party analytics, no data broker, no sub-processor. `metering.consumption_reading`
lives in its own Postgres **schema**, not just a separate table — a real access boundary,
not a naming convention (docs/DATA.md §4).

**Real household consumption data never leaves production** — no "copy prod to staging"
for testing (docs/DEVOPS.md §1). Staging is seeded with synthetic profiles only.

## 4. Retention

Default **3 years** per meter (`meter_profile.consumption_retention_years`), matching
Elhub's own retention period for private customers (docs/DATA.md §4). A household can
extend this per meter; there is currently no UI to do so (a documented gap, not a silent
one — the column exists, the setting isn't yet user-editable).

Enforced by `PurgeExpiredConsumptionUseCase` (`Spotlys.Ingestion.Metering.PurgeExpiredConsumptionJob`),
run hourly. Every purge that actually deletes rows writes an audit record to
`consumption_retention_purge` (meter id, cutoff applied, rows deleted, when) — a retention
job with no record of what it did would itself be unauditable (docs/DEVOPS.md §7).

Deleting the account (§6 below) removes everything immediately, superseding the
3-year schedule for that household entirely.

## 5. Data subject rights

### Access / export

`GET /api/v1/account/export` (authenticated, self only). Returns every piece of personal
data Spotlys holds about the requester in one JSON document: the account's own fields
(minus the password hash, which is never exposed even to its owner), every owned meter
profile, and every consumption reading ever imported for each meter. No manual process,
no delay, no support ticket required — available from day one, not retrofitted
(docs/ARCHITECTURE.md §9).

### Erasure

`DELETE /api/v1/account` (authenticated, self only, requires re-entering the account
password since the action is irreversible). Deletes the `app_user` row; every
`meter_profile` and every `metering.consumption_reading` row cascades via foreign key in
the same transaction. Nothing is soft-deleted or retained "for analytics."

### Rectification

Not yet built (a documented gap): there is no endpoint to edit a meter profile's own
fields after creation. Export + delete + re-register is the interim path. Worth building
once the product has more than one meter-editing use case to justify it.

## 6. Security measures

- Passwords hashed by ASP.NET Core Identity's own `PasswordHasher` (PBKDF2), never stored
  or logged in plain text.
- Cookie authentication: `HttpOnly`, sliding 14-day expiration (docs/ARCHITECTURE.md §6).
- Every meter-scoped endpoint checks `meter_profile.user_id` against the authenticated
  user before returning or accepting data for it — a data-dependent check performed in the
  use case, not just relying on the route being behind `RequireAuthorization()`
  (see `Spotlys.Api.Metering.ConsumptionImportEndpoints` for the pattern: a mismatch
  returns 404, not 403, so a meter id a caller doesn't own doesn't even confirm it exists).
- Consumption CSV upload: size-capped, strict line-by-line parsing into a validated DTO,
  never `File.ReadAllText` into a raw string (docs/ARCHITECTURE.md §9).

## 7. Contact

For a real deployment this section would name a controller and a contact address. For this
portfolio project, treat the repository's own issue tracker as the contact point.
