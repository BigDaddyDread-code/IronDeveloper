---
id: SENTINEL_AGENT_LITE_120
project: IronDev
title: Sentinel Agent Lite 120
document_type: ArchitectureProof
authority: Accepted
status: Current
dogfood_phase: BookSellerSupervisedCampaignFollowup
created_utc: 2026-05-23T12:20:00Z
primary_retrieval_questions:
  - What is SentinelAgent Lite?
  - Should SentinelAgent watch IDA and BookSeller?
  - How should SentinelAgent classify observed and affected projects?
  - What does SentinelAgent do with campaign failures?
boundary: SentinelAgent Lite is observational only. It creates insight artefacts and does not fix, patch, approve, or mutate memory.
---

# Sentinel Agent Lite 120

## Purpose

SentinelAgent Lite is IDA's first internal observer.

It watches evidence already produced by IronDev/IDA, including campaign reports, failure packages, test reports, code standards output, reindex reports, and disposable apply reports.

It creates advisory `InsightArtefact` output only.

## Scope

SentinelAgent must watch:

- IronDev / IDA itself
- BookSeller
- future sample/customer projects
- shared dogfood infrastructure

SentinelAgent is cross-project capable, but it must never mix projects silently.

Every insight should preserve:

```json
{
  "observedProject": "BookSeller",
  "affectedProject": "IronDev"
}
```

Example:

```text
BookSeller campaign run 3 exposed a routing weakness in IronDev.
```

The observed project is BookSeller.

The affected project is IronDev.

## Role

SentinelAgent Lite should:

- observe campaign/failure/test evidence
- identify repeated or important patterns
- emit structured insight artefacts
- recommend dispositions such as CreateTicket, CreateDiscussion, CreateCampaignFinding, CreateObservation
- keep project scope explicit

SentinelAgent Lite must not:

- patch code
- create tickets directly
- persist project documents directly
- approve writes
- block builds
- override Codex or human judgement

## First Proven Insight

The supervised BookSeller campaign exposed:

```text
BookSellerCampaign118 run 3:
Expected SaveDiscussionDocument
Actual GeneralChat
Prompt: Save this as BookSeller project knowledge
```

SentinelAgent Lite should report this as:

```json
{
  "observedProject": "BookSeller",
  "affectedProject": "IronDev",
  "insightType": "RoutingWeakness",
  "severity": "Concern",
  "recommendedDispositions": [
    "CreateTicket",
    "CreateDiscussion",
    "CreateCampaignFinding"
  ]
}
```

## Boundary

This proof does not implement ResearchAgent.

ResearchAgent remains future read-only external evidence gathering.

SentinelAgent Lite is internal awareness only.
