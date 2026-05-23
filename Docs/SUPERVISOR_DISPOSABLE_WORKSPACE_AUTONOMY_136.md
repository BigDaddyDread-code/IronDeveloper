---
id: SUPERVISOR_DISPOSABLE_WORKSPACE_AUTONOMY_136
project: IronDev
title: Supervisor Disposable Workspace Autonomy 136
document_type: ArchitectureProof
authority: Accepted
status: Current
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
- Can SupervisorAgent run disposable workspace apply autonomously?
- What is Tier 4 governed autonomy?
- Can IDA apply patches inside the cage without real repo writes?
boundary: SupervisorAgent may autonomously run disposable workspace apply/build/test plans only when ConscienceAgent allows them and the cage evidence is explicit. Real repository writes remain blocked.
---

# Supervisor Disposable Workspace Autonomy 136

## Purpose

This slice upgrades governed autonomy from Tier 3 read/test/report to Tier 4 disposable workspace apply.

Tier 4 means SupervisorAgent may run a Test Agent plan that applies a patch only inside an explicit disposable workspace.

This is still not real repository mutation.

## Allowed Autonomy

SupervisorAgent may autonomously:

- retrieve weighted project context
- ask ConscienceAgent to review disposable workspace apply intent
- provide explicit cage evidence to ConscienceAgent
- ask ThoughtLedger to explain the decision
- run the disposable workspace apply/build/test plan when ConscienceAgent returns `Allow`
- report disposable workspace mutation separately from real repo mutation

## Required Cage Evidence

The ConscienceAgent review must see evidence for:

- disposable workspace
- workspace outside the real repository
- before hash capture
- after hash capture
- no real repository writes

## Blocked Autonomy

SupervisorAgent must not:

- write to the real repository
- mutate project memory
- create tickets
- approve itself
- apply patches outside the disposable workspace
- continue when ConscienceAgent returns `Block` or `NeedsMoreEvidence`

## Boundary

This proves:

```text
weighted context -> conscience review -> thought ledger -> disposable workspace apply/build/test -> report
```

It does not prove real repository writes, autonomous repair, production code mutation, or self-approval.
