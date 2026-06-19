# PR302-308 - Product Hardening and Release Readiness

Block AI hardens the product and reports release readiness. It does not merge, release, deploy, or continue workflow.

## What landed

- AI1 dogfood script.
- AI2 artifact consistency auditor.
- AI3 secrets/unsafe material hardening.
- AI4 error recovery/resume hardening.
- AI5 release-readiness evaluator.
- AI6 release-readiness decision record.
- AI7 bypass tests and receipt.

## Boundary

Product hardening is evidence.
Release readiness is evidence.
Release readiness decision is bounded.
No merge/release/deploy authority is added.
No workflow continuation authority is added.
No source/workspace mutation authority is added.

## Artifacts

The `irondev product-hardening dogfood` command writes run-scoped evidence artifacts:

- `dogfood-run.json`
- `dogfood-run.md`
- `dogfood-artifact-checklist.json`
- `dogfood-artifact-checklist.md`
- `dogfood-known-risks.md`
- `artifact-consistency-report.json`
- `artifact-consistency-report.md`
- `artifact-consistency-issues.jsonl`
- `unsafe-material-report.json`
- `unsafe-material-report.md`
- `unsafe-material-findings.jsonl`
- `resume-report.json`
- `resume-report.md`
- `failure-summary.json`
- `failure-summary.md`
- `release-readiness-report.json`
- `release-readiness-report.md`
- `release-readiness-checklist.json`
- `release-readiness-blockers.jsonl`
- `release-readiness-decision-record.json`
- `release-readiness-decision-record.md`
- `product-hardening-bypass-report.json`
- `product-hardening-bypass-report.md`

The wrapper script lives at `tools/dogfood/Invoke-ProductHardeningDogfood.ps1`.

## Bypass Proof

These remain evidence only and cannot authorize merge, release, deploy, source mutation, or workflow continuation:

- dogfood success
- artifact consistency report
- unsafe material clean report
- resume report
- release-readiness report
- release-readiness decision record
- test success
- build success
- diff-check success

Commit, push, PR creation, merge, release, deploy, source apply, rollback, memory promotion, and workflow continuation command shapes remain unsupported by Block AI.

## Review

Block AI hardens the product and reports release readiness. It does not merge, release, deploy, or continue workflow.

## Killjoy

A readiness report is not a release. A green checklist is not permission to ship.
