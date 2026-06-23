# C13 - Production Environment Safety Validation

## Summary

C13 adds production-like startup safety validation for `IronDev.Api`.

Production environment safety validation prevents production-like API startup from using obvious local, test, placeholder, or dangerous resources. It does not grant authority, approval, policy satisfaction, execution permission, release readiness, deployment readiness, or workflow continuation.

Passing production safety validation is not release readiness. It is only startup configuration evidence.

## Boundary

C13 is a startup guard only.

It does not provision infrastructure, create production resources, change SQL ownership, run validation, run source apply, commit, push, create pull requests, merge, release, deploy, promote memory, satisfy policy, or continue workflow.

Passing C13 means only that the API did not obviously start in a production-like environment with local, test, placeholder, or dangerous configuration.

## Production-Like Scope

Production-like means any environment name other than:

- `Development`
- `Test`
- `LocalTest`

This includes:

- `Production`
- `Staging`
- `UAT`
- `Demo`
- `Accept`
- `Live`
- unknown or custom environment names

Custom environment names fail into production-like safety rules.

## Accepted Production-Like Shape

A production-like API startup may proceed when:

- the database connection string is present
- the database name is present
- the database name does not carry local, test, dev, scratch, or temp markers
- the database server is not local-only
- dangerous real repo writes are not enabled
- optional workspace or evidence roots are either omitted or do not look local, test, dev, temp, user-profile, or repository-worktree scoped

C13 does not require optional LocalTest or DisposableBuild roots in production-like environments. It rejects unsafe configured roots when they are present.

## Rejected Unsafe Shapes

C13 fails startup when a production-like environment uses:

- missing or blank database connection strings
- placeholder database server configuration such as `YOUR_SERVER`
- missing database names
- test-like database names such as `IronDeveloper_Test`, `IronDeveloper_Local`, `IronDeveloper_Dev`, or `IronDeveloper_LocalTest`
- local database servers such as `localhost`, loopback addresses, localdb, desktop machine names, or local developer SQL instances
- password-bearing database configuration
- LocalTest workspace or logs roots that look local, test, dev, scratch, temp, user-profile, or repo-worktree scoped
- DisposableBuild workspace or evidence roots that look local, test, dev, scratch, temp, user-profile, or repo-worktree scoped
- `LocalTest:DangerRealRepoWritesEnabled=true`

Startup errors name the failed safety category and do not echo full connection strings, secrets, or full filesystem paths.

## CI Lane

C13 adds `BlockC13ProductionEnvironmentSafetyRegressionTests` to the existing security boundary lane in `Scripts/ci/run-governance-boundary-ci.ps1`.

No new workflow, external service, artifact upload, SQL service, frontend contract lane, or deployment lane is added by C13.

## Forbidden Mutation Paths

C13 must not touch or create:

- SQL migrations, stores, or procedures
- source-apply execution
- commit or push execution
- pull request creation
- merge, release, deployment, tag, or publish execution
- rollback execution
- memory write or promotion
- governance authority expansion
- frontend or generated-client runtime changes
- deployment provisioning

## Validation

- Focused C13 API startup tests: 31/31 passed
- Focused C13 static regression tests: 11/11 passed
- C06-C13 security boundary lane: 86/86 passed
- Governance boundary CI script:
  - B-series profile boundary tests: 133/133 passed
  - BQ-BU compatibility boundary tests: 80/80 passed
  - Security boundary tests: 29/29 passed
  - API boundary tests: 38/38 passed
  - CLI boundary tests: 41/41 passed
- Build: 0 errors / 2 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed

## Review Traps

Reject C13 if:

- production-like API can start with `YOUR_SERVER`
- production-like API can start without a database name
- production-like API can start with a test, local, dev, scratch, or temp database name
- production-like API can start with localhost, localdb, loopback, desktop-machine, or local developer SQL server posture
- production-like API can start with LocalTest test, temp, dev, or local workspace roots
- production-like API can start with LocalTest test, temp, dev, or local logs roots
- production-like API can enable dangerous real repo writes
- LocalTest C12 safety is weakened
- non-production environments are accidentally blocked by production-only rules
- errors echo full connection strings or secrets
- JWT, CORS, Weaviate, or environment endpoint behavior changes outside proof
- SQL, governance, memory, source-apply, release, or deploy paths are touched
- C13 becomes deployment provisioning or release readiness

## Killjoy

A production environment with local/test config is not production-safe; it is mislabelled danger.
