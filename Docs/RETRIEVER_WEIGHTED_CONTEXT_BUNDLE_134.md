---
id: RETRIEVER_WEIGHTED_CONTEXT_BUNDLE_134
project: IronDev
title: Retriever Weighted Context Bundle 134
document_type: ArchitectureProof
authority: Accepted
status: Current
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
- What does RetrieverAgent return to other agents?
- How does IronDev package weighted context?
- Does RetrieverAgent explain included and rejected context?
boundary: RetrieverAgent packages weighted context only. It does not decide implementation, create tickets, mutate memory, or apply code changes.
---

# Retriever Weighted Context Bundle 134

## Purpose

This proof upgrades RetrieverAgent from a simple memory-search wrapper into a clearer context packaging agent.

RetrieverAgent still uses the real Codex-facing memory search path. It does not change retrieval semantics, ranking, source indexing, or project filtering.

The new responsibility is to package the returned memory into a `WeightedContextBundle` that other agents can consume safely.

## Bundle Shape

The weighted context bundle includes:

- project
- query
- semantic trace id
- included sources
- rejected sources
- raw vector rank
- final authority rank
- why included
- why rejected
- source risk notes
- summary for agent

## Rules

RetrieverAgent may:

- query project memory
- preserve raw vector and final authority ranking
- explain why a source was included
- explain why a source was rejected or treated as historical
- return risk notes
- preserve project identity and trace evidence

RetrieverAgent must not:

- decide implementation
- create tickets
- mutate memory
- apply patches
- hide uncertainty
- treat raw vector rank as truth

## Why This Matters

PlannerAgent, BuilderAgent, CriticAgent, SupervisorAgent, ConscienceAgent, and ThoughtLedger all need a context package that explains what IDA believes and why.

Raw memory search is useful, but it is not enough. Other agents need the difference between:

- what vector search found
- what authority ranking accepted
- what was demoted
- what should only be historical
- what risks remain

## Boundary

This slice proves context packaging only.

It does not grant RetrieverAgent authority to change the project, decide architecture, approve actions, or write files.
