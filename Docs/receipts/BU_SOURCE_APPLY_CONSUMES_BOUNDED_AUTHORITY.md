# BU — Source Apply Consumes Bounded Authority

## Review Line

Run authority can approve source apply only for the patch it actually governed.

## Boundary

This PR adds a source-apply authority decision only.

It does not apply source.
It does not execute commands.
It does not add a runner.
It does not mutate durable source.
It does not issue grants.
It does not store grants.
It does not create approvals.
It does not satisfy policy.
It does not run validation.
It does not create validation evidence.
It does not promote memory.
It does not continue workflow.
It does not add frontend/API/CLI.
It does not add commit, push, PR, merge, release, or deployment authority.

Source apply authority must bind repo, branch, run id, patch hash, file scope, and expiry.
Repository, branch, and run id scopes must be single explicit values; wildcard, `any`, or `all` scopes fail closed.
Run authority can approve source apply only for the patch it actually governed.

## Authority Paths

Accepted apply request evidence may satisfy source apply only when typed evidence binds repository, branch, run id, patch hash, file scope, expiry, and human principal.

Bounded run authority may satisfy source apply only when the grant explicitly allows `SourceApply`, excludes downstream operations, stops before commit/push/PR/merge/release/deployment/memory/workflow continuation, binds patch hash and file scope, and carries passed patch-bound validation evidence.

If both paths are present, they must describe the same source-apply authority scope. Conflicting authority paths fail closed.

## Killjoy

Source apply authority is patch-bound, scope-bound, run-bound, and expiry-bound. Anything looser is a bypass.
