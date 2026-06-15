# PR176 Policy Requirement/Satisfaction Evaluator

PR176 adds the Policy Requirement/Satisfaction Evaluator.

This PR evaluates whether a policy requirement is satisfied by an approval satisfaction evaluation.

This PR is pure evaluation only.

## What changed

- Added the `PolicyRequirement` contract.
- Added deterministic `PolicyRequirementHash` generation.
- Added the `PolicyRequirementSatisfactionEvaluation` result contract.
- Added `IPolicyRequirementSatisfactionEvaluator`.
- Added the deterministic `PolicyRequirementSatisfactionEvaluator`.
- Added focused regression tests for exact binding, expiry, evidence, boundary, and non-authority behavior.

## This PR does not

This PR adds no SQL.
This PR adds no API.
This PR adds no CLI.
This PR adds no UI.
This PR does not create policy satisfaction records.
This PR does not store policy satisfaction records.
This PR does not satisfy policy.
This PR does not run dry-runs.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.

## Boundary

Policy requirement satisfaction evaluation is not a policy satisfaction record.

Policy requirement satisfaction evaluation is not dry-run execution.

Policy requirement satisfaction evaluation is not patch artifact creation.

Policy requirement satisfaction evaluation is not source apply.

Policy requirement satisfaction evaluation is not rollback.

Policy requirement satisfaction evaluation is not workflow continuation.

Policy requirement satisfaction evaluation is not release readiness.

Satisfied policy requirement does not authorize execution.

## Evaluation rules

The evaluator requires:

- a policy requirement
- a satisfied approval satisfaction evaluation
- a present accepted approval ID
- no approval evaluation issues
- project binding
- policy code
- policy version
- subject kind
- subject ID
- subject hash
- capability code
- approval target kind
- approval target ID
- approval target hash
- approval purpose
- approval requirement hash
- evaluation timestamp
- unexpired policy requirement when expiry exists
- exact required evidence references
- exact required boundary maxims

The evaluator does not perform semantic matching.

The evaluator does not infer missing evidence.

The evaluator does not infer policy satisfaction from human-looking approval text.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR176 checks whether policy is satisfied. It does not record or spend it.

## Next target

The next Block Q target is Governed Policy Satisfaction Create API.

Suggested next PR:

PR177 - Governed Policy Satisfaction Create API
