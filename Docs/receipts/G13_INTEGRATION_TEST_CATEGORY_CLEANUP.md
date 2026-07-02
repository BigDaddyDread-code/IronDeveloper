# G13 - Integration Test Category Cleanup

## Purpose

Clean up integration test category metadata so integration lanes are easier to select, audit, and reason about.

Test categories are not test quality.

A label does not make a slow test safe.

If the test count drops, the PR is guilty until proven innocent.

## Files Changed

- `Docs/receipts/G13_INTEGRATION_TEST_CATEGORY_CLEANUP.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `IronDev.IntegrationTests/Governance/IntegrationTestCategoryContractTests.cs`
- Category-only metadata edits in `IronDev.IntegrationTests/**/*.cs`
- Category-only metadata edits in `IronDev.IntegrationTests.Api/**/*.cs`

No production, Core, Infrastructure, API, CLI, SQL, UI, project, package, workflow, provider, tool, fixture, assertion, setup, cleanup, timeout, or CI script files were changed.

## Category Changes

Categories added:

- `StaticBoundary`: added to 36 static-boundary test classes that lacked a boundary-style category.
- `Receipt`: added to 19 receipt test classes that lacked a receipt-style category.
- `Store`: added to 9 store test classes that lacked a store-style category.
- `Governance`, `Contract`, and `Boundary`: added to the new G13 category contract test.

Categories renamed:

- None.

Categories removed:

- None.

No `[Ignore]` attributes were added.

No tests were deleted.

No integration tests were moved to `IronDev.UnitTests`.

## Category Inventory

Full inventory:

- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

Inventory snapshot after G13:

- Source roots scanned: `IronDev.IntegrationTests`, `IronDev.IntegrationTests.Api`
- Source files scanned: 589
- Test classes found: 583
- Test methods found: 9495
- Category names found: 196

The inventory includes every category name found, source-derived class counts, source-derived method counts selected by class category, explicit method-category counts, and file counts.

## CI-Facing Categories

G13 did not remove or rename CI-facing categories.

Protected category filters:

- `TestCategory=ApiCliContract`
- `TestCategory=ApiCliReleaseGate`

Before/after status:

- `ApiCliContract`: unchanged category membership; inventory after G13 shows 3 classes and 24 source-counted methods.
- `ApiCliReleaseGate`: unchanged category membership; inventory after G13 shows 1 class and 10 source-counted methods.
- Combined local execution after G13: 41/41 passed.

No `FullyQualifiedName~...` CI-facing test class was renamed.

`Scripts/ci/run-governance-boundary-ci.ps1` was not changed.

## Category Contract Tests

Added:

- `IronDev.IntegrationTests/Governance/IntegrationTestCategoryContractTests.cs`

The contract test scans integration-test source files only. It does not execute SQL, inspect production state, call providers, or use runtime dependencies.

It verifies:

- category names are non-empty and trimmed
- forbidden hiding/quarantine category fragments are absent
- test classes do not carry class-level `[Ignore]`
- duplicate class-level categories are rejected
- the single pre-existing method-level `[Ignore]` remains explicitly limited to `ManualIndexingTask`
- CI category filters in `run-governance-boundary-ci.ps1` resolve to existing categories
- static-boundary classes keep `Boundary`/`StaticBoundary` metadata
- receipt classes keep `Receipt` metadata
- store/real-database classes keep `Store`/`RealDatabase`/`Smoke`-style metadata
- API/CLI contract tests keep `ApiCliContract` or `ApiCliReleaseGate`
- the inventory document lists every current category
- G13-added categories are broad metadata, not authority or hiding labels

## Validation

Completed locally:

- `dotnet restore IronDev.slnx`: passed with existing restore warnings.
- `dotnet build IronDev.slnx --no-restore -v:minimal -clp:ErrorsOnly`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore -v:minimal -clp:ErrorsOnly`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj --no-restore -v:minimal -clp:ErrorsOnly`: passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests`: 7/7 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "TestCategory=ApiCliContract|TestCategory=ApiCliReleaseGate"`: 41/41 passed.
- `dotnet test IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj --no-build --filter FullyQualifiedName~WorkflowContinuationApiRegressionTests`: 3/3 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory=Store`: listed 108 tests.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory~RealDatabase`: listed 202 tests.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the G13 files.

Attempted but not completed locally:

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter TestCategory=Store`: timed out locally.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter TestCategory~Store`: timed out locally.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter TestCategory~RealDatabase`: timed out locally.

The store/real-database timeout is an execution-evidence gap, not a category-selection gap. The category selectors list tests successfully, but this PR should not claim local store/real-database execution proof.

Pending before final merge posture:

- GitHub CI on the current PR head, if workflows run.

## Proof Boundaries

G13 proves category metadata visibility improved.

G13 does not prove test quality.

G13 does not make slow tests fast.

G13 does not quarantine slow tests.

G13 does not delete flaky tests.

G13 does not change integration behavior.

G13 does not change authority behavior.

G13 does not change API, CLI, SQL, UI, or production code.

G13 does not approve release, merge, deployment, source apply, rollback, workflow continuation, or memory promotion.

## Known Limitations

One pre-existing method-level ignored manual local indexing task remains:

- `IronDev.IntegrationTests/ManualIndexingTask.cs`

G13 does not fix that legacy manual task because doing so would change execution behavior. The new contract test records it explicitly and fails if ignored-test debt grows.

Store and real-database execution lanes timed out locally. This receipt records that gap rather than treating category-selection proof as database execution proof.

## Next Intended Slice

G14 - Slow test quarantine/category split.

Review line: Quarantine is not deletion.

## Killjoy

A slow test in a new bucket is still a slow test.
