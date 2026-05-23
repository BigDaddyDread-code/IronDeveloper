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
source: Docs/SUPERVISOR_DISPOSABLE_WORKSPACE_AUTONOMY_136.md
---

# Supervisor Disposable Workspace Autonomy 136

## Purpose

This slice upgrades governed autonomy from Tier 3 read/test/report to Tier 4 disposable workspace apply.

Tier 4 means SupervisorAgent may run a Test Agent plan that applies a patch only inside an explicit disposable workspace.

This is still not real repository mutation.

## Required Cage Evidence

ConscienceAgent must see disposable workspace, outside-real-repo, before-hash, after-hash, and no-real-repository-write evidence.

## Boundary

This proves weighted context, Conscience review, ThoughtLedger explanation, disposable workspace apply/build/test, and report.

It does not prove real repository writes, autonomous repair, production code mutation, or self-approval.
