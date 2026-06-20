# BC Deployment Readiness Separation

## Purpose

Block BC consumes a verified BB controlled release execution receipt and packages deployment-readiness separation evidence.

BC exists to stop this unsafe jump:

```text
release executed -> deploy
```

The correct chain is:

```text
BB release execution receipt
-> BC deployment readiness separation package
-> future deployment readiness decision package
-> future controlled deployment executor
```

## Boundary

BC consumes BB release execution receipt.

Release execution is not deployment readiness.

Release execution receipt is not deployment authority.

Deployment readiness separation is not deployment readiness decision.

Deployment readiness decision is not deployment execution.

BC does not deploy.

BC does not publish packages.

BC does not promote memory.

BC does not continue workflow.

BC does not mutate source.

BC does not dispatch deployment pipelines.

BC does not mutate environments.

BC does not execute rollback.

A tag is not deployment.

A GitHub release is not deployment.

Uploaded release artifacts are not package publication.

## Acceptance

BC only produces `CanProceedToDeploymentReadinessDecision` when:

- the BB release execution receipt exists
- the BB receipt verdict is `ExecutedAndVerified`
- BB pre-state and post-state were verified
- the expected tag and GitHub release were created
- requested artifact upload, when present, completed
- repository, commit, version, tag, and channel match the requested package identity
- no deployment, package publication, memory promotion, workflow continuation, or rollback execution was attempted
- the BB receipt boundary carries no deployment, publication, memory, continuation, commit, push, source mutation, workspace mutation, rollback, review, or merge authority
- deployment target is explicitly declared
- deployment readiness scope is explicitly declared

## Non-Authority

`CanProceedToDeploymentReadinessDecision = true` means only that a future deployment-readiness decision package may consume the BC package.

It does not mean:

- deployment ready
- deploy now
- publish packages
- promote environment
- trigger pipeline
- continue workflow
- promote memory
- execute rollback

## Killjoy

A release execution receipt is not deployment readiness. BC keeps the release door and the deployment door separate.
