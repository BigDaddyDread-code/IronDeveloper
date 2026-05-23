# Live Critic And Planner Agents 159

## Purpose

IRONDEV-159 extends the opt-in live governed agent path from ArchitectAgent to CriticAgent and PlannerAgent.

The goal is to let these agents use configured model profiles as advisory intelligence while keeping deterministic fallback and hard governance boundaries in force.

## What Changed

- `CriticAgent` can attempt an opt-in live model call while reviewing a failure package.
- `PlannerAgent` can attempt an opt-in live model call while drafting a test plan or classifying product-spike intake.
- `campaign live-critic-planner-159` proves deterministic fallback, live-provider attempt recording, and no-write governance.
- `agent critic review-failure`, `agent planner intake-product-spike`, and `agent planner draft-test-plan` accept `--live-llm` and `--model-profile`.

## Boundary

Live model output is evidence only.

CriticAgent and PlannerAgent still must not:

- patch files
- create tickets
- mutate memory
- apply patches
- approve writes
- weaken assertions to get green
- bypass ConscienceAgent or ThoughtLedger

If the configured provider is unavailable, the agent records the attempt and deterministic behaviour remains in force.

## Validation

Primary smoke:

```text
test run-plan --plan tools/dogfood/test-agent-plans/irondev-live-critic-planner-agents-159.json --run-id LiveCriticPlanner159 --json
```

The smoke proves:

- deterministic CriticAgent fallback review is still available
- deterministic PlannerAgent product-spike intake is still available
- opt-in live local provider attempts are recorded for both agents
- provider failure falls back safely
- real repo writes, memory mutation, ticket creation, patch apply, and self-approval remain blocked
