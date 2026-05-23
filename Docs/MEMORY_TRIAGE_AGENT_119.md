---
id: MEMORY_TRIAGE_AGENT_119
project: IronDev
title: Memory Triage Agent 119
document_type: ArchitectureProof
authority: Accepted
status: Current
dogfood_phase: BookSellerSupervisedCampaignFollowup
created_utc: 2026-05-23T12:00:00Z
primary_retrieval_questions:
  - How should IDA decide what becomes memory?
  - Should routing failures become global memory or project memory?
  - How should IDA classify user feedback like ticket went wrong route?
  - What should happen when a user says this is memory?
boundary: Memory triage classifies context. It does not persist documents, create tickets, or apply fixes.
---

# Memory Triage Agent 119

## Purpose

IDA needs a smart triage step between conversation/test evidence and durable memory.

The goal is not to remember everything. The goal is to decide whether a message should become future-retrievable memory, and if so, what scope and artefact type it should use.

## Rule

Only promote context to memory when it changes future behaviour, explains a failure, creates a decision, or becomes work.

## Scope Rules

Global memory is for IDA/IronDev rules that affect all projects:

- routing rules
- safety boundaries
- Test Agent behaviour
- builder/write rules
- memory classification rules

Project memory is for facts about one project:

- BookSeller inventory rules
- BookSeller storage choices
- BookSeller tickets
- project-specific campaign findings

Some findings create both:

- a project-scoped finding that records where the issue appeared
- an IronDev/global ticket or decision candidate that fixes the general behaviour

## Classification Output

Memory triage should return:

```json
{
  "shouldSave": true,
  "scope": "Global",
  "project": "IronDev",
  "memoryType": "RoutingFinding",
  "authority": "Proposed",
  "confidence": 0.86,
  "reason": "The message describes an observed routing failure that can affect all projects.",
  "recommendedArtifacts": ["CampaignFinding", "BugTicket", "DiscussionDocument"]
}
```

## BookSeller Campaign Finding

The supervised BookSeller campaign exposed this useful weakness:

```text
Prompt: I need inventory but don't overthink it. Save this as BookSeller project knowledge: use SQL Server and Dapper for books, stock, and storage locations.
Expected: SaveDiscussionDocument
Actual: GeneralChat
```

This should become:

- CampaignFinding
- BugTicket
- DiscussionDocument about project knowledge/save vocabulary

## Boundary

Memory triage is classification only.

It must not:

- write project documents
- create tickets
- mutate source files
- apply fixes
- decide final accepted authority without review

## Proof

The Test Agent plan `irondev-memory-triage-smoke.json` proves:

- routing failures become global RoutingFinding memory candidates
- BookSeller product rules become project memory candidates
- project knowledge save phrases become document capture candidates
- questions about memory do not save new memory
- global safety rules become decision candidates
