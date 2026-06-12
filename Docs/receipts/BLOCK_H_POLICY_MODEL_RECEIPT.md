# Block H Policy Model Receipt

## 1. Summary

Block H added IronDev's project authority and approval policy model.

It defines policy and approval vocabulary, deterministic approval requirement evaluation, approval package review envelopes, starter profile templates, fail-closed policy behaviour, and approval-laundering regression tests.

Block H does not activate policy, approve actions, execute tools, mutate source, promote memory, continue workflow, satisfy policy, or approve release.

The correct Block H claim is narrow:

Project policy vocabulary exists. Approval rule vocabulary exists. Approval requirements can be evaluated deterministically. Approval packages can gather review paperwork. Policy profiles can generate draft policy and rule templates. Missing policy fails closed. Approval cannot be inferred from gates, receipts, critics, profiles, packages, validation, model output, retrieval, ThoughtLedger, governance events, A2A evidence, or workflow evidence. No Block H object grants execution authority.

This report is a receipt, not a trophy.

## 2. What Block H delivered

| PR | Purpose | Durable/runtime object added | Grants authority? | Main validation evidence |
| --- | --- | --- | --- | --- |
| PR 82 | Project Autonomy Policy Contract | Core policy contract models only | No | ProjectAutonomyPolicy validation |
| PR 83 | Project Approval Rule Contract | Core approval rule contract models only | No | ProjectApprovalRule validation |
| PR 84 | Approval Requirement Evaluator | Deterministic Core evaluator only | No | ApprovalRequirementEvaluator passed 69/69 |
| PR 85 | Approval Package Model | Core approval package model only | No | ApprovalPackage passed 83/83 |
| PR 86 | Conservative/Balanced/Experimental Policy Profiles | Core draft profile factory only | No | ProjectPolicyProfile passed 15/15 |
| PR 87 | Missing Policy Fails Closed Tests | Tests only | No | MissingPolicyFailsClosed passed |
| PR 88 | Approval Is Not Gate/Receipt/Critic Test Pack | Tests and documentation only | No | ApprovalAuthorityBoundary passed 91/91 |
| PR 89 | Block H Policy Model Receipt | Receipt document and guard tests only | No | BlockHPolicyModelReceipt |

Block H defines and guards model language. It does not put policy into force.

## 3. Current policy model components

| Component | Records or computes | Does not grant | Active runtime authority? | Writes SQL? | Exposes API/CLI? | Can execute? |
| --- | --- | --- | --- | --- | --- | --- |
| ProjectAutonomyPolicy | Project policy shape and scoped policy intent | Approval, execution, source apply, memory promotion, workflow continuation, release approval | No | No | No | No |
| ProjectApprovalRule | Approval rule shape for scoped requirements | Approval, policy satisfaction, execution, release approval | No | No | No | No |
| ApprovalRequirementEvaluator | Deterministic requirement list from policy/rules/request context | Approval, permission, policy satisfaction, execution | No | No | No | No |
| ApprovalRequirementEvaluationResult | Evaluation result and required approvals | Approval, execution, source apply, memory promotion | No | No | No | No |
| ApprovalRequirement | Required approval paperwork | Approval, execution, release approval | No | No | No | No |
| ApprovalPackage | Review envelope for approval paperwork | Approval, policy satisfaction, execution, source apply, memory promotion | No | No | No | No |
| ApprovalPackageRequirement | Requirement evidence inside a package | Approval, execution, release approval | No | No | No | No |
| ApprovalPackageEvidenceReference | Evidence reference for human review | Approval, execution, source apply, memory promotion | No | No | No | No |
| ProjectPolicyProfile | Starter template description | Active policy, approval, permission, execution | No | No | No | No |
| ProjectPolicyProfileFactory | Draft policy/rule request generation | Active policy, approval, hidden defaults, runtime permission | No | No | No | No |

ApprovalRequirementEvaluator returns requirements, not approval.

ApprovalPackage ReadyForReview means ready for review, not approved.

ProjectPolicyProfile produces draft templates, not active policy.

