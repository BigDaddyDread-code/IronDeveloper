---
id: SOLITAIRE_PRODUCT_SPIKE_INTAKE_137
project: IronDev
title: Solitaire Product Spike Intake 137
document_type: ArchitectureProof
authority: Accepted
status: Current
source: Docs/SOLITAIRE_PRODUCT_SPIKE_INTAKE_137.md
dogfood_run_id: SolitaireProductSpikeIntake137
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
  - How should IDA handle "i want build solitare"?
  - What should PlannerAgent do with vague new product requests?
  - Should IDA immediately build Solitaire?
boundary: Product spike intake only. No project memory, ticket, disposable workspace, patch, or real repository write is created by this slice.
---

# Solitaire Product Spike Intake 137

IDA should not treat `i want build solitare` as permission to build an app.

The chat router classifies the prompt as `ProjectPlanningDiscussion`.

PlannerAgent produces a structured product spike intake package with:

- detected project `Solitaire`
- normalized prompt `i want build Solitaire`
- spelling correction assumption
- clarifying questions
- safe recommended next steps
- blocked unsafe actions
- no real repository writes

This proves product-intake usefulness only. It does not create project memory, tickets, disposable workspaces, patches, or real repository writes.
