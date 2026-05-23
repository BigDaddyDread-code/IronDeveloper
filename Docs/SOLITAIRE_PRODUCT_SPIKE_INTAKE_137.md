---
id: SOLITAIRE_PRODUCT_SPIKE_INTAKE_137
project: IronDev
title: Solitaire Product Spike Intake 137
document_type: ArchitectureProof
authority: Accepted
status: Current
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
  - How should IDA handle "i want build solitare"?
  - What should PlannerAgent do with vague new product requests?
  - Should IDA immediately build Solitaire?
boundary: Product spike intake only. No project memory, ticket, disposable workspace, patch, or real repository write is created by this slice.
---

# Solitaire Product Spike Intake 137

## Purpose

This slice proves IDA does not treat a vague human prompt like:

```text
i want build solitare
```

as permission to build an app.

It should instead classify the prompt as a new product spike intake candidate, preserve uncertainty, ask clarifying questions, and recommend safe next steps.

## Expected Behaviour

The chat router should identify the prompt as a `ProjectPlanningDiscussion`, not `GeneralChat` and not a build/apply command.

PlannerAgent should then produce a structured intake package:

- detected project: `Solitaire`
- normalized prompt: `i want build Solitaire`
- assumptions, including the spelling correction from `solitare`
- clarifying questions
- recommended next steps
- blocked unsafe actions
- boundary statement

## Safety Boundary

This slice does not:

- create project memory
- create tickets
- create a disposable workspace
- apply patches
- write files through the builder
- mutate the real repository

It only proves the agents can respond more usefully to a vague new product request.

## Why This Matters

IDA should not be a passive chat wrapper, but it also should not blindly build from a half-formed prompt.

The useful middle path is:

```text
vague human request
        ↓
planning intake
        ↓
clarifying questions / reviewed artefact proposal
        ↓
Conscience review
        ↓
disposable workspace only, if explicitly approved later
```
