---
id: PROMOTION_REVIEW_COCKPIT_171
project: IronDev
title: Promotion Review Cockpit 171
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:10:00Z
primary_retrieval_questions:
  - How does IronDev review a promotion package?
  - Where are promotion package files, blocked files, risks, and approval state shown?
  - What promotion settings are configurable and what safety rules are hard invariants?
boundary: Read/review UI only. No real repo writes, approval execution, memory mutation, or agent authority expansion.
---

# Promotion Review Cockpit 171

Slice 171 hardens the Run Reports viewer into the first promotion review cockpit.

It does not add a new apply path. It makes existing run evidence easier to inspect through shared C# services.

The key review surface now includes:

- promotion package id
- proposed change id
- approval state
- recommendation
- runtime profile
- target language and stack
- promotable files
- blocked generated files
- build/test attempts
- active repo mutation count
- workspace path
- evidence files
- configurable review policy
- hard safety invariants

## Architecture

The WPF viewer still follows the correct long-term shape:

```text
WPF RunReportsView
  -> RunReportsViewModel
  -> IRunReportService / IRunEvidenceService
  -> file-backed run reports for Alpha
```

It does not shell out to ReplayRunner.

The same service can now read:

- trace-backed build reports
- standard Test Agent reports
- promotion package reports
- isolated promotion apply reports

## Policy Shape

Slice 171 starts the policy/settings split.

Configurable policy includes:

- runtime profile selection
- build command per runtime profile
- test command per runtime profile
- promotable source extensions
- blocked generated path segments
- human review checklist
- risk visibility thresholds

Hard invariants include:

- real repo writes require explicit reviewed approval
- agents cannot approve their own changes
- ConscienceAgent and ThoughtLedger cannot be bypassed for governed apply
- mutation requires trace and evidence
- project scope must be explicit
- blocked files must not be promoted silently

## Boundary

This is a read/review slice.

It does not:

- write to the real repository
- approve promotion
- apply promotion packages
- mutate accepted memory
- create tickets
- expand BuilderAgent or SupervisorAgent authority

## Why It Matters

170 proved the isolated candidate workspace.

171 makes that proof inspectable.

The next safe product move is to use this cockpit as the human/Codex review surface before designing any controlled branch or PR write path.
