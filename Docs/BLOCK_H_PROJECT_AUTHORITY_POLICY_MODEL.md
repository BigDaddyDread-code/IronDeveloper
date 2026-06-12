# Block H Project Authority Policy Model

Block H begins with policy vocabulary only.

PR82 defines ProjectAutonomyPolicy contracts.

PR82 does not evaluate policy.
PR82 does not approve or execute anything.

Project autonomy levels are:

- Conservative
- Balanced
- Experimental

The word "free" is intentionally forbidden as an autonomy level because it suggests unbounded agent authority.

Missing policy must later fail closed.

Sensitive actions remain human-review gated, including source apply, accepted-memory promotion, destructive operations, external side effects, and release approval.

Project autonomy policy is not approval, execution permission, workflow routing, source apply, memory promotion, release approval, or model authority.

Later blocks may use this vocabulary to evaluate approval requirements, but this contract does not grant authority.

## PR83 Project Approval Rule Contract

PR83 defines project approval rule vocabulary only.

A project approval rule can describe which project scope, subject category, action pattern, risk level, approval type, and approver type vocabulary applies to a governed action.

PR83 does not evaluate rules.
PR83 does not create approval decisions.
PR83 does not execute anything.
PR83 does not add SQL, API, CLI, or runtime wiring.
PR83 does not satisfy policy, continue workflow, mutate source, promote memory, approve release readiness, or create A2A handoffs.

Sensitive scopes require human approval rules:

- source_apply
- memory_promotion
- release_readiness
- external_side_effect
- destructive_operation

ApprovalType=None is forbidden for source apply, memory promotion, release readiness, external side effects, and destructive operations.

System and Agent approver types are not allowed for sensitive scopes. Model, LLM, Critic, Retriever, VectorStore, Workflow, LangGraph, A2A, DogfoodReceipt, GateDecision, and PolicyDecision are forbidden approver types because they are evidence or infrastructure, not approvers.

The word free remains forbidden for approval rules. Missing approval rules must later fail closed. Model output remains advisory only.

PR83 names the approval rulebook. It does not approve anything.