Experimental means less friction for non-sensitive scopes, not permission.

Missing policy means fail closed.

Gate, receipt, critic, model, retrieval, validation, ThoughtLedger, governance event, A2A, and workflow evidence is not approval.

## 4. Authority boundary matrix

| Component | Approval? | Execution? | Source apply? | Memory promotion? | Workflow? | Release approval? |
| --- | --- | --- | --- | --- | --- | --- |
| ProjectAutonomyPolicy | No | No | No | No | No | No |
| ProjectApprovalRule | No | No | No | No | No | No |
| ApprovalRequirementEvaluator | No | No | No | No | No | No |
| ApprovalRequirementEvaluationResult | No | No | No | No | No | No |
| ApprovalRequirement | No | No | No | No | No | No |
| ApprovalPackage | No | No | No | No | No | No |
| ApprovalPackageRequirement | No | No | No | No | No | No |
| ApprovalPackageEvidenceReference | No | No | No | No | No | No |
| ProjectPolicyProfile | No | No | No | No | No | No |
| ProjectPolicyProfileFactory | No | No | No | No | No | No |
| MissingPolicyFailsClosed tests | No | No | No | No | No | No |
| ApprovalAuthorityBoundary tests | No | No | No | No | No | No |

No Block H object grants runtime authority.

No Block H object grants execution authority.

No Block H object grants source mutation authority.

No Block H object grants memory promotion authority.

No Block H object grants workflow continuation authority.

No Block H object grants release approval authority.

## 5. Fail-closed model

No active policy is not permission.

No matching rule is not permission.

Invalid policy is not permission.

Invalid rule is not permission.

Ambiguous rule selection is not permission.

Draft generated profile policy is not active policy.

ReadyForReview package does not override missing policy.

No policy means fail closed.

No matching approval rule means fail closed.

Invalid policy means fail closed.

Invalid rule means fail closed.

Ambiguous rules mean fail closed.

## 6. Approval-laundering boundary

Approval cannot be inferred from:

- gate decisions
- policy decision events
- dogfood receipts
- critic output
- code standards output
- approval requirement evaluator results
- approval packages
- policy profiles
- ThoughtLedger references
- governance events
- validation output
- run reports
- model output
- retrieval/vector matches
- A2A evidence
- workflow-route evidence

Only explicit approval decision records can be treated as approval evidence.

Even an approval decision record does not execute, mutate source, promote memory, continue workflow, satisfy policy, or approve release by itself.

Gate pass is not approval.

Dogfood receipt is not approval.

Critic output is not approval.

Validation output is not approval.

Model output is not approval.

Retrieval output is not approval.

ReadyForReview is not approved.

Experimental is not permission.

## 7. Sensitive scope policy

Sensitive scopes:

- source_apply
- memory_promotion
- release_readiness
- external_side_effect
- destructive_operation

Every sensitive scope requires explicit human approval rules.

Experimental profiles do not bypass sensitive approval.

ApprovalType=None is not valid for sensitive scopes.

Agent, System, Model, Critic, Workflow, Retrieval, A2A, validation, gate, policy-decision, and dogfood concepts cannot satisfy sensitive approval.

## 8. Profile status

Conservative, Balanced, and Experimental profiles are starter templates only.

They generate draft policy and rule shapes.

They do not activate policy.

They do not evaluate policy.

They do not approve anything.

They do not become hidden defaults.

They do not satisfy approval requirements.

They do not create approval decisions.

They do not create policy decision events.

They do not start workflow.

They do not mutate source.

They do not promote memory.

Experimental relaxes only non-sensitive draft settings. Experimental does not bypass source apply, memory promotion, release readiness, external side effect, or destructive operation approval.

## 9. API/CLI/runtime status

Block H adds no API endpoint.

Block H adds no CLI command.

Block H adds no SQL persistence.

Block H adds no runtime DI wiring.

Block H adds no workflow runner.

Block H adds no policy activation path.

Block H adds no approval decision lookup.

Block H adds no approval satisfaction checker.

Block H adds no policy decision recording.

Block H adds no dogfood receipt recording.

