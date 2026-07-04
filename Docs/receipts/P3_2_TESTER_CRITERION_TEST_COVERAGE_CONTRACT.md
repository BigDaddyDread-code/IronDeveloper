# P3-2 Tester Criterion-Test Coverage Contract

## Purpose

P3-2 adds a Core-only tester coverage contract that maps confirmed acceptance criteria to authored test intent.

The Tester writes from the work contract and acceptance criteria. It does not write from the Builder implementation, Builder diff, Builder patch, or Builder reasoning.

## Required Boundary Line

A tester coverage package maps acceptance criteria to test intent. It is not test execution, not test proof, not approval, not critic review, not policy satisfaction, not workflow continuation, not source apply permission, not release readiness, and not deployment readiness.

## Files Changed

- `IronDev.Core/Builder/TesterCriterionCoverageModels.cs`
- `IronDev.Core/Builder/TesterCriterionCoverageValidator.cs`
- `IronDev.IntegrationTests/Builder/TesterCriterionCoverageContractTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/receipts/P3_2_TESTER_CRITERION_TEST_COVERAGE_CONTRACT.md`

## Criterion-To-Test Coverage Rules

`TesterCriterionCoveragePackage` requires:

- package, ticket, project, contract, tester agent, and tester run identity
- at least one acceptance criterion reference
- a coverage matrix that maps criterion IDs to authored test IDs
- explicit uncovered-criterion records for criteria that cannot yet map to a test
- known risks and known gaps as descriptive metadata only

Every criterion must have either:

- a `Covered` coverage row with at least one known test ID
- an explicit uncovered criterion record with a meaningful reason and required human decision

Every authored test must trace to at least one known criterion ID.

Unknown criterion references, unknown test references, and covered/uncovered conflicts are rejected.

## Uncovered-Criterion Rules

An uncovered criterion is allowed only when it is explicit:

- `CriterionId`
- `Reason`
- `RequiredHumanDecision`

Vague gap reasons such as `skip`, `later`, `not needed`, `too hard`, and `none` are rejected.

Naming a gap is not approval to ignore it.

## Tester Independence Boundary

`SkeletonTestAuthoringRequest` remains requirement-side only:

- `TicketId`
- `ProjectId`
- `TicketTitle`
- `AcceptanceCriteria`
- `Problem`

It does not expose Builder proposal, diff, patch, changed-file, content-after, implementation, or Builder-reasoning channels.

`TesterAuthoredTestCase` also records that the test intent was generated from criteria and that the Tester did not see Builder diff, patch, or reasoning.

## Authority Boundary

A tester coverage package cannot claim:

- tests passed
- test proof
- approval
- critic satisfaction
- contract satisfaction
- policy satisfaction
- workflow continuation
- source apply permission
- release readiness
- deployment readiness

Coverage means intended traceability only. It is not execution evidence and not correctness proof.

## Tests Added

- `TesterCoveragePackage_WithCriterionToTestMatrix_PassesValidation`
- `TesterCoveragePackage_CriterionWithoutTestOrGap_IsRejected`
- `TesterCoveragePackage_TestWithoutCriterion_IsRejected`
- `TesterCoveragePackage_UnknownCriterionReference_IsRejected`
- `TesterCoveragePackage_UnknownTestReference_IsRejected`
- `TesterCoveragePackage_CriterionCannotBeBothCoveredAndUncovered`
- `TesterCoveragePackage_ExplicitUncoveredCriterion_IsValidButNamesHumanDecision`
- `TesterCoveragePackage_VagueUncoveredReason_IsRejected`
- `SkeletonTestAuthoringRequest_HasNoBuilderOutputChannel`
- `TesterCoveragePackage_DoesNotExposeAuthorityOrProofSurface`
- `TesterCoveragePackage_BoundarySaysCoverageIsNotProofOrAuthority`
- `TesterCoveragePackage_RejectsAuthorityClaimTextAndBuilderVisibility`
- `TesterCoveragePackage_SourceFilesIntroduceNoExecutionApplyApprovalCriticOrPersistenceSurface`

## Category Inventory

P3-2 adds the focused `TesterCoverage` integration-test category.

The category is visibility metadata only. It does not prove tests passed, approve work, satisfy policy, run critic review, apply source, continue workflow, release, or deploy.

## Out Of Scope

P3-2 does not add:

- test execution
- a test runner
- test result parsing
- Builder patch package behavior
- critic execution
- critic review recording
- approval gate changes
- continuation or source-apply gate changes
- API endpoints
- UI
- database schema
- release or deployment gates
- memory topology
- channel or chat behavior

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~TesterCriterionCoverageContractTests --logger "console;verbosity=minimal"`: passed, 17/17.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "console;verbosity=minimal"`: passed, 7/7.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BuilderPatchPackageContractTests|FullyQualifiedName~OrchestratorAgentBoundaryTests|FullyQualifiedName~OrchestratorContractBoundaryTests" --logger "console;verbosity=minimal"`: passed, 23/23.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact P3-2 files.

Additional local validation and GitHub CI are tracked by the PR body.

## Next PR

P3-4 - Sealed role package: contract + tests + patch + critic + dispositions.

Review line: The sealed package lets roles disagree without letting any role hide the disagreement.

Killjoy line: If the evidence bundle can be edited after review, it was never evidence.
