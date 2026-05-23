---
id: 20260522120300000-agent-capability-matrix
project: IronDev
title: AGENT_CAPABILITY_MATRIX
document_type: Architecture
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\AGENT_CAPABILITY_MATRIX.md
dogfood_run_id: AlphaTestPhase-094-103
created_utc: 2026-05-22T12:00:00.0000000+00:00
---

# Agent Capability Matrix

## Purpose

This document states what IronDev agents can actually do today.

Primary retrieval question: which IronDev agents are real vs stubbed?

## Capability Classes

- Real execution: performs an actual tool/control-plane action.
- Deterministic wrapper: produces structured deterministic output around existing commands.
- Memory wrapper: wraps memory search/context packaging.
- Stub: registered but not meaningfully intelligent yet.
- Unsafe/not allowed: intentionally blocked by Alpha safety boundary.

## Current Agents

| Agent | Current class | Current capability | Trust boundary |
| --- | --- | --- | --- |
| SupervisorAgent | Governed autonomous wrapper | Retrieves weighted memory, asks ConscienceAgent to review, asks ThoughtLedger to explain, and dispatches TesterAgent for bounded plans only when allowed. | Tier 3 read/test/report autonomy only; no writes, tickets, memory mutation, builder apply, or real repository changes. |
| PlannerAgent | Deterministic wrapper | Drafts Test Agent plan JSON from a goal. | Draft only; does not execute or patch. |
| ArchitectAgent | Stub | Registered as part of the eight-agent skeleton. | Do not trust for architecture decisions yet. |
| BuilderAgent | Stub/control-plane future | Builder path exists as preview safety smoke, not autonomous BuilderAgent execution. | No patch apply. |
| TesterAgent | Real execution wrapper | Runs Test Agent plans through CLI/PowerShell and returns structured report. | Executes plans; does not interpret or fix failures. |
| QualityAgent | Deterministic wrapper | Runs code standards/tooling gate through existing plan machinery. | Reports quality; does not refactor. |
| RetrieverAgent | Memory wrapper | Runs memory search and packages a weighted context bundle with included/rejected sources, ranking evidence, risk notes, and source guidance. | Uses dogfood memory search; not a full retrieval planner yet. |
| CriticAgent | Deterministic wrapper | Reviews failure packages and returns evidence-backed recommendation. | Reviews only; no code changes. |
| SentinelAgent | Deterministic wrapper | Observes campaign/failure/test evidence and emits insight artefacts. | Observational only; no tickets, memory writes, patches, or approvals. |
| ResearchAgent | Deterministic wrapper | Packages explicit external evidence as a ResearchPackage. | Read-only; does not decide architecture, write memory, create tickets, patch code, or override project memory. |
| ConscienceAgent | Deterministic safety reviewer | Reviews proposed actions and returns Allow, Block, or NeedsMoreEvidence. | Reviews only; no patching, ticket creation, memory mutation, or self-approval. |
| ThoughtLedger | Deterministic visible reasoning service | Explains visible reasoning summaries, uncertainty, blocked actions, and safer alternatives. | Explanation only; no hidden chain-of-thought, writes, patches, tickets, or memory mutation. |
| GovernedActionReview | Deterministic control-plane package | Combines ConscienceAgent and ThoughtLedger into one review package. | Reviews and explains only; does not execute the proposed action. |

## Blunt Assessment

The agent layer is a useful skeleton, not a mature autonomous system.

The safest real path today is:

`Codex -> memory search -> TesterAgent plan -> failure package -> Codex analysis`

## Boundary

No agent currently has permission to mutate project source files autonomously.


