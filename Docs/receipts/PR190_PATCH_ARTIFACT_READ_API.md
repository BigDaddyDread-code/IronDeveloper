# PR190 ? Patch Artifact Read API

PR190 adds the Patch Artifact Read API.

This PR exposes project-scoped read-only endpoints for persisted PatchArtifact records.

This PR does not create patch artifacts.
This PR does not validate patch artifacts as source-apply authority.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add CLI.
This PR does not add UI.

## Boundary

Patch artifact read API is read-only.
Patch artifact read API is not patch artifact creation.
Patch artifact read API is not source apply.
Patch artifact read API is not rollback.
Patch artifact read API is not workflow continuation.
Patch artifact read API is not release readiness.
Patch artifact read API does not authorize source mutation by itself.
Reading a patch artifact does not authorize source mutation.
Patch artifact must still be reviewed before source apply.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

## Next target

The next Block S target is Patch Artifact Creator.

PR191 - Patch Artifact Creator

## Review line

PR190 opens the package window. It does not ship or apply it.
