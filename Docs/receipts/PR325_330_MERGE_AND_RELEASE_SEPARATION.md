# PR325-330 - Merge and Release Separation

Block AO separates merge readiness from release readiness.

It does not merge.
It does not release.
It does not deploy.
It does not tag.
It does not publish.
It does not continue workflow.

## What Landed

- AO1 merge/release separation request.
- AO2 merge readiness evidence package.
- AO3 release readiness evidence package.
- AO4 merge-to-release boundary map.
- AO5 separation readiness records.
- AO6 bypass tests and receipt.

## Boundary

CI pass is not merge permission.
Review approval is not merge permission.
No known blocking feedback is not merge permission.
A draft pull request is not merge readiness.
Merge readiness is not release readiness.
A merge decision is not a release decision.
A merged pull request is not release candidate evidence.
A merged PR is not a deployment.
Release readiness is not release execution.
A release-readiness report is not a release.

Block AO may say what evidence is missing before merge or release.

Block AO may not perform merge or release.

## CLI Surface

Supported commands:

- `irondev merge-release request --run <run-id-or-path> --repo <owner/name> --pr <number> --expected-head <sha> [--json]`
- `irondev merge-release merge-evidence --run <run-id-or-path> [--json]`
- `irondev merge-release release-evidence --run <run-id-or-path> [--json]`
- `irondev merge-release boundary-map --run <run-id-or-path> [--json]`
- `irondev merge-release records --run <run-id-or-path> [--reviewed-by <name>] [--json]`
- `irondev merge-release status --run <run-id-or-path> [--json]`

Unsupported authority-shaped commands:

- `merge`
- `auto-merge`
- `enable-auto-merge`
- `release`
- `deploy`
- `tag`
- `publish`
- `continue`

## Artifact Set

The AO CLI writes run-scoped artifacts:

- `merge-release-separation-request.json`
- `merge-release-separation-request.md`
- `merge-readiness-evidence-package.json`
- `merge-readiness-evidence-report.md`
- `merge-readiness-blockers.jsonl`
- `merge-evidence-gaps.jsonl`
- `release-readiness-evidence-package.json`
- `release-readiness-evidence-report.md`
- `release-readiness-blockers.jsonl`
- `release-evidence-gaps.jsonl`
- `merge-release-boundary-map.json`
- `merge-release-boundary-map.md`
- `merge-release-boundary-violations.jsonl`
- `merge-separation-readiness-record.json`
- `release-separation-readiness-record.json`
- `merge-release-separation-report.json`
- `merge-release-separation-report.md`
- `merge-release-bypass-report.json`
- `merge-release-bypass-report.md`
- `governance-events.jsonl`

## Evidence Families

Merge evidence includes commit package evidence, pull request creation evidence, CI observation, review feedback, feedback readiness, artifact consistency evidence, and unsafe material evidence.

Release evidence includes product hardening evidence, release-readiness reports, release-readiness decision records, known risks, recovery/resume evidence, artifact consistency evidence, and unsafe material evidence.

Shared evidence is only shared where explicitly allowed.

Forbidden cross-use:

- CI pass cannot be release evidence by itself.
- Review approval cannot be release evidence.
- Merge readiness cannot be release readiness.
- Release readiness cannot be merge readiness.
- Draft PR creation cannot be merge readiness.
- No known blocking feedback cannot be release readiness.
- A draft pull request cannot be merge decision readiness.
- A pull request URL cannot be release candidate evidence.

## Bypass Proof

These remain evidence only and cannot merge, release, deploy, tag, publish, or continue workflow:

- CI pass
- review approval
- no known blocking feedback
- feedback readiness report
- merge readiness evidence package
- release readiness evidence package
- merge-release boundary map
- merge separation readiness record
- release separation readiness record
- release-readiness report
- human-looking approval text
- AI review text
- memory plan text
- test success
- build success

## Review

Block AO separates merge readiness from release readiness. It does not merge, release, deploy, tag, publish, or continue workflow.

## Killjoy

Merging code is not shipping product.
