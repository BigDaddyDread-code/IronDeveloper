# L4/L5 Operational Debugging Guide

PR 54 is operational debugging documentation, not runtime change.

Documentation-only; no behavior change intended.

No SQL/API/CLI/UI/runtime/persistence/capability changes.

No new backend capability is allowed until PR 56 Backend Contract Freeze Report passes.

## Purpose

This guide gives developers a practical way to debug high-risk L4/L5 governed backend flows without changing the backend. It helps answer:

- What failed?
- Which boundary was involved?
- What evidence should exist?
- Which logs, traces, reports, or focused tests should be inspected?
- What must not be inferred from model output, audit records, retrieval matches, critic text, or gate decisions?

The guide is deliberately operational. It is not an architecture rewrite, not a runtime plan, and not a new authority path.

## L4/L5 scope

L4 covers governed backend flows where evidence, request, gate, audit, critic, proposal, and manual review boundaries matter. Examples include tool request creation, approval evidence validation, audit record creation, proposal generation, critic review creation, dogfood receipt generation, repair proposal loops, and memory improvement detection.

L4 does not mean automatic source mutation or automatic memory promotion.

L5 covers the highest-risk operation classes: source apply paths, memory promotion paths, future controlled source mutation, future controlled durable memory mutation, and any future path where human approval is required before persistence or mutation.

L5 requires explicit human review and approval. Do not use this guide to imply L5 automation exists if it does not.

## Boundary rules

Keep these rules in front of every investigation:

- SQL is source of truth.
- Weaviate/vector/index is retrieval only, never truth/authority/approval/promotion.
- Proposal is not apply.
- Candidate is not memory.
- Retrieval match is not memory candidate.
- Audit is not approval.
- Gate is not executor.
- Critic is not governance.
- Memory safe is not approval.
- Memory safe is not promotion.
- Tool request is request form, not execution permission.
- Model output is advisory only.
- Human review remains required for source apply and memory promotion.

If a failure investigation needs one sentence, use this one: evidence explains what happened; it does not grant authority by itself.

## Common Failure Shapes

### Tool Request Failure

Debug questions:

- Was a request created?
- Was approval evidence present?
- Was the request audited?
- Was execution attempted?
- Was execution correctly blocked if approval was missing?
- Did the request claim approval, execution permission, or an execution result?

Boundary reminder:

- Tool request is not execution permission.
- Audit is not approval.

First checks:

- Inspect the tool request contract validation result.
- Inspect the gate decision and its blockers.
- Inspect any tool execution audit record only as evidence.
- Confirm the request status did not become an execution state.

### Audit Store Failure

Debug questions:

- Was the evidence recorded?
- Was the audit record treated as approval anywhere?
- Did the audit write occur without mutating source or memory?
- Did idempotency or conflict handling explain the failure?
- Did the audit payload include raw private reasoning, approval claims, source mutation claims, or memory promotion claims?

Boundary reminder:

- Audit is evidence locker, not robot arm.
- Audit is not approval.

First checks:

- Inspect append status and hash comparison.
- Inspect projected safety flags.
- Inspect tenant/project/run scoping.
- Confirm audit append did not trigger execution or mutation.

### Proposal Loop Failure

Debug questions:

- Was a proposal created?
- Was it reviewed?
- Was apply attempted?
- Was source mutated?
- Was human approval required?
- Did critic text become governance or approval?

Boundary reminder:

- Proposal is not apply.
- Critic is not governance.
- Model output is advisory only.

First checks:

- Inspect proposal result and review result separately.
- Inspect audit envelope boundaries.
- Confirm no source apply artifact was created by proposal creation.
- Confirm human review is still required.

### Repair/Fix Proposal Failure

Debug questions:

- Did test failure produce a repair proposal?
- Did the repair proposal mutate source?
- Did any test output become authority?
- Was review evidence separate from apply evidence?
- Did the loop rerun tests or only propose a repair?

