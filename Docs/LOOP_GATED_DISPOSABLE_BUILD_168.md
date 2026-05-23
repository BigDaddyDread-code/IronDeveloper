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

Key evidence includes:

- `docs/SOLITAIRE_PRODUCT_SPIKE_INTAKE_168.md`
- `docs/SOLITAIRE_DISPOSABLE_BUILD_BRIEF_168.md`
- `docs/SOLITAIRE_DISPOSABLE_BUILD_TICKET_168.md`
- `loop-gated-disposable-build-168-report.json`
- `loop-gated-disposable-build-168-report.md`
- Planner/Critic trace/report from the nested plan-review run
- Builder repair-loop trace/report from the nested caged build run
- Quality/Killjoy command log

## Boundary

Generated Solitaire app files stay inside the explicit temp disposable workspace.

The command does not:

- mutate the real IronDev repository through generated app files
- mutate accepted memory
- accept tickets
- approve promotion
- bypass ConscienceAgent or ThoughtLedger boundaries
- make BuilderAgent a real-repo writer

Run-scoped docs are evidence artefacts, not accepted project memory.

## Validation Result

Direct validation proved:

- prompt preserved as `I want build solitaire`
- route classified as `ProjectPlanningDiscussion`
- `productSpikeCandidate = true`
- run-scoped docs created
- Planner/Critic evidence validation passed
- BuilderAgent caged repair loop passed
- QualityAgent/Killjoy passed with the existing three warnings
- disposable files changed count was 17
- real repo mutation count was 0
- final recommendation was `PromoteLater`

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
