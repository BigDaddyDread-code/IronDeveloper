# PR279-283 Controlled Source Apply and Rollback Receipt

## Purpose

Block AG adds the first controlled source repository working-tree mutation path.

This block allows IronDev to apply a prepared patch to the source repository working tree only after the Block AF readiness package, explicit human Conscience decision, ThoughtLedger reference, source snapshot, rollback draft, and execution gate all pass.

It also adds the matching controlled rollback path for that exact applied patch when the current source diff still matches the recorded post-apply diff.

## Boundary

This block performs source working-tree mutation.

It does not create git commits.
It does not push.
It does not create pull requests.
It does not merge.
It does not release.
It does not deploy.
It does not continue workflow.
It does not approve release.
It does not satisfy policy.
It does not promote memory.
It does not dispatch agents.
It does not call models.
It does not create API, SQL, UI, scheduler, worker, or autonomous runtime behavior.

A successful source apply leaves uncommitted working-tree changes in the source repository.

A successful rollback removes those uncommitted working-tree changes only when the current diff matches the recorded post-apply diff.

## AG1 approval binding

Block AG now has an explicit approval-binding seam before source apply.

The binding seam writes:

- `source-apply-request.json`
- `source-apply-request.md`
- `source-apply-binding-report.json`
- `source-apply-binding-report.md`

The binding report proves the approval belongs to the same run, source-apply request, patch hash, changed files, source repository identity, and base commit. It also requires a Conscience decision reference, a ThoughtLedger entry reference, a human reviewer, and bounded approval language.

Approval binding is evidence only. It does not apply source, grant commit permission, grant push permission, create pull requests, merge, release, deploy, rollback, continue workflow, satisfy policy, or promote memory.

## Added CLI surface

- `irondev source-apply request --run <run-id> [--json]`
- `irondev source-apply decision-template --run <run-id> --out <decision.json> [--json]`
- `irondev source-apply validate-approval --run <run-id> --approval <approval.json> [--json]`
- `irondev source-apply approval-status --run <run-id> [--json]`
- `irondev source-apply apply --run <run-id> --decision <source-apply-decision.json> --thought-ledger-ref <ref> [--json]`
- `irondev source-apply rollback-template --run <run-id> --out <rollback-decision.json> [--json]`
- `irondev source-apply rollback --run <run-id> --decision <rollback-decision.json> --thought-ledger-ref <ref> [--json]`
- `irondev source-apply applied-status --run <run-id> [--json]`

Forbidden command shapes remain unsupported:

- `irondev source-apply commit`
- `irondev source-apply push`
- `irondev source-apply pr`
- `irondev source-apply merge`
- `irondev source-apply release`
- `irondev source-apply deploy`

## Apply receipts

Controlled apply writes:

- `source-apply-execution-request.json`
- `source-apply-execution-gate-decision.json`
- `source-apply-pre-source-snapshot.json`
- `source-apply-command-result.json`
- `source-apply-post-source-snapshot.json`
- `source-apply-diff-after.diff`
- `source-apply-receipt.json`
- `source-apply-receipt.md`
- `source-apply-output/apply.stdout.txt`
- `source-apply-output/apply.stderr.txt`
- `source-apply-output/apply.combined.txt`
- `governance-events.jsonl`

The receipt records mutation evidence only. It is not commit permission, push permission, PR creation, merge readiness, release readiness, release approval, deployment approval, workflow continuation, policy satisfaction, or memory promotion.

## Rollback receipts

Controlled rollback writes:

- `source-rollback-request.json`
- `source-rollback-gate-decision.json`
- `source-rollback-command-result.json`
- `source-rollback-receipt.json`
- `source-rollback-receipt.md`
- `source-rollback-output/rollback.stdout.txt`
- `source-rollback-output/rollback.stderr.txt`
- `source-rollback-output/rollback.combined.txt`
- `governance-events.jsonl`

The rollback receipt records rollback mutation evidence only. It is not cleanup certification, release readiness, release approval, workflow continuation, policy satisfaction, or memory promotion.

## Review line

PR279-283 turns the key in the source working tree, then files the receipt. It does not commit, push, merge, release, or drive away.
