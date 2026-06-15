# PR158 - Memory Proposal Review UI

## Summary

PR158 adds the Memory Proposal Review UI.

The UI route is:

- `/governance/memory-proposals`

Memory Proposal Review UI is read-only.

The UI consumes existing GET-only memory proposal/governance evidence APIs.

In this PR, the concrete read surface is the existing governance trace API. No dedicated memory proposal controller is added.

## Boundary

Memory proposal is not accepted memory.

Proposed memory summary is not memory.

Memory review is not memory promotion.

Candidate learning is not portable engineering memory.

Retrieval candidate is not retrieval activation.

Cross-project learning suggestion is not cross-project authority.

Copy proposal id is not acceptance.

This PR does not accept memory, promote memory, write memory, activate retrieval, approve cross-project learning, or create accepted memory records.

## What changed

- Added `MemoryProposalReviewRoute`.
- Added `MemoryProposalReviewTypes`.
- Added route wiring for `/governance/memory-proposals`.
- Added scoped UI styling.
- Added Playwright coverage for the read-only review UI.
- Added static C# boundary tests.

## What did not change

- No backend controller was added.
- No SQL migration was added.
- No store was added.
- No CLI command was added.
- No runner was added.
- No executor was added.
- No memory acceptance path was added.
- No memory promotion path was added.
- No memory write path was added.
- No retrieval activation path was added.
- No cross-project learning approval path was added.
- No accepted memory record creation path was added.

## Allowed UI actions

- Search
- Refresh
- Clear Filters
- Copy Proposal ID
- Copy Correlation ID
- Open Proposal
- Open Trace
- Open Timeline
- Open Workflow

These actions inspect existing safe evidence. They do not accept, promote, write, activate, or remember memory.

## Review line

PR158 shows the memory proposal. It does not remember it.
