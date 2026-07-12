# Project Access Sweep

**Status:** Canonical cleanup contract

**Programme slice:** CLN-16

## Access Boundary

Selected tenant scope does not imply project visibility. A project request is visible only when:

- the project row belongs to the selected tenant;
- the authenticated user is active;
- the user remains an active tenant member; and
- the user has active membership in that project.

`ProjectMembershipService` joins the project row when resolving membership. Orphaned, stale, or scope-inconsistent membership data cannot establish access.

## Artifact Routes

Project-qualified routes are checked before controller dispatch and must also bind route artifact IDs to that project. Route and body scope binding remains authoritative for writes.

Compatibility routes that identify only an artifact declare `RequireProjectArtifactAccess`. The guard resolves the artifact to its owning project and applies the same tenant and active-membership checks. Covered artifact families are:

- project documents and immutable document versions;
- tickets and ticket-derived implementation plans;
- project memory documents;
- database-backed runs; and
- file-backed run reports and evidence.

Artifact absence, malformed ownership, stale membership, and cross-project access all return the same inert not-found refusal. File-backed report lists are filtered to accessible projects rather than returning the unscoped store contents.

## Authority

Project visibility permits only the endpoint operation that independently authorizes the request. It grants no project administration, workflow continuation, approval, apply, filesystem, tool, or evidence authority.
