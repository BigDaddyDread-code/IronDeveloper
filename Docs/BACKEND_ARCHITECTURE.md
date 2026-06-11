# Backend Architecture

PR 52 is documentation alignment, not architecture change.

No behavior change intended.
No schema semantics change.
No stored procedure result-shape change.
No SQL/API/CLI/UI/runtime/persistence/capability changes.

This document describes the current backend shape after PR 42-51.5 so the Backend Contract Freeze Report can freeze the real contract instead of a hoped-for one.

## Current phase

Block E is backend consolidation and cleanup. No new backend capability is allowed until PR 56, the Backend Contract Freeze Report, passes.

The current work is making the backend reviewable:

- inventories describe SQL, inline SQL, naming, test fixtures, and entity/table ownership;
- guard tests pin important boundaries;
- known debt is named instead of being hidden;
- production behavior is intentionally unchanged.

## Core invariants

- SQL remains the source of truth.
- Vector/index/retrieval is retrieval only.
- Retrieval match is not a memory candidate.
- Candidate is not memory.
- Proposal is not apply.
- Audit is not approval.
- Gate is not executor.
- Critic is not governance.
- Memory safe is not approval.
- Tool request is a request form, not execution permission.
- Model output is advisory only.
- Human review remains required for source apply and memory promotion.

## Backend purpose

The backend owns governed project state, agent evidence, memory evidence, tool/audit records, and workspace apply evidence. Its job is to preserve accountability and safe boundaries around agent-assisted work.

The backend is not an autonomy engine. It does not turn model output, critic output, memory retrieval, audit records, or proposal records into authority. Authority still comes from explicit governed flows and human review where required.

## Source-of-truth model

SQL remains the source of truth for governed backend state. SQL tables, append-only events, triggers, constraints, and stored procedure boundaries protect memory, audit, proposal, and execution-evidence records.

File-backed workspace evidence is also part of the backend contract for disposable workspace flows. It is evidence, not automatic approval.

Vector, index, and retrieval systems are lookup accelerators only. They can return retrieval matches. They cannot become truth, approval, promotion, or source-mutation authority.

## Memory and retrieval model

Agent local memory is scoped by tenant, project, campaign, run, and agent. Local memory is append-only and influence must be explicit and auditable.

Collective memory has separate proposal, aggregation, review, and promotion concepts. Candidate is not memory. A memory proposal is not promotion. Retrieval match is not a memory candidate.

Memory safety results and retrieval confidence can inform review. They do not approve actions, satisfy policy approval, promote memory, or authorize source mutation.

## Proposal, review, and apply model

Proposals are suggested changes. They do not apply source changes.

Workspace apply evidence follows a governed chain: workspace metadata, validation, diff, promotion package, immutable approval evidence, apply preflight, apply dry-run, apply-copy, apply verification, post-apply validation, and source report.

Only the controlled apply-copy path may mutate source, and only for approved add/modify copy operations. Delete support, Git commits, pull requests, and automatic promotion remain outside this contract.

Human review remains required for source apply and memory promotion.

## Tool request, gate, audit, and execution model

Tool request is a request form, not execution permission.

The gate evaluates whether a path is allowed or blocked. Gate is not executor.

Tool execution audit records evidence after supported manual tool execution paths. Audit is not approval and is not execution.

Tester and implementation manual paths remain bounded by typed requests, gate evidence, audit envelopes, and output validators. Patch proposal remains proposal-only.

## Critic, model, and governance boundaries

Critic output is review/advice. Critic is not governance.

Model output is advisory only. Model-backed manual agents use adapter and sanitisation boundaries, reject raw/private reasoning, and produce typed review-only or proposal-only output.

Governance boundaries are explicit contracts and evidence checks. They are not inferred from prose, model output, critic findings, memory retrieval, audit records, or plan text.

## Dogfood harness role

The dogfood harness proves governed manual flows can be assembled without granting autonomy. It packages evidence for human review. It does not apply patches, rerun tests automatically, mutate source by itself, create pull requests, promote memory, or create authority.