Boundary reminder:

- Repair proposal is not repair execution.
- Test output is evidence, not authority.

First checks:

- Inspect failure input, critic stage, proposal stage, and summary separately.
- Confirm proposal-only flags remain true.
- Confirm separate test rerun remains required.
- Confirm no patch apply or source mutation happened.

### Memory Improvement Detection Failure

Debug questions:

- Was the output a suggestion, candidate, proposal, or promoted memory?
- Was memory safety treated as approval?
- Was SQL promotion evidence present?
- Did retrieval/vector output become memory?
- Did a proposal become accepted memory without explicit promotion evidence?

Boundary reminder:

- Candidate is not memory.
- Memory safe is not promotion.
- Memory safe is not approval.
- Retrieval match is not memory candidate.

First checks:

- Inspect local memory, proposal, and promotion records as separate concepts.
- Inspect influence and handoff records only as evidence.
- Inspect whether a proposal service or promotion path actually persisted authority.
- Confirm index/retrieval output did not become memory by itself.

### Dogfood Harness Failure

Debug questions:

- Was a dogfood receipt created?
- Did the receipt claim success without evidence?
- Did dogfood output cause apply or promotion?
- Is the receipt only observational?
- Did the loop produce a source report, failure package, or audit envelope?

Boundary reminder:

- Dogfood receipt is evidence, not the dog.
- Receipt text is not release readiness by itself.

First checks:

- Inspect receipt inputs and outputs.
- Inspect source report or failure package evidence.
- Confirm dogfood output did not create approval, apply source, promote memory, or bypass a gate.

### Gate/Critic Failure

Debug questions:

- Did a critic review become a governance decision?
- Did a gate execute anything?
- Was model confidence treated as permission?
- Was the decision backed by explicit backend policy or contract logic?
- Did a gate decision appear without the request/evidence it depends on?

Boundary reminder:

- Gate is not executor.
- Critic is not governance.
- Model output is advisory only.

First checks:

- Inspect request validation, gate decision, critic review, and audit evidence as separate artifacts.
- Confirm the gate records allow/block posture but does not run the operation.
- Confirm critic text remains review output, not policy.

### L5 Source Apply / Memory Promotion Failure

Debug questions:

- Was explicit human approval present?
- Was approval scoped to the exact operation?
- Was proposal/candidate state converted without approval?
- Was source or memory mutated?
- Was apply or promotion evidence bound to the exact reviewed artifact?

Boundary reminder:

- Human review remains required for source apply and memory promotion.
- Proposal is not apply.
- Candidate is not memory.

First checks:

- Inspect approval evidence identity, scope, expiry, and artifact hash.
- Inspect source apply or memory promotion records separately from proposal/review records.
- Confirm mutation happened only after the required gate and approval evidence.
- If mutation happened without the required evidence, treat it as a blocker, not a warning.

## Diagnostic checklist

Use this checklist during a failed L4/L5 run:

1. Identify the flow.
2. Identify the boundary involved.
3. Locate SQL authoritative records.
4. Locate audit/evidence records.
5. Locate run report/traces/logs.
6. Confirm whether human approval was required.
7. Confirm whether human approval was present.
8. Confirm whether source or memory was mutated.
9. Confirm whether mutation was allowed.
10. Confirm no advisory output was treated as authority.
11. Run the focused test filter.
12. Decide whether this is a bug, missing evidence, stale fixture, or invalid expectation.

When in doubt, split the investigation at the boundary: request, gate, audit, critic, proposal, apply, promotion, and report are different artifacts.

## Evidence Map

