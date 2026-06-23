# C04 - GitHub Actions CLI Boundary CI

## Summary

CLI CI proves the CLI boundary is checked.

CLI CI reports evidence only.

This PR adds an explicit no-SQL CLI-facing boundary lane to the existing governance boundary CI script. The lane runs selected API/CLI contract tests through explicit category filters.

## Boundary

CLI CI is not approval.
CLI CI is not CLI authority.
CLI CI is not merge readiness.
CLI CI is not release readiness.
CLI CI is not deployment readiness.
CLI CI is not policy satisfaction.
CLI CI is not execution permission.
CLI CI is not workflow continuation.

CLI status and output may explain backend state. They must not create backend state.
Backend authority remains the source of truth.

The workflow uses read-only repository permissions.
The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.
No CLI behavior, command, authorization, API behavior, generated client, SQL, executor, approval, policy, source-apply, commit, push, PR, release, deploy, memory, or workflow-continuation path was added.

## Workflow Scope

- pull_request to main
- `workflow_dispatch`
- `contents: read`
- no write permissions
- Windows-hosted runner
- existing governance-boundary-ci workflow entry point

## CLI Scope

The CLI lane runs the integration test project directly.
The CLI lane uses explicit CLI boundary category filters.
The CLI lane does not run a broad CLI sweep.
The CLI lane does not run a broad solution sweep.
The CLI lane does not require SQL Server, Docker, external AI providers, secrets, external HTTP, live hosted API processes, or live mutation commands.
The CLI lane preserves the C-series split: C01/C03/C04 stay no-SQL boundary CI, while C02 remains the SQL-backed integration CI.

## CI Lane

The workflow restores packages.
The workflow builds IronDev.slnx.
The workflow runs explicit governance boundary tests.
The workflow runs explicit API boundary tests.
The workflow runs explicit CLI boundary tests.
The workflow does not run a broad Block sweep.
The workflow does not run a broad API sweep.
The workflow does not run a broad CLI sweep.

Commands:

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `./Scripts/ci/run-governance-boundary-ci.ps1`
- explicit governance boundary test filters
- explicit API boundary test filters
- explicit CLI boundary test filters
- no broad CLI sweep

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
- no CLI behavior change
- no CLI command change
- no command authorization change
- no API behavior change
- no generated client change
- no SQL integration expansion

## Validation

- CLI boundary lane: 41/41 passed.
- Focused C04 static boundary tests: 8/8 passed.
- C01/C02/C03/C04 static CI proof lane: 32/32 passed.
- Local governance-boundary-ci script: 292/292 passed.
- B-series profile boundary script lane: 133/133 passed.
- BQ-BU compatibility boundary script lane: 80/80 passed.
- API boundary script lane: 38/38 passed.
- CLI boundary script lane: 41/41 passed.
- Build: 0 errors / 2 warnings.
- Broad unfiltered integration corridor attempt: not recorded as passing; local command hit the 5-minute tool timeout before returning results. C04 does not widen this branch to repair unrelated corridor behavior.
- `git diff --check`: passed with normal LF/CRLF warning.
- `git diff --cached --check`: passed.

## Review Traps

Reject this PR if:

- CLI CI output is treated as approval.
- CLI CI output is treated as CLI authority.
- CLI CI output is treated as policy satisfaction.
- CLI CI output is treated as merge readiness.
- CLI CI output is treated as release readiness.
- CLI CI output is treated as deployment readiness.
- CLI CI output is treated as execution permission.
- CLI CI output is treated as workflow continuation.
- The workflow gains write permissions.
- The workflow mutates PRs, issues, labels, releases, deployments, packages, memory, receipts, or workflow state.
- The script runs a broad CLI, API, or solution sweep instead of explicit filters.
- The script runs `dotnet test IronDev.slnx`.
- The CLI lane requires SQL Server.
- The CLI lane starts Docker.
- The CLI lane calls external AI or provider systems.
- The CLI lane requires secrets.
- The CLI lane starts a live hosted API process.
- The CLI lane shells into live mutation commands.
- C04 changes CLI behavior, commands, authorization, API behavior, generated clients, SQL CI, executors, CLI runtime, UI, memory, source-apply, commit, push, PR, release, or deployment code.

## Killjoy

A passing CLI test is evidence, not permission to execute, mutate, approve, continue workflow, release, deploy, or bypass the backend gate.
