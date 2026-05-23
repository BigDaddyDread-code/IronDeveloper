---
id: SUPERVISOR_GOVERNED_AUTONOMOUS_LOOP_135
project: IronDev
title: Supervisor Governed Autonomous Loop 135
document_type: ArchitectureProof
authority: Accepted
status: Current
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
- What autonomy can SupervisorAgent perform?
- Can IronDev run bounded autonomous validation?
- How does ConscienceAgent govern SupervisorAgent?
boundary: SupervisorAgent may autonomously run safe read/test/report loops only when ConscienceAgent allows them. It must stop before writes, ticket creation, memory mutation, builder apply, or real repository changes.
source: Docs/SUPERVISOR_GOVERNED_AUTONOMOUS_LOOP_135.md
---

# Supervisor Governed Autonomous Loop 135

## Purpose

This slice upgrades SupervisorAgent from a narrow memory-to-test coordinator into the first governed autonomous execution loop.

The goal is bounded autonomy inside a safe cage.

## Allowed Autonomy

SupervisorAgent may autonomously retrieve project memory, consume a weighted context bundle, ask ConscienceAgent to review the proposed action, ask ThoughtLedger to explain visible reasoning, run TesterAgent only when ConscienceAgent returns `Allow`, and produce a compact handoff.

## Blocked Autonomy

SupervisorAgent must not write to the real repository, mutate project memory, create tickets, apply patches, approve itself, weaken tests, bypass ConscienceAgent, or run TesterAgent when ConscienceAgent returns `Block` or `NeedsMoreEvidence`.

## Boundary

This proves Tier 3 governed autonomy:

```text
read context -> review -> explain -> execute tests -> report
```

It does not prove autonomous repair, patch apply, ticket creation, memory mutation, or real repository writes.
