---
id: SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138
project: IronDev
title: Solitaire Product Spike Architecture And Associated Tickets 138
document_type: ArchitecturePlan
authority: Proposed
status: Draft
source: Docs/SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138.md
source_intake: SOLITAIRE_PRODUCT_SPIKE_INTAKE_137
recommended_next_slice: 139 - Build Solitaire In Disposable Workspace
dogfood_run_id: SolitaireProductSpikePlan138
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
  - What comes after Solitaire product spike intake?
  - Should IDA jump straight from Solitaire intake to build?
  - What architecture and tickets are needed before building Solitaire?
  - What should slice 139 do for Solitaire?
boundary: Planning package only. This does not build Solitaire, create a disposable workspace, apply patches, or write the real repository.
---

# Solitaire Product Spike Architecture And Associated Tickets 138

This is the answer to what comes after `SOLITAIRE_PRODUCT_SPIKE_INTAKE_137`.

Do not jump straight from intake to build. Create the architecture and draft ticket layer first.

This document answers the retrieval question: should IDA jump straight from Solitaire intake to build, or create architecture tickets first? IDA should create the architecture and draft ticket layer first, then attempt the disposable workspace build in slice 139 only after that planning package is available.

Safe defaults:

- project: Solitaire
- platform: WPF
- game type: Klondike Solitaire
- scope: playable vertical slice
- input: click-to-move first
- drag/drop: out of scope
- save/load: out of scope
- animations: out of scope
- persistence: none
- tests: deterministic core rule tests
- build target: disposable workspace only
- real repo writes: blocked

Future build attempts must go through RetrieverAgent weighted context, ConscienceAgent review, ThoughtLedger explanation, Supervisor Tier 4 disposable workspace apply/build/test, TesterAgent execution, and evidence reporting.

Associated draft artefacts:

- `SOLITAIRE_PRODUCT_SPIKE_SPEC_138`
- `SOLITAIRE_DISPOSABLE_BUILD_TICKET_DRAFT_138`
- `SOLITAIRE_RULE_ENGINE_TICKET_DRAFT_138`
- `SOLITAIRE_WPF_UI_TICKET_DRAFT_138`
- `SOLITAIRE_TEST_EVIDENCE_TICKET_DRAFT_138`
- future draft ticket `SOL-139-001`

Recommended next slice:

```text
139 - Build Solitaire In Disposable Workspace
```

Only begin 139 after 138 proves the architecture/spec/ticket path is stable.
