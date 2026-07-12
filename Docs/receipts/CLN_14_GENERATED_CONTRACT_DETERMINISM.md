# CLN-14 Generated Contract Determinism Receipt

**Recorded:** 13 July 2026

## Delivered

- Added twice-generated SHA-256 comparison to the isolated contract generator.
- Made the determinism proof mandatory in frontend contract CI.
- Kept checked-in OpenAPI and TypeScript artifacts protected by the existing dirty-tree check.
- Corrected planned routes to advertise their runtime `501` response explicitly.
- Added version-pin, CI-wiring, and planned-route contract tests.

## Boundary

Generated consistency proves contract reproducibility. It is evidence, not API approval, release readiness, or authority.
