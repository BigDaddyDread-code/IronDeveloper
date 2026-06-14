# PR139 Controlled Apply Plan Model

## Summary

PR139 adds a Controlled Apply Plan model.

Controlled apply plan is not controlled apply execution.

Plan step is not execution step.

Apply placeholder is not executable.

Validation reference is not validation execution.

Rollback note is not rollback execution.

Source apply approval requirement is not approval satisfaction.

Patch proposal evidence package is not a patch.

This PR does not apply source, apply patches, mutate files, read source files, run commands, invoke tools, dispatch agents, call models, build prompts, run validation, run rollback, satisfy approval, satisfy policy, transition workflow, create tickets, promote memory, activate retrieval, write SQL, or add runtime wiring.

Controlled apply execution remains unimplemented.

Source apply remains unimplemented.

Patch apply remains unimplemented.

## Boundary

The controlled apply plan is a non-authoritative package of supplied references. It can describe an intended route, list prerequisites, name validation references, record rollback notes, and summarize missing evidence.

It cannot perform the route.

It cannot convert approval requirements into approval satisfaction.

It cannot convert patch proposal evidence into a patch payload.

It cannot convert validation references into validation results.

It cannot convert rollback notes into rollback execution.

It cannot transition workflow state.

## Validation posture

The plan workflow is deterministic and Core-only. It accepts supplied request material, validates the request shape, blocks unsafe language and authority claims, and returns a plan-only result.

Every produced result keeps execution, source apply, patch application, file mutation, command, tool, validation, rollback, approval, policy, workflow transition, ticket, memory, retrieval, and SQL authority flags false.

## Review line

PR139 draws the apply route. It does not drive it.