| Flow | Required evidence | Forbidden inference |
| --- | --- | --- |
| Tool request | request record, approval evidence if execution is allowed, gate decision, audit record | request alone means permission |
| Proposal review | proposal record, review/critic output, audit/report evidence | proposal means source changed |
| Source apply | scoped human approval evidence, apply report, source diff/result, verification evidence | critic approval is enough |
| Memory promotion | accepted promotion evidence, SQL persisted memory record, promotion audit evidence | safe classification is approval |
| Retrieval | retrieval match/result, query scope, source reference | match is memory candidate |
| Dogfood | receipt/report/logs, source report or failure package where applicable | receipt means release-ready |
| Audit store | append result, stored hashes, projected safety flags, tenant/project/run scope | audit record grants approval |
| Gate decision | validated request, policy result, blockers, approval binding where required | gate executed the operation |
| Critic review | review-only output, findings, warnings, evidence references | critic text is governance |
| Memory improvement | detection result, proposal draft, evidence references, manual review state | proposal created accepted memory |

## Log/trace/run report map

Use this map to find the right evidence before speculating:

| Area | Inspect first | Then inspect | Do not infer |
| --- | --- | --- | --- |
| Agent run | AgentRunAuditEnvelope, ThoughtLedger references, boundary decisions | stored read projection and run report | envelope means approval |
| Tool execution | AgentToolRequest, gate decision, ToolExecutionAuditRecord | request validation issues and audit append status | request means execution |
| Workspace apply | source-report.json, failure-package.json, apply-verify.json | apply-copy and post-apply validation evidence | report text means commit-ready |
| Memory governance | local memory events, influence records, handoff slices | run memory report and proposal queue | influence means authority |
| Retrieval/index | retrieval result, projection metadata, source reference | SQL source record backing the projection | index result means truth |
| Dogfood | dogfood receipt, source report, failure package | tool audit and agent audit envelopes | receipt means release gate passed |
| Model-backed manual agents | sanitised model audit, typed output validation, audit envelope | model adapter and sanitiser issues | model confidence means permission |

## Focused Test Filter Map

Use focused filters first. Adjust names if test classes move, but keep the investigation scoped.

| Area | Suggested filter |
| --- | --- |
| Tool execution audit | `ToolExecutionAuditStore` |
| Manual tester tool execution | `ManualTesterAgentToolExecution` |
| Manual implementation patch proposal | `ManualImplementationPatchProposal` |
| Memory improvement proposal | `MemoryImprovementProposal` |
| Backend SQL cleanup | `BackendSqlCleanup` |
| Inline SQL cleanup | `InlineSql` |
| Backend naming normalisation | `BackendNamingNormalisation` |
| Backend fixture consolidation | `BackendFixtureConsolidation` |
| Backend entity/table inventory | `BackendEntityTableInventory` |
| Backend architecture docs | `BackendArchitecture` |
| Backend ADR docs | `BackendAdr` |
| Operational debugging docs | `OperationalDebugging` |
| Agent run audit envelope | `AgentRunAuditEnvelope` |
| ThoughtLedger safety | `ThoughtLedgerSafety` |
| Tool request contract | `AgentToolRequestContract` |
| Tool execution gate | `AgentToolExecutionGate` |
| Dogfood harness | `ManualDogfoodHarness` |
| Real-run memory improvement | `ManualRealRunMemoryImprovement` |
| Test failure repair loop | `ManualTestFailureRepairProposalLoop` |
| Ticket review fix loop | `ManualTicketReviewFixProposalLoop` |

## Known Red Lanes / Not This Guide

These lanes are known broad or adjacent issues. This guide documents them so they are not hidden inside PR 54.