Block H adds no A2A runtime.

Block H adds no LangGraph runtime.

Block H adds no source apply.

Block H adds no memory promotion.

Block H adds no release approval.

## 10. Explicit non-claims

Block H does not mean:

- IronDev is release-ready.
- L4 agents are ready.
- Workflow orchestration exists.
- A2A exists.
- LangGraph is integrated.
- Policy activation exists.
- Approval satisfaction exists.
- Approval decision lookup exists.
- Source apply is available.
- Memory promotion is available.
- Release approval is available.
- Policy profiles are active by default.
- Experimental mode is permission.
- ReadyForReview means approved.
- Gate pass means approved.
- Dogfood passed means release approved.
- Critic clean means approved.
- Validation passed means approved.
- Policy engine runtime authority exists.
- Approval engine runtime authority exists.
- Block H does not claim autonomous execution exists.
- Block H does not claim the system can execute because Block H exists.
- Block H does not claim the system can ship because Block H exists.

Block H does not claim release readiness.

Block H does not claim L4 completion.

Block H does not claim workflow readiness.

Block H does not claim source apply readiness.

Block H does not claim memory promotion readiness.

Block H does not claim policy engine runtime authority.

## 11. Known gaps after Block H

No active policy storage yet.

No policy activation path yet.

No approval satisfaction checker yet.

No approval decision lookup against requirements yet.

No workflow checkpointing yet.

No A2A handoff spine yet.

No source apply path yet.

No memory promotion path yet.

No release approval gate yet.

Suggested next blocks:

- Block I - A2A Handoff Contract Spine
- Block J - Workflow State and Checkpoint Spine
- Block K - MemoryImprovementAgent L2/L3
- Block L - Minimal Governed Workflow Runner

## 12. Validation evidence

| Band | Evidence |
| --- | --- |
| BlockHPolicyModelReceipt | Passed 15/15 |
| ProjectPolicyProfile | Passed 15/15 |
| ApprovalPackage | Passed 83/83 |
| ApprovalRequirementEvaluator | Passed 69/69 |
| ProjectAutonomyPolicy\|ProjectApprovalRule | Passed 84/84 |
| MissingPolicyFailsClosed | Passed 90/90 |
| ApprovalAuthorityBoundary | Passed 91/91 |
| GovernanceSubstrateContract\|BlockGGovernanceSubstrateReceipt | Passed 19/19 |
| ToolRequestApi\|ToolGateApi\|DogfoodLoopApi | Passed 44/44 |
| ApiCliContract\|ApiCliReleaseGate\|ThoughtLedger | Passed 70/70 |
| dotnet build IronDev.slnx --no-restore -v:minimal | Passed, 0 errors |
| git diff --check | Passed, LF/CRLF warning only |

## 13. Merge standard

This PR may merge only when:

- Block H receipt exists.
- Receipt lists PR 82 through PR 89.
- Receipt lists all Block H model components.
- Receipt clearly states what Block H delivered.
- Receipt clearly states what Block H did not deliver.
- Receipt refuses release, L4, workflow, source apply, and memory promotion readiness claims.
- Receipt states missing policy fails closed.
- Receipt states approval cannot be inferred from nearby evidence.
- Receipt states sensitive scopes require human approval.
- Receipt states profiles are templates only.
- Receipt includes validation evidence.
- Receipt tests pass.
- PR 82 through PR 88 focused tests remain green.
- Block G tests remain green.
- API/CLI/ThoughtLedger tests remain green.
- Build passes.
- Diff-check passes.
- No runtime behaviour is added.

## 14. Final receipt statement

Block H is complete as a project authority and approval policy model.

It gives IronDev bounded policy vocabulary, approval rule vocabulary, deterministic requirement evaluation, approval package modelling, safe starter profiles, fail-closed behaviour, and approval-laundering regression coverage.

It does not activate policy.

It does not approve actions.

It does not execute tools.

It does not mutate source.

It does not promote memory.

It does not continue workflow.

It does not approve release.

The policy model is now defined and guarded.

The system still cannot act merely because the model exists.
