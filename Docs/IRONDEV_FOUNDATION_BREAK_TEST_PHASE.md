---
id: IRONDEV_FOUNDATION_BREAK_TEST_PHASE
project: IronDev
title: IronDev Foundation Break-Test Phase
document_type: TestStrategy
authority: Accepted
status: Accepted
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
  - How should IronDev test its foundation before UI work?
  - What should IDA try to break before real app/UI work?
  - What foundation tests are needed after disposable workspace apply?
  - How should BookSeller supervised campaigns evolve?
boundary: Foundation hardening only. No real repository writes. No UI work until the foundation gates pass.
---

# IronDev Foundation Break-Test Phase

## Purpose

IronDev has reached the point where the foundation should be attacked before more product or UI work is added.

The question is:

> Can IronDev/IDA survive messy prompts, memory drift, reindexing changes, failed builds, unsafe patches, repeated campaigns, and project bleed without touching the real repo or losing evidence?

If the answer is not confidently yes, UI work waits.

## Current Boundary

No real repository writes.

No production or developer working tree mutation.

Patch apply is allowed only inside explicit disposable workspaces.

TesterAgent executes only; it does not fix.

SentinelAgent observes only; it does not create tickets directly or patch.

Codex reviews only unless a later phase explicitly promotes repair authority.

## What To Break

### Memory And Reindexing

Try to break current/stale ranking, exact-title promotion, broad architecture documents beating narrow docs, project isolation, duplicate chunks after repeated reindex, and wrong-project raw candidates being promoted.

Required proof:

- Current accepted memory wins.
- Stale memory remains visible but demoted.
- Wrong-project memory is rejected or clearly labelled.
- Duplicate counts stay zero.
- Trace evidence explains ranking.

### Routing And Natural Language

Try to break save/project-knowledge/remember/store vocabulary, ticket generation vocabulary, bounded build requests, unsafe patch requests, and TesterAgent repair requests.

Required proof:

- Save phrases route to project document/discussion save.
- Ticket phrases route to ticket generation.
- Build phrases remain bounded and disposable-workspace only.
- Unsafe patch phrases are blocked or caged.
- TesterAgent is not allowed to fix.
- IronDev memory does not bleed into BookSeller unless explicitly requested.

### BookSeller Campaigns

Campaigns must stay sequential until build isolation is proven.

Every run needs:

- Unique run id.
- Unique disposable workspace path.
- Real repo mutation count of zero.
- Compact report.
- Classified finding for every useful failure.

Useful campaigns:

- Basic messy prompt campaign.
- Project knowledge/save campaign.
- Ticket generation campaign.
- Disposable apply campaign.
- Failure/recovery evidence campaign.
- Project bleed chaos campaign.

The goal is not always green. The goal is safe, classified, evidence-rich failure.

### Disposable Workspace Safety

Try path traversal, absolute paths outside the workspace, real repo targets, generated build-output targets, missing hashes, missing trace IDs, missing source links, workspace reuse without reset, and parallel build conflicts.

Required proof:

- Unsafe proposals fail closed.
- Real repo hash remains unchanged.
- Disposable workspace paths are explicit.
- Before/after evidence is captured.
- Failure packages exist for blocked apply attempts.

### IDA Code Comparison

Feed IDA deliberately imperfect patches:

- Missing tests.
- Extra unrelated file.
- UI added too early.
- Database added too early.
- Correct code with weak validation.

IDA should report scope match, unexpected files, test weakness, and recommendation. It must not approve real repo writes.

### SentinelAgent Lite

Sentinel should observe campaign evidence and emit advisory InsightArtefacts only.

It must label:

- ObservedProject.
- AffectedProject.
- InsightType.
- Evidence refs.
- Recommended dispositions.

It must not create tickets, patch code, or approve writes.

## Memory And Artefact Lifecycle

IDA can clean the workshop, but it must not burn the evidence locker.

Documents and discussions are versioned. New understanding creates a new version.

Tickets can be updated, split, superseded, closed, or linked to findings. Keep history.

Bad or obsolete memory should be marked stale, superseded, archived, or rejected. Do not hard-delete canonical project knowledge unless it is explicitly disposable dogfood noise.

Campaign junk can be deleted or reset aggressively by DogfoodRunId.

Weaviate/index chunks can be rebuilt. SQL remains truth.

Raw logs can expire. Compact findings, decisions, and source-linked tickets should persist.

Lifecycle states:

```text
Current
Draft
Accepted
Superseded
Stale
Archived
Rejected
DeletedTestArtefact
```

Destructive cleanup must say:

```json
{
  "target": "BookSeller campaign run artefacts",
  "scope": "DogfoodRunId",
  "hardDeleteAllowed": true,
  "canonicalMemoryAffected": false
}
```

## Foundation Break-Test Slices

121: CampaignFinding contract and triage proof.

122: Fix project-knowledge save routing.

123: BookSeller campaign reset scoped by DogfoodRunId.

124: Build isolation or campaign lock.

125: BookSeller 10-run campaign rerun.

126: Project bleed chaos campaign.

127: Disposable apply abuse campaign.

128: IDA code comparison hardening.

129: SentinelAgent campaign insight pack.

130: Foundation break-test report.

## Baseline Gate

Before and after major work:

- ReplayRunner build.
- Code Standards Alpha.
- Memory reindex freshness smoke.
- Main alpha regression pack.
- BookSeller campaign where relevant.

## Blunt Assessment

The foundation is now good enough to attack.

Do not go UI yet.

Break routing, memory, reindexing, disposable apply, build isolation, campaign reset, IDA comparison, and Sentinel classification.

If IronDev survives this phase with real repo mutation still zero and evidence still clean, UI planning becomes much safer.


