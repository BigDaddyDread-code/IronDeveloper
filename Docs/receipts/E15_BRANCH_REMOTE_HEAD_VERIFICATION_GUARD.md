# E15 - Branch / Remote / Head Verification Guard

## Review Line

The branch you meant is not automatically the branch you are on.

## Killjoy

A matching branch/head observation is evidence. It is not source safety, validation freshness, approval, or mutation authority.

## Purpose

Block E15 adds a backend-only, Core-only branch / remote / head verification guard contract and shared guard service.

The guard consumes supplied reference-only branch, remote, and head observation evidence. It does not observe Git, GitHub, remotes, branches, worktrees, source files, CI, builds, tests, or provider state.

It answers only whether supplied branch / remote / head evidence is fresh, trusted, internally consistent, and matched to the expected mutation target before the flow may continue to the next authority gate.

## Boundary

E15 is read-only. It does not call Git/GitHub/CI/shell/file system. It does not fetch remotes, inspect `.git`, inspect worktrees, read source files, parse raw Git output, parse raw GitHub responses, parse raw provider output, run validation, run tests, run builds, acquire locks, release locks, renew locks, enforce locks, write operation status, write receipts, write read models, write retry records, write recovery records, apply source, rollback source, commit, push, open pull requests, mark pull requests ready, merge, release, deploy, approve work, satisfy policy, promote memory, or continue workflow.

`MayProceedToNextAuthorityGate` is not mutation authority.

Matching branch/head evidence only says the branch / remote / head verification guard did not stop the flow. Fresh authority, fresh validation, dirty worktree guard evidence, moved-base guard evidence, stale-validation guard evidence, concurrent guard evidence, post-state observation, and human review remain required before mutation-adjacent execution.

## Explicit Denials

E15 does not grant:

- approval
- policy satisfaction
- validation freshness
- source safety
- source apply authority
- commit authority
- push authority
- pull request authority
- merge authority
- release authority
- deployment authority
- retry authority
- recovery authority
- rollback authority
- mutation authority
- workflow continuation

## Guard Rules

- Unknown subject, evidence kind, trust level, freshness, or verification outcome blocks fail-closed.
- Only `Verified` branch / remote / head evidence with `Fresh` observation freshness may proceed to the next authority gate.
- Stale, expired, and not-timestamped observations block.
- Detached head, ambiguous branch, missing branch, missing remote, missing head, unavailable remote, and deleted remote branch states block.
- Branch, remote, remote URL fingerprint, local head, remote head, base, source-state, patch-package, and commit mismatches block when expected refs are supplied.
- E15 does not hard-code local head equals remote head for every mutation surface.
- Self-reported evidence requires corroboration and still cannot proceed.
- Test-fixture branch evidence is valid only from test-labelled sources and cannot proceed.
- Positive decisions require branch observation, remote observation, head observation, dirty worktree guard, moved-base guard, stale-validation guard, concurrent guard, and post-state observation refs.
- Composite evidence requires composite observation refs.
- Provider-backed evidence requires provider branch state refs.
- Operator-observed evidence requires operator observation refs.
- Raw Git output, raw GitHub responses, raw provider responses, raw CI output, raw console output, raw command lines, raw remote URLs, credentials, raw patch, raw diff, raw source, raw logs, stack traces, private reasoning, and authority-claim text are rejected.
- Unsafe rejected evidence is not echoed through returned decisions.
- Valid domain refs such as `branch:feature/e15`, `remote:origin`, `head:e15`, `base:e15`, `patch-package:e15`, `merge-target:e15`, `release-candidate:e15`, and `deploy-target:e15` are not rejected merely because they name mutation domains.

## Validation

- Focused E15 validation: 145/145 passed
- E14/E15 compatibility validation: 272/272 passed
- E01-E15 corridor: 1297/1297 passed
- Combined A02/A05 + D01-D20 + E01-E15 corridor: 2797/2797 passed
- Governance boundary CI: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject this slice if:

- E15 calls Git
- E15 calls GitHub
- E15 fetches remotes
- E15 reads `.git`
- E15 shells out
- E15 reads source files
- E15 inspects worktrees
- E15 parses raw Git/GitHub/provider output
- E15 runs validation, tests, builds, or CI
- E15 writes operation status, receipts, read models, retry records, or recovery records
- E15 acquires, releases, renews, or enforces locks
- E15 calls E08-E14 services directly
- E15 grants source safety
- E15 grants approval
- E15 grants policy satisfaction
- E15 grants mutation authority
- E15 grants apply, commit, push, pull request, merge, release, or deploy authority
- E15 treats matching branch/head evidence as validation freshness
- E15 treats branch verification as dirty worktree safety
- E15 treats branch verification as moved-base safety
- E15 hard-codes local head must always equal remote head for every mutation surface
- E15 accepts self-reported evidence as proceedable
- E15 accepts test-fixture evidence as proceedable
- E15 echoes unsafe rejected payloads
- E15 stores raw remote URLs, raw Git output, raw provider output, raw patches, raw diffs, credentials, or private material
- broad unsafe marker scanning rejects valid domain refs such as `release-candidate:e15`

## Killjoy Restated

A matching branch/head observation is evidence. It is not source safety, validation freshness, approval, or mutation authority.
