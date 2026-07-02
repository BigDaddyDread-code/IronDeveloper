# ADR-017: Migration State Tracking

## Status

Accepted.

## Context

IronDev already has database migration scripts, `Database/migrations.json`, `Database/apply-migrations.ps1`, `Database/verify-migrations.ps1`, and SQL CI evidence.

Those pieces can list intended migrations, run scripts, verify database objects, and prove configured SQL-backed lanes ran. They do not yet define what a durable migration-state record means.

Without that decision, a later migration-state table could drift into false authority: a row could be treated as release approval, deployment readiness, schema verification, runtime mutation permission, or permission to skip verification.

Migration state is evidence, not database authority.

## Decision

IronDev will track migration state separately from migration execution.

The future migration-state tracker must be durable, append-only or append-preferred, inspectable, and bounded to evidence about migration attempts and observations. It must not become the only source of truth.

The source-of-truth chain is:

1. Migration manifest defines expected migration identity and order.
2. Migration scripts define intended database changes.
3. Apply execution attempts to run scripts.
4. Database verification proves expected objects, constraints, and procedures exist.
5. Migration state records evidence about what happened.

The order matters. State follows execution and verification. State does not replace execution or verification.

## Non-goals

H01 does not create a migration-state table.

H01 does not create a C# migration-state model.

H01 does not create stored procedures.

H01 does not change `Database/migrations.json`.

H01 does not change `Database/apply-migrations.ps1`.

H01 does not change `Database/verify-migrations.ps1`.

H01 does not create a migration runner, migration lock, API endpoint, CLI command, UI, SQL schema change, or runtime writer.

## Migration State Model

The future model is conceptual in H01. Candidate future fields are:

- `MigrationId`
- `ManifestPath`
- `ScriptPath`
- `ScriptHash`
- `ManifestOrder`
- `DatabaseName`
- `DatabaseFingerprint`
- `AppliedAtUtc`
- `AppliedBy`
- `ApplyRunId`
- `ApplyAttemptId`
- `ExecutionStatus`
- `VerificationStatus`
- `VerificationEvidenceRef`
- `ErrorCode`
- `ErrorMessage`
- `PreviousStateHash`
- `StateRecordHash`
- `CreatedAtUtc`

The future model must be scoped to a database and migration manifest entry. Unknown migration IDs must fail closed. Duplicate migration IDs must fail closed. Changed script hash for a previously recorded migration must be treated as drift. Manifest order violations must be treated as drift.

## State Meanings

Allowed future statuses are:

- `Pending`: the migration is expected by the manifest and has not been observed as started.
- `Applying`: an apply attempt has started and has not yet produced a final observation.
- `Applied`: apply execution completed, but verification has not completed successfully.
- `Verified`: apply execution completed and database verification completed successfully.
- `Failed`: apply execution failed or could not be completed.
- `RolledBackByManualIntervention`: explicit manual intervention evidence says the migration effect was reversed or remediated.
- `Superseded`: later migration policy makes this state historical but not current.
- `Unknown`: supplied state cannot be trusted or classified.

Only `Verified` may be treated as evidence that both apply and verify completed.

Even `Verified` is not release approval.

## Boundary Rules

A migration state record is not release approval.

A migration state record is not deployment approval.

A migration state record is not merge approval.

A migration state record is not schema verification by itself.

A migration state record is not permission for runtime schema mutation.

A migration state record is not permission to skip `verify-migrations.ps1`.

A migration state record is not permission to skip verification.

A migration state record is not proof that production is safe.

A migration state record is not authority to apply the next migration.

A migration state record is not authority to roll back.

A migration state record is not authority to alter data.

A migration state record is evidence only.

## Failure Handling

Failed migration attempts must be recorded separately from successful verified migrations.

Partial success must not be recorded as `Verified`.

A failed apply may record an attempted state, but it must not mark the migration as `Applied` or `Verified`.

A failed verify may record `Applied` plus `VerificationFailedAfterApply` only if apply completed and verification failed.

Retried attempts must be separate attempts, not overwritten history.

Manual intervention must be explicit evidence, not hidden mutation.

## Manifest Relationship

A state record must refer to a manifest entry.

Unknown migration IDs must fail closed.

Duplicate migration IDs must fail closed.

Changed script hash for a previously recorded migration must be treated as drift.

Manifest order violations must be treated as drift.

The manifest remains the expected identity and ordering source. Migration state can report what happened against the manifest; it cannot change the manifest or substitute for it.

## Verification Relationship

Verification remains required.

Migration state can point to verification evidence.

Migration state cannot replace `verify-migrations.ps1`.

A database with state records but failed verification is not current.

A database with verified objects but missing state is not cleanly tracked.

Both cases are drift states.

Conceptual drift states include:

- `StateMissingButObjectsExist`
- `StateExistsButObjectsMissing`
- `ScriptHashChangedAfterApply`
- `ManifestOrderMismatch`
- `DuplicateStateRecord`
- `UnknownStateRecord`
- `VerificationFailedAfterApply`
- `ManualInterventionDetected`

## Runtime Prohibition

Runtime application services must not create or update migration state as a side effect of normal API, CLI, agent, tool, or workflow execution.

Allowed future writers are limited to:

- migration CLI
- migration CI script
- controlled migration runner
- explicit administrative migration command

Forbidden writers include:

- API request handlers
- agent runtime
- workflow runner
- tool execution path
- source apply executor
- rollback executor
- memory promotion path
- frontend
- background worker unless explicitly acting as migration runner

## Security and Audit Posture

Migration state must be audit evidence. It must record enough identity, hash, run, attempt, verification, error, and previous-state information for humans to inspect what happened and detect drift.

Migration state must not carry secrets, raw connection strings, database passwords, provider tokens, private reasoning, or unbounded logs.

Migration state must be written only by explicitly bounded migration actors in future slices. Runtime services may read migration state for diagnostics only after a read contract is defined; they must not infer database safety from it.

## Future Implementation Slices

Future slices may add:

- migration-state schema contract
- migration-state repository contract
- migration-state append writer for migration tooling
- migration-state read model
- script hash enforcement
- schema drift detector
- verification evidence resolver
- controlled migration runner
- administrative migration command

Future slices must still avoid treating migration state as release approval, deployment approval, schema verification replacement, runtime mutation permission, rollback authority, or authority to apply the next migration.

## Consequences

The next implementation work can build migration-state persistence without arguing about what the state means.

Any future migration-state table must preserve the source-of-truth chain: manifest, scripts, apply execution, database verification, then state evidence.

Any future writer must be explicit and bounded. Normal API, CLI, agent, tool, workflow, frontend, source apply, rollback, and memory paths must not write migration state as incidental work.

Any future reader must treat missing, unknown, duplicate, out-of-order, hash-drifted, or verification-failed state as drift requiring inspection.

## Killjoy Line

A recorded migration is not a safe database.
