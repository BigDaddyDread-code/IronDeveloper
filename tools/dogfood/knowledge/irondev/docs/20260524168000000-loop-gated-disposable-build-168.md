---
id: LOOP_GATED_DISPOSABLE_BUILD_168
project: IronDev
title: Loop-Gated Disposable Build 168
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T16:08:00Z
primary_retrieval_questions:
  - How does IronDev turn a messy product prompt into docs and a disposable build?
  - What does build disposable run do now?
  - Can IronDev build Solitaire through the governed loop without touching the real repo?
boundary: Run-scoped docs and disposable workspace generation only. No real repository writes, accepted memory mutation, ticket acceptance, or self-approval.
---

# Loop-Gated Disposable Build 168

## Purpose

This slice turns the messy prompt:

```text
I want build solitaire
```

into a governed end-to-end disposable build workflow.

The point is not that Solitaire is now a product. The point is that IronDev/IDA can take a rough product request, create run-scoped planning artefacts, gather Planner/Critic tool evidence, run the caged BuilderAgent repair loop, run QualityAgent/Killjoy evidence, and return one compact report.

## Command

```text
build disposable run --project Solitaire --goal "I want build solitaire" --run-id <run> --json
```

Dogfood regression command:

```text
campaign loop-gated-disposable-build-168 --run-id <run> --json
```

## Workflow

```text
messy prompt
  -> run-scoped intake/build/ticket docs
  -> governed Planner/Critic tool loop
  -> trace-backed caged BuilderAgent repair loop
  -> QualityAgent/Killjoy gate
  -> final evidence report
```

## Evidence Produced

The run writes under:

```text
tools/dogfood/runs/{runId}/
```

Key evidence includes run-scoped docs, Planner/Critic trace/report, Builder repair-loop trace/report, Quality/Killjoy log, and the final loop-gated report.

## Boundary

Generated Solitaire app files stay inside the explicit temp disposable workspace.

The command does not mutate the real IronDev repository through generated app files, mutate accepted memory, accept tickets, approve promotion, bypass governance, or make BuilderAgent a real-repo writer.

Run-scoped docs are evidence artefacts, not accepted project memory.

## Validation Result

Direct validation proved prompt preservation, project planning route, product-spike candidacy, run-scoped docs, Planner/Critic evidence, caged build pass, QualityAgent pass, 17 disposable files changed, real repo mutation count zero, and `PromoteLater`.

## Blunt Assessment

This is the first useful product-shaped dogfood loop:

```text
human mess
  -> IDA planning evidence
  -> caged build
  -> deterministic validation
  -> report
```

It still does not grant real repo write authority. That remains a later, reviewed control-path decision.
