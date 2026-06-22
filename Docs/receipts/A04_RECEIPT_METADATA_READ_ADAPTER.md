# A04 - Receipt Metadata Read Adapter

## Purpose

A04 adds a dedicated read-only receipt metadata repository adapter for frontend readiness receipt refs.

Review line:

```text
Receipt proves something happened. It does not decide what may happen next.
```

Killjoy:

```text
A receipt is an audit artifact, not a keycard.
```

## Files Changed

- `IronDev.Core/Governance/ReceiptMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/ReceiptMetadataReadRepository.cs`
- `IronDev.Infrastructure/Governance/ReceiptMetadataFrontendReadinessBackendTruthSource.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA04ReceiptMetadataReadAdapterTests.cs`
- `Docs/receipts/A04_RECEIPT_METADATA_READ_ADAPTER.md`

## Repository And Source Wired

- `IReceiptMetadataReadRepository` is the read-only receipt metadata repository contract.
- `ReceiptMetadataReadRepository` is the narrow first adapter over supplied receipt metadata records.
- `ReceiptMetadataFrontendReadinessBackendTruthSource` exposes repository-backed receipt metadata to frontend readiness.
- Production API registration places the receipt metadata source before the run-report source, so canonical receipt metadata wins over run-report fallback metadata.

The repository does not create, repair, refresh, promote, classify, or authenticate receipts.

## Tenant Scope

Tenant-scoped receipt metadata records require record-level tenant ownership.

- Matching tenant may read.
- Wrong tenant returns not found.
- Tenantless tenant-scoped records fail closed.
- Unscoped reads cannot access tenant-scoped records.
- Global metadata is readable only when explicitly marked `IsTenantScoped = false`.

Record ownership is the boundary. Source-level tenant filtering is not enough.

## Unsafe And Private Material Behavior

Receipt metadata is reference-only.

Unsafe stored metadata is returned only as redacted diagnostic metadata with read-only boundary and non-authority warnings.

The adapter redacts when metadata indicates:

- raw payload material
- private material
- hidden material
- patch payload material
- authority claims
- approval claims
- policy satisfaction claims
- workflow continuation claims
- invalid required metadata shape

Returned metadata always forces:

- `ReferenceOnly = true`
- `GrantsAuthority = false`
- `ContinuesWorkflow = false`
- `Boundary = FrontendReadBoundary.ReadOnlyStatus`

## Frontend Readiness Integration

Frontend readiness receipt metadata reads now consult the repository-backed source before run reports.

Returned metadata still passes through the existing `FrontendReadinessReadApi` sanitizer and keeps:

- receipt ref
- receipt kind
- summary
- reference-only marker
- no-authority marker
- no-continuation marker
- warnings
- read-only boundary

Receipt metadata is displayed as metadata only. It is not a raw payload reader and does not expose raw receipt text, patch diffs, private reasoning, tool output, hidden material, or authority text as usable authority.

## Boundary

```text
A04 adds a read-only receipt metadata repository adapter.
It does not read raw receipt payloads.
It does not expose hidden reasoning, raw prompts, raw completions, raw tool output, raw patches, full diffs, secrets, or private material.
It does not create approval.
It does not satisfy policy.
It does not grant authority.
It does not continue workflow.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.
```

Receipt metadata is display context, not authority.

Receipt metadata is not approval.
Receipt metadata is not policy satisfaction.
Receipt metadata is not source apply authority.
Receipt metadata is not rollback authority.
Receipt metadata is not commit authority.
Receipt metadata is not push authority.
Receipt metadata is not PR authority.
Receipt metadata is not merge authority.
Receipt metadata is not release authority.
Receipt metadata is not deployment authority.
Receipt metadata is not memory promotion authority.
Receipt metadata is not workflow continuation.
Receipt metadata cannot approve itself.
Receipt metadata cannot continue itself.

## Intentionally Unwired

- No frontend UI changes.
- No mutation endpoint.
- No receipt payload store.
- No raw receipt payload reader.
- No patch diff exposure.
- No executor or provider path.
- No approval creation.
- No policy satisfaction creation.
- No memory promotion.
- No workflow continuation.
- No release or deployment behavior.
- No new SQL migration or broad receipt projection system.

## Validation

- Focused A04: 35/35 passed
- Focused A03: 28/28 passed
- Focused A02: 21/21 passed
- Focused A01: 20/20 passed
- Existing PR29/PR30/PR31/PR32 frontend readiness lane plus A01/A02/A03/A04: 292/292 passed
- Stable governance/status corridor through A04: 1117/1117 passed
- Wider BG-through-A04 confidence lane: 1252/1252 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed

## Review Traps

Reject this PR if:

- run-report receipt metadata still wins over canonical receipt metadata
- repository reads raw receipt payloads
- repository exposes raw prompt, completion, or tool output
- repository exposes hidden reasoning or private scratchpad material
- repository exposes raw patch or full diff
- repository exposes secrets, private keys, bearer tokens, API keys, or passwords
- unsafe metadata is returned as safe metadata
- missing metadata becomes fake metadata
- receipt metadata becomes approval
- receipt metadata satisfies policy
- receipt metadata enables execution
- receipt metadata continues workflow
- receipt metadata promotes memory
- receipt metadata enables release or deployment
- tenantless records are visible
- wrong-tenant records are visible
- UI files are touched
- mutation endpoints are added
- executors are wired
- workflow continuation is added
- memory promotion is added
- release or deploy behavior is added
