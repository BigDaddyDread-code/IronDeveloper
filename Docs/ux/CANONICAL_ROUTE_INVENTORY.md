# Canonical Route Inventory

This is the navigation contract for the IronDev product shell. It names product surfaces, not every URL the router can resolve. Compatibility URLs may remain safe deep links, but they are not product information architecture and must not appear in primary navigation.

| Order | Surface | Canonical route | Scope | Primary IA |
|---:|---|---|---|---|
| 1 | Session / front door | `/` | Session | No |
| 2 | Project chooser | `/projects` | Tenant/session | No |
| 3 | Board | `/projects/{projectId}/board` | Project | Yes |
| 4 | Work Item | `/projects/{projectId}/work-items/{workItemId}` | Project | Yes |
| 5 | Library | `/projects/{projectId}/library` | Project | Yes |
| 6 | Governance | `/projects/{projectId}/library/governance` | Project | No |
| 7 | Audit | `/projects/{projectId}/library/audit` | Project | No |
| 8 | Settings | `/projects/{projectId}/library/settings` | Project/environment | No |

Board, Work Item, and Library are the primary product IA. Governance and Audit are explicit read/evidence surfaces under Library; neither grants approval or execution authority. Session entry and project selection establish scope before project work. Project Settings describes the client environment and does not establish backend readiness; `/settings` remains an unscoped compatibility entry path.

The executable counterpart is `canonicalSurfaces` in `IronDev.TauriShell/src/flow/navigation/productRoutes.ts`. `canonical-route-inventory.spec.ts` locks the membership, order, primary set, and non-compatibility classification.

## Route ownership rules

- Route parsing may preserve safe legacy links, but compatibility aliases are not added to `canonicalSurfaces`.
- Project routes never infer access from a URL. The backend remains authoritative for tenant membership, project access, readiness, governance, and audit truth.
- A Work Item URL identifies the requested item; it does not prove the item belongs to the route project.
- New primary navigation must target one of the canonical route templates above.
