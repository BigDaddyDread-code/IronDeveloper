# PR126 - Block L Governed Runner Receipt

## Summary

Block L establishes the governed runner substrate. It provides typed workflow step contracts, evaluation-only runner checks, policy preflight, ThoughtLedger traceability, A2A handoff validation, approval-required halt state, safe dry-run execution, boxed advisory routing labels, and regression tests proving workflow artifacts cannot grant authority.

Block L closes the governed runner foundation. This receipt does not claim IronDev can execute real workflows, run agents, invoke tools, mutate source, promote memory, activate retrieval, satisfy policy, grant approval, or operate production workflow orchestration.

Receipt is not capability.

## What Block L can do

- represent workflow steps as typed contracts
- evaluate supplied step contracts
- report missing evidence
- report policy preflight blockers
- require ThoughtLedger traceability
- validate supplied A2A handoff snapshots
- report approval-required halt state
- execute a deterministic non-mutating dry-run action from supplied eligible snapshots
- map supplied runner/dry-run snapshots to advisory route labels
- prove workflow artifacts cannot grant authority

## What Block L cannot do

- cannot execute real workflow steps
- cannot transition workflow state
- cannot complete workflow steps
- cannot create approvals
- cannot grant approvals
- cannot deny approvals
- cannot satisfy policy
- cannot dispatch agents
- cannot send A2A handoffs
- cannot invoke tools
- cannot call models
- cannot build prompts
- cannot mutate source
- cannot apply patches
- cannot promote memory
- cannot activate retrieval
- cannot write SQL
- cannot expose API/CLI/UI runtime execution
- cannot make LangGraph the route owner

## PR evidence table

| PR | Evidence | Boundary |
| --- | --- | --- |
| PR117 | Typed workflow step contract | Contract is not execution |
| PR118 | Runner skeleton | Evaluation only |
| PR119 | Policy preflight | Check is not approval |
| PR120 | ThoughtLedger reference required | Traceability is not authority |
| PR121 | A2A handoff validation | Validation is not dispatch |
| PR122 | Approval-required halt | Halt is not approval |
| PR123 | Safe dry-run executor | Dry-run is not execution |
| PR124 | Boxed routing adapter | Route label is not decision ownership |
| PR125 | Cannot-grant-authority tests | Workflow cannot mint authority |

## Block L invariant set

```text
Evidence is not approval.
Traceability is not authority.
Validation is not dispatch.
Halt is not approval.
Dry-run is not execution.
Route label is not decision ownership.
Receipt is not capability.
```

## Block L boundary matrix

| Artifact | Allowed meaning | Forbidden meaning |
| --- | --- | --- |
| Typed workflow step contract | Describes a step and its required evidence | Execute, dispatch, approve, mutate, or promote |
| Runner skeleton evaluation | Reports eligibility and blockers from supplied material | Transition workflow state or run a step |
| Policy preflight | Records policy evidence presence or blockers | Approval, policy satisfaction, or override |
| ThoughtLedger reference | Provides traceability anchor | Authority or trusted hidden reasoning |
| A2A handoff validation | Validates a supplied handoff snapshot | Sends a handoff or resolves an agent |
| Approval-required halt | Reports that approval is still required | Creates, grants, denies, or satisfies approval |
| Safe dry-run | Produces deterministic non-mutating review material | Executes workflow, tools, prompts, or source changes |
| Boxed route label | Suggests advisory route labels from supplied snapshots | Owns routing, dispatch, or decision authority |
| Authority-boundary tests | Prove workflow artifacts cannot mint authority | Grant capability or release readiness |

## Not yet implemented

Block L does not implement real workflow execution, workflow state transition mutation, agent dispatch, A2A send, tool invocation, model calls, prompt construction, source apply, patch application, memory promotion, retrieval activation, SQL write ownership, API runtime execution, CLI runtime execution, UI runtime execution, hosted services, schedulers, orchestrators, workers, external LangGraph runtime dependency, or production workflow orchestration.

Block L does not claim controlled source apply exists.
Block L does not claim L4 candidate workflows exist.
Block L does not claim operational readiness exists.
Block L does not claim UI consumption exists.
Block L does not claim release readiness.

SQL remains source of truth. Workflow artifacts remain descriptive unless explicitly executed by later governed code.

## Block M handoff

Block M may begin candidate L4 workflows only on top of this governed runner substrate. Candidate workflows must remain non-mutating until later controlled source-apply boundaries exist.

Block M must not treat Block L receipt as permission to dispatch agents, invoke tools, mutate source, promote memory, activate retrieval, or transition workflow state.

## Review line

PR126 closes the runner substrate receipt. It does not claim the runner can drive.
