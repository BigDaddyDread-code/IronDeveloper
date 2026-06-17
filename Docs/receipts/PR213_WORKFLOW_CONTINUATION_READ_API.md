# PR213 - Workflow Continuation Read API

PR213 adds workflow continuation read API only.

PR213 exposes stored WorkflowTransitionRecord evidence for review.

PR213 does not continue workflow.
PR213 does not mutate workflow state.
PR213 does not transition workflow.
PR213 does not complete workflow steps.
PR213 does not start next workflow steps.
PR213 does not add SQL.
PR213 does not add CLI.
PR213 does not add UI.
PR213 does not add runtime execution.
PR213 does not approve release.
PR213 does not infer release readiness.
PR213 does not declare rollback cleanup.
PR213 does not expand source apply.
PR213 does not execute rollback.
PR213 does not call agents, models, or tools.
PR213 does not promote memory.
PR213 does not activate retrieval.
PR213 does not git commit.
PR213 does not git push.
PR213 does not merge.

Workflow continuation read API is not workflow continuation.
Workflow continuation read API is not workflow state mutation.
Workflow continuation read API is not workflow transition.
Workflow continuation read API is not release readiness.
Workflow continuation read API is not release approval.
Workflow continuation read API is not source apply.
Workflow continuation read API is not rollback execution.

Read WorkflowTransitionRecord is evidence only.
Read WorkflowTransitionRecord is not WorkflowContinued.
Read WorkflowTransitionRecord is not ReleaseReady.
Read WorkflowTransitionRecord is not ReleaseApproved.
Human review remains required.

Review line: PR213 opens the movement-receipt window. It does not press continue.
