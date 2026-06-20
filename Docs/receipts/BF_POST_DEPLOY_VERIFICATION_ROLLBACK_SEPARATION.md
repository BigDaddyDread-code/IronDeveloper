# BF Post-Deploy Verification / Rollback Separation

Block BF consumes a BE deployment execution receipt and explicit post-deployment observation evidence.

BF packages post-deploy verification and rollback separation evidence. It can say the deployment remains verified, or that rollback should be considered by a future decision package.

BF does not rollback, decide rollback, deploy again, retry deployment, publish packages, promote memory, continue workflow, mutate environments, mutate source, commit, push, merge, create tags, create GitHub releases, or dispatch pipelines.

## Boundary

BF consumes BE deployment execution receipt.
Deployment execution is not post-deployment verification.
Post-deployment verification is not workflow continuation.
Post-deployment verification is not memory promotion.
Post-deployment verification is not package publication.
Failed verification is not rollback approval.
Rollback consideration is not rollback decision.
Rollback decision is not rollback execution.
BF does not rollback.
BF does not deploy again.
BF does not retry deployment.
BF does not publish packages.
BF does not promote memory.
BF does not continue workflow.
BF does not mutate source.
BF does not mutate environments.
BF does not dispatch pipelines.
CanProceedToRollbackDecision is not rollback execution.

## Acceptance

BF requires:

- a BE deployment execution receipt
- post-deploy observation evidence
- receipt identity matching repository, commit, version, tag, channel, target, environment, artifact name, and artifact checksum
- receipt boundary with no package-publication, memory-promotion, workflow-continuation, rollback, source-mutation, environment-mutation, commit/push, tag/release, or pipeline-dispatch authority
- observation identity matching repository, commit, version, tag, channel, target, and environment
- observation source and timestamp

For `DeploymentVerified`, BF requires:

- BE receipt verdict is `ExecutedAndVerified`
- BE receipt pre-state and post-state were verified
- BE receipt attempted and accepted deployment
- observed version matches expected version
- observed commit matches expected commit
- observed artifact name matches expected artifact name
- observed artifact checksum matches expected artifact checksum
- health check succeeded with an explicit summary

For `RollbackConsiderationRequired`, BF may set `CanProceedToRollbackDecision = true` only when deployment verification fails, deployment was partial/failed, post-state is unverified, post-deploy observation fails, observed deployment state mismatches expected state, health check fails, or deployment state is uncertain.

That flag means only that a future rollback decision package may consider the evidence.

It does not mean:

- rollback approved
- rollback requested
- rollback executed
- deployment retried
- workflow continued

## CLI

```text
irondev post-deploy-verification package --deployment-execution-receipt <deployment-execution-receipt.json> --observation <post-deploy-observation.json> --repo <owner/name> --candidate-commit <sha> --version <version> --tag <tag-name> --channel <internal|preview|release-candidate|stable|hotfix> --deployment-target <target-name> --deployment-environment <environment> --artifact-name <artifact-name> --artifact-sha256 <sha256> --created-by <github-login> --out <path> --json
irondev post-deploy-verification inspect --package <post-deploy-verification-package.json> --json
irondev post-deploy-verification status --package <post-deploy-verification-package.json> --json
irondev post-deploy-verification records --package <post-deploy-verification-package.json> --json
```

Exit code `0` is reserved for `DeploymentVerified` or `RollbackConsiderationRequired` package creation.

`RollbackConsiderationRequired` exits `0` only because the evidence package was created successfully. It is not rollback success and not rollback approval.

Blocked, incomplete, invalid, and inconsistent package states return non-zero.

Read-only commands emit a read-only boundary.

## Phase 5 Close

Phase 5 closes only when BF proves:

```text
release execution did not become deployment readiness
deployment readiness separation did not become deployment decision
deployment decision did not become deployment execution
deployment execution did not become rollback execution
post-deploy verification did not become workflow continuation
failed verification did not become automatic rollback
```

## Review Line

Block BF consumes a controlled deployment execution receipt and post-deployment observation evidence, packages post-deploy verification and rollback separation evidence, and proves deployment verification is not rollback execution. It does not deploy, rollback, publish packages, promote memory, continue workflow, mutate environments, mutate source, or dispatch pipelines.

## Killjoy

BF can say:

```text
deployment verified
```

or:

```text
rollback should be considered
```

BF cannot rollback.

Rollback execution is another mutation boundary and must earn its own decision package and executor.
