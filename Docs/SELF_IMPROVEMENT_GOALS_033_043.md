# IronDev Self-Improvement Goals 033-043

## Purpose

This branch is the next batch after the BookSeller dogfood loop in goals 022-032. The goal is to make the agents more operational without jumping into autonomous code application.

The PR should be opened only after the batch has a coherent tested path.

## Branch

`codex/self-improvement-goals-033-043`

## Rules

- Keep each goal evidence-first.
- Prefer narrow Test Agent plans over broad feature work.
- Do not add autonomous code application in this batch.
- Builder remains preview-first and approval-gated.
- Every agent proof must state what the agent decided, what it did not decide, and what evidence it used.
- Every project-scoped proof must report project identity.
- Every proof must have a validation plan.
- Code standards may pass with allowlisted warnings, but warning count and meaning must stay visible.
- The Test Agent remains the boring executor; Codex/Supervisor/Critic may interpret evidence, but only inside bounded proofs.

## Goal List

### 033: Batch Goal Pack

Status: implemented in this branch.

Create the 033-043 goal pack, branch rules, and build order so the next self-improvement batch has a clear boundary before implementation starts.

### 034: RetrieverAgent Context Bundle

Status: implemented in this branch.

Make RetrieverAgent return a richer context bundle instead of raw memory search shape only.

The bundle should include project, query, accepted sources, rejected or demoted sources where available, raw rank, final rank, source document/version IDs, excerpts, match reasons, trace ID, and a short "use this / treat as historical / ignore this" guidance field.

### 035: Supervisor Tiny Decision Loop

Status: implemented in this branch.

Give SupervisorAgent a tiny allowed decision set based on TesterAgent reports.

Allowed decisions should stay narrow:

- continue
- stop_on_failure
- request_failure_package
- request_retrieval_context
- report_ready

This should not become autonomous planning yet.

### 036: CriticAgent Failure Package Review

Status: implemented in this branch.

Make CriticAgent review a Codex-facing failure package and return a structured risk/recommendation report.

It should identify whether the failure is actionable, whether evidence is sufficient, likely area, and whether Codex should fix, ask for more evidence, or reject the run as invalid.

### 037: QualityAgent Real Path

Status: implemented in this branch.

Make QualityAgent wrap the existing code standards/toolchain gate and return a structured quality report.

This should reuse the existing deterministic checks rather than inventing LLM review.

### 038: PlannerAgent Test Plan Draft

Status: implemented in this branch.

Make PlannerAgent turn a bounded vague goal into a draft Test Agent plan.

The first version may be deterministic/template-backed, but it must produce valid plan JSON with goal, scope, commands/actions, expected checks, and stop conditions.

### 039: BookSeller Messy Prompt Batch

Status: implemented in this branch.

Create a batch of messy BookSeller prompts that tests route stability across vague, typo-heavy, shorthand, contradictory, and follow-up style user messages.

This should test behaviours, not exact assistant wording.

### 040: BookSeller Retrieval Chaos Batch

Status: implemented in this branch.

Create a BookSeller retrieval chaos batch with similar IronDev/BookSeller language, stale/current variants, and tempting wrong-project chunks.

The proof must show project and authority ranking still protect the result.

### 041: Codex Handoff From Failed Batch

Run a failing batch and prove the system produces a compact Codex handoff with failure package, evidence paths, repro command, validation command, and recommended next action.

### 042: Batch Regression Pack

Create a single batch plan that runs the key 033-041 proofs and returns a compact pass/fail report.

### 043: PR-Ready Quality Gate

Run build, focused tests, format, package audit, code standards, and the 033-041 batch regression pack. The branch is PR-ready only when these pass.

## Current First Slice

033 is implemented first because the agent loop can easily drift into vague "make it smarter" work. This document fixes the boundary: make agents more operational, but keep autonomy narrow, evidence-backed, and preview-first.