## SQL inventory role

The SQL and entity/table inventories support contract freeze by naming the current persistence surface. They do not redesign schema.

The supporting inventories are:

- [BACKEND_SQL_INVENTORY.md](BACKEND_SQL_INVENTORY.md)
- [BACKEND_INLINE_SQL_INVENTORY.md](BACKEND_INLINE_SQL_INVENTORY.md)
- [BACKEND_ENTITY_TABLE_INVENTORY.md](BACKEND_ENTITY_TABLE_INVENTORY.md)
- [BACKEND_TEST_FIXTURE_INVENTORY.md](BACKEND_TEST_FIXTURE_INVENTORY.md)
- [BACKEND_NAMING_INVENTORY.md](BACKEND_NAMING_INVENTORY.md)

The architecture document explains the boundaries. The inventories carry detailed ownership and cleanup evidence.

## Test fixture and support role

Backend test fixtures exist to keep boundary proof readable. Shared fixtures are allowed when they remove duplicate setup without hiding dangerous states. SQL-heavy fixture helpers may centralize dependency-order setup or teardown, but they must not weaken constraints, bypass database guards, or mask cleanup failures.

## Backend Boundary Map

| Concept | What it does | What it does not do |
| --- | --- | --- |
| SQL | Stores authoritative state | Does not infer authority |
| Vector/index | Retrieves matches | Is not truth |
| Retrieval match | Lookup result | Is not memory candidate |
| Memory candidate | Possible memory item | Is not persisted memory |
| Memory proposal | Reviewable change | Is not promotion |
| Memory safety result | Advisory classification | Is not approval |
| Promotion | Accepted persistence step | Is not automatic |
| Proposal | Suggested change | Does not apply source |
| Source apply | Mutates source | Requires approval |
| Audit | Records evidence | Does not approve |
| Gate | Blocks/allows path | Does not execute |
| Critic | Reviews/advises | Does not govern |
| Tool request | Request form | Not execution permission |
| Model output | Advisory | Not authority |

## PR 42-51.5 architecture delta summary

### PR 42 - Tool Execution Audit Store

Landed: durable append-only tool execution audit records for supported manual tester and implementation proposal paths.

Protects: audit/evidence separation from approval and execution.

Does not: execute tools, apply patches, mutate source, promote memory, or expose API/CLI.

### PR 43 - Manual Ticket Review to Critic to Fix Proposal Loop

Landed: manual loop that turns ticket evidence into critic review and patch proposal output.

Protects: critic review and patch proposal stay advisory.

Does not: submit GitHub reviews, apply fixes, mutate source, or create autonomous runtime behavior.

### PR 44 - Test Failure to Critic to Repair Proposal Loop

Landed: manual loop that turns test failure evidence into critic review and repair proposal output.

Protects: repair proposal remains separate from repair execution.

Does not: rerun tests, apply patches, mutate source, create pull requests, or promote memory.

### PR 45 - Real-run Memory Improvement Detection

Landed: manual workflow that detects possible memory improvements from real run evidence.

Protects: candidate/proposal/promotion separation.

Does not: auto-promote memory, create collective memory, write Weaviate, or change source.

### PR 46 - Manual Dogfood Harness

Landed: manual dogfood harness for exercising governed backend paths and collecting evidence.

Protects: dogfood evidence remains evidence.

Does not: grant autonomy, bypass gates, mutate source by itself, or create authority.

### PR 47 - Backend Dead Code and Redundant Contract Sweep

Landed: cleanup of stale backend wording, dead markers, and redundant contract language.

Protects: reviewed backend contract language from stale placeholders.

Does not: add capability, change runtime wiring, change persistence, or weaken tests.

### PR 48 - Agent/Memory Naming Normalisation

Landed: retrieval output vocabulary changed from candidate to retrieval match where safe.

Protects: retrieval match, memory candidate, proposal, and promotion separation.

Does not: change retrieval behavior, SQL meaning, API/CLI/UI, or memory promotion.

