# PR187 - Patch Artifact Contract

PR187 adds the Patch Artifact contract.

This PR is contract/test/receipt only.
This PR defines the patch artifact shape.
This PR does not create patch artifacts.
This PR does not persist patch artifacts.
This PR does not read patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add SQL.
This PR does not add API.
This PR does not add CLI.
This PR does not add UI.

## Boundary

Patch artifact is not source apply.
Patch artifact is not rollback.
Patch artifact is not workflow continuation.
Patch artifact is not release readiness.
Patch artifact does not authorize source mutation by itself.
Patch artifact is a proposed change package only.
Patch artifact must be reviewed before source apply.
Patch artifact must remain bound to its dry-run receipt and source baseline.

## Binding

Patch artifact binds dry-run receipt hash, dry-run audit hash, policy satisfaction hash, subject hash, source baseline hash, workspace boundary hash, validation plan hash, patch hash, change set hash, evidence references, and boundary maxims.

## Authority chain

The full authority chain remains:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate
```

PR187 stops at contract definition.

## Next target

The next Block S target is Patch Artifact Store.

Suggested next PR:

```text
PR188 - Patch Artifact Store
```

## Review line

PR187 defines the package. It does not ship or apply it.
