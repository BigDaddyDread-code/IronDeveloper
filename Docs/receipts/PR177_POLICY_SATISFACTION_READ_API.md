# PR177 Policy Satisfaction Read API

PR177 adds the Policy Satisfaction Read API.

This PR exposes policy satisfaction records through read-only project-scoped GET endpoints.

## What changed

- Added the policy satisfaction read model and boundary model.
- Added `IPolicySatisfactionQueryService`.
- Added `PolicySatisfactionQueryService`.
- Added `PolicySatisfactionsV1Controller`.
- Registered read-only policy satisfaction API dependencies.
- Added focused API tests for project-scoped reads, hostile lookup validation, non-mutation, and boundary language.

## This PR does not

This PR does not create policy satisfaction records.
This PR does not add a policy satisfaction create API.
This PR does not evaluate policy satisfaction.
This PR does not run dry-runs.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add UI.
This PR does not add CLI.

## Boundary

Policy satisfaction read API is not policy satisfaction creation.

Reading persisted policy satisfaction is not dry-run execution.

Reading persisted policy satisfaction is not patch artifact creation.

Reading persisted policy satisfaction is not source apply.

Reading persisted policy satisfaction is not rollback.

Reading persisted policy satisfaction is not workflow continuation.

Reading persisted policy satisfaction is not release readiness.

Reading persisted policy satisfaction does not authorize execution by itself.

Persisted policy satisfaction is not dry-run execution, patch artifact creation, source apply, rollback, workflow continuation, or release readiness.

## API surface

The API exposes only project-scoped GET routes:

- `GET /api/v1/projects/{projectId}/policy-satisfactions/{policySatisfactionId}`
- `GET /api/v1/projects/{projectId}/policy-satisfactions/by-subject/{subjectKind}/{subjectId}`
- `GET /api/v1/projects/{projectId}/policy-satisfactions/by-accepted-approval/{acceptedApprovalId}`
- `GET /api/v1/projects/{projectId}/policy-satisfactions/by-correlation/{correlationId}`

There is no unscoped correlation route.

There is no create endpoint.

There is no update endpoint.

There is no delete endpoint.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR177 opens the policy vault window. It does not spend the policy brick.

## Next target

The next Block Q target is Governed Policy Satisfaction Create API.

Suggested next PR:

PR178 - Governed Policy Satisfaction Create API
