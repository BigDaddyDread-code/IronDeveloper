---
id: BOOKSELLER_SUPERVISED_DOGFOOD_REVIEW_117
project: IronDev
title: BookSeller Supervised Dogfood Review 117
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
source: C:\Users\bob\source\repos\AIDeveloper\Docs\BOOKSELLER_SUPERVISED_DOGFOOD_REVIEW_117.md
created_utc: 2026-05-22T22:55:00Z
primary_retrieval_questions:
  - What happens when Codex oversees IDA against BookSeller?
  - Can BookSeller prompts create documents, tickets, context, and safe disposable code changes?
  - What weaknesses remain before a real adaptive BookSeller build loop?
  - Should IronDev move toward a 10-iteration supervised BookSeller campaign?
boundary: This review proves the current deterministic dogfood harness and disposable cage. It does not prove a full adaptive LLM repair loop or real BookSeller product generation.
---

# BookSeller Supervised Dogfood Review 117

## Purpose

This checkpoint reviews the current IronDev/IDA dogfood system by running the strongest existing BookSeller and IronDev safety proofs together.

The question was:

```text
If Codex oversees IronDev, sends messy prompts, watches BookSeller docs/tickets/code evidence, resets, and repeats, what weaknesses do we see?
```

The short answer:

```text
The controlled proof spine is strong enough to start designing a supervised BookSeller campaign, but the true adaptive 10-iteration loop is not implemented yet.
```

## What Was Run

The review ran these plans and gates:

- `bookseller-project-smoke.json`
- `bookseller-messy-prompt-batch-smoke.json`
- `bookseller-retrieval-chaos-batch-smoke.json`
- `bookseller-builder-preview-smoke.json`
- `bookseller-disposable-workspace-apply-smoke.json`
- `bookseller-disposable-workspace-fail-closed-smoke.json`
- `bookseller-discussion-to-document-smoke.json`
- `bookseller-document-to-tickets-smoke.json`
- `bookseller-ticket-to-builder-context-smoke.json`
- `irondev-memory-reindex-freshness-smoke.json`
- `irondev-code-standards-alpha.json`
- `main-alpha-regression-pack.json`

All meaningful sequential runs passed.

One parallel execution attempt exposed a useful harness weakness: simultaneous builds/Test Agent plan runs can contend for compiler/object outputs. Future batch execution should serialize build steps or use isolated build output folders.

## Evidence Summary

### BookSeller Project Memory

`bookseller-project-smoke.json` passed.

Evidence:

- BookSeller architecture memory returned `BOOKSELLER_ARCHITECTURE_CURRENT`.
- `BOOK-001` returned `BOOKSELLER_TICKET_BOOK_001_ADD_BOOK_INVENTORY`.
- BookSeller test plan returned `BOOKSELLER_TEST_PLAN_CURRENT`.
- Reports remained scoped to `project = BookSeller`.
- Raw rank and final rank were reported.

This proves BookSeller is a real controlled project fixture, not an implicit default.

### Messy Prompt Routing

`bookseller-messy-prompt-batch-smoke.json` passed.

Evidence:

- Vague prompt `i dunno just make bookseller save stuff somewhre` produced a clarification-style response first.
- Clarified storage detail routed to `SaveDiscussionDocument`.
- `ok take that and make tickets todo the work pls` routed to `CreateMultipleDraftTickets`.
- `save this descusion for later so codex can find it` routed to `SaveDiscussionDocument`.
- `build BOOK-001 but dont write files ask first` routed to `BuildTicket`.
- Build remained preview-first with zero file writes.

This is good behaviour for messy human input.

Boundary:

These chat results are still deterministic-headless/dry-run. They do not yet prove production provider LLM behaviour across the same prompts.

### Discussion To Document

`bookseller-discussion-to-document-smoke.json` passed.

Evidence:

- Created SQL ProjectDocument.
- Created approved current ProjectDocumentVersion.
- Preserved source discussion link.
- Wrote semantic trace.
- Did not generate tickets or build code.

This proves the first part of the intent chain:

```text
discussion -> document/version -> source link -> semantic trace
```

### Document To Tickets

`bookseller-document-to-tickets-smoke.json` passed.

Evidence:

- Source document version was created.
- Three SQL tickets were generated.
- All tickets preserved `SourceDocumentVersionId`.
- All source version links resolved.
- No builder context or code writes happened.

This proves:

```text
document/version -> linked tickets
```

### Ticket To Builder Context

`bookseller-ticket-to-builder-context-smoke.json` passed.

Evidence:

- BookSeller ticket loaded.
- Source ProjectDocumentVersion resolved.
- Builder context included ticket and source document memory.
- Wrong-project memory was excluded.
- Orphan, missing version, wrong-project, and historical source cases failed or resolved with explicit status.

This proves:

```text
ticket -> source document version -> builder context package
```

It still does not prove code generation.

### Retrieval Chaos

`bookseller-retrieval-chaos-batch-smoke.json` passed.

Evidence:

- BookSeller queries containing tempting `Codex`, `self improvement`, and `memory spine` language still returned BookSeller sources.
- `BOOKSELLER_ARCHITECTURE_CURRENT`, `BOOKSELLER_TICKET_BOOK_001_ADD_BOOK_INVENTORY`, and `BOOKSELLER_TEST_PLAN_CURRENT` won as expected.
- IronDev/CODEX documents were rejected from BookSeller authority.

This is the right result: project scope beats semantic temptation.

### Builder Preview

`bookseller-builder-preview-smoke.json` passed.

Evidence:

- Builder proposal generated for BookSeller.
- Source context included.
- Approval gate blocked apply.
- Direct patch apply was blocked.
- File hashes stayed unchanged.

Boundary:

