# IronDev Self-Improvement Goals 022-032

## Purpose

This branch is a batch branch for the next self-improvement proof set. It should not open a PR per proof slice. The PR should be opened only after the batch has a coherent tested path.

## Branch

`codex/self-improvement-goals-022-032`

## Rules

- Keep each goal evidence-first.
- Prefer narrow Test Agent plans over broad feature work.
- Do not mutate the production BookSeller repository unless a later goal explicitly allows it.
- Builder remains preview-first until write safety is proven again under BookSeller scope.
- Every project-scoped proof must report project identity.
- Every proof must have a validation plan.
- Code standards may pass with allowlisted warnings, but warning count and meaning must stay visible.

## Goal List

### 022: BookSeller Builder Preview Proof

Status: implemented in this branch.

Prove BookSeller builder preview is project-scoped, includes BookSeller source context, excludes IronDev/CODEX authority, and performs no file writes before approval.

### 023: BookSeller Test-After-Preview Proof

Status: implemented in this branch.

Prove the Test Agent can run a BookSeller-scoped validation plan after builder preview without applying code.

### 024: RetrieverAgent Real Path

Status: implemented in this branch.

Make RetrieverAgent wrap the existing memory search path and return structured project-scoped context packages.

### 025: Supervisor/Codex Loop Proof

Status: implemented in this branch.

Prove Codex/Supervisor can create a test plan, TesterAgent can execute it, and IronDev can return a repair-ready report.

### 026: BookSeller Vague Prompt Routing

Status: implemented in this branch.

Prove messy BookSeller prompts route to the right action instead of prose fallback when action intent is clear.

### 027: BookSeller Discussion-To-Document Proof

Status: implemented in this branch.

Prove a BookSeller discussion can become a project-scoped accepted or draft document with source trace.

### 028: BookSeller Document-To-Tickets Proof

Status: implemented in this branch.

Prove a BookSeller document can produce linked draft tickets with source document version IDs.

### 029: BookSeller Ticket-To-Builder Context Proof

Prove BookSeller tickets produce builder context packages with linked BookSeller source memory and without IronDev/CODEX bleed.

### 030: BookSeller Failure Package Proof

Prove a failed BookSeller dogfood run creates a Codex-readable failure package with project, prompt, expected/actual, trace, evidence, and validation command.

### 031: Batch Regression Pack

Create a single batch plan that runs the key 020-030 proofs and returns a compact pass/fail report.

### 032: PR-Ready Quality Gate

Run build, focused tests, format, package audit, code standards, and the batch regression pack. The branch is PR-ready only when these pass.

## Current First Slice

022 is implemented first because it is the next dangerous boundary after project-scoped memory and source links: the builder must prove preview-only behaviour under BookSeller scope before any later test-after-preview or write-path work.
