# Block K - Governed Memory Proposal Substrate

## Purpose

Block K starts the governed memory proposal substrate.

PR107 adds durable staging for memory proposals. A staged proposal is an inbox record for later human/governed review. It is not accepted memory, promoted memory, project truth, retrieval authority, policy, approval, workflow progress, source apply, or vector index content.

## PR107 boundary

```text
memory.MemoryProposal
memory.MemoryProposalEvidenceReference
memory.MemoryProposalGroundingReference
memory.MemoryProposalWorkflowReference
```

The store records safe proposed-memory text, source identity, workflow references, grounding references, and evidence references.

The store does not:

- accept memory
- promote memory
- write collective memory
- write agent memory
- write vector or retrieval indexes
- grant approval
- grant execution
- satisfy policy
- start or continue workflow
- mutate source
- approve release
- expose API, CLI, UI, scheduler, or runtime worker behavior

## Naming boundary

```text
retrieval match != memory candidate
candidate != memory proposal
staged memory proposal != accepted memory
staged memory proposal != memory promotion
memory proposal evidence != authority
```

The older `agent.AgentMemoryImprovementProposal` table remains the manual improvement proposal queue. PR107 does not repurpose it. PR107 adds `memory.MemoryProposal` as the durable staging inbox for proposed memory candidates.

## Review rule

Human/governed review remains required before any staged memory proposal can become accepted memory or promoted memory.

## PR108 boundary

PR108 adds a reviewable evidence package model for staged memory proposals.

A memory proposal evidence package is not accepted memory.
A memory proposal evidence package is not promoted memory.
A memory proposal evidence package is not active project memory.
A memory proposal evidence package is not active agent memory.
A memory proposal evidence package is not Portable Engineering Memory.
A memory proposal evidence package is not retrieval authority.
A memory proposal evidence package is not approval.
A memory proposal evidence package is not policy satisfaction.

The package gathers evidence for review.
It does not decide the proposal.
It does not index memory.
It does not create embeddings.
It does not write to vector storage.

The package may summarize staged proposal evidence, grounding references, workflow context, risk notes, confidentiality notes, sanitization notes, and review hints. Those facts remain review material only.

## PR109 boundary

PR109 adds deterministic duplicate detection for staged memory proposals.

A duplicate candidate is not a duplicate decision.
A duplicate candidate does not merge proposals.
A duplicate candidate does not reject proposals.
A duplicate candidate does not accept memory.
A duplicate candidate does not promote memory.
A duplicate candidate does not activate retrieval.
A similarity score is not approval.
A similarity score is not truth.

The detector is deterministic and does not use model calls, embeddings, vector search, Weaviate, retrieval indexing, API, CLI, or runtime dispatch.

Duplicate detection produces review candidates only. Human/governed review remains required before any proposal merge, rejection, acceptance, promotion, retrieval activation, or memory-truth decision.

## PR110 boundary

PR110 adds deterministic stale detection for staged memory proposals.

A stale candidate is not a stale decision.
A stale candidate does not reject proposals.
A stale candidate does not delete proposals.
A stale candidate does not correct proposals.
A stale candidate does not accept memory.
A stale candidate does not promote memory.
A stale candidate does not activate retrieval.
A staleness score is not approval.
A staleness score is not truth.

The detector is deterministic and does not use model calls, embeddings, vector search, Weaviate, retrieval indexing, API, CLI, or runtime dispatch.

Stale detection produces review candidates only. Human/governed review remains required before any correction, rejection, deletion, acceptance, promotion, retrieval activation, or memory-truth decision.
