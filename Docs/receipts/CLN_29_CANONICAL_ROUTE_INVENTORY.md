# CLN-29 Canonical Route Inventory Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Outcome

The frontend now has one executable and documented inventory for Session/front door, Project chooser, Board, Work Item, Library, Governance, Audit, and Settings. Board, Work Item, and Library are explicitly the primary product IA.

## Evidence

- `Docs/ux/CANONICAL_ROUTE_INVENTORY.md`
- `IronDev.TauriShell/src/flow/navigation/productRoutes.ts`
- `IronDev.TauriShell/tests/canonical-route-inventory.spec.ts`

The executable proof checks exact templates, parser kinds, project scope, Work Item identity, and the complete unscoped compatibility alias set. A `notFound` parse is not accepted as a canonical route.

## Boundary

This slice classifies routes only. It does not turn client routing into access, readiness, governance, approval, or execution authority. Compatibility-route presentation is handled separately by CLN-31.
