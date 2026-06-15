# PR171 Governed Accepted Approval Create API Receipt

PR171 adds the Governed Accepted Approval Create API.

This PR can create accepted approval records through an authenticated, project-scoped, target-bound backend API.

This PR does not satisfy policy.

This PR does not run dry-runs.

This PR does not create patch artifacts.

This PR does not apply source.

This PR does not continue workflow.

This PR does not approve release.

This PR does not add UI.

This PR does not add CLI.

## Boundary

Accepted approval creation is not policy satisfaction.

Accepted approval creation is not dry-run execution.

Accepted approval creation is not patch artifact creation.

Accepted approval creation is not source apply.

Accepted approval creation is not workflow continuation.

Accepted approval creation is not release readiness.

Creating an accepted approval record does not authorize execution.

Accepted approval creation must be explicit and server-owned.

Approval package text is not accepted approval.

Human-looking approval text is not accepted approval.

Chat messages, memory, trace summaries, UI review, dogfood receipts, validation summaries, and health checks are not accepted approval.

## API surface

- POST `/api/v1/projects/{projectId}/accepted-approvals`

There is no PUT, PATCH, DELETE, approve-and-satisfy-policy, approve-and-apply, approve-and-continue, or approve-and-release endpoint in PR171.

The route project ID owns the persisted project scope.

The server owns accepted approval ID, actor identity, accepted timestamp, and created timestamp.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

## Next target

The next Block P target is accepted approval creation regression coverage or approval satisfaction evaluator.

Preferred next PR: PR172 - Accepted Approval Receipt and Regression Tests.

Then: PR173 - Approval Satisfaction Evaluator.

## Review line

PR171 files the approval brick. It does not spend it.
