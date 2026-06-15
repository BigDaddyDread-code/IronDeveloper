# PR150 - Governance Data Retention and Cleanup Rules

PR150 adds Governance Data Retention and Cleanup Rules.

This is a Core rule contract and evaluator for classifying governance data retention posture and future cleanup-review eligibility.

It is not a cleanup executor.

## Boundary

Retention rule evaluation is not cleanup execution.

Cleanup eligibility is not deletion permission.

Cleanup recommendation is not cleanup approval.

Expired retention window is not purge authority.

Archive recommendation is not archive execution.

Redaction recommendation is not redaction execution.

Legal hold beats cleanup.

Audit hold beats cleanup.

Governance events are append-only and preserved.

Authority decision records are preserved unless a later explicitly governed retention executor exists.

Retention durations are engineering defaults for future governed review, not legal advice and not cleanup execution.

## What changed

- Added Governance Data Retention Rule models.
- Added a rule evaluator service.
- Added retention class and preservation reason contracts.
- Added cleanup recommendation contracts.
- Added boundary tests proving rules do not delete, purge, archive, redact, schedule, or mutate.
- Added static no-cleanup-execution tests.
- Added this PR150 receipt.

## What this does not do

This PR does not delete data, purge data, archive data, redact data, run cleanup, schedule cleanup, mutate SQL, run migrations, create background jobs, create hosted services, expose API write endpoints, expose CLI cleanup commands, bypass legal holds, bypass audit holds, expose raw payloads, expose raw prompts/completions/tool outputs, expose source content, expose patch payloads, or expose hidden/private reasoning.

It does not add an API, CLI command, UI, SQL migration, hosted service, background worker, scheduler, cleanup runner, or retention executor.

## Preservation posture

Governance events are preserved as append-only audit evidence.

Approval decisions, policy decision events, and tool gate decisions are preserved as authority decision records.

Workflow runs are preserved for the engineering audit window.

Open workflow, approval, policy, tool gate, and memory proposal references preserve the related record while referenced.

Legal hold and audit hold always block cleanup review.

Records with private payload risk, unknown kind, missing created timestamp, or missing required correlation reference require human review.

Old unreferenced report summaries may become eligible for future human cleanup review after an engineering audit window.

Even then, eligibility is review material only.

## Review line

PR150 writes the cleanup law. It does not swing the broom.
