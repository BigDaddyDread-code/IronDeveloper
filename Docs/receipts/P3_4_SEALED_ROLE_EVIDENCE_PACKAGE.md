# P3-4 Sealed Role Evidence Package

## Purpose

P3-4 adds a Core-only sealed role evidence package contract.

The package binds independently produced role artifacts into a structurally complete, tamper-evident review bundle:

- Orchestrator contract
- Tester criterion coverage package
- Builder contract-bound patch package
- critic review references
- human finding disposition references

The sealed package preserves the evidence chain. It does not prove the work is correct.

## Required Boundary Line

A sealed role evidence package binds contract, tester coverage, builder patch, critic review, and finding dispositions into a tamper-evident review bundle. It is not approval, not test proof, not critic authority, not policy satisfaction, not workflow continuation, not source apply permission, not release readiness, and not deployment readiness.

## Files Changed

- `IronDev.Core/Orchestration/SealedRoleEvidencePackageModels.cs`
- `IronDev.Core/Orchestration/SealedRoleEvidencePackageValidator.cs`
- `IronDev.Core/Orchestration/SealedRoleEvidencePackageHasher.cs`
- `IronDev.IntegrationTests/Orchestration/SealedRoleEvidencePackageContractTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/receipts/P3_4_SEALED_ROLE_EVIDENCE_PACKAGE.md`

## Role Artifact Binding Rules

The package is invalid unless all three pre-critic role artifacts are present:

- `OrchestratorContract`, produced by `Orchestrator`
- `TesterCoveragePackage`, produced by `Tester`
- `BuilderPatchPackage`, produced by `Builder`

Each role artifact requires:

- `ArtifactId`
- `ArtifactKind`
- `ProducedByRole`
- `ProducedByAgentId`
- `Sha256`
- `EvidenceRef`

Kind or role mismatches are rejected.

## Pre-Critic Hash Rule

The pre-critic evidence hash covers the Orchestrator, Tester, and Builder artifact references before critic or disposition evidence is added.

The critic reviews this pre-critic evidence package. The critic review does not hash itself into the evidence it reviews.

## Critic Review Rule

At least one critic review is required.

Every critic review must reference the package's `PreCriticEvidenceHash` through `ReviewedPackageHash`.

A critic review is witness evidence. It is not approval, policy satisfaction, workflow continuation, source apply permission, release readiness, or deployment readiness.

## Finding Disposition Rule

Every critic finding ID must have a matching finding disposition.

Every disposition must include a reason and user decision reference.

Unknown dispositions are rejected so invented dispositions cannot pad the package.

Blocking findings and ground-truth mismatches remain visible. A disposition records a human response; it does not erase disagreement and does not continue workflow.

## Final Seal Rule

The final seal hash covers package identity, contract identity, role artifact hashes, the pre-critic evidence hash, critic review refs/hashes/finding IDs, finding disposition refs/hashes/reasons, and known risks/gaps.

`CreatedUtc` is intentionally excluded from the deterministic seal hash so tests do not become time-sensitive.

Changing any role artifact hash, critic review hash, or disposition hash changes the final seal hash.

## Authority Boundary

The sealed role package cannot claim:

- approval
- test proof
- critic authority
- policy satisfaction
- workflow continuation
- source apply permission
- release readiness
- deployment readiness

The seal means bundled and tamper-evident. It does not mean accepted.

## Tests Added

- `SealedRolePackage_WithContractTestsPatchCriticAndDispositions_PassesValidation`
- `SealedRolePackage_MissingRequiredRoleArtifact_IsRejected`
- `SealedRolePackage_RoleArtifactWithoutHash_IsRejected`
- `SealedRolePackage_RoleArtifactKindOrRoleMismatch_IsRejected`
- `SealedRolePackage_WithoutCriticReview_IsRejected`
- `SealedRolePackage_CriticReviewHashMismatch_IsRejected`
- `SealedRolePackage_CriticFindingWithoutDisposition_IsRejected`
- `SealedRolePackage_DispositionForUnknownFinding_IsRejected`
- `SealedRolePackage_BlockingFindingWithDisposition_RemainsValid`
- `SealedRolePackage_GroundTruthMismatchWithDisposition_RemainsVisibleAndValid`
- `SealedRolePackage_FinalHashChangesWhenArtifactHashChanges`
- `SealedRolePackage_DoesNotExposeApprovalReadinessOrPermissionSurface`
- `SealedRolePackage_BoundarySaysSealIsNotAuthority`
- `SealedRolePackage_SourceFilesIntroduceNoRuntimeAuthoritySurface`

## Out Of Scope

P3-4 does not add:

- runtime package emission
- skeleton run integration
- test execution
- test result parsing
- critic execution
- critic review recording
- finding disposition recording
- approval logic
- continuation or source-apply gates
- source mutation
- API
- UI
- SQL
- release or deploy behavior
- memory topology
- channels or chat
- batch behavior

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~SealedRoleEvidencePackageContractTests --logger "console;verbosity=minimal"`: passed, 14/14.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "console;verbosity=minimal"`: passed, 7/7.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~OrchestratorAgentBoundaryTests|FullyQualifiedName~OrchestratorContractBoundaryTests|FullyQualifiedName~TesterCriterionCoverageContractTests|FullyQualifiedName~BuilderPatchPackageContractTests" --logger "console;verbosity=minimal"`: passed, 40/40.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact P3-4 files.

GitHub CI is tracked by the draft PR checks and PR body.

## Next PR

P3-5 - emit sealed role package from skeleton evidence path.

Review line: Emitting the sealed package records the evidence chain. It does not approve the chain.

Killjoy line: A sealed bundle is a receipt, not a permission slip.
