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
---

# Supervisor Governed Autonomous Loop 135

## Purpose

This slice upgrades SupervisorAgent from a narrow memory-to-test coordinator into the first governed autonomous execution loop.

The goal is not broad autonomy. The goal is bounded autonomy inside a safe cage.

## Allowed Autonomy

SupervisorAgent may autonomously:

- retrieve project memory through RetrieverAgent
- consume a weighted context bundle
- ask ConscienceAgent to review the proposed action
- ask ThoughtLedger to explain the visible reasoning summary
- run TesterAgent only when ConscienceAgent returns `Allow`
- produce a compact Codex handoff

## Blocked Autonomy

SupervisorAgent must not:

- write to the real repository
- mutate project memory
- create tickets
- apply patches
- approve itself
- weaken tests
- bypass ConscienceAgent
- run TesterAgent when ConscienceAgent returns `Block` or `NeedsMoreEvidence`

## Loop Shape

```text
Resolve project and goal
  -> RetrieverAgent weighted context
  -> ConscienceAgent review
  -> ThoughtLedger explanation
  -> TesterAgent plan execution when allowed
  -> Codex/human handoff
```

## Decision Rule

Human/Codex should not need to approve every safe read/test/report action.

Human/Codex should intervene when:

- confidence is insufficient
- evidence is missing
- a safety boundary is crossed
- real repository writes are proposed
- architecture authority needs changing

## Boundary

This proves Tier 3 governed autonomy:

```text
read context -> review -> explain -> execute tests -> report
```

It does not prove autonomous repair, patch apply, ticket creation, memory mutation, or real repository writes.
