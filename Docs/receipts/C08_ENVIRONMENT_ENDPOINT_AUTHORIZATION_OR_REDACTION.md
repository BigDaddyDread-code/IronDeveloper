# C08 - Environment Endpoint Authorization / Redaction

## Summary

C08 protects `/api/environment` from anonymous operational-state disclosure.

The endpoint now requires authenticated API access before returning full environment diagnostics. `/health` remains anonymous and returns only health status.

Environment visibility is diagnostic evidence. It is not authority, and it must not leak operational state anonymously.

## Boundary

Environment diagnostics are evidence only.

Environment diagnostics are not approval, policy satisfaction, tenant authority, source-apply authority, commit authority, push authority, PR authority, merge readiness, release readiness, deployment readiness, memory authority, or workflow continuation.

C08 does not redesign authentication, change JWT signing behavior, change token claims, change tenant authorization, add new environment data, add redacted DTOs, change OpenAPI/generated clients, add SQL storage, alter governance authority, mutate source, create commits, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Endpoint Scope

Protected endpoint:

- `GET /api/environment`

Anonymous endpoint intentionally preserved:

- `GET /health`

Full environment details remain available only through the authenticated `/api/environment` path.

## Anonymous Behavior

Anonymous callers to `/api/environment` receive `401 Unauthorized`.

Anonymous callers must not receive:

- database name
- workspace root
- logs root
- real-repo-write flag
- Weaviate prefix
- test-environment flag
- machine-specific or operational paths

## Authenticated Behavior

Authenticated callers may receive the existing `EnvironmentInfoDto` diagnostics:

- environment name
- database name
- Weaviate prefix
- test-environment flag
- workspace root
- logs root
- real-repo-write flag

The response shape is unchanged for authenticated callers.

## Forbidden Mutation Paths

- no auth redesign
- no token claim change
- no JWT signing-key resolver change
- no tenant authorization change
- no frontend runtime change
- no generated client change
- no OpenAPI snapshot change
- no SQL migration
- no SQL store/procedure change
- no governance authority change
- no source apply
- no commit
- no push
- no PR creation/update
- no merge
- no release
- no deployment
- no memory write or promotion
- no workflow continuation

## Validation

- Focused C08 tests: 5/5 passed.
- API environment/auth endpoint tests (`ApiHarnessTests|AuthControllerTests|TenantControllerTests`): 18/18 passed.
- API boundary lane: 38/38 passed.
- C06/C07 JWT configuration and startup validation tests: 27/27 passed.
- CLI boundary lane: 41/41 passed.
- Governance-boundary CI script:
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Frontend contract CI script: passed.
- Build: passed with 0 errors.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.

## Review Traps

Reject this PR if:

- `/api/environment` remains anonymous
- anonymous callers receive database, workspace, logs, or write-flag details
- `/health` stops working anonymously
- authenticated `/api/environment` diagnostics break
- token claims change
- JWT signing behavior changes
- tenant authorization changes
- frontend/generated/OpenAPI surfaces change without an explicit contract reason
- SQL/governance/memory/source-apply/commit/push/merge/release/deploy paths are touched
- environment diagnostics become approval, policy, execution, release, deployment, memory, or workflow-continuation authority

## Killjoy

An environment endpoint that exposes database/workspace details anonymously is not convenience; it is reconnaissance.
