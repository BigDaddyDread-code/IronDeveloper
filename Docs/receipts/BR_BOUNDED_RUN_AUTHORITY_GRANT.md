# BR Bounded Run Authority Grant

## Review Line

A bounded grant can open one marked door for one run; it cannot become a master key.

## Receipt

This PR adds a bounded run authority grant contract only.

It does not issue grants.
It does not execute commands.
It does not add a runner.
It does not mutate source.
It does not apply patches.
It does not create approvals.
It does not satisfy policy.
It does not run validation.
It does not create validation evidence.
It does not promote memory.
It does not continue workflow.
It does not add frontend/API/CLI.
It does not add source apply.
It does not create global authority.
It does not create cross-repo authority.
It does not accept memory-supplied authority.

A valid grant is necessary but not sufficient for any future operation.

## Boundary

The grant contract records repository, branch, run id, typed operation categories, file scope, expiry, mutation budget, declarative validation requirements, stop-before boundaries, grant source, and human-readable intent.

The contract does not issue the grant. It does not store the grant. It does not consume the grant to run anything. Human-readable intent is explanation only and is not parsed into authority.

Allowed operation kinds, file globs, and stop-before operation kinds are envelope data. A future operation must still independently validate profile ceiling, grant validity, repository, branch, run id, operation kind, file scope, expiry, mutation budget, validation evidence, stop-before boundaries, operation-specific governance, and current source state.

Required validation is declarative only. It does not run tests, prove tests passed, create validation evidence, create approval, or satisfy policy.

Granted-by evidence is a reference only. Memory, model output, agent output, UI state, historical receipts, and inferred approval cannot grant authority.

## Killjoy

A bounded grant can open one marked door for one run; it cannot become a master key.
