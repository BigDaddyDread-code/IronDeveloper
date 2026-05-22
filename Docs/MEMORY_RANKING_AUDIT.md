# Memory Ranking Audit

## Purpose

This audit checks whether IronDev memory ranking is still trustworthy after exact-title and wider-candidate changes.

## Current Evidence

- Exact accepted batch docs can beat broad umbrella architecture docs.
- Raw Weaviate rank and final IronDev rank are both reported.
- `SELF_IMPROVEMENT_GOALS_074_093` was raw Weaviate rank 20 and final IronDev rank 1.
- `MAIN_BRANCH_ALPHA_CHECKPOINT_094` was raw rank 6 and final rank 1 for exact-title lookup.
- The natural query `which IronDev agents are real vs stubbed` retrieved `AGENT_CAPABILITY_MATRIX` in the top 5 but not as rank 1; broader self-improvement memory still wins.
- BookSeller memory plans reject IronDev/CODEX title fragments where expected.
- Cross-project memory smokes exist and remain part of the regression wall.

## Risk

Raw vector search can bury exact accepted docs below broad architecture docs. This is expected and acceptable only because the final ranking layer reports and corrects it.

Natural-language audit questions can still prefer broad architecture memory over narrow audit documents. That is useful evidence and should be addressed before treating memory search as a fully reliable question-answering layer.

## Required Ongoing Checks

- Exact document title query returns the named document.
- Current accepted project memory beats stale or broad memory.
- BookSeller queries return BookSeller sources.
- IronDev queries return IronDev sources.
- Wrong-project candidates are rejected or demoted.
- Raw rank and final rank remain visible.

## Boundary

This audit does not introduce new ranking semantics. It documents the current risk and expected proof shape.
