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


## PR111 boundary

PR111 adds deterministic conflict detection for staged memory proposals.

A conflict candidate is not a conflict decision.
A conflict candidate does not choose truth.
A conflict candidate does not reject proposals.
A conflict candidate does not delete proposals.
A conflict candidate does not correct proposals.
A conflict candidate does not accept memory.
A conflict candidate does not promote memory.
A conflict candidate does not activate retrieval.
A conflict score is not approval.
A conflict score is not truth.

The detector is deterministic and does not use model calls, embeddings, vector search, Weaviate, retrieval indexing, API, CLI, or runtime dispatch.

Conflict detection produces review candidates only. Human/governed review remains required before any truth, correction, rejection, deletion, acceptance, promotion, retrieval activation, or memory decision.

## PR112 boundary

PR112 adds deterministic cross-run pattern detection for staged memory proposals.

A cross-run pattern candidate is not accepted memory.
A cross-run pattern candidate is not promoted memory.
A cross-run pattern candidate is not Portable Engineering Memory.
A cross-run pattern candidate is not retrieval authority.
A cross-run pattern candidate is not vector index content.
A cross-run pattern candidate is not an embedding.
A cross-run pattern candidate is not policy satisfaction.
A cross-run pattern candidate is not approval.
A cross-run pattern candidate is not workflow progress.
A cross-run pattern candidate is not source apply.

The detector only looks for repeated staged proposal themes across workflow runs. It may identify repeated facts, decisions, boundaries, failure modes, risks, conventions, debugging lessons, validation findings, review findings, workflow patterns, policy invariants, and portable-review candidates.

The detector is deterministic and does not use SQL writes, model calls, embeddings, vector search, Weaviate, retrieval indexing, API, CLI, or runtime dispatch.

Cross-run pattern detection produces review candidates only. Human/governed review remains required before any proposal acceptance, memory promotion, Portable Engineering Memory creation, retrieval activation, indexing, embedding, policy, approval, workflow, source apply, or memory-truth decision.

## PR113 boundary

PR113 adds a promotion request package for staged memory proposals.

A promotion request package is not accepted memory.
A promotion request package is not promoted memory.
A promotion request package is not active project memory.
A promotion request package is not active agent memory.
A promotion request package is not Portable Engineering Memory.
A promotion request package is not retrieval authority.
A promotion request package is not approval.
A promotion request package is not policy satisfaction.
A promotion request package is not a promotion decision.

The package gathers review material for a later governed promotion decision.
It does not decide the proposal.
It does not index memory.
It does not create embeddings.
It does not write to vector storage.

Evidence remains evidence only. Grounding remains traceability only. Duplicate, stale, conflict, and cross-run pattern signals remain advisory only. Approval requirement references remain unsatisfied requirements only.

Human/governed approval remains required before any proposal acceptance, memory promotion, Portable Engineering Memory creation, retrieval activation, indexing, embedding, policy, approval, workflow, source apply, or memory-truth decision.

## PR113A boundary receipt

PR113A hardens the PR113 memory promotion request boundary.

See [PR113A Memory Promotion Request Boundary Receipt](receipts/PR113A_MEMORY_PROMOTION_REQUEST_BOUNDARY_RECEIPT.md).

A promotion request package is a review folder. It is not promotion approval, triage action, memory acceptance, memory rejection, correction, merge, rewrite, retrieval activation, approval satisfaction, or policy satisfaction.

Duplicate, stale, conflict, and cross-run pattern signals remain review-only. Approval requirement references remain unsatisfied requirements only. Evidence remains evidence only, and grounding remains traceability only.

The package can request review. It cannot approve promotion, accept memory, promote memory, create Portable Engineering Memory, activate retrieval, satisfy approval, or satisfy policy.
