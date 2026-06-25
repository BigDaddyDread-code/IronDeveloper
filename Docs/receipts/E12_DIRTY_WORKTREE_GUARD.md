# E12 - Dirty Worktree Guard

## Review Line

A clean worktree observation is evidence. It is not source authority.

## Killjoy

A dirty worktree is a stop sign. A clean worktree is not a green light.

## Purpose

Block E12 adds a backend-only, Core-only dirty worktree guard contract and shared guard service.

The dirty worktree guard consumes reference-only evidence. It does not inspect the worktree.

It answers only whether supplied worktree-state evidence must stop before the next authority gate.

## Boundary

E12 is read-only. It does not inspect source, inspect worktrees, read source files, call Git, call GitHub, shell out, clean the worktree, stash changes, reset changes, revert changes, checkout branches, apply source, rollback source, recover state, commit, push, open pull requests, merge, release, deploy, acquire locks, release locks, renew locks, enforce locks, write operation status, write receipts, write read models, promote memory, satisfy policy, approve work, or continue workflow.

`MayProceedToNextAuthorityGate` is not mutation authority.

Clean worktree evidence only says the dirty-worktree guard did not stop the flow. Fresh authority, validation, concurrent guard evidence, post-state observation, and human review remain required before mutation-adjacent execution.

Dirty worktree evidence is a stop sign, not rollback authority.

Conflict or in-progress source-state evidence is not recovery authority.

Head match evidence is not push authority.

Branch match evidence is not checkout authority.

## Explicit Denials

E12 does not grant:

- source safety
- source apply authority
- commit authority
- push authority
- pull request authority
- retry authority
- recovery authority
- rollback authority
- mutation authority
- approval
- policy satisfaction
- validation freshness
- patch freshness
- workflow continuation
- merge readiness
- release readiness
- deployment readiness

## Guard Rules

- Unknown subject, evidence kind, trust level, freshness, or worktree state blocks fail-closed.
- Only `Clean` may proceed to the next authority gate.
- Dirty, modified, untracked, deleted, renamed, conflict, merge/rebase/cherry-pick in progress, detached head, and index-locked states block.
- Unknown, unreadable, and unavailable states block as unknown worktree state.
- Stale, expired, and not-timestamped observations block.
- Self-reported evidence requires corroboration and still cannot proceed.
- Test-fixture and synthetic-test evidence are valid only from test-labelled sources and cannot proceed.
- Post-state-backed evidence requires a post-state observation ref.
- Receipt-backed evidence requires a failure or mutation receipt ref.
- Provider-metadata-backed evidence requires a provider-state ref.
- Operator-observed evidence requires an operator-observation ref.
- Expected head, branch, and fingerprint refs must match observed refs when supplied.
- Raw Git status, raw Git output, raw file lists, raw patch, raw diff, raw source, command text, provider body, credentials, private reasoning, and authority-claim text are rejected.
- Valid domain refs such as `patch-package:*`, `merge-target:*`, `release-candidate:*`, and `deploy-target:*` are not rejected merely because they name mutation domains.

## Validation

- Focused E12 validation: 96/96 passed
- E11/E12 compatibility validation: 189/189 passed
- E01-E12 corridor: 908/908 passed
- Combined A02/A05 + D01-D20 + E01-E12 corridor: 2408/2408 passed
- Governance boundary CI: passed locally, including security boundary scan
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject this slice if:

- E12 runs worktree inspection
- E12 reads source files
- E12 shells out
- E12 calls Git or GitHub
- E12 cleans, stashes, resets, reverts, or checks out anything
- E12 treats clean worktree evidence as mutation authority
- E12 treats dirty worktree evidence as rollback authority
- E12 treats conflict evidence as recovery authority
- E12 treats head match as push authority
- E12 treats branch match as checkout authority
- E12 accepts stale or expired evidence
- E12 accepts self-reported evidence as proceedable
- E12 accepts test-fixture evidence as proceedable
- E12 stores raw Git output, raw status, file lists, patch, diff, source, command text, provider output, or private material
- broad unsafe marker scanning rejects valid refs like `release-candidate:e12`

## Killjoy Restated

A dirty worktree is a stop sign. A clean worktree is not a green light.
