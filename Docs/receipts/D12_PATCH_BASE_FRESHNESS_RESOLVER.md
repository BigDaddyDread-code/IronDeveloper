# D12 Patch Base Freshness Resolver

## Purpose

D12 adds a read-only patch hash / base branch freshness resolver for governed operations.

The resolver consumes scoped patch artifact metadata, scoped base branch observation metadata, and scoped freshness rules. It classifies whether supplied patch metadata is fresh, stale, expired, hash-mismatched, base-moved, missing a rule, missing base observation, ambiguous, or unassessable.

## Stack

Stack base while open:

```text
status/validation-result-staleness-resolver
```

Suggested title:

```text
core(status): add patch base freshness resolver
```

## Files

```text
IronDev.Core/Governance/PatchBaseFreshnessResolverModels.cs
IronDev.Core/Governance/PatchBaseFreshnessResolverValidator.cs
IronDev.Core/Governance/PatchBaseFreshnessResolver.cs
IronDev.IntegrationTests/BlockD12PatchBaseFreshnessResolverTests.cs
Docs/receipts/D12_PATCH_BASE_FRESHNESS_RESOLVER.md
```

## Boundary

The patch hash / base branch freshness resolver classifies supplied patch artifact metadata and supplied base branch observation metadata using supplied freshness rules and supplied AsOfUtc only. It does not call Git, inspect source or worktree state, calculate patch hashes from raw patches, fetch raw patch content, accept approval, satisfy policy, grant authority, choose next safe action, execute mutation, apply patches, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

D12 is metadata-only. It does not fetch raw patch content, return raw patch content, fetch raw diff content, return raw diff content, inspect source files, inspect worktree state, inspect HEAD state, fetch remote refs, calculate validation freshness, calculate missing evidence, resolve forbidden actions, resolve receipts, resolve evidence, run validation, or write stores.

Fresh patch metadata is not authority.

Patch hash match is not approval.

Matching base branch metadata is not source apply permission.

Base moved is not merge denial.

Patch expired is not next-safe-action selection.

Complete patch/base assessment is not action allowed.

Ambiguous patch/base metadata never silently selects a winner.

## Validation

Local validation on `status/patch-base-freshness-resolver`:

```text
Focused D12: 109/109 passed
D01-D12 stacked resolver lane: 913/913 passed
A02 + A05 read-adapter corridor: 61/61 passed
Governance/status corridor through D12: 1093/1093 passed for BJ/BK/BL/BT/BZ plus D01-D12
dotnet build IronDev.slnx --no-restore -v:minimal: 0 errors / 4 warnings
git diff --check: passed
git diff --cached --check: passed
```

## Review Line

Patch/base freshness explains whether supplied patch metadata still lines up. It does not authorize source mutation.

## Killjoy

A fresh patch hash is still not permission to apply the patch.
