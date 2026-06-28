# E13 - Moved-Base Guard

## Review Line

A matching base is evidence. It is not permission to apply, commit, push, or merge.

## Killjoy

Base did not move is not a green light. Base moved is a stop sign.

## Purpose

Block E13 adds a backend-only, Core-only moved-base guard contract and shared guard service.

The moved-base guard consumes reference-only evidence. It does not inspect Git, GitHub, branches, commits, remotes, or source.

It answers only whether supplied base/head/ref evidence must stop before the next authority gate.

## Boundary

E13 is read-only. It does not query Git, call GitHub, compare commits, inspect branches, fetch remotes, inspect source, inspect worktrees, read source files, checkout branches, pull, rebase, merge, apply source, rollback source, recover state, commit, push, open pull requests, release, deploy, acquire locks, release locks, renew locks, enforce locks, write operation status, write receipts, write read models, promote memory, satisfy policy, approve work, or continue workflow.

`MayProceedToNextAuthorityGate` is not mutation authority.

Matching ref evidence only says the moved-base guard did not stop the flow. Fresh authority, validation, concurrent guard evidence, dirty worktree guard evidence, post-state observation, and human review remain required before mutation-adjacent execution.

Moved base evidence is a stop sign, not rebase authority.

Head movement evidence is not pull, fetch, push, or merge authority.

Branch movement evidence is not checkout authority.

Divergence evidence is not merge or reconciliation authority.

## Explicit Denials

E13 does not grant:

- source safety
- source apply authority
- commit authority
- push authority
- pull request authority
- merge authority
- retry authority
- recovery authority
- rollback authority
- mutation authority
- approval
- policy satisfaction
- validation freshness
- patch freshness
- workflow continuation
- release readiness
- deployment readiness

## Guard Rules

- Unknown subject, evidence kind, trust level, freshness, or observed state blocks fail-closed.
- Only `Matching` may proceed to the next authority gate.
- Base moved, merge-base moved, head moved, remote head moved, and branch moved states block.
- Diverged, ahead, and behind states block because this guard does not decide reconciliation safety.
- Missing, deleted, unavailable, ambiguous, and unknown states block as unknown base state.
- Stale, expired, and not-timestamped observations block.
- Self-reported evidence requires corroboration and still cannot proceed.
- Test-fixture and synthetic-test evidence are valid only from test-labelled sources and cannot proceed.
- Matching state requires a ref observation, observed base, observed head, observed branch, and observed target fingerprint.
- Post-state-backed evidence requires a post-state observation ref.
- Dirty-worktree-guard-backed evidence requires a dirty-worktree guard ref.
- Receipt-backed evidence requires the matching receipt/package ref.
- Provider-backed evidence requires provider state.
- Operator-observed evidence requires an operator-observation ref.
- Expected base, head, remote head, branch, merge-base fingerprint, and target fingerprint refs must match observed refs when supplied.
- Raw Git log, raw Git rev-parse, raw Git merge-base, raw branch lists, raw commit graphs, raw provider branch responses, raw pull request responses, raw patch, raw diff, raw source, command text, provider body, credentials, private reasoning, and authority-claim text are rejected.
- Valid domain refs such as `patch-package:*`, `merge-target:*`, `release-candidate:*`, and `deploy-target:*` are not rejected merely because they name mutation domains.

## Validation

- Focused E13 validation: 117/117 passed
- E12/E13 compatibility validation: 213/213 passed
- E01-E13 corridor: 1025/1025 passed
- Combined A02/A05 + D01-D20 + E01-E13 corridor: 2525/2525 passed
- Governance boundary CI: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject this slice if:

- E13 runs Git commands
- E13 reads `.git`
- E13 calls GitHub
- E13 compares commits directly
- E13 fetches remotes
- E13 checks out, pulls, rebases, or merges
- E13 reads source files
- E13 shells out
- E13 treats matching base evidence as source apply authority
- E13 treats matching head evidence as commit or push authority
- E13 treats matching PR base evidence as merge authority
- E13 treats moved base evidence as rebase authority
- E13 treats divergence evidence as merge authority
- E13 accepts stale or expired evidence
- E13 accepts self-reported evidence as proceedable
- E13 accepts test-fixture evidence as proceedable
- E13 stores raw Git output, raw commit graph, branch lists, patch, diff, source, command text, provider output, or private material
- broad unsafe marker scanning rejects valid refs like `release-candidate:e13`

## Killjoy Restated

Base did not move is not a green light. Base moved is a stop sign.
