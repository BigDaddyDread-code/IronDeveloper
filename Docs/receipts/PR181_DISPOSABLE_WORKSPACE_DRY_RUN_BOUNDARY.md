# PR181 - Disposable Workspace Dry-run Boundary

PR181 defines the Disposable Workspace Dry-run Boundary.

This PR is receipt/test only.

This PR adds no production code.
This PR adds no SQL.
This PR adds no API.
This PR adds no CLI.
This PR adds no UI.

This PR does not create disposable workspaces.
This PR does not execute dry-runs.
This PR does not create dry-run results.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.

## Why this boundary exists

PR180 defined the dry-run request slip.

Before building the dry-run SQL store or runner, the workspace boundary must be nailed down.

A controlled dry-run without a workspace boundary becomes a hidden source-mutation path.

## Authority chain

Current completed chain:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run request
```

PR181 adds a receipt boundary around the next required cage:

```text
controlled dry-run request -> disposable workspace boundary -> controlled dry-run execution
```

The full authority chain remains:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate
```

## Dry-run workspace boundary

Future controlled dry-runs must run only inside disposable/caged workspaces.
The source workspace is not a dry-run workspace.

A dry-run request is not workspace creation.
A disposable workspace boundary is not dry-run execution.
Disposable workspace preparation is not patch artifact creation.
Disposable workspace preparation is not source apply.
Disposable workspace preparation is not rollback.
Disposable workspace preparation is not workflow continuation.
Disposable workspace preparation is not release readiness.
Disposable workspace preparation does not authorize execution by itself.

## Hard workspace rules

The disposable workspace must be isolated from the source workspace.
The disposable workspace must be reproducible from explicit inputs.
The disposable workspace must have a workspace boundary hash.
The disposable workspace must have a source snapshot reference.
The disposable workspace must have an allowed write root.
The disposable workspace must have a cleanup expectation.
The disposable workspace must not receive ambient source mutation authority.
The disposable workspace must not inherit hidden credentials.
The disposable workspace must not promote memory.
The disposable workspace must not activate retrieval.
The disposable workspace must not call models or agents by itself.

## Forbidden shortcut

Policy satisfaction does not imply workspace access.
Controlled dry-run request does not imply workspace access.
Workspace access must be granted by a future governed workspace preparation step.

No policy satisfaction record, dry-run request, memory entry, retrieved context, UI state, or agent confidence may grant workspace mutation authority.

## Future runner boundary

The future dry-run runner must consume an explicit controlled dry-run request.
The future dry-run runner must consume an explicit disposable workspace boundary.
The future dry-run runner must emit a dry-run result.
The future dry-run runner must not create a patch artifact directly.
The future dry-run runner must not apply source directly.

## Next target

The next Block R target is Controlled Dry-run SQL Store.

Suggested next PR:

```text
PR182 - Controlled Dry-run SQL Store
```

## Review line

PR181 draws the dry-run cage. It does not build or enter it.
