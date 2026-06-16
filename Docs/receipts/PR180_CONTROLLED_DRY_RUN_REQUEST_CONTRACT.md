# PR180 - Controlled Dry-run Request Contract

PR180 adds the Controlled Dry-run Request contract.

This PR starts Block R.

This PR is contract/test/receipt only.

This PR adds no SQL.
This PR adds no API.
This PR adds no CLI.
This PR adds no UI.

This PR does not execute dry-runs.
This PR does not create dry-run results.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.

## Authority chain

Current completed chain:

```text
accepted approval record -> policy satisfaction record
```

PR180 starts defining the next brick:

```text
policy satisfaction record -> controlled dry-run request
```

The full authority chain remains:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate
```

## Boundary

Policy satisfaction is an input to controlled dry-run request.
Policy satisfaction is not controlled dry-run execution.

Controlled dry-run request is not dry-run execution.
Controlled dry-run request is not a dry-run result.
Controlled dry-run request is not patch artifact creation.
Controlled dry-run request is not source apply.
Controlled dry-run request is not rollback.
Controlled dry-run request is not workflow continuation.
Controlled dry-run request is not release readiness.
Controlled dry-run request does not authorize execution by itself.

## What the request binds

The request binds:

- controlled dry-run request ID
- project ID
- policy satisfaction ID and hash
- subject kind, ID, and hash
- capability code
- workspace kind, ID, and boundary hash
- requested operation and operation hash
- validation plan kind, ID, and hash
- requested timestamp
- optional expiry timestamp
- correlation and causation IDs
- evidence references
- boundary maxims

The policy satisfaction hash, subject hash, workspace boundary hash, requested operation hash, and validation plan hash prevent the request from silently floating onto a different meaning later.

## Next target

The next Block R target is Controlled Dry-run SQL Store.

Suggested next PR:

```text
PR181 - Controlled Dry-run SQL Store
```

## Review line

PR180 defines the dry-run request slip. It does not run it.
