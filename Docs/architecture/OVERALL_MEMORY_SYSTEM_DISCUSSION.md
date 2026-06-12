# Overall Memory System Discussion

## Status

Future discussion / parking-lot architecture note.

Not part of the current active implementation blocks.

Documentation-only. No production code, database schema, API, CLI, runtime behaviour, agent behaviour, memory write path, background job, LangGraph orchestration, Weaviate change, Hopfield implementation, tool permission, or ConscienceAgent behaviour is introduced by this document.

## Problem

IronDev needs durable project-level learning without turning agent memory into hidden authority.

The system needs a way to preserve lessons from work, failures, reviews, governance decisions, and repeated patterns. At the same time, it must not allow local agent memory, raw evidence, model output, retrieval similarity, or session context to become trusted project truth by accident.

The dangerous failure mode is simple: memory remembers something, then the system behaves as if that memory is approved authority. That would collapse evidence, proposal, governance, and canon into one unsafe blob.

## Core Idea

Agents can learn independently, but the project learns globally.

Agents can learn privately.
The system can learn globally.
Only governed evidence becomes project truth.

Individual agents may collect observations, preferences, local patterns, repeated errors, and operational hints. Those memories can improve the agent's future proposals or focus. They do not automatically become project policy, project canon, approval evidence, source truth, or memory promotion truth.

Project-level learning should happen only through governed promotion. The system can become smarter over time, but only when evidence is preserved, proposals are reviewable, governance checks run, and approved memory is clearly separated from raw observations.

## Memory Layers

### Project Canon

Project Canon is the trusted project memory layer.

It contains durable project truth that has passed governance and evidence review. It may influence future behaviour only because it was promoted through an explicit governed path.

Project Canon is not raw agent memory. It is not retrieval output. It is not model output. It is not similarity.

### Operational Memory

Operational Memory records active working context, recurring operational facts, and bounded run/process knowledge.

It can help keep work coherent across tasks, but it remains scoped and reviewable. Operational Memory should not be treated as approval, source truth, release readiness, or policy.

### Failure Mode Registry

Failure Mode Registry records repeated failure shapes, known traps, repeated review findings, broken assumptions, and recurring validation problems.

It should help agents and humans recognize patterns earlier. It should not punish, block, approve, or execute anything by itself.

### Agent Memory

Agent Memory belongs to an individual agent scope.

It can contain that agent's observations, local preferences, task-specific lessons, and candidate patterns. It must remain scoped, auditable, and non-authoritative unless a governed promotion path explicitly moves an evidence-backed proposal into trusted project memory.

Agent Memory is not shared project truth by default.

### Session Memory

Session Memory is short-lived conversational or run-local context.

It helps preserve continuity inside a bounded session. It is not durable project canon. It should not be used as approval evidence unless copied into a governed evidence artifact by an explicit process.

### Raw Evidence Store

Raw Evidence Store contains source artifacts, logs, reports, audit records, validation results, gate decisions, dogfood receipts, ThoughtLedger references, review output, and other provenance.

Raw evidence is accountability material. It is not automatically memory, proposal, approval, policy, or trusted project truth.

## ConscienceAgent Role

The ConscienceAgent is not the memory.

It is the memory integrity governor.

Its future role should be to inspect memory proposals, check evidence, classify authority level, detect stale or conflicting claims, enforce promotion rules, and prevent private memory or raw evidence from becoming project truth without governance.

ConscienceAgent should not be a hidden memory writer, hidden policy engine, approval shortcut, source mutation path, memory promotion bypass, or runtime executor.

## Memory Update Principle

Evidence -> Proposal -> Governance -> Approved Memory -> Future Behaviour.

This is the intended direction:

1. Evidence is collected from runs, reports, audits, reviews, validation, failures, and human context.
2. A proposal is created from evidence.
3. Governance checks the proposal, scope, authority, evidence quality, conflicts, freshness, and limitations.
4. Approved Memory is written only after the governed path succeeds.
5. Future Behaviour may then use that approved memory within its declared authority limits.

Anything that skips this chain is not trusted project learning.

## Authority Rule

Memory can suggest.

Only governed, evidence-backed memory can become trusted project truth.

Raw evidence can inform.

Agent memory can propose.

Retrieval matches can surface.

Model output can draft.

Associative recall can suggest.

ConscienceAgent can govern.

Human review can approve promotion where required.

None of those steps should be collapsed into one unreviewed authority path.

## Associative Memory Note

Future Hopfield-style or graph-style overlap may be useful for pattern recall.

Associative memory may suggest.
It must not decide.

Associative similarity can help surface related failures, similar architecture decisions, repeated review comments, or likely relevant project canon. However, similarity is not evidence quality, freshness, approval, policy satisfaction, source truth, or memory promotion.

Future associative memory must preserve the same boundary:

- similarity is retrieval support
- retrieval support is not authority
- authority comes only from governed, evidence-backed memory
- conflicts and stale memory must remain visible
- human review remains required where policy requires it

## Non-Goals

This document does not implement memory compilation.

This document does not implement memory proposal storage.

This document does not implement Hopfield recall.

This document does not implement graph recall.

This document does not implement agent memory mutation.

This document does not implement Project Canon writes.

This document does not add new production code.

This document does not add new database migrations.

This document does not add new APIs.

This document does not add new agent runtime behaviour.

This document does not add new memory write paths.

This document does not add new background jobs.

This document does not add new LangGraph orchestration.

This document does not add new Weaviate changes.

This document does not add a Hopfield implementation.

This document does not add new tool permissions.

This document does not add new ConscienceAgent behaviour.

This document does not change the current governed memory, tool-request, or event-store spine.

## Future Work

Possible future PRs may introduce:

- MemoryProposal model
- MemoryProposalStore
- MemoryCompiler
- ConscienceAgent memory validation path
- memory authority levels
- stale/superseded memory checks
- evidence-linked promotion workflow
- Project Canon read model
- Project Canon promotion receipt
- associative-memory retrieval contract
- graph-style memory relationship model
- Hopfield-style pattern recall experiment

Any future work must preserve the core boundary:

Evidence is not authority.

Proposal is not promotion.

Similarity is not truth.

Private agent memory is not Project Canon.

Governed evidence is the only path to trusted project truth.
