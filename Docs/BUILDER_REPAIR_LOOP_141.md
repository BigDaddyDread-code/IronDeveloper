---
id: BUILDER_REPAIR_LOOP_141
project: IronDev
title: Trace-Backed BuilderAgent Repair Loop 141
document_type: ArchitectureProof
authority: Accepted
status: Current
dogfood_phase: DisposableWorkspaceRepairLoop
created_utc: 2026-05-23T08:35:00Z
primary_retrieval_questions:
  - What did IronDev prove in BuilderAgent repair loop 141?
  - Can BuilderAgent repair failures inside a disposable workspace?
  - Is BuilderAgent allowed to write to the real repo?
boundary: BuilderAgent repairs only inside explicit disposable workspaces. Real repository writes remain blocked.
---

# Trace-Backed BuilderAgent Repair Loop 141

## Purpose

Slice 141 proves BuilderAgent can perform a bounded repair loop inside the disposable Solitaire workspace while writing every important action into the build-run trace spine from 140.

This is the first real caged repair loop. It is not a real repository write path.

## Command

```text
agent builder repair-loop --project Solitaire --dogfood-run-id <run> --json
```

## What It Does

The command:

- resolves Solitaire project scope from the accepted 138 product spike plan,
- records RetrieverAgent, ConscienceAgent, ThoughtLedger, BuilderAgent, TesterAgent, CriticAgent, QualityAgent, and SupervisorAgent stages,
- creates a disposable Solitaire workspace under temp,
- generates the Solitaire Core/WPF/Test projects inside that workspace,
- intentionally removes the WPF project reference to force a build failure,
- records the build failure as `MissingProjectReference`,
- repairs the project reference inside the disposable workspace,
- intentionally breaks the empty-tableau King rule to force a rule-test failure,
- records the test failure as `RuleBug`,
- repairs `KlondikeRules.cs` inside the disposable workspace,
- reruns build and tests,
- records final build/test success,
- records changed disposable files,
- records real repo mutation count.

## Boundary

Allowed:

- create and edit generated Solitaire files inside the explicit disposable workspace,
- run build/test commands against the disposable workspace,
- repair within the retry budget,
- write trace/report/evidence files under `tools/dogfood/runs`.

Blocked:

- real IronDev repository writes from BuilderAgent,
- memory mutation,
- guardrail mutation,
- regression pack mutation,
- self-approval,
- promotion of generated Solitaire files into the real repo.

## Evidence Shape

The command writes:

```text
tools/dogfood/runs/{runId}/builder-repair-loop-trace.json
tools/dogfood/runs/{runId}/builder-repair-loop-report.json
tools/dogfood/runs/{runId}/builder-repair-loop-report.md
tools/dogfood/runs/{runId}/evidence/*
```

The trace includes:

- context and rejected context,
- ConscienceAgent decision,
- ThoughtLedger summary,
- BuilderAgent plan,
- workspace mutation evidence,
- build attempts,
- test attempts,
- repair attempts,
- evidence artifacts,
- final recommendation.

## What This Proves

141 proves:

- BuilderAgent can operate inside the disposable cage,
- a build failure can be classified and repaired,
- a rule-test failure can be classified and repaired,
- repairs are limited to generated disposable files,
- final build/test success is recorded,
- real repo mutation count remains zero,
- the trace/report is Codex-readable.

## What This Does Not Prove

141 does not prove:

- production-grade autonomous engineering,
- real repo writes,
- promotion to main,
- UI cockpit rendering,
- long-running multi-ticket repair,
- provider-backed LLM repair reasoning.

## Next Step

The next useful hardening step is to use this same trace-backed repair-loop shape against messier disposable product spikes and failure packages, while keeping Killjoy and the real-repo mutation boundary active.
