# P3-3 Builder Contract-Bound Patch Package

## Purpose

P3-3 adds a Core-only builder patch package contract that binds Builder output to a human-confirmed work contract.

The Builder may produce implementation evidence only. Each proposed change must trace to acceptance criteria, scoped work, or an explicit implementation-support reason.

## Required Boundary Line

A builder patch package is an implementation attempt against a confirmed contract. It is not approval, not test proof, not critic review, not policy satisfaction, not workflow continuation, not source apply permission, not release readiness, or not deployment readiness.

## Contract Binding Rules

`BuilderPatchPackage` requires:

- `PackageId`
- `TicketId`
- `ProjectId`
- `ContractId`
- `ContractHash`
- `ContractTitle`
- at least one package change

The Builder may implement only against a confirmed contract reference, not loose conversation text.

## Traceability Rules

Every `BuilderPatchPackageChange` must include at least one of:

- `CoveredAcceptanceCriterionIds`
- `CoveredScopeItemIds`
- `SupportReasons`

Support reasons must be meaningful. Vague reasons such as cleanup, misc, nice to have, while I was here, probably needed, and general improvement are rejected.

In strict mode, production C# changes using support-only trace must reference a criterion or scope identifier.

## Authority Boundary

The package and changes cannot claim:

- approval
- test proof
- critic satisfaction
- policy satisfaction
- contract satisfaction
- workflow continuation
- source apply permission
- release readiness
- deployment readiness

Validation means package-shape validation only. It is not proof that the work satisfies the contract.

## Files Changed

- `IronDev.Core/Builder/BuilderPatchPackageModels.cs`
- `IronDev.Core/Builder/BuilderPatchPackageValidator.cs`
- `IronDev.IntegrationTests/BuilderPatchPackageContractTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/receipts/P3_3_BUILDER_CONTRACT_BOUND_PATCH_PACKAGE.md`

## Tests Added

- `BuilderPatchPackage_WithContractTrace_PassesValidation`
- `BuilderPatchPackage_ChangeWithoutContractTrace_IsRejected`
- `BuilderPatchPackage_VagueSupportReason_IsRejected`
- `BuilderPatchPackage_ProductionSupportReasonWithoutCriterionReference_IsRejected`
- `BuilderPatchPackage_DoesNotExposeAuthorityOrReadinessSurface`
- `BuilderPatchPackage_PathSafetyValidation_IsPreserved`
- `BuilderPatchPackage_BoundarySaysPatchIsNotAuthority`
- `BuilderPatchPackage_RejectsAuthorityClaimText`
- `BuilderPatchPackage_ProductionFilesIntroduceNoApplyOrCriticExecutionSurface`

## Category Inventory

P3-3 adds the focused `Builder` integration-test category to make the builder contract lane selectable and visible.

`Builder` is category metadata only. It is not proof that a builder package satisfies a contract, and it does not grant approval, critic satisfaction, source apply permission, workflow continuation, release readiness, or deployment readiness.

## Out Of Scope

P3-3 does not add:

- durable Orchestrator loop behavior
- Builder execution loop behavior
- source apply behavior
- approval behavior
- critic behavior
- test authoring behavior
- UI
- API endpoints
- database schema
- release or deployment gates
- memory topology
- channel or chat behavior

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~BuilderPatchPackageContractTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BuilderProposalValidationTests --logger "console;verbosity=minimal"`: passed, 6/6.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "console;verbosity=minimal"`: passed, 7/7.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact P3-3 files.

GitHub CI: tracked by the draft PR checks and PR body; this receipt records local validation for the P3-3 branch.

## Next PR

P3-4 - Sealed role package: contract + tests + patch + critic + dispositions.

Review line: The sealed package lets roles disagree without letting any role hide the disagreement.

Killjoy line: If the evidence bundle can be edited after review, it was never evidence.
