# C01 - GitHub Actions Build + Boundary CI

## Summary

GitHub Actions CI reports evidence only.

This PR adds a minimal GitHub Actions workflow and a local PowerShell boundary test runner for governance profile and authority-boundary tests.

## Boundary

CI is not approval.
CI is not merge readiness.
CI is not release readiness.
CI is not deployment readiness.
CI is not policy satisfaction.
CI is not execution permission.

The workflow uses read-only repository permissions.
The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.
No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, generated client, release, or deployment path was added.

## Workflow Scope

- pull_request to main
- `workflow_dispatch`
- `contents: read`
- no write permissions
- Windows-hosted runner
- .NET 10 SDK for the current `net10.0` solution

## CI Lane

The workflow restores packages.
The workflow builds IronDev.slnx.
The workflow runs explicit governance boundary tests.
The workflow does not run a broad Block sweep.
The workflow does not require SQL Server, Docker, external AI providers, or secrets.

Commands:

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `./Scripts/ci/run-governance-boundary-ci.ps1`
- explicit governance boundary test filters
- no broad Block sweep

## Forbidden Mutation Paths

- no git push
- no PR mutation
- no issue mutation
- no label mutation
- no release
- no deployment
- no package publish
- no generated client publish
- no memory writes
- no receipt writes from CI
- no workflow continuation

## Validation

- Focused C01: 8/8 passed.
- Boundary CI script lane, local Windows PowerShell fallback: 213/213 passed.
- B-series profile boundary script lane: 133/133 passed.
- BQ/BR/BS/BT/BU script lane: 80/80 passed.
- B12 compatibility: 13/13 passed.
- B11 compatibility: 8/8 passed.
- B10 compatibility: 8/8 passed.
- B09/B08/B07/B06 compatibility: 53/53 passed.
- B05/B04/B03/B01 compatibility: 51/51 passed.
- BQ/BR/BS/BT/BU compatibility: 80/80 passed.
- Stable governance/status corridor: 1951/1951 passed.
- Restore: passed with existing NU1510 warnings.
- Build: 0 errors / 4 warnings.
- Local shell note: `pwsh` was not installed on this machine, so the script was executed with Windows PowerShell for local validation; GitHub Actions uses `shell: pwsh`.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject this PR if:

- CI output is treated as approval.
- CI output is treated as policy satisfaction.
- CI output is treated as merge readiness.
- CI output is treated as release readiness.
- CI output is treated as deployment readiness.
- The workflow gains write permissions.
- The workflow mutates PRs, issues, labels, releases, deployments, packages, memory, receipts, or workflow state.
- The script runs a broad `Block` test sweep instead of explicit filters.
- The workflow requires SQL Server, Docker, external AI providers, or secrets.

## Killjoy

A green check is evidence, not permission.
