# C02 - GitHub Actions SQL Integration Gate

## Summary

SQL integration CI reports evidence only.

This PR adds a separate SQL-backed GitHub Actions workflow for an explicit governance store integration lane. C01 remains the no-SQL build and governance boundary lane.

## Boundary

SQL CI is not approval.
SQL CI is not merge readiness.
SQL CI is not release readiness.
SQL CI is not deployment readiness.
SQL CI is not policy satisfaction.
SQL CI is not execution permission.

The workflow uses read-only repository permissions.
The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.
No executor, mutation, approval, policy, UI, API, CLI, durable store, generated client, release, or deployment path was added.

## Workflow Scope

- pull_request to main
- workflow_dispatch
- contents: read
- no write permissions
- ubuntu-latest runner
- SQL Server 2022 service container

## SQL Scope

The SQL Server database is ephemeral and CI-scoped.
SQL Server is ephemeral for the workflow run.
Database name is run-scoped.
The database name must start with IronDev_CI_.
No production or shared database is used.
No repo secrets are required.

## CI Lane

The workflow restores packages.
The workflow builds IronDev.slnx.
The workflow runs explicit SQL-backed integration tests.
The workflow does not run a broad test sweep.
The workflow does not require production secrets.
The workflow does not call external AI providers.

Commands:

- dotnet restore IronDev.slnx
- dotnet build IronDev.slnx --no-restore
- ./Scripts/ci/run-sql-integration-ci.ps1
- explicit SQL-backed integration test filters
- no broad test sweep

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

- Focused C02 static boundary tests: 9/9 passed.
- C01 compatibility: 8/8 passed.
- B12 compatibility: 13/13 passed.
- B11/B10 compatibility: 16/16 passed.
- B09/B08/B07/B06 compatibility: 53/53 passed.
- B05/B04/B03/B01 compatibility: 51/51 passed.
- BQ/BR/BS/BT/BU compatibility: 80/80 passed.
- Governance/status corridor: 1960/1960 passed.
- Restore: passed with existing NU1510 warnings.
- Build: 0 errors / 4 warnings.
- SQL integration lane: live GitHub Actions SQL Server service proof passed on PR head d8e7f1e50684117c678b4b19f37d7f447e254843.
- GitHub Actions sql-integration-ci run 28005862147: passed on PR head d8e7f1e50684117c678b4b19f37d7f447e254843.
- GitHub Actions governance-boundary-ci run 28005862141: passed on PR head d8e7f1e50684117c678b4b19f37d7f447e254843.
- This receipt update records the live CI evidence after the initial local receipt was written; CI evidence remains evidence only.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject this PR if:

- it modifies the C01 workflow instead of adding a separate SQL workflow.
- workflow has write permissions.
- workflow comments on PRs.
- workflow labels PRs.
- workflow changes PR draft or ready state.
- workflow merges.
- workflow pushes commits or tags.
- workflow publishes packages.
- workflow creates releases.
- workflow deploys.
- workflow uploads authority-looking approval artifacts.
- workflow uses repo secrets unnecessarily.
- SQL database name is not run-scoped.
- SQL setup can touch non-CI databases.
- script runs a broad test sweep.
- script starts Docker manually.
- script calls external AI/provider systems.
- SQL CI output is treated as approval, merge readiness, release readiness, deployment readiness, policy satisfaction, or execution permission.
- C02 changes production governance behavior.
- C02 touches executors, API, CLI, UI, or generated-client files.

## Killjoy

A database-backed green check is evidence, not permission.
