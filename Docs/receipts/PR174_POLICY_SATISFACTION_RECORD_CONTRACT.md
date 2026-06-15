# PR174 Policy Satisfaction Record Contract

PR174 adds the Policy Satisfaction Record contract.

This PR is contract/test/receipt only.

This PR defines the future durable policy satisfaction record shape.

## What changed

- Added the `PolicySatisfactionSubject` contract.
- Added the `PolicySatisfactionRecord` contract.
- Added shape-only `PolicySatisfactionValidation` helpers.
- Added focused contract tests for binding, expiry, boundary language, and non-authority behavior.

## This PR does not

This PR does not create policy satisfaction records.
This PR does not store policy satisfaction records.
This PR does not read or write policy satisfaction records.
This PR does not expose policy satisfaction APIs.
This PR does not evaluate policy satisfaction.
This PR does not satisfy policy.
This PR does not run dry-runs.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add SQL.
This PR does not add API.
This PR does not add CLI.
This PR does not add UI.

## Required binding

A policy satisfaction record is bound to:

- the project
- the policy
- the policy version
- the subject being authorized
- the subject hash
- the capability
- the accepted approval used as input
- the approval requirement hash used during evaluation
- the approval evaluation timestamp
- the satisfied timestamp
- evidence references
- boundary maxims

Accepted approval alone is not enough context.

A policy satisfaction record for subject hash A must not satisfy subject hash B.

A policy satisfaction record for policy version A must not silently satisfy policy version B.

## Boundary

Accepted approval is an input to policy satisfaction.

Approval satisfaction evaluation is an input to policy satisfaction.

Accepted approval is not policy satisfaction.

Satisfied approval requirement is not policy satisfaction.

Policy satisfaction record is not dry-run execution.

Policy satisfaction record is not patch artifact creation.

Policy satisfaction record is not source apply.

Policy satisfaction record is not rollback.

Policy satisfaction record is not workflow continuation.

Policy satisfaction record is not release readiness.

Policy satisfaction record does not authorize execution by itself.

## Validation rules

Validation is shape-only.

Validation does not create policy satisfaction.

Validation does not store policy satisfaction.

Validation does not satisfy policy.

Validation does not authorize execution.

The expiry rule is shape-only: `ExpiresAtUtc`, when present, must be after `SatisfiedAtUtc`.

No revocation, deletion, freshness inference, policy evaluation, dry-run, patch artifact, source apply, workflow continuation, or release readiness behavior is created here.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR174 defines the policy satisfaction brick. It does not satisfy policy yet.

## Next target

The next Block Q target is Policy Satisfaction SQL Store.

Suggested next PR:

PR175 - Policy Satisfaction SQL Store

