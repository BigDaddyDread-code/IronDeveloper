# BQ Run Authority Profile Contract

## Review Line

A run authority profile is an authority ceiling, not an authority grant.

## Receipt

This PR adds a run authority profile contract only.

It does not add a runner.
It does not execute commands.
It does not mutate source.
It does not create approvals.
It does not satisfy policy.
It does not promote memory.
It does not continue workflow.
It does not add frontend/API/CLI.
It does not add source apply.

Allowed by profile is necessary but not sufficient.

## Boundary

The contract defines a typed ProposalOnly authority ceiling. ProposalOnly may describe proposal-safe operation categories such as repository inspection, task interpretation, disposable workspace activity, proposal evidence, patch package evidence, validation result package evidence, and governed status inspection.

ProposalOnly must not describe durable source mutation, patch application, rollback execution, commit, push, pull request creation, ready-for-review, merge, release, deployment, approval creation, policy satisfaction, memory promotion, workflow continuation, provider mutation, package publication, or durable event write as allowed.

The evaluator returns `IsAllowedByProfile` only. It does not return approval, authorization, policy satisfaction, execution permission, or any operation-specific validation result. A positive profile decision still requires independent operation-specific validation.

## Killjoy

A profile can describe the sandbox. It cannot open the gate.
