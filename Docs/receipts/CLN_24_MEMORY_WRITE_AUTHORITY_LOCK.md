# CLN-24 Memory Write Authority Lock Receipt

**Status:** Historical receipt

**Date:** 14 July 2026

## Delivered

- Split hosted memory mutations into observation, proposal, governed-promotion refusal, lifecycle maintenance, and reindex maintenance.
- Bound accepted write models to route project and token tenant scope.
- Forced context observations to non-binding `ObservedFact` / `Active` state without supersession.
- Disabled direct decision/rule Project Canon writes.
- Restricted archive and reindex through the explicit `project-memory.maintain` capability, currently granted to Owner and TenantAdmin.
- Returned the canonical `GovernedRefusalEnvelope` for promotion, scope, authentication, and maintenance refusals.
- Added SQL-backed API behavior proving Member refusal, Owner/TenantAdmin acceptance, cross-project rejection, canonical refusal payloads, and durable actor attribution.
- Regenerated deterministic OpenAPI and TypeScript client contracts for the changed response surfaces.

## Boundary

This slice locks existing authority holes. It does not implement Project Canon promotion, versioning, or an automatic memory injection path.
