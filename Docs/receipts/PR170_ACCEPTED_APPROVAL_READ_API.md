# PR170 Accepted Approval Read API Receipt

PR170 adds the Accepted Approval Read API.

This PR exposes accepted approval records through read-only project-scoped GET endpoints.

This PR does not create accepted approvals.

This PR does not add an approval create API.

This PR does not satisfy policy.

This PR does not run dry-runs.

This PR does not create patch artifacts.

This PR does not apply source.

This PR does not continue workflow.

This PR does not approve release.

## Boundary

Accepted approval read API is not approval creation.

Reading a persisted approval is not policy satisfaction.

Reading a persisted approval is not dry-run execution.

Reading a persisted approval is not patch artifact creation.

Reading a persisted approval is not source apply.

Reading a persisted approval is not workflow continuation.

Reading a persisted approval is not release readiness.

Reading a persisted approval does not authorize execution.

## API surface

- GET `/api/v1/projects/{projectId}/accepted-approvals/{acceptedApprovalId}`
- GET `/api/v1/projects/{projectId}/accepted-approvals/by-target/{approvalTargetKind}/{approvalTargetId}`
- GET `/api/v1/projects/{projectId}/accepted-approvals/by-correlation/{correlationId}`

There is no POST, PUT, PATCH, or DELETE endpoint in PR170.

Correlation lookup is project-scoped at the API and SQL read boundary.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

## Next target

The next Block P target is governed accepted approval creation boundary.

Preferred next PR: PR171 - Governed Accepted Approval Create Boundary.

## Review line

PR170 opens the approval vault window. It does not hand out keys.
