# A07 Validation Result Metadata Read Adapter

## Purpose

A07 adds a read-only validation result metadata repository adapter so frontend readiness can show validation identity, outcome, what ran, what passed, what failed, what was skipped, freshness, evidence refs, and receipt refs without exposing raw validation output or treating validation as approval.

Review line:

> Validation can explain confidence. It cannot approve mutation.

## Files Changed

- `IronDev.Core/Governance/ValidationResultMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/ValidationResultMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/ValidationResultMetadataFrontendReadinessBackendTruthSource.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA07ValidationResultMetadataReadAdapterTests.cs`
- `Docs/receipts/A07_VALIDATION_RESULT_METADATA_READ_ADAPTER.md`

## Repository And Source Wiring

A07 adds `IValidationResultMetadataReadRepository` with a narrow record-backed adapter over canonical validation result metadata. The adapter reads by validation result id and returns sanitized frontend validation result metadata with a read-only boundary.

`ValidationResultMetadataFrontendReadinessBackendTruthSource` reads through that repository. `Program.cs` registers the source before run-report fallback:

```text
OperationStatusFrontendReadinessBackendTruthSource
EvidenceMetadataFrontendReadinessBackendTruthSource
ReceiptMetadataFrontendReadinessBackendTruthSource
OperationTimelineFrontendReadinessBackendTruthSource
PatchPackageMetadataFrontendReadinessBackendTruthSource
ValidationResultMetadataFrontendReadinessBackendTruthSource
RunReportFrontendReadinessBackendTruthSource
BackendFrontendReadinessReadApi
```

## Tenant Scope

Validation result metadata visibility is enforced per record.

- Matching tenant can read tenant-scoped validation result metadata.
- Wrong tenant metadata returns not found.
- Tenantless tenant-scoped metadata fails closed.
- Unscoped reads cannot access tenant-scoped metadata.
- Global metadata must be explicitly marked non-tenant-scoped.

Source visibility is not used as a substitute for record ownership.

## Unsafe Material

Unsafe validation result metadata is not returned as ordinary metadata. Records are redacted when they carry raw validation logs, raw command output, raw test output, raw build output, patch payloads, full diff material, private material, hidden material, secret material, approval claims, policy-satisfaction claims, source-apply authority claims, execution claims, commit/push/PR authority claims, workflow-continuation claims, release/deployment claims, or unsafe authority-shaped text.

Redacted validation result metadata uses:

```text
ValidationResultId: <same validation result id>
Repository: [redacted]
Branch: [redacted]
RunId: [redacted]
PatchHash: [redacted]
Outcome: UnsafeValidationMetadata
WhatRan: []
WhatPassed: []
WhatFailed: []
WhatWasSkipped: [ValidationMetadataUnsafe]
IsStale: true
EvidenceRefs: []
ReceiptRefs: []
```

A07 does not read raw validation logs, raw command output, raw test output, raw build output, raw prompts, raw completions, raw tool output, raw patches, full diffs, secrets, or private material.

## Freshness

Validation freshness stays explicit.

- `FreshnessKnown = false` makes metadata stale.
- Unknown freshness adds `FreshnessUnknown` to skipped validation metadata.
- Expired validation metadata is stale.
- Passed validation can still be stale.
- Missing or default observed timestamps fail closed and redact metadata.

Current time is not used to fabricate missing observation evidence.

## Validation Identity And Refs

Safe metadata preserves validation result id, repository, branch, run id, patch hash, outcome, what ran, what passed, what failed, what was skipped, evidence refs, receipt refs, stale status, and read-only boundary.

Evidence refs remain references only.
Receipt refs remain references only.
Validation outcome remains explanatory metadata only.

## Boundary

A07 adds a read-only validation result metadata repository adapter.

It does not create approval.
It does not satisfy policy.
It does not grant source apply authority.
It does not apply source.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.

Validation passed is not approval.
Validation failed is not rollback authority.
Validation skipped is not workflow continuation authority.
Missing validation metadata fails closed.

## Frontend Readiness

The repository-backed validation result metadata source has priority over run-report fallback metadata. Missing repository metadata does not invent fake validation metadata. The existing frontend readiness sanitizer still runs over repository metadata output and forces `FrontendReadBoundary.ReadOnlyStatus`.

## Validation

Local validation:

- A07 focused: 57/57 passed
- A01-A07 read adapter stack: 249/249 passed
- Frontend lane plus A01-A07: 437/437 passed
- Build: 0 errors / 4 warnings
- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings

Not counted as validation evidence:

- An overbroad local `Block|Frontend` sweep was attempted twice and timed out. It did not produce a pass result and is not reported as stable-lane evidence.

## Review Traps

Reject A07 if:

- run-report validation metadata still wins over canonical validation metadata
- repository reads raw validation logs
- repository exposes raw command/test/build output
- repository exposes raw prompt/completion/tool-output
- repository exposes raw patch/full diff
- repository exposes hidden reasoning/private scratchpad
- repository exposes secrets/private keys/bearer tokens/API keys/passwords
- unsafe validation metadata is returned as safe metadata
- missing validation metadata becomes fake metadata
- missing timestamps are replaced with current time
- unknown freshness is treated as fresh
- stale passed validation is treated as fresh
- expired validation is treated as fresh
- validation metadata becomes approval
- validation metadata satisfies policy
- validation metadata enables source apply
- validation metadata enables commit/push/PR
- validation metadata continues workflow
- validation metadata promotes memory
- validation metadata enables release/deployment
- tenantless records are visible
- wrong-tenant records are visible
- UI files are touched
- mutation endpoints are added
- executors are wired
- workflow continuation is added
- memory promotion is added
- release or deploy behavior is added

## Intentionally Unwired

A07 does not add UI, SQL migrations, raw validation log/output viewing, validation execution, validation repair, validation projection storage, role/visibility modeling, source apply, commit, push, PR, rollback, release, deploy, memory promotion, or workflow continuation.
