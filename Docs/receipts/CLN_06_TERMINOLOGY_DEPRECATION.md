# CLN-06 Terminology Deprecation Receipt

**Date:** 13 July 2026

**Slice:** CLN-06

## Delivered

- Added the canonical terminology deprecation map.
- Corrected current Board and Governance prose.
- Corrected Board-focused test names and comments without renaming compatibility selectors.
- Replaced one visible technical-evidence warning.
- Preserved historical receipts, quoted user fixtures, routes, DTOs, persisted values, filenames, and compatibility identifiers.
- Updated the documentation truth inventory.

## Behavior

User-visible wording changed in one read-only Governance warning. Runtime authority, routing, persistence, and capability are unchanged.

## Verification

```text
Required terminology examples: PASS (10 of 10)
Deprecated active-language scan: PASS (0 hits outside the map and inventory)
Focused frontend tests: PASS (5 Board-entry tests)
TypeScript type-check and production build: PASS
Documentation inventory coverage: PASS (617 expected, 617 unique rows)
Historical receipt mutations: PASS (none)
git diff --check: PASS
```

## Review Line

Current language names the real product surface and keeps evidence distinct from authority.

## Killjoy Line

Compatibility identifiers survive this pass because copy cleanup is not permission to break contracts.
