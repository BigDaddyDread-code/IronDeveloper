# Codex Goal Pack

> **Status: Parking lot.** This historical goal pack is not an active implementation plan. Current cleanup work is governed by [Cleanup and Product Completion Plan](product/IRONDEV_CLEANUP_AND_PRODUCT_COMPLETION_PLAN.md).

## Purpose

Codex needs explicit goals when driving IronDev through the Test Agent and CLI. The goal pack prevents broad, unfocused "improve IronDev" work and keeps each loop measurable.

## Mission

Use the IronDev CLI and Test Agent to prove IronDev can correctly preserve project intent from discussion to document to ticket to builder context.

## Hard Rules

- Do not change UI first.
- Do not optimise for one happy-path test.
- Do not write code unless a failing behaviour is proven by evidence.
- Prefer fixing traceability, retrieval, and testability before adding features.
- Every proposed fix must include observed failure, evidence, likely cause, files to change, validation command, and regression test.
- SQL remains source of truth.
- Weaviate/local retrieval stores are retrieval/index layers only.
- Test Agent executes; Codex diagnoses and repairs.

## Stop Conditions

Stop the current loop when:

- setup is invalid
- the failure package is incomplete
- the same failure repeats without new evidence
- the proposed fix would require broad feature work outside the goal scope
- writes would affect a non-dogfood/non-sandbox project

## Goal 1: Retrieval Correctness

Goal id: `retrieval-correctness-001`

Prove IronDev retrieves the right memory for its own project before trusting it to build anything.

### Scenario

Create or use:

- current IronDev architecture document
- stale IronDev architecture document
- similar BookSeller architecture document
- ticket or query about builder safety rules

Expected retrieval:

- current IronDev document
- correct project
- correct document version
- correct authority level
- source link present
- stale/superseded chunks excluded or ranked lower
- no cross-project bleed
- trace explains why each chunk was selected

Current smoke slices:

- `irondev-memory-spine-smoke.json` proves Weaviate health and local dogfood document ranking evidence.
- `irondev-memory-spine-sql-version-smoke.json` proves a SQL-backed project document version can be indexed into semantic memory tables, linked to source context, ranked above a stale version, and traced.
- `irondev-memory-spine-weaviate-sql-version-smoke.json` proves SQL-backed chunks can be upserted into Weaviate and returned by a real vector query while final authority ranking still corrects stale raw vector preference.
- `irondev-memory-spine-cross-project-smoke.json` proves raw Weaviate retrieval can prefer a similar BookSeller document while IronDev project context rejects it before final ranking.
- `irondev-memory-spine-ticket-source-link-smoke.json` proves a real SQL `ProjectTicket` created from a project document preserves and resolves the exact `ProjectDocumentVersion` source link. It also proves missing source links are reported as orphan failures. It does not prove builder context yet.
- `irondev-memory-spine-builder-context-source-smoke.json` proves builder context assembly includes the ticket, the linked source `ProjectDocumentVersion`, source document metadata, safe source markdown excerpt, and source link evidence. It also proves orphan, missing-version, wrong-project, and historical-source controls fail or mark context cleanly. It does not prove code generation or patch application.

## Goal 1B: Code Standards Alpha

Goal id: `code-standards-alpha-009`

Before adding more memory-spine ribs, prove the Test Agent can run a deterministic quality gate and return structured findings for Codex.

Expected:

- build passes
- focused router tests pass
- format check passes
- package audit passes
- large harness/code-shape warnings are reported, not hidden
- proof-boundary docs exist for the dogfood slices

Warnings are allowed at this stage. The purpose is to prevent silent codebase drift and identify extraction candidates before broadening the branch.

### Failure Examples

- old architecture outranks current decision
- BookSeller context appears in IronDev result
- retrieved context has no source document/version
- trace cannot explain why a chunk was selected

## Goal 2: Trace And Replay Quality

Goal id: `trace-quality-001`

Verify each major IronDev decision leaves enough evidence to debug.

Required evidence:

- input
- route decision
- prompt
- retrieved memory
- selected chunks
- model/provider/role
- response
- parsed output
- created document/ticket/proposal
- errors
- time/cost where available

## Goal 3: Chat To Document To Ticket Integrity

Goal id: `chat-doc-ticket-integrity-001`

Start with a vague discussion and verify user intent survives formalisation.

Expected:

- discussion document created
- architecture/decision points extracted
- tickets generated
- tickets link to source document/version
- builder context includes source document
- assumptions/open questions are marked
- no invented requirements are treated as facts

## Goal 4: Builder Proposal Safety

Goal id: `builder-proposal-safety-001`

Verify builder workflows remain proposal-first.

Expected:

- no file writes before approval
- proposal lists affected files
- proposal explains why each file changes
- risks are stated
- test plan is included
- project standards are respected
- rejection/cancel is safe

Any uncontrolled write is a critical failure.

## Goal 5: Focused Regression Coverage

Goal id: `focused-regression-coverage-001`

Use coverage and behavioural failure reports to add one missing regression test at a time.

Each new test must protect a real behaviour or previous failure.

## Goal 6: Cost Control

Goal id: `cost-control-001`

Every run should report:

- run id
- model calls
- estimated cost when available
- duration
- CLI commands run
- failures found
- useful failures
- wasted runs

Prefer fewer high-quality tests over hundreds of noisy tests.
