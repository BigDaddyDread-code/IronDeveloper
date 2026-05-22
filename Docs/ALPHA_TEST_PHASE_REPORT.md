# Alpha Test Phase Report

## Decision

CONDITIONAL GO: IronDev is ready to design the disposable workspace apply proof, but not ready to apply patches outside a disposable workspace.

## What Is Proven

- Codex can query accepted IronDev memory through CLI.
- Memory search reports raw vector rank and final authority rank.
- Authority ranking can correct weak vector rank.
- BookSeller and IronDev project scopes are tested separately.
- TesterAgent can execute plans and return structured reports.
- RetrieverAgent can package context from memory search.
- PlannerAgent can draft bounded plans without execution.
- SupervisorAgent can coordinate memory lookup plus TesterAgent plan execution.
- CriticAgent can review failure package evidence.
- QualityAgent can run deterministic gates.
- Builder preview safety is proven as no-write.
- Failure handoff exists and is usable by Codex.
- Regression packs through `033-090` pass.

## What Is Not Proven

- Autonomous repair.
- Production provider LLM reasoning across the full loop.
- Builder patch apply.
- Disposable workspace apply.
- Real BookSeller app generation.
- UI reliability.
- Long-running 1000-iteration repair loop.

## Biggest Risks

1. Fake confidence from green harness loops.
2. Raw vector retrieval burying exact accepted docs.
3. Agent capability being overestimated because wrappers are named as agents.
4. ReplayRunner procedural debt growing further.
5. Accidentally crossing the write boundary before disposable workspace controls exist.

Additional retrieval note: exact-title memory retrieval is strong enough for Codex grounding, but natural-language audit questions can still rank broad architecture memory above narrow audit docs. For example, `which IronDev agents are real vs stubbed` surfaced `AGENT_CAPABILITY_MATRIX` in the top 5, but not at rank 1.

## Recommended Next Phase

Move to disposable workspace design, not direct patch application.

Recommended next sequence:

1. Disposable workspace model.
2. Copy BookSeller fixture into disposable workspace.
3. Apply patch only inside disposable workspace.
4. Build/test disposable workspace.
5. Generate failure package from apply/build/test result.
6. Human approval gate review.
7. Decide whether controlled write path can exist.

## Hard Boundary

No production or developer working tree mutation by agent flows.

Disposable workspace apply must be explicit, isolated, resettable, and trace-stamped.

## Blunt Assessment

IronDev is now promising enough to deserve discipline.

The next risk is not lack of tests. The next risk is believing the tests prove more than they do.

The system can move toward disposable workspace patch application only as a separate, explicit proof phase.
