# CLN-15 Authentication And Tenant Token Contract Receipt

**Recorded:** 13 July 2026

## Delivered

- Blocked base tokens from all product data while retaining identity, environment, tenant-list, tenant-selection, and logout entry operations.
- Required exactly one positive selected-tenant claim for product access.
- Bound tenant-scoped routes to the selected tenant with a generic not-found refusal.
- Bound project creation to the selected tenant instead of request-body scope.
- Ordered authenticated write attribution before tenant and project refusals.
- Added middleware and SQL-backed API contract tests for base, selected, and cross-tenant access.

## Boundary

Tenant selection establishes scope only. It grants no project visibility or governed-action authority.
