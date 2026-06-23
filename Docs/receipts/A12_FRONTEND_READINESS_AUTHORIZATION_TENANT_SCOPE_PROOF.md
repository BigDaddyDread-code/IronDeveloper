# A12 - Frontend Readiness Authorization / Tenant Scope Proof

## Purpose

A12 proves frontend-readiness backend read authorization and tenant-scope behavior.

It does not add a new backend read adapter.
It does not add UI.
It does not generate frontend clients.
It does not create approval.
It does not satisfy policy.
It does not grant source apply authority.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.

Read-only data still requires authorization.
Wrong-tenant and missing-tenant reads fail closed and do not fall back to visible data.

## Files Changed

- `IronDev.IntegrationTests/BlockA12FrontendReadinessAuthorizationTenantScopeProofTests.cs`
- `Docs/receipts/A12_FRONTEND_READINESS_AUTHORIZATION_TENANT_SCOPE_PROOF.md`

## Authorization Proof Scope

A12 proves the frontend-readiness controller is covered by `[Authorize]`.

It proves these read endpoints are covered by authorization and do not carry `[AllowAnonymous]`:

- `GET /api/frontend-readiness/operations/{operationId}/status`
- `GET /api/frontend-readiness/operations/{operationId}/timeline`
- `GET /api/frontend-readiness/patch-packages/{packageId}/metadata`
- `GET /api/frontend-readiness/patch-packages/{packageId}/artifacts`
- `GET /api/frontend-readiness/validation-results/{validationResultId}/metadata`
- `GET /api/frontend-readiness/evidence/{evidenceRef}/metadata`
- `GET /api/frontend-readiness/receipts/{receiptRef}/metadata`

The existing `POST /api/frontend-readiness/action-requests` endpoint is explicitly out of A12 scope.

Static boundary scans read the actual frontend-readiness controller, backend read API, and repository-backed frontend-readiness source files. They do not scan an empty string and do not use receipt prose as evidence.

## Tenant-Scope Proof Scope

A12 proves record-level tenant behavior through:

```text
repository -> frontend-readiness source -> BackendFrontendReadinessReadApi -> controller envelope
```

Repository-backed tenant-scope proof covers:

- operation status
- evidence metadata
- receipt metadata
- operation timeline
- patch package metadata
- validation result metadata

Patch package artifacts do not yet have a dedicated repository-backed tenant metadata source. A12 covers artifacts through the current tenant-scoped frontend-readiness source path and records this as an intentionally unwired repository-backed proof.

## Matching Tenant Behavior

Matching tenant reads return data for every covered read surface.

Returned envelopes remain read-only.
Authenticated read access does not create approval, satisfy policy, execute, mutate source, rollback, commit, push, create PRs, mark ready, merge, release, deploy, promote memory, or continue workflow.

## Wrong-Tenant Behavior

Wrong-tenant reads return:

```text
Data: null
ReadState.Kind: NotVisible
```

Wrong-tenant canonical data does not fall back to visible fallback data.

## Missing-Tenant Behavior

Authenticated callers without a tenant/read scope cannot read tenant-scoped records.

Missing-tenant reads return `NotVisible` and do not return hidden tenant data.

## Explicit Global Records

Global visibility is explicit.

Repository-backed records become globally readable only when marked non-tenant-scoped.
Tenant-scoped records with no tenant id fail closed and do not become global.

## Fallback Behavior

Fallback is allowed only for true `NotFound`.

A12 proves these canonical states do not fall back to visible data:

- wrong tenant
- missing tenant
- unavailable
- invalid
- redacted

## No-Leak Behavior

Not-visible envelopes preserve:

- read state
- freshness
- boundary
- warnings
- errors

Not-visible envelopes do not expose hidden:

- repository
- branch
- run id
- patch hash
- evidence refs
- receipt refs
- artifact refs
- summaries
- timestamps

## No-Authority Behavior

Successful authorized reads do not grant:

- approval creation
- approval acceptance
- policy satisfaction
- execution
- source mutation
- rollback
- commit
- push
- PR creation
- ready-for-review
- merge
- release
- deployment
- memory promotion
- workflow continuation

## Intentionally Unwired Items

A12 does not add a new SQL migration, durable auth store, role/permission system, UI, generated frontend client, raw payload reader, validation runner, executor, provider mutation path, or action request creation path.

Full HTTP unauthenticated 401/403 integration coverage was not added in A12. This slice proves authorization metadata on the controller/read endpoints plus tenant/read-scope behavior through backend API/controller envelopes. A future security test-host slice can add live unauthenticated HTTP pipeline proof.

Patch package artifacts have no dedicated repository-backed tenant metadata adapter in the current frontend-readiness stack. A12 covers artifact tenant scope through the current source-level path and records repository-backed artifact tenant proof as unwired.

## Validation

- Focused A12: 87/87 passed
- A11 compatibility: 256/256 passed
- A10 compatibility: 68/68 passed
- A09 compatibility: 75/75 passed
- A08 compatibility: 62/62 passed
- A01-A12 frontend-readiness adapter stack: 797/797 passed
- Frontend readiness lane: 654/654 passed
- Stable governance/status corridor: 1467/1467 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject A12 if:

- any frontend-readiness read endpoint allows anonymous access
- `[AllowAnonymous]` appears on a frontend-readiness read endpoint
- wrong-tenant data is returned
- missing-tenant data is returned
- tenant-hidden data falls back to visible data
- tenant-hidden responses leak hidden record details
- tenantless tenant-scoped records become global
- global visibility is implicit rather than explicit
- authorized read grants approval, policy satisfaction, execution, mutation, source apply, commit, push, PR, ready-for-review, merge, release, deploy, memory promotion, or workflow continuation
- frontend files or generated clients are touched
- mutation endpoints are added for read resources
- executors or provider mutation paths are wired
- raw payload readers are added
- validation refresh/run/retry/repair is added
- broad role/permission or SQL migration work is added

## Review Line

A documented read boundary is useless if the wrong caller can read it.

## Killjoy

Read-only leaks are still leaks.
