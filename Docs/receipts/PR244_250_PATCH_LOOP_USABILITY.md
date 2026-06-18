# PR244-250 - Patch Loop Usability Receipt

## Purpose

Block AA improves the manual patch proposal loop so it is easier to review, rerun, clean up, and trust.

This is usability and inspection hardening for the existing manual patch proposal workflow. It is not source apply.

## What changed

- Added allow/forbid file-scope checks for patch proposal runs.
- Added source repository snapshot warnings for dirty source state and source HEAD drift.
- Added built-in and repo-local test profile support.
- Added `patch test` for rerunning the stored run test command.
- Added `patch list` for read-only run inspection.
- Added `patch cleanup` for explicit workspace/run cleanup.
- Added test-output summaries for failed test commands.
- Added patch risk summaries and file-scope result artifacts.
- Added run metadata for workspace/source identity, test status, cleanup status, changed file count, and blocked file count.
- Added Block AA regression tests for the patch-loop usability surface.

## Boundary

This block still does not apply source.

It does not:

- apply a patch
- mutate the source repository
- create a git commit
- push a branch
- create a pull request
- approve anything
- satisfy policy
- continue workflow
- promote memory
- dispatch an agent
- call a model
- add API, SQL, UI, scheduler, worker, or autonomous runtime behavior

The workspace remains disposable. The source repository remains evidence input only.

## Review guarantees

- Forbidden paths block patch packaging before the proposal can be treated as successful.
- Allowed paths clarify review scope but do not grant permission to apply.
- Test profile resolution is explicit and deterministic.
- Failed tests produce a reviewable summary instead of hiding in raw output.
- Source HEAD drift and source dirty state are warnings, not approval or rejection authority.
- Cleanup is explicit; cleanup does not imply the proposal was accepted or applied.
- Rerunning finish refreshes generated artifacts without mutating source.

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "BlockAAPatchLoopUsability" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "IronDevCliTests|BlockZManualPatchProposalProduct|BlockAAPatchLoopUsability" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
```

Results:

- Block AA patch loop usability: 5/5 passed.
- Block Z + CLI + Block AA regression band: 184/184 passed.
- Solution build: passed with 0 errors and 4 warnings.
- `git diff --check`: passed with LF/CRLF warnings only.

## Review line

PR244-250 makes the patch loop easier to drive. It still cannot apply the patch.
