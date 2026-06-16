# PR184 - Dry-run Receipt Store

PR184 adds the Dry-run Receipt Store.

This PR persists supplied controlled dry-run execution audit receipts.

This PR does not execute dry-runs.
This PR does not create dry-run audits from executor output.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add API.
This PR does not add CLI.
This PR does not add UI.

## Why this exists

PR183 defined the dry-run execution audit contract.

PR184 puts that audit evidence into durable append-only SQL storage.

The store persists supplied audit receipts only. It does not execute the dry-run, create the audit from executor output, package a patch artifact, or spend the receipt as authority.

## Boundary

Persisted dry-run receipt is not dry-run execution.
Persisted dry-run receipt is not patch artifact creation.
Persisted dry-run receipt is not source apply.
Persisted dry-run receipt is not rollback.
Persisted dry-run receipt is not workflow continuation.
Persisted dry-run receipt is not release readiness.
Persisted dry-run receipt does not authorize source mutation by itself.
Dry-run receipt storage records evidence only.

## Authority chain

Current chain:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run request -> disposable workspace boundary -> controlled dry-run execution report -> dry-run execution audit
```

PR184 adds:

```text
dry-run execution audit -> durable dry-run receipt
```

The full authority chain remains:

```text
accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate
```

PR184 stops before patch artifact creation.

## Storage rule

The receipt store is append-only.

It has no update path, delete path, overwrite path, or upsert path.

If revocation or supersession is needed later, it must be a separate record in a future PR.

## Safety rule

The SQL store validates `ControlledDryRunExecutionAudit` before saving.

The SQL layer also rejects direct unsafe material and authority claims in receipt text or JSON fields.

The receipt may contain sanitized summaries and hashes. It must not contain raw prompts, raw completions, raw tool output, chain-of-thought, private reasoning, hidden reasoning, scratchpad, system prompts, developer prompts, passwords, API keys, secrets, private keys, or bearer tokens.

## Next target

The next Block R target is Dry-run Receipt Read API.

Suggested next PR:

```text
PR185 - Dry-run Receipt Read API
```

## Review line

PR184 puts the cage-run receipt in the vault. It does not package or spend it.