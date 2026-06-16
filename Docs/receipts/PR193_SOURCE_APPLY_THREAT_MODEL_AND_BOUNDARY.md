# PR193 - Source Apply Threat Model and Boundary Receipt

PR193 adds the Source Apply Threat Model and Boundary Receipt.

This PR is docs/receipt/test only.
This PR names source-apply footguns before executor work.
This PR does not add source apply.
This PR does not mutate source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add API.
This PR does not add CLI.
This PR does not add UI.
This PR does not add runtime workers or schedulers.

## Why this exists

Patch artifacts now exist as proposed packages.

The next dangerous boundary is source apply.

Source apply is mutation.
Patch artifact is not mutation.
Patch artifact read is not mutation.
Patch artifact creation is not mutation.
Patch base/hash validation is not mutation.

PR193 does not enter controlled source apply.
PR193 only names the source-apply threat boundary.

## Required source-apply footguns

The source-apply footguns are:

- wrong branch
- dirty worktree
- stale base
- missing rollback
- partial apply
- silent mutation
- validation bypass
- approval/policy drift

## Threat model

### 1. Wrong branch

Threat: Patch is applied to the wrong branch.

Boundary: Future source apply must verify the expected branch before mutation.

### 2. Dirty worktree

Threat: Patch is applied over uncommitted or unknown local changes.

Boundary: Future source apply must prove the worktree is clean or explicitly capture and reject dirty state.

### 3. Stale base

Threat: Patch artifact was validated against one source baseline but applied to another.

Boundary: Future source apply must verify source baseline hash before mutation.

### 4. Missing rollback

Threat: Patch applies, then validation fails, but there is no reliable recovery path.

Boundary: Future source apply must require rollback evidence before source mutation is considered safe.

### 5. Partial apply

Threat: Some files change, some fail, and the system reports success.

Boundary: Future source apply must detect partial apply and mark it as failure requiring rollback or human intervention.

### 6. Silent mutation

Threat: Source changes occur without an auditable apply record.

Boundary: Future source apply must produce a durable source-apply record before any downstream authority exists.

### 7. Validation bypass

Threat: Patch is applied without proving artifact integrity or post-apply correctness.

Boundary: Future source apply must require patch artifact validation, base/hash validation, and post-apply validation evidence.

### 8. Approval/policy drift

Threat: Approval or policy was valid when the patch was created, but no longer valid when source apply is attempted.

Boundary: Future source apply must re-check approval/policy binding before mutation.

## Future source apply requirements

Source apply must require explicit authority, clean source state, baseline match, rollback plan, validation proof, and durable apply evidence.

Those requirements are future requirements only. PR193 does not implement them.

## Authority chain

Current chain:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact
```

Full chain remains:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate
```

## Review line

PR193 names the dragon. It does not fight it.
