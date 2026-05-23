---
id: IDA_GOVERNED_AUTONOMY_CONTROL_PLANE_133
project: IronDev
title: IDA Governed Autonomy Control Plane 133
document_type: ArchitectureProof
authority: Accepted
status: Accepted
dogfood_run_id: GovernedAutonomy133
created_utc: 2026-05-23T13:30:00Z
primary_retrieval_questions:
  - What is IDA governed autonomy?
  - What does govern review do?
  - Does governed autonomy allow real repo writes?
  - How do ConscienceAgent and ThoughtLedger work together?
boundary: Governed autonomy reviews and explains only in this slice. It does not execute actions, patch files, create tickets, or mutate memory.
---

# IDA Governed Autonomy Control Plane 133

IDA governed autonomy is the control-plane pattern that reviews proposed actions before execution.

The rule is:

```text
Intent
  -> Govern review
  -> Conscience decision
  -> ThoughtLedger visible explanation
  -> Allowed executor only
  -> Evidence package
```

This slice adds the first `govern review` command.

## What It Does

`govern review` combines:

- ConscienceAgent decision: `Allow`, `Block`, or `NeedsMoreEvidence`
- ThoughtLedger visible reasoning summary
- mutationAllowed flag
- recommended disposition

## What It Does Not Do

It does not:

- execute the action
- patch files
- write to the real repository
- create tickets
- mutate memory
- approve itself

## Constitution Rules

Initial rules:

- No real repository writes.
- Patch apply remains disposable-workspace only.
- TesterAgent executes only.
- SentinelAgent observes only.
- ResearchAgent packages evidence only.
- Project memory remains authority.
- ThoughtLedger explains only.

## Boundary

This is governed autonomy, not free autonomy. The first job is to stop unsafe action and explain safer next moves.
