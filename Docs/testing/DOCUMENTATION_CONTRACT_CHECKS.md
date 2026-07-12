# Documentation Contract Checks

**Status:** Canonical executable documentation contract

**Last reviewed:** 13 July 2026

**Programme slice:** CLN-07

## Entry Point

```powershell
.\Scripts\ci\run-documentation-contract-ci.ps1
```

GitHub executes the same command in `documentation-contract-ci`. The job uploads a sanitized JSON report from `artifacts/ci/documentation-contract`.

## Enforced Contracts

| Check | Failure meaning |
| --- | --- |
| Complete inventory | A Markdown file is absent from the truth inventory, an inventory path no longer exists, or a path is duplicated. |
| Required columns and statuses | The inventory can no longer answer authority, verification, replacement, action, or ownership questions. |
| Relative links | Any local Markdown target is missing, malformed, or escapes the repository. There is no broken-link baseline. |
| Active document identity | Two Canonical/Supporting documents claim the same H1 identity. |
| Non-current banners | A Superseded or ParkingLot document can appear active when opened directly. |
| Deprecated language | A canonical document or user-facing client/API source reintroduces an unambiguous deprecated phrase or removed display value. |
| Product routes | A canonical document names a product route outside the implemented canonical or explicitly supported compatibility route set. |
| Canonical references | A documentation entry point stops linking to its named authority documents. |

## Boundaries

- The check validates file targets, not remote URLs or same-page anchor spelling.
- Historical receipts are link-checked but never rewritten automatically.
- Broad words such as `chat`, `ticket`, `viewer`, and `cockpit` are not banned as substrings. Only the phrases fixed by the terminology contract are rejected in current authority surfaces.
- API routes are excluded from the product-navigation route parser and remain owned by OpenAPI/controller contract checks.
- A green documentation job is evidence only. It is not approval, release readiness, deployment readiness, or execution permission.

## Local Result

At CLN-07 the repository has zero broken relative links and zero duplicate active H1 identities. New defects therefore fail directly; no allowlist hides legacy drift.
