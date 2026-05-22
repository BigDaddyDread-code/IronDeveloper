# IronDev Self-Improvement Goals 044-053

## Purpose

This batch follows the merged 033-043 agent dogfood batch. The goal is to make the current agent loop more reliable and project-scoped without adding autonomous code application.

The slice stays evidence-first:

- Codex and agents must retrieve accepted IronDev or BookSeller memory before acting.
- PlannerAgent may draft plans, but does not execute or patch.
- SupervisorAgent may coordinate memory plus TesterAgent, but only inside the existing tiny decision set.
- CriticAgent may review failure packages, but does not patch code.
- Builder remains preview-first and approval-gated.
- QualityAgent remains the deterministic gatekeeper.

## Branch

`codex/self-improvement-goals-044-053`

## Rules

- Do not add autonomous code application.
- Do not broaden SupervisorAgent decisions beyond the narrow approved set.
- Do not make TesterAgent smart; it remains the boring executor.
- Keep BookSeller explicitly project-scoped.
- Every plan must return compact, structured evidence.
- Every proof must state what it proves and what it does not prove.
- Every project-scoped proof must report the active project.
- Code standards may pass with intentional warnings, but warnings must remain visible.

## Goal List

### 044: Batch Goal Pack And Memory Import

Create this 044-053 goal pack and add it to IronDev accepted dogfood memory so Codex can retrieve the current batch direction before editing.

### 045: IronDev Planner Memory-First Draft

Prove PlannerAgent can draft an IronDev-scoped Test Agent plan from a vague self-improvement goal, and that the draft starts with `memory_search` before any quality or builder action.

### 046: IronDev Supervisor Current Memory Loop

Prove SupervisorAgent can retrieve current IronDev batch memory and dispatch TesterAgent against a bounded memory smoke plan, producing a compact Codex handoff.

### 047: RetrieverAgent Current Batch Context

Prove RetrieverAgent can fetch the accepted 044-053 goal pack as current IronDev memory with guidance, source links, raw rank, final rank, and semantic trace evidence.

### 048: BookSeller Supervisor Builder Preview Loop

Prove SupervisorAgent can coordinate BookSeller memory retrieval plus the BookSeller builder preview smoke while preserving project scope and no-write boundaries.

### 049: BookSeller Planner Vague Goal Draft

Prove PlannerAgent can draft a project-scoped BookSeller test plan from a vague goal without executing the plan or generating code.

### 050: CriticAgent BookSeller Failed Batch Review

Prove CriticAgent can review a Codex-facing failure package generated from a failed BookSeller batch and return an actionable, evidence-backed recommendation.

### 051: BookSeller Failed Batch Handoff Regression

Keep the BookSeller failed batch handoff path green so Codex still receives repro command, validation command, evidence paths, likely areas, and safety rules.

### 052: 044-051 Batch Regression Pack

Run the key 044-051 proofs through TesterAgent as one compact regression pack.

### 053: PR-Ready Quality Gate

Run the 044-051 regression pack plus the Code Standards Alpha gate. The branch is PR-ready only when both pass.

## Boundary

This batch strengthens the existing dogfood loop. It does not prove autonomous coding, real LLM repair, or builder patch application. It proves Codex can ask memory, Planner can draft, Supervisor can coordinate, Tester can validate, Critic can review, and Quality can gate the work.