| Failure group | Why it matters | PR 56 posture | Owner/split guidance |
| --- | --- | --- | --- |
| Stored manual agent DI construction issue in API lane | API host construction can fail for StoredManualIndependentCriticAgentService and StoredManualMemoryImprovementAgentService. | Must be fixed before freeze or listed as a freeze exception. | Split into an API DI/composition cleanup PR. |
| Broad governance/memory/architecture lanes | These lanes test older and broader boundary assertions beyond this docs guide. | Must be triaged before freeze or explicitly listed as red/freeze exception evidence. | Split by failing boundary, not by blanket cleanup. |
| Legacy runtime DDL/bootstrap ownership exceptions from PR 51 | Runtime bootstrap ownership debt can blur migration ownership. | Freeze exception unless removed before PR 56. | Split into runtime bootstrap ownership cleanup. |
| SQL/entity artifacts marked uncertain in inventories | Uncertain persistence artifacts cannot be deleted without proof. | Freeze exception or future cleanup item. | Leave in place until usage proof is complete. |
| Intentionally ugly names left from PR 51.5 | Risky renames can break stored contracts and serialized reports. | Document as allowed ugliness or post-freeze cleanup. | Rename only with contract migration evidence. |
| Full solution broad lanes still failing | Broad suite failures can hide real regressions if not classified. | Must be recorded honestly in PR 56. | Keep focused PR bands green and list broad failures exactly. |

This guide does not fix these lanes. If investigation exposes a real production bug, document it and split the fix into a later PR.

## What not to infer

Do not infer approval from audit records.

Do not infer authority from model output.

Do not infer memory from retrieval matches.

Do not infer source mutation from proposals.

Do not infer governance from critic text.

Do not infer execution from a gate decision.

Do not infer promotion from memory safety results.

Do not infer release readiness from dogfood receipts.

Do not infer human approval from a review-only result.

Do not infer runtime behavior from documentation.

## Escalation/split guidance

Use these split rules when a debugging pass finds a real problem:

- If the failure is missing evidence, fix the evidence-producing command or store in its own PR.
- If the failure is stale fixture setup, fix the fixture or schema-test support in its own PR.
- If the failure is a boundary violation, block merge and split a hardening PR before cleanup continues.
- If the failure is naming confusion only, document it or move it to the naming cleanup lane.
- If the failure is API construction, split it into API DI/composition work.
- If the failure is schema ownership, split it into migration/bootstrap ownership work.
- If the failure is broad-lane drift, do not bury it in docs; list the exact failed test group and owner.
- If the failure requires source apply or memory promotion changes, treat it as L5 and require explicit human review.

A good split has one boundary, one artifact family, one validation band, and no unrelated cleanup.

## Related architecture and ADR docs

Use these docs as authority for the boundaries. This guide is a debugging map; the architecture and ADR docs own the decisions.

- `Docs/BACKEND_ARCHITECTURE.md` - [Backend architecture](BACKEND_ARCHITECTURE.md)
- `Docs/ADR/README.md` - [ADR index](ADR/README.md)
- `Docs/ADR/ADR-001-SQL-source-of-truth.md` - [ADR-001 SQL source of truth](ADR/ADR-001-SQL-source-of-truth.md)
- `Docs/ADR/ADR-002-retrieval-match-not-memory-candidate.md` - [ADR-002 retrieval match not memory candidate](ADR/ADR-002-retrieval-match-not-memory-candidate.md)
- `Docs/ADR/ADR-003-memory-candidate-proposal-promotion-boundary.md` - [ADR-003 memory candidate/proposal/promotion boundary](ADR/ADR-003-memory-candidate-proposal-promotion-boundary.md)
- `Docs/ADR/ADR-004-proposal-review-apply-boundary.md` - [ADR-004 proposal/review/apply boundary](ADR/ADR-004-proposal-review-apply-boundary.md)
- `Docs/ADR/ADR-005-tool-request-audit-execution-boundary.md` - [ADR-005 tool request/audit/execution boundary](ADR/ADR-005-tool-request-audit-execution-boundary.md)
- `Docs/ADR/ADR-006-critic-gate-governance-boundary.md` - [ADR-006 critic/gate/governance boundary](ADR/ADR-006-critic-gate-governance-boundary.md)
- `Docs/ADR/ADR-007-human-review-required-for-apply-and-promotion.md` - [ADR-007 human review required for apply and promotion](ADR/ADR-007-human-review-required-for-apply-and-promotion.md)
