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

## PR84 Approval Requirement Evaluator

PR84 adds deterministic requirement evaluation.

The evaluator consumes a project autonomy policy, project approval rules, and action context. It returns approval requirements only.

PR84 does not check existing approval decisions.
PR84 does not create approval decisions.
PR84 does not create policy decision events.
PR84 does not execute anything.
PR84 does not start or continue workflow.
PR84 does not expose API or CLI endpoints.
PR84 does not write SQL, call agents, call models, create A2A handoffs, mutate source, promote memory, create dogfood receipts, or mark release readiness.

Missing policy or rules fail closed. Invalid policy or rules fail closed. Sensitive scopes require human approval. Experimental autonomy does not bypass sensitive approval.

The evaluator answers only what approval is required. It does not grant approval, satisfy policy, or allow execution.

## PR85 Approval Package Model

PR85 adds a review package model.

Approval packages gather requirements and evidence for review.
Approval packages do not approve anything.
Approval packages do not check approval decisions.
Approval packages do not satisfy policy.
Approval packages do not execute tools.
Approval packages do not mutate source.
Approval packages do not promote memory.
Approval packages do not continue workflow.
Approval packages do not expose API or CLI endpoints.

ReadyForReview means ready for human review, not approved.

Approval package evidence remains supporting material only. Requirement entries preserve evaluator output without satisfying those requirements. All authority flags remain false.


## PR86 Conservative/Balanced/Experimental Policy Profiles

PR86 adds canonical starter policy profiles only:

- Conservative
- Balanced
- Experimental

Profiles produce draft policy and rule shapes only.

Profiles do not activate policy.
Profiles do not evaluate policy.
Profiles do not approve anything.
Profiles do not execute anything.
Profiles do not satisfy policy.
Profiles do not start or continue workflow.
Profiles do not mutate source.
Profiles do not promote memory.
Profiles do not add SQL, API, CLI, or runtime wiring.

Generated project autonomy policies are Draft.
Generated project approval rules are Draft.

Sensitive scopes always require human approval across all profiles:

- source_apply
- memory_promotion
- release_readiness
- external_side_effect
- destructive_operation

Experimental may relax only non-sensitive scopes. Experimental does not bypass human approval for sensitive scopes.

Missing policy or rules still fail closed. Profiles are starter templates for explicit later setup, not hidden defaults.


## PR87 Missing Policy Fails Closed Tests

PR87 proves missing policy and missing approval rules fail closed.
No active policy is not permission.
No matching rule is not permission.
Draft policies are not active policies.
Retired and superseded policies are not active policies.
Profile templates are not active policies.
Generated profile policies and rules remain draft until an explicit later setup path makes them active.
Experimental is not permission.
ReadyForReview approval packages do not override missing policy.
Sensitive scopes require explicit human approval rules.

PR87 is tests and documentation only. It adds no SQL, API, CLI, runtime wiring, workflow runner, A2A handoff, source apply, memory promotion, release approval, execution engine, or policy activation path.

## PR88 Approval Is Not Gate/Receipt/Critic Test Pack

Approval is explicit.
Approval cannot be inferred from gate decisions, policy decisions, dogfood receipts, critic output, validation output, model output, retrieval output, approval packages, policy profiles, ThoughtLedger references, or governance events.
ReadyForReview is not Approved.
NoPolicyBlock is not Approved.
Dogfood Passed is not ReleaseApproved.
Experimental is not Free.
Gate Passed is not Approved.

Gate decisions are evidence, not approval decisions.
Policy decisions are evidence, not approval decisions.
Dogfood receipts are evidence, not release approval.
Critic and code standards reviews are recommendations, not approval.
Approval requirement evaluations describe requirements; they do not satisfy requirements.
Approval packages gather evidence for review; they do not approve anything.
ThoughtLedger references and governance events preserve provenance; they do not transfer authority.
Validation output and run reports are evidence; they do not approve release, source apply, or memory promotion.
Model output, retrieval matches, workflow routes, and A2A handoff evidence remain advisory or evidentiary only.
An explicit approval decision record is still not execution, source mutation, memory promotion, workflow continuation, or release approval.

PR88 is tests and documentation only. It adds no SQL, API, CLI, runtime wiring, workflow runner, A2A handoff, LangGraph runtime, source apply, memory promotion, release approval, execution engine, approval lookup, approval satisfaction checker, or policy activation path.
