---
id: ISOLATED_PROMOTION_APPLY_170
project: IronDev
title: Isolated Promotion Apply Proof 170
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:00:00Z
primary_retrieval_questions:
  - How does IronDev apply a PromotionPackage safely?
  - What does isolated promotion apply prove?
  - Is IronDev allowed to write to main after 170?
boundary: Isolated candidate workspace only. No main writes, accepted memory mutation, ticket acceptance, auto-merge, or self-approval.
---

# Isolated Promotion Apply Proof 170

## Purpose

Slice 170 proves that a `PromotionPackage` can become an isolated candidate workspace without touching the active IronDev working tree.

The bridge is:

```text
PromotionPackage
  -> isolated candidate workspace outside the repo
  -> copy only FilesToPromote
  -> reject FilesBlocked
  -> runtime build/test
  -> isolated apply report
  -> human/Codex review
```

This is still not real repository write approval.

## Command

Apply an existing package into an isolated candidate workspace:

```text
promotion apply isolated --package-run-id <package-run> --run-id <apply-run> --json
```

Run the full proof:

```text
campaign isolated-promotion-apply-170 --run-id <run> --json
```

The campaign first runs 169 to create a fresh package, then applies that package into the isolated candidate workspace.

## What 170 Proves

- `PromotionPackage` is readable and actionable.
- `ProposedChangeId` remains attached to the apply proof.
- C#/.NET runtime profile can build and test the isolated candidate.
- Only `FilesToPromote` are copied.
- `FilesBlocked` remain rejected.
- Generated `bin/` and `obj/` outputs are not promoted.
- The isolated candidate workspace is outside the active repo.
- The active repo status is unchanged before/after the apply proof.
- The result produces JSON and Markdown reports.

## What 170 Does Not Prove

- Real repo writes.
- Main branch writes.
- Pull request creation.
- Auto-merge.
- Accepted memory mutation.
- Ticket acceptance.
- Autonomous approval.

## Output

The command writes:

```text
tools/dogfood/runs/{runId}/isolated-promotion-apply-report.json
tools/dogfood/runs/{runId}/isolated-promotion-apply-report.md
tools/dogfood/runs/{runId}/isolated-workspace-manifest.json
tools/dogfood/runs/{runId}/logs/isolated-build.log
tools/dogfood/runs/{runId}/logs/isolated-test.log
```

The report includes:

- package id
- proposed change id
- source run/trace ids
- isolated workspace path
- isolated branch name
- runtime profile
- applied files and hash checks
- rejected blocked files
- build/test evidence
- active repo mutation count
- recommendation
- approval state

## Blunt Assessment

This is the right next rung after 169.

IronDev can now move from disposable output to an isolated candidate without crossing the real repo write boundary.

The next slice should harden the review UI and then design the real branch/PR path as a separate, explicit gate.
