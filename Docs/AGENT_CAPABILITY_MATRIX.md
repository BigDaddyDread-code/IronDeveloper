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
| SupervisorAgent | Deterministic wrapper | Retrieves memory and dispatches TesterAgent for bounded plans. | Coordination only; no autonomous decisions beyond current narrow path. |
| PlannerAgent | Deterministic wrapper | Drafts Test Agent plan JSON from a goal. | Draft only; does not execute or patch. |
| ArchitectAgent | Stub | Registered as part of the eight-agent skeleton. | Do not trust for architecture decisions yet. |
| BuilderAgent | Stub/control-plane future | Builder path exists as preview safety smoke, not autonomous BuilderAgent execution. | No patch apply. |
| TesterAgent | Real execution wrapper | Runs Test Agent plans through CLI/PowerShell and returns structured report. | Executes plans; does not interpret or fix failures. |
| QualityAgent | Deterministic wrapper | Runs code standards/tooling gate through existing plan machinery. | Reports quality; does not refactor. |
| RetrieverAgent | Memory wrapper | Runs memory search and packages context bundle with source guidance. | Uses dogfood memory search; not a full retrieval planner yet. |
| CriticAgent | Deterministic wrapper | Reviews failure packages and returns evidence-backed recommendation. | Reviews only; no code changes. |
| SentinelAgent | Deterministic wrapper | Observes campaign/failure/test evidence and emits insight artefacts. | Observational only; no tickets, memory writes, patches, or approvals. |

## Blunt Assessment

The agent layer is a useful skeleton, not a mature autonomous system.

The safest real path today is:

`Codex -> memory search -> TesterAgent plan -> failure package -> Codex analysis`

## Boundary

No agent currently has permission to mutate project source files autonomously.
