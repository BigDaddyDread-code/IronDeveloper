# Evidence Link Safety

**Status:** Canonical cleanup contract

**Programme slice:** CLN-17

## Stored References

An evidence or source reference is inert metadata until its complete scope is revalidated. New `ArtifactSourceReference` rows require:

- a supported artifact type and an existing artifact;
- a supported source type and an existing source;
- matching tenant and project ownership for artifact and source; and
- a positive source ID and named relationship type.

Reads require tenant, project, artifact type, and artifact ID. The former unscoped artifact lookup has been removed. Malformed or legacy cross-project rows are filtered from readback rather than becoming traceability evidence.

Project document links apply the same rule. Link insertion validates the exact immutable document version, supported target type, target existence, and matching tenant/project. Readback filters malformed stored rows.

## Navigation

Backend-provided audit, evidence, and governance routes are not redirects. The client accepts only a same-origin route recognized by the product router whose parsed project ID equals the current project. Absolute URLs, protocol-relative URLs, unknown routes, compatibility routes without project identity, and cross-project routes remain visible only as unavailable/inert state where appropriate.

## Evidence Files

Run evidence reads remain bounded to the validated run directory and require CLN-16 project-artifact access before dispatch. A stored path does not grant filesystem authority.

## Authority

A valid link proves only that one scoped record names another scoped record. It grants no visibility beyond current membership and no approval, continuation, apply, tool, process, network, filesystem, or memory authority.
