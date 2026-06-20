# BD Deployment Readiness Decision Package

## Purpose

Block BD consumes an eligible BC deployment-readiness separation package and packages an explicit deployment-readiness decision for the future controlled deployment executor.

BD exists to stop this unsafe jump:

```text
BC deployment-readiness separation package -> deployment execution
```

The correct chain is:

```text
BC separation package
-> BD deployment-readiness decision package
-> BE controlled deployment executor
```

## Boundary

BD consumes BC deployment-readiness separation evidence.

BC separation package is not deployment readiness decision.

Deployment readiness decision package is not deployment execution.

BD does not deploy.

BD does not publish packages.

BD does not promote memory.

BD does not continue workflow.

BD does not mutate environments.

BD does not mutate source.

BD does not execute rollback.

`CanProceedToControlledDeploymentExecutor` is not deployment execution.

## Acceptance

BD only sets `CanProceedToControlledDeploymentExecutor = true` when:

- an eligible BC deployment-readiness separation package exists
- the BC package verdict is `PackageReadyForDeploymentReadinessDecision`
- the BC package allows future deployment-readiness decision consumption
- the BC package boundary is evidence-only and carries no forbidden authority
- repository, commit, version, tag, channel, and deployment target match the BC package
- deployment environment is explicit
- deployment artifact name is explicit
- deployment artifact checksum is explicit
- an explicit deployment-readiness decision exists
- the decision is `ApprovedForControlledDeploymentExecutor`
- decision maker, decision time, and rationale are present
- the decision was made after the BC package was created
- the decision maker is not the BC package creator

## Non-Authority

`CanProceedToControlledDeploymentExecutor = true` means only that a future BE controlled deployment executor may consider this package eligible.

It does not mean:

- deploy now
- deployment executed
- publish packages
- promote memory
- mutate environments
- mutate source
- continue workflow
- execute rollback

## Killjoy

BD can say a human has decided this may proceed to a controlled deployment executor.

BD is not allowed to deploy one byte.
