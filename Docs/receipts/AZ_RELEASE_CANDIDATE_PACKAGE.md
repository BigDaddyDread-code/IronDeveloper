# AZ Release Candidate Package

Block AZ packages release-candidate readiness for an already merged commit using controlled merge execution evidence, current release source state evidence, release validation evidence, version evidence, release notes evidence, optional artifact manifest evidence, and explicit release-candidate decision evidence.

AZ is package-only. It does not create a tag, create a GitHub release, publish artifacts, deploy, promote memory, mutate source, commit, push, or continue workflow.

## Boundary

Merge execution is not release readiness.
Release candidate package is not release execution.
Release execution is not deployment.
Release is not deployment.
Validation evidence is not release authority.
Release notes are not release authority.
Version selection is not tag creation.
No hidden publication.
No hidden deployment.
No hidden workflow continuation.

## Authority

`CanReleaseForExecutor = true` only means a future BA release executor may consume the package.

The package boundary remains evidence-only:

```text
EvidenceOnly = true
CanRelease = false
CanDeploy = false
CanTag = false
CanPublish = false
CanPromoteMemory = false
CanContinueWorkflow = false
CanCommit = false
CanPush = false
CanMutateSource = false
CanMutateWorkspace = false
```

## Acceptance

AZ requires executed AY merge evidence, verified post-merge state, candidate commit identity, observed release source state, release validation evidence from executed result lanes, explicit version/tag evidence, release notes evidence, required artifact manifest evidence when applicable, an explicit release-candidate decision, and a valid release channel.

Result lane evidence proves validation execution. Required lane names only declare intent.

Packaging or regression validation lanes may be marked not applicable only with an explicit non-empty reason. A not-applicable lane without a reason does not satisfy required release validation.

## Review Line

Block AZ packages release-candidate readiness for a merged commit. It does not tag, release, publish, deploy, promote memory, or continue workflow.
