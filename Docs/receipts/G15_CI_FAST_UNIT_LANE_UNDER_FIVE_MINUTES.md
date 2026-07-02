# G15 CI Fast Unit Lane Under Five Minutes Receipt

## Purpose

G15 makes the fast unit CI lane project-scoped, timed, and evidence-producing while preserving its actual purpose: fast feedback for pure unit tests.

A fast lane is not a complete lane.

Five minutes of green does not buy five minutes of trust.

## Files changed

- `.github/workflows/fast-unit-ci.yml`
- `Scripts/ci/run-fast-unit-ci.ps1`
- `Docs/receipts/G15_CI_FAST_UNIT_LANE_UNDER_FIVE_MINUTES.md`

No production, Core behavior, Infrastructure behavior, API behavior, CLI behavior, SQL behavior, UI behavior, test body, test assertion, test project reference, integration workflow, SQL workflow, or frontend workflow file is changed by G15.

## Old fast lane shape

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`

That mixed full-solution restore/build with fast unit test execution.

## New fast lane shape

- `pwsh ./Scripts/ci/run-fast-unit-ci.ps1`

The script runs:

- `dotnet restore IronDev.UnitTests/IronDev.UnitTests.csproj --verbosity minimal`
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore --verbosity minimal`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build --logger "console;verbosity=minimal" --logger "trx;LogFileName=fast-unit.trx" --results-directory artifacts/ci/fast-unit/test-results`

The workflow then scans the bounded evidence artifacts and uploads `artifacts/ci/fast-unit` with 14-day retention.

## Full solution restore/build decision

Full solution restore/build was removed from `fast-unit-ci`.

The lane is now intentionally scoped to `IronDev.UnitTests/IronDev.UnitTests.csproj`. Full solution build proof remains outside this fast lane.

## Why this is not coverage reduction

The lane still runs the full `IronDev.UnitTests` project.

No test filters are added.

No tests are deleted.

No tests are marked ignored.

No test assertions or test bodies are changed.

No project references are changed.

No slower CI lane is weakened.

## Workflow guardrails

- `pull_request` targets remain `main` and `governance/block-f-rollup-to-main`.
- `workflow_dispatch` remains enabled.
- `permissions: contents: read` remains enforced.
- Superseded runs are still cancelled by ref.
- Job timeout is `timeout-minutes: 10`.
- Test results are written as TRX.
- The script fails if no TRX file is produced.
- The script fails if zero tests are discovered.
- Artifact upload uses `if-no-files-found: error`.
- Evidence artifact safety scanning runs before upload.

## Local commands run

- `dotnet restore IronDev.UnitTests/IronDev.UnitTests.csproj`: passed.
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`: passed.
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 312/312 passed.
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build --list-tests`: listed 312 tests.
- `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/ci/run-fast-unit-ci.ps1`: passed; local `pwsh` was not on PATH.
- `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/ci/test-ci-evidence-artifact-safety.ps1 -ArtifactDirectory artifacts/ci/fast-unit`: passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

Recommended safety validation remains separate and is not replaced by G15.

## Recommended safety validation

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with 0 errors and existing NU1510 warnings.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.

## Local unit test count

Local fast-unit test count: 312.

Local script timing from `artifacts/ci/fast-unit/fast-unit-summary.md`:

- restore duration: `00:00:01.361`
- build duration: `00:00:28.093`
- test duration: `00:00:04.187`
- total duration: `00:00:33.793`

## GitHub fast-unit-ci evidence

Pending current-head GitHub run after this branch is pushed.

The PR body and GitHub checks must record:

- GitHub fast-unit-ci run ID
- GitHub fast-unit-ci job ID
- GitHub head SHA
- restore duration
- build duration
- test duration
- total duration
- test count

CI hold remains until that current-head evidence exists.

## Timing evidence fields

The fast lane emits `artifacts/ci/fast-unit/fast-unit-summary.md` with:

- restore duration
- build duration
- test duration
- total duration
- test count
- run ID
- run attempt
- commit SHA

## Count discipline

No test count drop is accepted without a replacement lane.

Before G15 fast-unit test count: 312.

After G15 fast-unit test count: 312.

Difference: 0.

Explanation for any difference: no difference; G15 does not change tests.

## What fast-unit-ci proves

fast-unit-ci proves only that the fast unit project restored, built, and passed its unit tests on this head.

## What fast-unit-ci does not prove

fast-unit-ci does not prove integration behavior, SQL behavior, API behavior, CLI behavior, frontend behavior, release readiness, merge readiness, deployment readiness, source apply safety, rollback safety, workflow continuation safety, or memory promotion safety.

## Separate truth sources preserved

G15 does not replace:

- `governance-boundary-ci`
- `sql-integration-ci`
- `frontend-contract-ci`
- manual local / slow / real-database lanes documented by G14

G15 does not edit those workflows.

## Execution gaps

- GitHub fast-unit-ci current-head timing proof is pending until the PR branch runs in GitHub Actions.
- G15 does not run integration tests.
- G15 does not run SQL-backed tests.
- G15 does not run frontend tests.
- G15 does not run slow, real-database, or manual-local tests.

## Known limitations

- G15 optimizes fast unit feedback only.
- G15 does not prove integration behavior.
- G15 does not prove SQL behavior.
- G15 does not prove frontend behavior.
- G15 does not run slow/real-database/manual-local tests.
- G15 does not replace G14 visibility.
- G15 does not replace governance-boundary-ci.
- G15 does not replace sql-integration-ci.
- G15 does not make the system release-ready.

## Next intended slice

G16 - CI full lane with SQL.

Review line: Full CI is not release approval.

Killjoy: SQL green means the database lane ran, not that the product is safe.

## Killjoy

A fast lane is not a complete lane.
