# BE Controlled Deployment Executor

Block BE consumes an eligible BD deployment-readiness decision package and an explicit deployment execution request.

BE is the controlled deployment executor. It re-observes deployment target state, deploys only the approved artifact to the approved target and environment, re-observes deployment state, writes a deployment execution receipt, records a governance event, and stops.

BE does not publish packages, promote memory, continue workflow, mutate source, commit, push, merge, create tags, create GitHub releases, dispatch arbitrary pipelines, or execute rollback.

## Boundary

BE consumes BD deployment-readiness decision package.
BC package is not deployment execution authority.
Release execution receipt is not deployment execution authority.
Deployment readiness decision package is not deployment execution by itself.
Deployment execution is not package publication.
Deployment execution is not workflow continuation.
Deployment execution is not memory promotion.
Deployment execution is not rollback execution.

## Authority

The BE executor boundary permits only this deployment surface:

```text
CanDeployApprovedArtifact = true
CanPublishPackages = false
CanPromoteMemory = false
CanContinueWorkflow = false
CanCommit = false
CanPush = false
CanMutateSource = false
CanMutateWorkspace = false
CanExecuteRollback = false
CanCreateTag = false
CanCreateGitHubRelease = false
CanDispatchPipeline = false
```

## Acceptance

BE requires:

- an eligible BD package with `PackageReadyForControlledDeploymentExecutor`
- `CanProceedToControlledDeploymentExecutor = true`
- an evidence-only BD package boundary carrying no deployment execution authority
- an explicit deployment execution request
- exact package/request identity binding
- exact repository, commit, version, tag, channel, target, environment, artifact name, and artifact checksum binding
- explicit `ConfirmDeploymentExecution = true`
- explicit approved action list
- only `DeployApprovedArtifact` in the approved action list
- deployment target observation before mutation
- successful target observation
- target not locked
- deployment not already in progress
- candidate artifact not already applied
- post-deployment observation and verification

BE deploys only the approved artifact to the approved target/environment.

Partial deployment is non-success.

Post-deployment verification failure is non-success.

No successful exit is allowed for partial deployment.

No automatic rollback is allowed.

No workflow continuation is allowed.

## CLI

```text
irondev deployment-execution execute --deployment-readiness-decision-package <deployment-readiness-decision-package.json> --request <deployment-execution-request.json> --out <path> --json
irondev deployment-execution inspect --receipt <deployment-execution-receipt.json> --json
irondev deployment-execution status --receipt <deployment-execution-receipt.json> --json
irondev deployment-execution records --receipt <deployment-execution-receipt.json> --json
```

Exit code `0` is reserved for `ExecutedAndVerified`.

Blocked, failed, partial, rejected, and post-verification-failed executions return non-zero.

Read-only commands emit a read-only boundary.

## Non-Authority

BE does not:

- publish packages
- mutate source
- commit
- push
- merge
- create tags
- create GitHub releases
- promote memory
- continue workflow
- execute rollback
- dispatch arbitrary pipelines

## Review Line

Block BE consumes an eligible BD deployment-readiness decision package and explicit deployment execution request, re-observes deployment target state, deploys only the approved artifact to the approved target/environment, post-verifies deployment state, writes a deployment execution receipt, and stops.

## Killjoy

BE is allowed to deploy exactly one approved artifact to exactly one approved target/environment.

It is not allowed to publish packages, promote memory, continue workflow, mutate source, or clean up its own mess with rollback.

A deployment mutation without post-state verification is not success.

A partial deployment is not governed success.
