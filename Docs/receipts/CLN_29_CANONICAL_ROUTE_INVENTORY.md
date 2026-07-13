# CLN-29 Canonical Route Inventory Receipt

## Outcome

The frontend now has one executable and documented inventory for Session/front door, Project chooser, Board, Work Item, Library, Governance, Audit, and Settings. Board, Work Item, and Library are explicitly the primary product IA.

## Evidence

- `Docs/ux/CANONICAL_ROUTE_INVENTORY.md`
- `IronDev.TauriShell/src/flow/navigation/productRoutes.ts`
- `IronDev.TauriShell/tests/canonical-route-inventory.spec.ts`

## Boundary

This slice classifies routes only. It does not turn client routing into access, readiness, governance, approval, or execution authority. Compatibility-route presentation is handled separately by CLN-31.
