# H02 Migration Runner Spike

## Status

Spike complete.

Recommendation: `RecommendDeferral`.

This is not adoption of DbUp or any other migration runner.

## Purpose

H02 evaluates whether DbUp, or an equivalent .NET migration runner, can fit IronDev's migration authority model without weakening the existing manifest, apply, verify, and migration-state boundaries.

A migration runner is not database authority.

Running scripts in order does not prove the database is safe.

## Current Chain

H01 defines migration state as evidence, not database authority. H02 preserves the H01 source-of-truth chain:

1. Migration manifest defines expected migration identity and order.
2. Migration scripts define intended database changes.
3. Apply execution attempts to run scripts.
4. Database verification proves expected objects, constraints, and procedures exist.
5. Migration state records evidence about what happened.

H02 must not reorder that chain. A runner can only participate in step 3 unless a later bounded contract explicitly says otherwise.

## Required Questions

### Can a runner execute the existing manifest model without replacing it?

Potentially, but only if the runner is fed the ordered entries from `Database/migrations.json`. The manifest must remain the source of migration identity and order. A future runner must not discover arbitrary scripts from folders, embedded resources, or naming conventions outside the manifest.

### Can execution be separated from verification?

Yes, but only if execution and verification remain two distinct phases. A future runner may report apply attempt evidence. Existing `Database/verify-migrations.ps1`, or a successor verifier, remains mandatory after execution.

### Can migration state remain evidence only?

Yes, if runner journal/state is treated as one evidence input and never as approval, verification, release readiness, deployment readiness, or schema safety.

### Can runtime/API/agent/workflow/frontend paths be kept out?

Yes, but only structurally. A future runner must live behind an explicit migration CLI, migration CI command, controlled migration runner, or administrative migration command. It must not be callable from API startup, normal API requests, normal CLI commands, agents, tools, workflow continuation, source apply, rollback, memory, or frontend paths.

### Can script hash, manifest order, journal/state records, and verification evidence be reconciled safely?

Not yet. H02 can define the requirement, but safe reconciliation needs at least a script-hash and manifest-order contract plus a migration-state schema contract before any runner is introduced.

## Comparison Table

| Option | What it gives us | Authority risk | Dependency risk | Fits manifest order? | Separates apply from verify? | Supports future H01 state model? | Runtime path risk | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Option A - Keep current PowerShell apply/verify path | Preserves the existing manifest-driven apply script and separate verifier. Avoids runner adoption while state/hash contracts are still unfinished. | Low. Existing split already avoids treating execution as verification, but still needs stronger state evidence. | Low. No new package. | Yes. Current apply path reads `Database/migrations.json`. | Yes. Apply and verify are separate scripts. | Partially. State persistence can be added after execution/verification without changing runner machinery. | Low if scripts remain explicit operator/CI tools only. | Keep as current baseline while H03 strengthens hash/order contracts. |
| Option B - Adopt DbUp through a bounded migration CLI | Mature .NET migration execution machinery that could be wrapped by a future explicit migration CLI or CI command. | Medium. Its journal can be mistaken for approval, verification, or readiness unless bounded hard. | Medium. Adds package selection, transaction-mode, SQL Server package, and maintenance risk. | Possible only if IronDev feeds scripts from the manifest and forbids independent discovery. | Yes only if existing verifier remains mandatory after execution. | Possible only if DbUp journal/state maps into H01 evidence rather than replacing it. | Medium-high unless API startup, agents, workflows, source apply, rollback, memory, and frontend paths are structurally excluded. | Defer. Keep DbUp as candidate after H03/H04 define hash/order and state contracts. |
| Option C - Custom minimal runner | Maximum control over manifest, hash, state, and verification sequencing. | Medium-high. Custom execution code can accidentally become database authority or duplicate safety logic badly. | Low external dependency, high internal maintenance. | Yes. It can be built around the manifest from the start. | Yes if designed that way. | Yes if designed around H01, but that is also the maintenance burden. | Medium unless isolated to a migration CLI/CI command. | Do not build yet. Reconsider only if DbUp cannot be bounded cleanly. |
| Option D - Equivalent runner, deferred | Avoids choosing execution machinery until evidence contracts are stronger. | Low. No runner means no new execution authority. | Low now. Later selection remains open. | Yes, because H03 can lock manifest/hash rules first. | Yes, because verification remains untouched. | Stronger long-term because state schema can be defined before any journal mapping. | Low now. Future runtime exclusion can be designed deliberately. | `RecommendDeferral`. |

## Option A - Current PowerShell Apply/Verify Path

The current path already preserves several important boundaries:

- `Database/migrations.json` is the ordered manifest.
- `Database/apply-migrations.ps1` performs apply execution.
- `Database/verify-migrations.ps1` performs verification separately.
- No package dependency is needed.
- Execution and verification remain separate observations.

What it fails to solve:

