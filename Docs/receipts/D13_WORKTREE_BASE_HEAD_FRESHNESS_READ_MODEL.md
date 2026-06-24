# D13 Worktree Base Head Freshness Read Model

## Purpose

D13 adds a read-only worktree / base / head freshness read model for governed operations.

The read model consumes scoped expectation metadata, scoped observation metadata, and scoped freshness rules. It classifies supplied source-state metadata as fresh, dirty, conflicted, base moved, head moved, detached, missing, repository mismatched, stale, expired, missing expectation, missing observation, missing rule, ambiguous, or unassessable.

## Stack

Stack base while open:

```text
status/patch-base-freshness-resolver
```

Suggested title:

```text
core(status): add worktree base head freshness read model
```

## Files

```text
IronDev.Core/Governance/WorktreeBaseHeadFreshnessReadModelModels.cs
IronDev.Core/Governance/WorktreeBaseHeadFreshnessReadModelValidator.cs
IronDev.Core/Governance/WorktreeBaseHeadFreshnessReadModelAssembler.cs
IronDev.IntegrationTests/BlockD13WorktreeBaseHeadFreshnessReadModelTests.cs
Docs/receipts/D13_WORKTREE_BASE_HEAD_FRESHNESS_READ_MODEL.md
```

## Boundary

The worktree/base/head freshness read model classifies supplied expectation and observation metadata using supplied freshness rules and supplied AsOfUtc only. It does not call Git, inspect source files, inspect worktree state, inspect HEAD, fetch refs, read raw patches or diffs, accept approval, satisfy policy, grant authority, choose next safe action, execute mutation, apply patches, commit, push, create PRs, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

D13 is metadata-only. It does not produce observations, fetch observations, inspect live source state, read raw source content, read raw patch content, read raw diff content, run validation, calculate missing evidence, resolve forbidden actions, resolve receipts, resolve evidence, resolve validation staleness, resolve patch/base freshness, or write stores.

Fresh worktree/base/head metadata is not authority.

Clean worktree metadata is not commit permission.

Matching head metadata is not push permission.

Matching base metadata is not merge readiness.

Dirty worktree metadata is not policy denial.

Detached head metadata is not next-safe-action selection.

Complete worktree/base/head assessment is not action allowed.

Ambiguous worktree/base/head observations never silently select a winner.

## Validation

Recorded after implementation:

```text
Focused D13: 128/128 passed
D01-D13 stacked resolver/read-model lane: 1041/1041 passed
A02 + A05 read-adapter corridor: 61/61 passed
Governance/status corridor: 1221/1221 passed
Build: 0 errors / 4 warnings
git diff --check: passed
git diff --cached --check: passed
```

## Review Line

Worktree/base/head freshness explains supplied source-state observations. It does not authorize source mutation.

## Killjoy

A clean worktree and matching head are still not permission to apply, commit, push, or open a PR.
