# PR122 - Approval-required Halt State Receipt

PR122 adds an explicit approval-required halt state to workflow runner evaluation.
A step with missing required approval evidence is blocked as approval-required and cannot be considered eligible for future execution.

## Boundary

Approval halt is not approval.
Approval evidence is not approval mutation.
The runner does not create, grant, deny, or satisfy approval.
The runner skeleton remains evaluation-only.
PR122 does not transition workflow state.
It reports halt state only.

PR122 evaluates supplied approval halt requests.
It does not yet infer every approval-sensitive workflow step by itself.

## Preserved non-goals

- No workflow execution.
- No workflow continuation.
- No workflow state transition.
- No approval request creation.
- No approval decision mutation.
- No policy satisfaction.
- No source apply.
- No memory promotion.
- No A2A dispatch.
- No SQL, API, CLI, UI, scheduler, orchestrator, or runtime surface.

## Review line

PR122 adds the stop sign. It does not approve the road.