### PR 49 - Test Fixture Consolidation

Landed: shared backend test fixtures for repeated manual tool request, gate, and audit setup.

Protects: boundary proof stays visible while duplicate setup shrinks.

Does not: remove boundary tests, auto-approve, auto-execute, auto-apply, or auto-promote.

### PR 50 - SQL Schema and Stored Procedure Cleanup Pass

Landed: SQL inventory and FK-safe integration reset ordering cleanup.

Protects: SQL source-of-truth visibility before contract freeze.

Does not: evolve schema, change stored procedure shapes, or remove SQL artifacts.

### PR 51 - Remove Inline SQL and Runtime DDL Leftovers

Landed: inline SQL/runtime DDL inventory and centralized agent-memory schema cleanup helper for tests.

Protects: SQL cleanup debt is visible and the AgentMemoryIndexEvent teardown failure is fixed safely.

Does not: fully remove runtime DDL, change runtime bootstrap behavior, or alter schema semantics.

### PR 51.5 - Entity/Table Contract Inventory and Cleanup

Landed: entity/table ownership inventory and guard tests for inventory boundaries and hidden Unicode.

Protects: persistence concept ownership and risky/uncertain artifact visibility before freeze.

Does not: rename entities/tables, drop uncertain artifacts, change schema, or change behavior.

## Known Backend Debt Before Contract Freeze

| Issue | Why it matters | Blocks PR 56? | Planned follow-up or freeze exception |
| --- | --- | --- | --- |
| Stored manual agent DI construction issue in API lane | Broad API host tests cannot construct `StoredManualIndependentCriticAgentService` and `StoredManualMemoryImprovementAgentService`. | Yes, unless fixed or explicitly listed as a freeze exception. | Separate API DI cleanup PR before PR 56, or named freeze exception if not fixed. |
| Remaining broad architecture/governance red lanes | Existing tests around high-impact approval, architecture boundaries, UTC time, chat context, memory boundary harness, L4 report, boxed-agent runtime scans, and specialist profile scans remain red in broad runs. | Yes, unless each is fixed or explicitly classified before freeze. | Separate focused cleanup PRs; PR 56 must list exact status. |
| Legacy runtime DDL/bootstrap ownership exceptions from PR 51 | Runtime services still contain legacy table/column bootstrap DDL. Leaving it ambiguous weakens migration ownership. | Yes, if not classified. | PR 52 or later cleanup may remove runtime bootstrap DDL only if behavior is preserved; otherwise freeze exception. |
| Intentionally ugly names left from PR 51.5 | Names such as authority filter candidate flags and gate decision grants can be misread as approval/execution authority. | No if documented; yes if undocumented. | Keep documented in `BACKEND_ENTITY_TABLE_INVENTORY.md` and `BACKEND_NAMING_INVENTORY.md`; revisit after freeze only with contract migration plan. |
| SQL/entity artifacts marked uncertain in inventories | Semantic-memory and legacy retrieval tables may still be active through older runtime paths. Unsafe deletion could break behavior. | No if left in place and documented; yes if deletion is attempted without proof. | Leave in place for PR 56; handle after freeze with dedicated usage proof. |
| Full solution broad lanes still failing | Broad red lanes hide regressions if they are not named. | Yes unless documented and triaged. | PR bodies must keep exact failing groups and reasons until lanes are green. |

## Freeze posture

PR 56 should freeze only contracts that are documented, tested, and honest about debt. A red lane or uncertain artifact is not automatically fatal, but it must be named. A hidden authority path, hidden runtime DDL ownership issue, hidden SQL artifact, or hidden model/memory approval path should block freeze.

## Explicit non-goals

This document does not introduce backend services, service registrations, runtime behavior, SQL schema, stored procedures, DTO contracts, API endpoints, CLI commands, UI behavior, memory promotion, approval logic, source apply logic, tool execution logic, vector/index behavior, or entity/table renames.

If this document reveals a real bug, split the bug into a follow-up PR. Do not fix it inside PR 52.
