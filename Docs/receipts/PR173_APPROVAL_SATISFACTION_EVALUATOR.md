# PR173 Approval Satisfaction Evaluator

PR173 adds the Approval Satisfaction Evaluator.

This PR evaluates whether an accepted approval record satisfies an approval requirement.

This PR is pure evaluation only.

## What changed

- Added the `ApprovalRequirement` contract.
- Added the `ApprovalSatisfactionEvaluation` result contract.
- Added `IApprovalSatisfactionEvaluator`.
- Added the deterministic `ApprovalSatisfactionEvaluator`.
- Added focused regression tests for exact binding, evidence, boundary, and non-authority behavior.

## Boundary

Approval satisfaction evaluation is not policy satisfaction.

Satisfied approval requirement is not policy satisfaction.

Satisfied approval requirement is not dry-run execution.

Satisfied approval requirement is not patch artifact creation.

Satisfied approval requirement is not source apply.

Satisfied approval requirement is not workflow continuation.

Satisfied approval requirement is not release readiness.

Satisfied approval requirement does not authorize execution.

## This PR does not

- add SQL
- add API
- add CLI
- add UI
- create accepted approvals
- create policy satisfaction records
- satisfy policy
- run dry-runs
- create patch artifacts
- apply source
- continue workflow
- approve release

## Evaluation rules

The evaluator requires exact matches for:

- project ID
- approval target kind
- approval target ID
- approval target hash
- capability code
- approval purpose
- required evidence references
- required boundary maxims

Expired accepted approvals do not satisfy a requirement.

Missing accepted approvals do not satisfy a requirement.

Invalid accepted approval records do not satisfy a requirement.

Evidence and boundary maxim matching is exact only.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR173 checks whether the approval brick fits. It does not open the gate.

## Next target

The next block target is Policy Satisfaction Record Contract.

Suggested next PR:

PR174 - Policy Satisfaction Record Contract
