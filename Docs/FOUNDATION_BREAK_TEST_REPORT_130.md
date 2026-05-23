---
id: FOUNDATION_BREAK_TEST_REPORT_130
project: IronDev
title: Foundation Break-Test Report 130
document_type: TestReport
authority: Accepted
status: Accepted
created_utc: 2026-05-23T09:30:00Z
primary_retrieval_questions:
  - What did foundation break-test 121-130 prove?
  - Is IronDev ready for UI work?
  - What remains unsafe after the break-test phase?
boundary: Report only. No real repository writes. No UI implementation approval.
---

# Foundation Break-Test Report 130

## Decision

CONDITIONAL GO for continued foundation hardening.

UI planning can be discussed after the branch lands, but UI implementation should still wait until the foundation break-test pack is part of the normal regression baseline.

## What This Branch Proves

- Campaign failures can be represented as CampaignFinding artefacts.
- The known BookSeller project-knowledge routing weakness has a direct fix and proof.
- Campaign reset policy is DogfoodRunId-scoped and protects canonical memory.
- Build isolation policy blocks parallel trampling until isolated outputs exist.
- BookSeller campaign rerun can distinguish known fixed failures from useful new failures.
- Project bleed chaos has an explicit expectation: wrong-project memory is rejected or labelled non-authoritative.
- Disposable abuse remains fail-closed.
- IDA comparison remains advisory and does not approve real repo writes.
- Sentinel campaign insight remains observational.

## What Is Still Not Proven

- Full real provider LLM routing across all messy prompts.
- High-volume campaign execution with many isolated builds.
- Long-running repair loops.
- UI reliability.
- Controlled real repository write path.

## Current Boundary

No real repository writes.

Patch apply only inside explicit disposable workspaces.

TesterAgent executes only.

Sentinel observes only.

ResearchAgent packages explicit external evidence only.

## Recommendation

Land the foundation break-test pack, promote it into the regression baseline, then run a few real BookSeller messy campaigns before UI implementation.


