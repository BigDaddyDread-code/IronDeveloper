# A06 Patch Package Metadata Read Adapter

## Purpose

A06 adds a read-only patch package metadata repository adapter so frontend readiness can show package identity, hash, changed-file refs, artifact refs, evidence refs, receipt refs, and review/risk refs without exposing raw patch payloads or treating a patch package as source-apply authority.

Review line:

> A patch package is review material. It is not source apply authority.

## Files Changed

- `IronDev.Core/Governance/PatchPackageMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/PatchPackageMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/PatchPackageMetadataFrontendReadinessBackendTruthSource.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA06PatchPackageMetadataReadAdapterTests.cs`
- `Docs/receipts/A06_PATCH_PACKAGE_METADATA_READ_ADAPTER.md`

## Repository And Source Wiring

A06 adds `IPatchPackageMetadataReadRepository` with a narrow record-backed adapter over canonical patch package metadata. The adapter reads by package id and returns sanitized frontend patch package metadata with a read-only boundary.

`PatchPackageMetadataFrontendReadinessBackendTruthSource` reads through that repository. `Program.cs` registers the source before run-report fallback:

```text
OperationStatusFrontendReadinessBackendTruthSource
EvidenceMetadataFrontendReadinessBackendTruthSource
ReceiptMetadataFrontendReadinessBackendTruthSource
OperationTimelineFrontendReadinessBackendTruthSource
PatchPackageMetadataFrontendReadinessBackendTruthSource
RunReportFrontendReadinessBackendTruthSource
BackendFrontendReadinessReadApi
```

## Tenant Scope

Patch package metadata visibility is enforced per record.

- Matching tenant can read tenant-scoped patch package metadata.
- Wrong tenant metadata returns not found.
- Tenantless tenant-scoped metadata fails closed.
- Unscoped reads cannot access tenant-scoped metadata.
- Global metadata must be explicitly marked non-tenant-scoped.

Source visibility is not used as a substitute for record ownership.

## Unsafe Material

Unsafe patch package metadata is not returned as ordinary metadata. Records are redacted when they carry raw patch payload, full diff material, private material, hidden material, secret material, source-apply authority claims, approval claims, policy-satisfaction claims, execution claims, commit/push/PR authority claims, workflow-continuation claims, release/deployment claims, or unsafe authority-shaped text.

Redacted package metadata uses:

```text
PackageId: <same package id>
Repository: [redacted]
Branch: [redacted]
RunId: [redacted]
PatchHash: [redacted]
ProposedFilePaths: []
ArtifactRefs: []
EvidenceRefs: []
ReceiptRefs: []
ReviewSummaryRef: [redacted]
KnownRisksRef: [redacted]
```

A06 does not read raw patch payloads. It does not expose hidden reasoning, raw prompts, raw completions, raw tool output, raw patches, full diffs, secrets, or private material.

## Package Identity And Refs

Safe metadata preserves package id, repository, branch, run id, patch hash, proposed file paths, artifact refs, evidence refs, receipt refs, review summary ref, known risks ref, and read-only boundary.

Artifact refs remain references only.
Evidence refs remain references only.
Receipt refs remain references only.
Review summary remains a ref only.
Known risks remain a ref only.

## Boundary

A06 adds a read-only patch package metadata repository adapter.

It does not create approval.
It does not satisfy policy.
It does not grant source apply authority.
It does not apply source.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.

Patch package metadata cannot approve itself.
Patch package metadata cannot apply itself.
Missing patch package metadata fails closed.

## Frontend Readiness

The repository-backed patch package metadata source has priority over run-report fallback metadata. Missing repository metadata does not invent fake package metadata. The existing frontend readiness sanitizer still runs over repository metadata output and forces `FrontendReadBoundary.ReadOnlyStatus`.

## Validation

Local validation:

- A06 focused: 48/48 passed
- A05 focused: 40/40 passed
- A04 focused: 35/35 passed
- A03 focused: 28/28 passed
- A02 focused: 21/21 passed
- A01 focused: 20/20 passed
- A01-A06 read adapter stack: 192/192 passed
- Frontend readiness lane plus A01-A06: 380/380 passed
- Stable governance/status corridor through A06: 1205/1205 passed
- Wider confidence lane through A06: 1340/1340 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject A06 if:

- run-report patch package metadata still wins over canonical package metadata
- repository reads raw patch payloads
- repository exposes full diffs
- repository exposes raw prompt/completion/tool-output
- repository exposes hidden reasoning/private scratchpad
- repository exposes secrets/private keys/bearer tokens/API keys/passwords
- unsafe package metadata is returned as safe metadata
- missing package metadata becomes fake metadata
- patch package metadata becomes approval
- patch package metadata satisfies policy
- patch package metadata enables source apply
- patch package metadata enables commit/push/PR
- patch package metadata continues workflow
- patch package metadata promotes memory
- patch package metadata enables release/deployment
- tenantless records are visible
- wrong-tenant records are visible
- timestamps are replaced with current time
- UI files are touched
- mutation endpoints are added
- executors are wired
- workflow continuation is added
- memory promotion is added
- release or deploy behavior is added

## Intentionally Unwired

A06 does not add UI, SQL migrations, raw patch/diff viewing, patch package creation, patch package repair, validation result metadata, role/visibility modeling, source apply, commit, push, PR, rollback, release, deploy, memory promotion, or workflow continuation.
