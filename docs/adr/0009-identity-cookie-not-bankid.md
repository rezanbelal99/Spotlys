# 0009 — ASP.NET Core Identity with cookie auth, not BankID

- **Status:** Accepted
- **Date:** 2026-09-22
- **Supersedes:** —

## Context

Phase 4 introduces the first personal data into the system (accounts, meter profiles,
metered consumption), so an authentication mechanism is needed. docs/ARCHITECTURE.md §6
already named two options when it was written: "ASP.NET Core Identity with cookie auth, or
BankID-style OIDC if you want the Norwegian flavour."

BankID (or the newer Norwegian ID-porten/MinID stack) is the idiomatic choice for a real
Norwegian consumer product — it's what every bank, Altinn integration and government
service actually uses, and integrating it would be a genuine, demonstrable piece of
Norwegian-market domain knowledge.

## Decision

ASP.NET Core Identity, `AddIdentityCore<AppUser>` (not the full `AddIdentity` — no
role-based authorization anywhere in this product, so `AspNetRoles` and friends are never
created), with cookie authentication.

## Consequences

**Good**

- No external dependency to register for, wait on, or pay for. BankID/ID-porten
  integration requires becoming a registered service provider with the broker (BankID
  Bedrift, Signicat, Criipto, or a direct ID-porten integration via Digdir) — a real
  business/legal onboarding step, not just an SDK, and not realistic for a solo portfolio
  project on a timeline (the same class of institutional barrier DATA.md §4 already
  documents for Elhub third-party access).
- Well-trodden or common on ASP.NET Core; the whole team is one person, so operational
  simplicity matters more than protocol sophistication.
- Keeps the account model entirely within this codebase's own control for the GDPR
  export/delete endpoints this same phase requires — no data residing with a third-party
  identity broker to also account for in the export.

**Bad / costs**

- Password-based auth is exactly the mechanism a Norwegian user would find unfamiliar for
  a financial-adjacent product; BankID login is the expected pattern in this market.
  Documented here rather than silently accepted.
- No 2FA/passkey support wired up this phase (ASP.NET Core Identity's own passkey support
  exists in this version but is deliberately not modeled — see `SpotlysDbContext`'s
  `Ignore<IdentityUserPasskey<Guid>>()` call and its own comment).

**Revisit if**

- The product moves beyond a portfolio piece toward something real users would actually
  register for. At that point BankID/ID-porten integration is worth the onboarding cost,
  and this ADR should be superseded, not quietly patched around.
