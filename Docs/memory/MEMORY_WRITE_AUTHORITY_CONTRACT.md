# Memory Write Authority Contract

**Status:** Canonical memory authority contract

**Last reviewed:** 14 July 2026

**Programme slice:** CLN-24

## Operation Split

| Operation | Direct authenticated user path | Authority rule |
| --- | --- | --- |
| Record context observation | Allowed for a project member | Route project and token tenant win; server stores `ObservedFact` / `Active`; no supersession claim |
| Create memory proposal | Allowed through the proposal-only API/store | Proposal cannot assert accepted memory, promotion, vector authority, or enforcement |
| Review proposal | Read/review surface only | Review is not promotion |
| Promote approved memory | No generic direct path | Requires a separate governed promotion implementation and receipt |
| Supersede/archive memory | Maintenance operation | Owner or TenantAdmin only until CLN-25 supplies version/lifecycle evidence |
| Reindex derived index | Maintenance operation | Owner or TenantAdmin only; SQL source remains authoritative |

## Enforced Rules

- The `{projectId}` route is authoritative. A non-zero mismatching body project is rejected; accepted bodies are rebound to the route project.
- Tenant scope comes from the authenticated token context, never the request body.
- `ProjectMembershipMiddleware` verifies tenant/project membership for project routes.
- `UserMutationAttributionMiddleware` records the authenticated actor, tenant, route project, correlation, client, attempt, and outcome for every write.
- Context-observation writes cannot self-assert Binding, StrongGuidance, status, or supersession. The server writes `ObservedFact`, `Active`, and no superseded version.
- Direct decision and rule writes are refused with `GovernedPromotionRequired`; those shapes are Project Canon, not generic CRUD.
- Binding/enforced memory has no generic authenticated write path. A future path must consume governed approval/promotion evidence.
- Reindex and archive require the explicit `project-memory.maintain` capability, currently granted only to Owner and TenantAdmin. This policy is separate from user administration; roles do not turn retrieval or proposals into authority.

## Compatibility Boundary

Summary and implementation-plan writes remain operational context operations, but route/tenant scope is server-bound. The existing client payload shapes remain accepted; caller-supplied scope is not trusted.

## Killjoy Line

Being authenticated lets a user ask. It does not let the user declare Project Canon.
