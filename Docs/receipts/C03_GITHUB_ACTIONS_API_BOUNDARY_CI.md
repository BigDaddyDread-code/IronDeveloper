# C03 - GitHub Actions API Boundary CI

## Summary

API CI proves the API boundary is checked.

This PR adds an explicit API-facing boundary lane to the existing governance boundary CI script. The lane runs selected no-SQL API-project contract tests through explicit class filters.

## Boundary

API CI is not approval.
API CI is not API authority.
API CI is not merge readiness.
API CI is not release readiness.
API CI is not deployment readiness.
API CI is not policy satisfaction.
API CI is not execution permission.

The workflow uses read-only repository permissions.
The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.
No API behavior, endpoint, auth, client generation, SQL, executor, approval, policy, source-apply, commit, push, PR, release, deploy, memory, or workflow-continuation path was added.

## Workflow Scope

- pull_request to main
- `workflow_dispatch`
- `contents: read`
- no write permissions
- Windows-hosted runner
- existing governance-boundary-ci workflow entry point

## API Scope

The API lane runs the API test project directly.
The API lane uses explicit API boundary class filters.
The API lane does not run a broad API project sweep.
The API lane does not require SQL Server, Docker, external AI providers, secrets, external HTTP, or a live hosted API process.
The API lane preserves the C01/C02 split: C01/C03 stay no-SQL boundary CI, while C02 remains the SQL-backed integration CI.

## CI Lane

The workflow restores packages.
The workflow builds IronDev.slnx.
The workflow runs explicit governance boundary tests.
The workflow runs explicit API boundary tests.
The workflow does not run a broad Block sweep.
The workflow does not run a broad API sweep.

Commands:

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `./Scripts/ci/run-governance-boundary-ci.ps1`
- explicit governance boundary test filters
- explicit API boundary test filters
- no broad API project sweep

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
- no API behavior change
- no endpoint change
- no auth change
- no generated client change
- no SQL integration expansion

## Validation

- API boundary lane: 38/38 passed.
- Focused C03 static boundary tests: 7/7 passed.
- C01/C02 compatibility: 17/17 passed.
- Combined C01/C02/C03 static CI proof lane: 24/24 passed.
- Local governance-boundary-ci script: 251/251 passed.
- B-series profile boundary script lane: 133/133 passed.
- BQ-BU compatibility boundary script lane: 80/80 passed.
- API boundary script lane: 38/38 passed.
- Build: 0 errors / 2 warnings.
- Governance/status corridor attempt: 1965/1967 passed; 2 A08 frontend-readiness tests failed because their fixture expires at 2026-06-23T07:00:00Z. C03 does not touch A08 and does not widen this branch to repair that unrelated stale fixture.
- `git diff --check`: passed with normal LF/CRLF warning.
- `git diff --cached --check`: passed.

## Review Traps

Reject this PR if:

- API CI output is treated as approval.
- API CI output is treated as API authority.
- API CI output is treated as policy satisfaction.
- API CI output is treated as merge readiness.
- API CI output is treated as release readiness.
- API CI output is treated as deployment readiness.
- The workflow gains write permissions.
- The workflow mutates PRs, issues, labels, releases, deployments, packages, memory, receipts, or workflow state.
- The script runs a broad API project sweep instead of explicit filters.
- The script runs `dotnet test IronDev.slnx`.
- The API lane requires SQL Server.
- The API lane starts Docker.
- The API lane calls external AI or provider systems.
- The API lane requires secrets.
- The API lane starts a live hosted API process.
- C03 changes API behavior, endpoints, auth, generated clients, SQL CI, executors, CLI, UI, memory, source-apply, commit, push, PR, release, or deployment code.

## Killjoy

A passing API test is evidence, not permission to call, mutate, approve, or bypass the backend gate.
