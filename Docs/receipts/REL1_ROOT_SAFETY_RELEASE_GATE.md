# REL-1 - Root Safety Release Gate

## Purpose

REL-1 adds a release-facing root-safety gate over the existing J10 local root
safety validator. The gate classifies all release-required roots with stable
statuses and typed blocker reason codes before a release path may treat local
outputs as evidence.

Root safety is a release precondition. It is not evidence, approval, policy satisfaction, source safety, execution authority, release readiness, and not permission to mutate.

## Roots Classified

- `LogsRoot`
- `EvidenceRoot`
- `WorkspaceRoot`
- `DisposableWorkspaceRoot`
- `SandboxRepositoryPath`
- `CanaryMeasurementRoot`
- `BatchMapEvidenceRoot`
- `SmokeArtifactRoot`

## Status Vocabulary

- `Passed`
- `Blocked`
- `NotConfigured`
- `NotEvaluated`

`NotEvaluated` is not a pass. Mutation-shaped release/smoke paths must stop or
evaluate root safety before writing artifacts. Check-only paths may report
`NotEvaluated` only when they do not write artifacts or mutate local state.

## Blocker Reason Codes

The release gate maps existing validator findings to release-facing reason
codes including:

- `RootNotConfigured`
- `RootNotAbsolute`
- `RootDoesNotResolve`
- `RootIsRepositoryRoot`
- `RootIsRepositoryChild`
- `RootIsFilesystemRoot`
- `RootIsDriveRoot`
- `RootIsUserHome`
- `RootIsRawTempRoot`
- `RootEscapesAllowedBase`
- `RootContainsSymlink`
- `RootContainsReparsePoint`
- `RootParentChildCollision`
- `RootEqualsSourceRepo`
- `SandboxEqualsSourceRepo`
- `SandboxUnderSourceRepo`
- `SourceRepoUnderSandbox`
- `EvidenceUnderDisposableWorkspace`
- `LogsUnderDisposableWorkspace`
- `WorkspaceUnderEvidenceRoot`
- `UnsafeRootPolicyMissing`

## Files Changed

- `IronDev.Core/Configuration/LocalRootSafetyModels.cs`
- `IronDev.Core/Configuration/LocalRootSafetyValidator.cs`
- `IronDev.Core/Configuration/RedactedConfigSummaryModels.cs`
- `IronDev.Core/Configuration/ReleaseRootSafetyGateModels.cs`
- `IronDev.Core/Configuration/ReleaseRootSafetyGate.cs`
- `IronDev.IntegrationTests/BlockREL1RootSafetyReleaseGateTests.cs`
- `IronDev.IntegrationTests/BlockJ10RootSafetyChecksTests.cs`
- `IronDev.IntegrationTests/BlockJ04LocalBootstrapScriptTests.cs`
- `IronDev.IntegrationTests/BlockJ05LocalSqlBootstrapCommandTests.cs`
- `IronDev.IntegrationTests/BlockJ06LocalWeaviateBootstrapCommandTests.cs`
- `IronDev.IntegrationTests/BlockJ07DeveloperEnvironmentDoctorTests.cs`
- `IronDev.IntegrationTests/Smoke/AlphaSmokeScriptContractTests.cs`
- `Scripts/local/doctor-local.ps1`
- `Scripts/smoke/alpha-smoke.ps1`
- `Docs/alpha-smoke/reason-codes.md`
- `Docs/local-development.md`
- `Docs/release/v0.1-local-alpha/READINESS_INVENTORY.md`
- `Docs/receipts/REL1_ROOT_SAFETY_RELEASE_GATE.md`

## What This Proves

- The release root vocabulary includes the smoke artifact root.
- Missing required roots block with `RootNotConfigured`.
- Explicitly unevaluated root safety reports `NotEvaluated` with
  `UnsafeRootPolicyMissing`.
- Source-repository, repository-child, user-home, temp-root, drive-root,
  traversal, non-resolving, sandbox/source, and workspace/evidence/log
  collisions map to release-facing reason codes.
- Returned display paths are redacted and do not expose raw user-local paths or
  full source-repository paths.
- Alpha smoke distinguishes check-only `RootSafetyNotEvaluated` from
  mutation-shaped `RootSafetyBlocked`.
- The developer doctor reports release-gate availability without invoking it or
  claiming readiness.

## What This Does Not Do

- Does not create directories.
- Does not delete or clean roots.
- Does not write evidence, logs, smoke artifacts, or reports.
- Does not run git, providers, SQL, Weaviate, API, CLI, UI, or workflow
  commands.
- Does not grant approval, policy satisfaction, source apply authority,
  rollback authority, release readiness, deployment readiness, workflow
  continuation, or permission to mutate.
- Does not yet wire one universal root-safety preflight through every setup,
  evidence, workspace, and apply path.

## Validation

- Focused REL-1 root safety release gate tests: 15/15 passed
- J10/J07/J08 local config boundary ring: 45/45 passed
- Alpha smoke script contract tests: 8/8 passed after committing the slice so the source repo was clean
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 existing warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed before the first commit; rerun after receipt update before amend

## Review Line

Root safety blocks bad local output roots. It does not make the output true.

## Killjoy

A clean root is a floor, not a launch key.
