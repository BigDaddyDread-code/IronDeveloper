# Main Branch Alpha Checkpoint 094

## Purpose

This checkpoint records what IronDev can actually prove on `main` after the self-improvement dogfood batches through `074-093`.

The purpose is confidence, not autonomy. This document separates working control-plane behaviour from simulated or still-blocked capability.

## Proven Capabilities

- Dogfood CLI control surface exists through `IronDev.ReplayRunner`.
- Codex-facing `memory search` exists and returns compact JSON with raw Weaviate rank, final IronDev rank, source links, excerpts, and semantic trace IDs.
- Weaviate-backed dogfood memory search is working.
- Authority/currentness ranking can correct weak raw vector rank for exact accepted project memory.
- SQL/Weaviate memory spine smoke tests exist for document version authority and cross-project rejection.
- Ticket source-link and builder-context source-memory proofs exist.
- Agent model profiles and eight agent stubs exist.
- TesterAgent is a real execution wrapper over Test Agent plans.
- RetrieverAgent is a memory-search/context-bundle wrapper.
- PlannerAgent, SupervisorAgent, QualityAgent, and CriticAgent have deterministic dogfood paths.
- Failure packages for Codex exist.
- Builder proposal safety smoke proves preview-first/no-write behaviour.
- BookSeller exists as a controlled non-IronDev project fixture.
- Regression packs exist through `033-090`.

## Still Unproven

- Production provider LLM traces are not yet the default for these dogfood loops.
- Agents are not autonomous reasoning agents yet; most are deterministic wrappers.
- Builder does not apply patches.
- Disposable workspace patch application is not proven.
- The WPF UI is not the focus of this phase.
- Real BookSeller app generation is not proven.
- Long-running repair loops are not proven.

## Safety Boundary

- No autonomous file writes.
- No builder patch application.
- No production working tree mutation.
- No hidden BookSeller default.
- TesterAgent executes and reports; it does not fix.
- Builder remains preview-first and approval-gated.

## Current Risk Review

The main risk is fake confidence. Green regression packs prove the control surface and evidence loop, but they do not prove full autonomous development.

The second risk is memory ranking drift. The latest evidence is encouraging: `SELF_IMPROVEMENT_GOALS_074_093` was raw Weaviate rank 20 but final IronDev rank 1. That proves authority ranking is doing useful work, but it also proves raw vector search alone is not reliable enough.

The third risk is codebase entropy. Code Standards Alpha is working, but some procedural dogfood paths remain intentionally allowlisted.

## Recommended Next Direction

Finish the Alpha Test Phase before moving toward disposable workspace apply.

Recommended next gate:

`main-alpha-regression-pack.json`

After that, produce a go/no-go report for disposable workspace patch application.

