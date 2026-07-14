# CLN-25 Memory Versioning and Lifecycle Receipt

**Status:** Historical receipt

**Date:** 14 July 2026

## Delivered

- Added manifest-owned stable Project Canon identities and immutable version records.
- Added required content hash, actor, source evidence, effective/retirement time, supersession, and promotion-receipt fields.
- Added append-only triggers and same-scope supersession validation.
- Added a current-truth view excluding superseded/archived heads and a complete history procedure.
- Added clean-database verifier and static contract coverage.
- Added explicitly executed SQL-backed lifecycle tests for current projection, history, append-only refusal, and cross-scope supersession refusal.
- Enforced one root and one current leaf with transactional chain locks, including concurrent-root and concurrent-successor regression tests.

## Boundary

This slice supplies lifecycle storage and read projection. It does not expose a generic promotion endpoint, treat receipt identifiers as self-validating approval, or make derived indexes authoritative.
