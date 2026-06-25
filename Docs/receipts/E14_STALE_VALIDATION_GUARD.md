# E14 - Stale Validation Guard

## Review Line

Validation passed then is not validation fresh now.

## Killjoy

A fresh validation record is evidence. It is not approval, policy satisfaction, or mutation authority.

## Purpose

Block E14 adds a backend-only, Core-only stale validation guard contract and shared guard service.

The stale validation guard consumes reference-only validation evidence. It does not run validation, tests, builds, CI, GitHub checks, or log parsing.

It answers only whether supplied validation evidence must stop before the next authority gate.

## Boundary

E14 is read-only. It does not run validation, run tests, run builds, call CI, call GitHub, parse logs, inspect source, inspect worktrees, read raw test output, read raw build output, read raw CI output, read raw provider output, shell out, call executors, write operation status, write receipts, write read models, write retry/recovery records, apply source, rollback source, commit, push, open pull requests, merge, release, deploy, acquire locks, release locks, renew locks, enforce locks, promote memory, satisfy policy, approve work, or continue workflow.

`MayProceedToNextAuthorityGate` is not mutation authority.

Fresh validation evidence only says the stale-validation guard did not stop the flow. Fresh authority, fresh validation, concurrent guard evidence, dirty worktree guard evidence, moved-base guard evidence, post-state observation, and human review remain required before mutation-adjacent execution.

Failed or incomplete validation is a stop sign, not retry, recovery, rollback, or continuation authority.

Validation target match is evidence, not source apply, commit, push, pull request, merge, release, or deploy authority.

## Explicit Denials

E14 does not grant:

- approval
- policy satisfaction
- validation execution
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
- workflow continuation
- release readiness
- deployment readiness

## Guard Rules

- Unknown subject, validation evidence kind, trust level, freshness, outcome, or scope blocks fail-closed.
- Only `Passed` validation with `Fresh` observation freshness may proceed to the next authority gate.
- Failed, timed-out, and cancelled validation states block as failed validation.
- Not-run, partial, and unavailable validation states block as incomplete validation.
- Unknown validation state blocks as unknown validation state.
- Stale, expired, and not-timestamped observations block.
- Self-reported evidence requires corroboration and still cannot proceed.
- Test-fixture and synthetic-test validation evidence are valid only from test-labelled sources and cannot proceed.
- Passed validation requires validation evidence ref, observed validation target ref, and observed validation fingerprint.
- Positive decisions require concurrent guard, dirty worktree guard, moved-base guard, and post-state observation refs.
- Receipt-backed validation requires validation/build/test/governance receipt evidence.
- Provider-backed validation requires provider CI state evidence.
- Operator-observed validation requires operator observation evidence.
- Expected validation target, validation fingerprint, source state, patch package, commit, head, and base refs must match observed refs when supplied.
- Raw test output, raw build output, raw CI output, raw console output, raw failure logs, raw stack traces, raw command lines, raw Git output, raw provider responses, raw patch, raw diff, raw source, private reasoning, credentials, and authority-claim text are rejected.
- Rejected unsafe payloads are not echoed through returned decision evidence fields.
- Valid domain refs such as `patch-package:*`, `merge-target:*`, `release-candidate:*`, and `deploy-target:*` are not rejected merely because they name mutation domains.

## Validation

- Focused E14 validation: 127/127 passed
- E13/E14 compatibility validation: 244/244 passed
- E01-E14 corridor: 1152/1152 passed
- Combined A02/A05 + D01-D20 + E01-E14 corridor: 2652/2652 passed
- Governance boundary CI: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject this slice if:

- E14 runs validation
- E14 runs tests
- E14 runs builds
- E14 calls CI
- E14 calls GitHub
- E14 parses logs
- E14 shells out
- E14 reads source files
- E14 inspects worktrees
- E14 calls Git
- E14 compares commits
- E14 calls E08/E09/E10/E11/E12/E13 services
- E14 calls or wires executors
- E14 writes operation status, receipts, read models, retry records, or recovery records
- E14 treats passed validation as approval
- E14 treats passed validation as policy satisfaction
- E14 treats passed validation as source safety
- E14 treats validation target match as source apply, commit, push, PR, merge, release, or deploy authority
- E14 accepts stale validation evidence
- E14 accepts expired validation evidence
- E14 accepts failed, partial, timed-out, cancelled, not-run, unavailable, or unknown validation evidence
- E14 accepts self-reported evidence as proceedable
- E14 accepts test-fixture evidence as proceedable
- E14 stores raw test output, build output, CI output, logs, stack traces, source, command text, provider output, or private material
- broad unsafe marker scanning rejects valid refs like `release-candidate:e14`

## Killjoy Restated

A fresh validation record is evidence. It is not approval, policy satisfaction, or mutation authority.
