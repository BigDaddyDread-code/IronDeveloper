# CLN-12 Actor Attribution Receipt

**Recorded:** 13 July 2026

## Delivered

- Added one authenticated-write attribution middleware for all API controllers and minimal routes.
- Added an append-only SQL ledger containing actor, tenant/project scope, correlation, optional causation, timestamp, source, request, phase, and status.
- Made the pre-dispatch attribution attempt fail closed.
- Projected terminal rows into the existing read-only project audit ledger.
- Added migration-manifest and verifier coverage plus focused middleware contract tests.

## Boundary

Attribution records what an authenticated actor attempted and what the API returned. It does not prove the actor was authorized, and it cannot grant authority.
