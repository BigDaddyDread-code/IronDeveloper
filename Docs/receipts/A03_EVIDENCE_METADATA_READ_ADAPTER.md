# A03 - Evidence Metadata Read Adapter

## Purpose

A03 adds a dedicated read-only evidence metadata repository adapter for frontend readiness evidence refs.

Review line:

```text
Evidence metadata may explain evidence. It must not become evidence payload, approval, policy satisfaction, execution authority, or workflow continuation.
```

Killjoy:

```text
Metadata can point at the receipt. It cannot become the receipt, the approval, or the button.
```

## Files Changed

- `IronDev.Core/Governance/EvidenceMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/EvidenceMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/EvidenceMetadataFrontendReadinessBackendTruthSource.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA03EvidenceMetadataReadAdapterTests.cs`
- `Docs/receipts/A03_EVIDENCE_METADATA_READ_ADAPTER.md`

## Source And Repository Wired

- `IEvidenceMetadataReadRepository` is the read-only evidence metadata repository contract.
- `EvidenceMetadataReadRepository` is the narrow first adapter over supplied evidence metadata records.
- `EvidenceMetadataFrontendReadinessBackendTruthSource` exposes repository-backed evidence metadata to frontend readiness.
- Production API registration places the evidence metadata source before the run-report source, so canonical evidence metadata wins over run-report fallback metadata.

The repository does not create, repair, refresh, resolve, or authenticate evidence records.

## Tenant Scope

Tenant-scoped evidence metadata records require record-level tenant ownership.

- Matching tenant may read.
- Wrong tenant returns not found.
- Tenantless tenant-scoped records fail closed.
- Unscoped reads cannot access tenant-scoped records.
- Global metadata is readable only when explicitly marked `IsTenantScoped = false`.

Record ownership is the boundary. Source-level tenant filtering is not enough.

## Redaction Behavior

Evidence metadata is reference-only.

Unsafe stored metadata is returned only as redacted diagnostic metadata with read-only boundary and non-authority warnings.

The adapter redacts when metadata indicates:

- raw payload material
- private material
- hidden material
- patch payload material
- authority-claim text
- invalid required metadata shape

Returned metadata always forces:

- `ReferenceOnly = true`
- `ContainsRawPayload = false`
- `Boundary = FrontendReadBoundary.ReadOnlyStatus`

## Frontend Readiness Integration

Frontend readiness evidence metadata reads now consult the repository-backed source before run reports.

Returned metadata still passes through the existing `FrontendReadinessReadApi` sanitizer and keeps:

- evidence ref
- evidence kind
- summary
- reference-only marker
- raw-payload false marker
- warnings
- read-only boundary

Evidence metadata is displayed as metadata only. It is not a raw payload reader and does not expose patch diffs, private reasoning, tool output, hidden material, or authority text as usable authority.

## Boundary

```text
A03 adds a read-only evidence metadata repository adapter.
It does not create evidence.
It does not read raw evidence payloads.
It does not expose private material.
It does not mutate source.
It does not create approval.
It does not satisfy policy.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.
```

Evidence metadata is display context, not authority.

Evidence refs are not approval.
Evidence refs are not policy satisfaction.
Evidence refs are not execution authority.
Evidence refs do not continue workflow.
Metadata summaries are not receipts.
Metadata warnings are not policy.
UI cannot approve through metadata.
Memory cannot approve through metadata.

## Intentionally Unwired

- No frontend UI changes.
- No mutation endpoint.
- No evidence payload store.
- No raw evidence payload reader.
- No patch diff exposure.
- No executor or provider path.
- No approval creation.
- No policy satisfaction creation.
- No memory promotion.
- No workflow continuation.
- No release or deployment behavior.
- No new SQL migration or broad evidence projection system.

## Validation

- Focused A03: 28/28 passed
- Focused A02: 21/21 passed
- Focused A01: 20/20 passed
- Existing PR29/PR30/PR31/PR32 frontend readiness lane plus A01/A02/A03: 257/257 passed
- Stable governance/status corridor through A03: 1082/1082 passed
- Wider BG-through-A03 confidence lane: 1217/1217 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed

## Review Traps

Reject this PR if:

- run report evidence metadata still wins over canonical evidence metadata
- evidence metadata is created, repaired, refreshed, or resolved by the read repository
- raw payloads are exposed
- patch diffs are exposed
- private or hidden material is exposed
- unsafe metadata is treated as usable
- tenantless records are visible
- wrong-tenant records are visible
- global metadata is visible without explicit global marking
- evidence refs become approval
- evidence refs become policy satisfaction
- evidence refs become execution authority
- evidence refs become workflow continuation
- UI files are touched
- mutation endpoints are added
- executors are wired
- workflow continuation is added
- memory promotion is added
- release or deploy behavior is added
