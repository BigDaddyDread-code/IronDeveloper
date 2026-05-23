---
id: RESEARCH_AGENT_LITE_121
project: IronDev
title: Research Agent Lite 121
document_type: ArchitectureProof
authority: Accepted
status: Current
dogfood_phase: BookSellerSupervisedCampaignFollowup
created_utc: 2026-05-23T12:40:00Z
primary_retrieval_questions:
  - What is ResearchAgent Lite?
  - How should external research fit into IDA?
  - Does external research override project memory?
  - When should ResearchAgent run?
boundary: ResearchAgent Lite is read-only and explicit-request only. It packages evidence but does not decide, patch, create tickets, or update memory.
---

# Research Agent Lite 121

## Purpose

ResearchAgent Lite is IDA's first external awareness agent.

SentinelAgent watches inside IronDev/IDA.

ResearchAgent looks outside IronDev/IDA.

## Rule

Project memory is authority.

ResearchAgent is evidence.

Codex/human provides judgement.

IDA enforces boundaries.

## Role

ResearchAgent Lite should:

- run only when explicitly requested
- package external evidence into a structured ResearchPackage
- include source URL, title, source type, credibility note, and snippet
- summarise key findings
- flag conflicts when known
- state confidence
- include an authority warning

ResearchAgent Lite must not:

- decide architecture
- patch code
- create tickets directly
- update project memory directly
- override accepted IronDev or BookSeller memory
- silently inject external research into builder context

## First Slice Boundary

This first implementation packages explicit external evidence supplied to the CLI.

It does not perform autonomous live browsing.

Future slices can add controlled provider-backed search, but only behind the same ResearchPackage contract.

## ResearchPackage Shape

```json
{
  "type": "ResearchPackage",
  "topic": "Simple .NET inventory management patterns",
  "project": "BookSeller",
  "sources": [],
  "keyFindings": [],
  "conflicts": [],
  "confidenceScore": 0.74,
  "authorityWarning": "External research is evidence only. Accepted BookSeller memory remains authoritative unless explicitly changed by a project decision."
}
```

## Boundary

ResearchAgent Lite is safe because it is evidence-only.

It does not enter builder context automatically.

It must later pass through WeightedContextBundle as low-authority external evidence.
