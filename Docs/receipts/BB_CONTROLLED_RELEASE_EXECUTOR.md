# BB Controlled Release Executor

Block BB consumes an eligible BA release-readiness decision package and an explicit release execution request.

BB is the controlled release executor. It may create the expected tag, create the expected GitHub release, and upload the expected release artifacts after re-observing current release source, tag, release, and artifact state.

BB does not deploy, publish packages, promote memory, continue workflow, mutate source, commit, push, merge, approve, mark ready, request reviewers, or execute rollback.

## Boundary

Release readiness decision package is not release execution.
Release execution is not deployment.
Release execution is not package publication.
Release execution receipt is not deployment authority.
Release execution receipt is not package publication authority.
Release execution receipt is not workflow continuation authority.
No implicit tag creation through release creation.
No hidden deployment.
No hidden package publication.
No hidden memory promotion.
No hidden workflow continuation.

## Authority

The BB executor boundary permits only this release surface:

```text
CanCreateTag = true
CanCreateGitHubRelease = true
CanUploadReleaseArtifacts = true
CanDeploy = false
CanPublishPackages = false
CanPromoteMemory = false
CanContinueWorkflow = false
CanCommit = false
CanPush = false
CanMerge = false
CanMutateSource = false
CanMutateWorkspace = false
CanExecuteRollback = false
```

## Acceptance

BB requires:

- an eligible BA package with `PackageReadyForReleaseExecutor`
- `CanReleaseForExecutor = true`
- an evidence-only BA package boundary
- an explicit release execution request
- exact package/request identity binding
- exact repository, source branch, commit, version, tag, and channel binding
- explicit `ConfirmReleaseExecution = true`
- explicit approved action list
- current source observation before mutation
- no existing candidate tag
- no existing candidate GitHub release
- release notes for GitHub release creation
- artifact upload authority bound to the BA release artifact readiness evidence
- requested artifact names and checksums matching the BA artifact readiness evidence
- no requested artifacts outside the BA artifact readiness evidence
- expected local artifact files and checksum matches for artifact upload
- post-execution observation and verification

The action order is deterministic:

```text
CreateTag
CreateGitHubRelease
UploadReleaseArtifacts
```

GitHub release creation cannot satisfy tag creation. If a GitHub release is requested, explicit tag creation must also be requested and completed first.

Artifact upload cannot satisfy release creation. If artifact upload is requested, explicit GitHub release creation must also be requested and completed first.

Existing tags or releases block release execution. They are not treated as success.

Tag and release lookup failures fail closed unless the lookup is explicitly a not-found result.

Mutation-reported artifact names are recorded in the receipt, but they do not satisfy post-state verification. Expected release artifacts must appear in the observed post-state release assets.

Partial execution is visible and non-success. BB does not automatically rollback or continue workflow after partial execution.

## CLI

```text
irondev release-execution execute --release-readiness-package <release-readiness-decision-package.json> --request <release-execution-request.json> --out <path> --json
irondev release-execution status --receipt <release-execution-receipt.json> --json
irondev release-execution records --receipt <release-execution-receipt.json> --json
irondev release-execution inspect --receipt <release-execution-receipt.json> --json
```

Exit code `0` is reserved for `ExecutedAndVerified`.

Blocked, failed, partial, rejected, and post-verification-failed executions return non-zero.

## Review Line

Block BB executes a controlled release by creating only the expected tag, GitHub release, and release artifacts. It does not deploy, publish packages, promote memory, or continue workflow.