This is proposal safety only. It does not run build/test or apply patches.

### Disposable Workspace Apply

`bookseller-disposable-workspace-apply-smoke.json` passed.

Evidence:

- Patch proposal file was consumed.
- Workspace path was explicit and under `%TEMP%\IronDevDisposableWorkspaces`.
- Workspace was outside the real repo.
- Real repo stayed unchanged.
- Patch applied only inside the disposable workspace.
- Build passed.
- Tests passed.
- Comparison review reported scope match and no unsafe changes.
- Success package was produced.
- Human approval boundary remained intact.

This proves:

```text
proposal file -> disposable workspace apply -> build/test -> comparison -> success package
```

It still does not permit real repo writes.

### Disposable Fail-Closed

`bookseller-disposable-workspace-fail-closed-smoke.json` passed.

Evidence:

- Unsafe proposal attempted `..\..\outside-disposable-workspace.txt`.
- Apply was blocked with `blocked_path_outside_workspace`.
- Patch was not applied.
- Failure package was produced.
- Real repo stayed unchanged.

This is the most important safety proof in the set.

### Memory Reindex Freshness

`irondev-memory-reindex-freshness-smoke.json` passed.

Evidence:

- Stale version raw rank 1.
- BookSeller wrong-project candidate raw rank 2.
- Current IronDev version raw rank 3.
- Final rank promoted current IronDev version to 1.
- Stale version remained visible as demoted evidence.
- Duplicate count was 0.
- Exact title promotion worked.

This proves reindexing can keep current accepted memory fresh, visible, project-scoped, and idempotent.

### Code Standards

`irondev-code-standards-alpha.json` passed.

Evidence:

- ReplayRunner build passed.
- Focused router tests passed.
- Format check passed.
- Package audit found no vulnerable packages.
- Code standards gate passed with 4 warnings.

Warnings remain intentional:

- `Program.cs` is still large.
- `Invoke-TestAgentPlan.ps1` is still large.
- Some older memory spine command handlers remain allowlisted.

This is acceptable for now, but it is still real debt.

### Main Regression Pack

`main-alpha-regression-pack.json` passed 11/11.

This is the compact current safety net.

## What Is Strong Now

- BookSeller project memory is reliably retrievable.
- BookSeller/IronDev project scope separation is strong in tested paths.
- Messy prompt routing is action-first where action intent is clear.
- Discussion-to-document and document-to-tickets source linking is proven.
- Ticket-to-builder-context source memory inclusion is proven.
- Builder preview is approval-first and no-write.
- Disposable workspace apply can write inside the cage only.
- Unsafe patch paths fail closed.
- Reindex freshness/idempotency is proven.
- Failure/success packages are Codex-readable.
- Code standards remain visible rather than hidden.

## Weaknesses Found

### 1. No True Adaptive 10-Iteration Campaign Yet

The current system has many deterministic smoke plans and batch proofs.

It does not yet run:

```text
reset BookSeller
send generated messy prompt
observe docs/tickets/context/build evidence
Codex reviews weakness
adjust next prompt
reset
repeat 10 times
```

That is the next useful dogfood layer.

### 2. Prompt Flow Is Still Mostly Deterministic

The messy prompt batch is valuable, but it is not production LLM behaviour.

Provider-backed LLM traces, model costs, and real routing uncertainty are still not proven across this BookSeller loop.

### 3. Reset Is Not Yet A Full BookSeller Project Reset

Disposable workspace reset is proven.

The broader BookSeller/IDA project reset story is not complete:

- SQL project cleanup/reset.
- Weaviate collection/chunk cleanup.
- repeated prompt-run isolation.
- stale dogfood project cleanup in the app UI.

### 4. Parallel Test Runs Can Fight Over Build Outputs

A parallel build/Test Agent attempt failed with compiler/object file locking.

This means high-volume batches should either serialize build steps or isolate build output per run.

### 5. BookSeller Is Still A Fixture, Not A Growing App

The BookSeller fixture proves safety and traceability.

It does not yet prove IronDev can grow a realistic BookSeller app across many tickets, migrations, APIs, tests, and UI flows.

### 6. Broad Natural-Language Memory Queries Still Need Retriever Shaping

Exact title and project-scoped queries work well.

Broader natural-language memory questions can still prefer older umbrella documents in some cases. RetrieverAgent should eventually rewrite/expand queries with document intent, authority, and freshness constraints before handing them to raw search.

## Recommendation

Proceed to a supervised BookSeller dogfood campaign, but keep it bounded.

Recommended next slice:

```text
118: BookSeller Supervised Iteration Campaign
```

Goal:

Run 10 project-scoped BookSeller iterations where each iteration has:

- unique DogfoodRunId
- reset/isolation step
- generated messy prompt
- expected behaviour
- actual route/result
- memory evidence
- document/ticket/context/build evidence where relevant
- safety boundary confirmation
- Codex review note
- weakness classification

Do not let it apply to the real repo.

The campaign should produce one compact report:

```text
Docs/BOOKSELLER_SUPERVISED_ITERATION_CAMPAIGN_REPORT.md
```

## Suggested Iteration Set

1. Vague storage prompt.
2. Clarified storage decision.
3. Messy ticket creation.
4. Save discussion for future retrieval.
5. Retrieve BookSeller current architecture with tempting IronDev language.
6. Generate linked tickets from document.
7. Assemble builder context for one ticket.
8. Builder preview with no writes.
9. Disposable workspace apply using proposal file.
10. Unsafe write attempt and failure package.

## Go/No-Go

Current decision:

```text
GO for supervised 10-iteration BookSeller campaign.
NO-GO for real repo writes.
NO-GO for autonomous repair loop.
```

The system is ready to be exercised harder.

It is not ready to be trusted unsupervised.
