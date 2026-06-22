# A01 - Frontend Readiness Backend Truth

## Purpose

A01 wires the frontend readiness read API to backend truth sources instead of the empty snapshot registration.

Review line:

```text
A read-only API that reads nothing real is not backend truth.
```

Killjoy:

```text
The frontend may look through the window. It may not paint the scenery.
```

## Files Changed

- `IronDev.Core/Governance/FrontendReadinessReadModels.cs`
- `IronDev.Infrastructure/Governance/BackendFrontendReadinessReadApi.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA01FrontendReadinessBackendTruthTests.cs`
- `Docs/receipts/A01_FRONTEND_READINESS_BACKEND_TRUTH.md`

## Backend Sources Wired

- Production API startup now registers `BackendFrontendReadinessReadApi` for `IFrontendReadinessReadApi`.
- Production API startup registers `RunReportFrontendReadinessBackendTruthSource` as the first backend truth source.
- The backend adapter composes registered `IFrontendReadinessBackendTruthSource` instances and reuses the existing frontend readiness sanitizer before returning models.
- Backend sources are read through `FrontendReadinessReadScope`.
- Tenant-scoped seeded sources are visible only when their source tenant id matches the current request tenant.
- Run-report records must carry tenant metadata and the record tenant must match the current request tenant.
- If no backend source can prove a record, the read API returns not-found/empty rather than synthetic success.
- Run-report status and validation metadata require a trustworthy observed timestamp from the report file or run events.
- Run-report validation metadata is stale with `FreshnessUnknown` unless freshness evidence is present.

## Endpoints Covered

- `GET /api/frontend-readiness/operations/{operationId}/status`
- `GET /api/frontend-readiness/operations/{operationId}/timeline`
- `GET /api/frontend-readiness/patch-packages/{packageId}/metadata`
- `GET /api/frontend-readiness/patch-packages/{packageId}/artifacts`
- `GET /api/frontend-readiness/validation-results/{validationResultId}/metadata`
- `GET /api/frontend-readiness/evidence/{evidenceRef}/metadata`
- `GET /api/frontend-readiness/receipts/{receiptRef}/metadata`

The existing controlled action request endpoint remains request-only and is not changed by A01.

## Boundary

```text
This PR wires read-only frontend readiness to backend truth.
It does not add frontend UI.
It does not add mutation.
It does not create approval.
It does not satisfy policy.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.
```

All frontend readiness models keep `FrontendReadBoundary.ReadOnlyStatus`.

Evidence metadata is reference-only. Receipt metadata is reference-only. Timeline entries are reference-only. Backend status remains display truth only.

## Authority Flags

A01 does not turn on any authority-bearing frontend readiness flags:

- no approval creation
- no approval acceptance
- no policy satisfaction
- no source mutation
- no rollback
- no commit
- no push
- no PR creation
- no ready-for-review
- no merge
- no release
- no deployment
- no memory promotion
- no workflow continuation

## Intentionally Unwired

- No frontend UI changes.
- No raw patch payload exposure.
- No raw prompt/completion/tool-output exposure.
- No hidden reasoning or scratchpad exposure.
- No executor/provider mutation path.
- Patch package and validation metadata are returned only when a registered backend truth source provides them.

## Validation

- Focused A01: 20/20 passed
- Existing PR29/PR30/PR31/PR32 frontend readiness lane plus A01: 208/208 passed
- Stable governance/status corridor through A01: 1033/1033 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed

## Review Traps

Reject this PR if:

- production still registers `FrontendReadinessReadApi.Empty` as the read API
- missing backend records become fake completed or eligible states
- compact mode hides forbidden actions
- compact mode hides missing evidence
- evidence refs become approval
- receipt refs become authority
- validation metadata becomes policy satisfaction
- timeline output becomes workflow continuation
- tenant-scoped sources can be read cross-tenant
- tenantless run-report records can be read
- validation freshness is inferred from current time instead of recorded evidence
- validation freshness defaults to not-stale without freshness evidence
- raw patch, prompt, completion, tool-output, hidden reasoning, or private material is exposed
- UI files are added
- mutation endpoints or executors are wired