- No durable migration-state evidence model exists yet.
- Script hash drift is not yet formalized as a contract.
- Manifest-order reconciliation is not yet a first-class state input.
- Failed apply and failed verify evidence are not yet normalized into durable state records.

Judgement: keep this as the baseline until the hash/order and state contracts exist.

## Option B - DbUp Through a Bounded Migration CLI

DbUp remains a plausible future runner only if adopted through a bounded migration CLI, migration CI command, controlled migration runner, or explicit administrative migration command.

Required conditions before adoption:

- DbUp must consume IronDev's manifest order instead of discovering arbitrary scripts.
- DbUp journal/state is not IronDev approval.
- DbUp journal/state is not verification.
- DbUp journal/state is not release readiness.
- DbUp journal/state is not deployment readiness.
- DbUp journal/state is evidence only.
- DbUp must not create the database.
- DbUp must not discover arbitrary scripts outside `Database/migrations.json`.
- DbUp must not run from API startup.
- DbUp must not run from normal CLI commands.
- DbUp must not run from agents.
- DbUp must not run from workflow runner paths.
- DbUp must not run from source apply.
- DbUp must not run from rollback.
- DbUp must not run from memory paths.
- DbUp must not run from frontend paths.
- DbUp package selection must prefer SQL Server-specific package guidance if adopted later.
- DbUp transaction strategy must be explicit, not defaulted silently.
- Script hash drift must be detected before execution.
- Failed apply and failed verify must be recorded separately.
- Existing `Database/verify-migrations.ps1`, or a successor verifier, remains mandatory after execution.

Judgement: DbUp may fit later, but H02 must not adopt it. H03/H04 must define the hash/order and state boundaries first.

## Option C - Custom Minimal Runner

A custom runner can be made manifest-first and state-aware from day one, but it creates its own risks:

- It would duplicate mature runner behavior.
- It could mishandle transactions, retries, partial failures, or idempotency.
- It could drift into runtime database authority because the code is "ours."
- It would require more maintenance than a library-backed runner.

Custom code may preserve IronDev's manifest/apply/verify shape more naturally than a general library, but that advantage is not enough before the state and hash contracts are clear.

Judgement: do not build a custom runner now. Reconsider only after H03/H04 if DbUp cannot be bounded safely.

## Option D - Equivalent Runner Deferred

The runner decision is blocked by missing contracts, not by lack of execution machinery.

Evidence missing before adoption:

- script hash and manifest-order contract
- migration-state schema contract
- journal-to-state mapping rules
- failed apply and failed verify state rules
- transaction strategy rules
- allowed writer boundary for migration execution
- verification evidence resolver contract

Judgement: defer runner selection. Define the script-hash and manifest-order contract next.

## Recommendation

Recommendation: `RecommendDeferral`.

Why:

- The current apply/verify split already preserves the most important authority boundary.
- DbUp is plausible but not safe to adopt until manifest-order, script-hash, transaction, and journal/state reconciliation rules are explicit.
- A custom runner would create unnecessary execution-code risk before those same contracts exist.
- The safest next step is to define what a runner must prove before it can run.

This recommendation does not authorize implementation.

H03 should be: Migration script hash and manifest order contract.

Review line: A script hash is evidence, not safety.

Killjoy: A matching hash proves identity, not correctness.

## Boundary Rules

A migration runner is not approval.

A migration runner is not verification.

A migration runner is not release readiness.

A migration runner is not deployment readiness.

A migration runner is not schema safety.

A migration runner is not authority to create databases.

A migration runner is not authority to alter data outside approved migration scripts.

A migration runner is not authority to skip manifest checks.

A migration runner is not authority to skip script hash checks.

A migration runner is not authority to skip verification.

A migration runner is not authority to self-record success without external verification evidence.

A migration runner is execution machinery only.

## Explicit Non-Implementation

H02 does not install DbUp.

H02 does not add a package reference.

H02 does not add a runner project.

H02 does not add a console app.

H02 does not add SQL schema changes.

H02 does not add a migration-state table.

H02 does not add a migration journal table.

H02 does not add stored procedures.

H02 does not change `Database/migrations.json`.

H02 does not change `Database/apply-migrations.ps1`.

H02 does not change `Database/verify-migrations.ps1`.

H02 does not change existing SQL migration scripts.

H02 does not add CI workflow migration execution.

H02 does not add production Core, Infrastructure, API, CLI, UI, agent, workflow, source-apply, rollback, or memory code.

H02 does not connect to SQL.

H02 does not invoke PowerShell.

H02 does not execute a migration.

## Acceptance Notes

H02 evaluates runner options without installing anything.

H02 preserves H01's source-of-truth chain.

H02 explicitly says migration execution is not verification.

H02 explicitly says runner journal/state is evidence only.

H02 identifies the next implementation-safe slice as H03 migration script hash and manifest order contract.

## Killjoy Line

Running scripts in order does not prove the database is safe.
