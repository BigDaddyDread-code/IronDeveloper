---
id: PROMOTION_REVIEW_COCKPIT_171
project: IronDev
title: Promotion Review Cockpit 171
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:10:00Z
---

# Promotion Review Cockpit 171

Slice 171 hardens the Run Reports viewer into the first promotion review cockpit.

It exposes promotion package id, proposed change id, approval state, runtime profile, target language and stack, promotable files, blocked files, build/test evidence, active repo mutation count, workspace path, configurable policy settings, and hard safety invariants.

The WPF surface still calls shared C# run report services directly:

```text
WPF RunReportsView
  -> RunReportsViewModel
  -> IRunReportService / IRunEvidenceService
```

It does not shell out to ReplayRunner.

Configurable policy includes runtime profile selection, build/test commands, promotable source extensions, blocked generated path segments, human review checklist, and risk visibility thresholds.

Hard invariants include no silent real repo writes, no self-approval, no ConscienceAgent or ThoughtLedger bypass for governed apply, mandatory trace/evidence for mutation, explicit project scope, and no silent promotion of blocked files.

Boundary: read/review only. No real repo writes, approval execution, accepted memory mutation, ticket creation, or agent authority expansion.
