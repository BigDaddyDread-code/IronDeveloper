# PR214 — Governed Continuation API/CLI

Review line: PR214 presses continue through a locked gate. It does not release the product.

## Summary

PR214 adds the first governed workflow continuation command surface.

It exposes a single governed continuation API endpoint and a matching CLI command. Both call the governed continuation service. The service revalidates supplied continuation gate evidence, checks expected workflow and step state hashes, performs a controlled workflow state transition, writes a `WorkflowTransitionRecord`, and returns the receipt.

## Boundary

This PR adds governed workflow continuation only.

Governed continuation is workflow state mutation.

Governed continuation is not release readiness.
Governed continuation is not release approval.
Governed continuation is not source apply.
Governed continuation is not rollback execution.
Governed continuation is not policy satisfaction.
Governed continuation is not tool execution.
Governed continuation is not agent dispatch.
Governed continuation is not model invocation.
Governed continuation is not memory promotion.
Governed continuation is not retrieval activation.
Governed continuation is not git commit, push, merge, branch creation, or PR creation.

Human review remains required for release readiness and release approval.

## What changed

- Added governed workflow continuation Core contracts, validation, and state/gate hash helpers.
- Added `IGovernedWorkflowContinuationService`.
- Added a SQL-backed controlled workflow state transition seam.
- Added `POST /api/v1/projects/{projectId}/workflow-continuation/governed`.
- Added `irondev workflow continue governed`.
- Added focused PR214 service and boundary tests.
- Updated the workflow SQL migration with the controlled transition stored procedure and trigger exception.

## Safety rules

- Invalid gate evidence is rejected before mutation.
- Stale workflow or step state hash is rejected before mutation.
- Unsupported transition kind is rejected before mutation.
- Unsafe raw/private material is rejected before mutation.
- Release/source/rollback/policy/agent/model/tool/git/memory/retrieval authority claims are rejected before mutation.
- Failure before mutation writes no `WorkflowTransitionRecord`.
- Failure after mutation is returned as a loud failure and never as success.
- Successful continuation writes a valid `WorkflowTransitionRecord`.

## Supported transitions

- `ContinueToNextStep`
- `MarkStepComplete`

## Not added

This PR does not add workflow scheduler, orchestrator, LangGraph runtime, background worker, autonomous continuation, source apply executor, rollback executor, release readiness evaluator, release decision store, memory promotion, retrieval activation, model call, tool call, git operation, or UI.
