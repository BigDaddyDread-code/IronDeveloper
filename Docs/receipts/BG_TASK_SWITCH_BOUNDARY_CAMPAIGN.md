# BG - Task Switch Boundary Campaign

## Purpose

Block BG adds an adversarial task-switch boundary campaign.

BG tests whether IronDev can move from one governed task to another without carrying old authority, stale receipts, memory claims, workflow state, or previous approvals into the new task.

Core rule:

```text
Context may transfer.
Authority must not transfer.
```

## Boundary

BG is a test campaign.

BG does not mutate.
BG does not approve.
BG does not execute.
BG does not deploy.
BG does not rollback.
BG does not release.
BG does not promote memory.
BG does not continue workflow.
BG does not publish packages.
BG does not dispatch pipelines.
BG does not mutate source.
BG does not mutate environments.

Memory may inform planning.
Memory must not become permission.

Receipts may inform planning.
Receipts must not become next-task authority.

Workflow state may inform history.
Workflow state must not continue a new task.

Wrong package type must block.
Stale package must block.
Rollback consideration is not rollback execution.

## Campaign Artifacts

BG produces machine-readable campaign evidence:

```text
task-switch-boundary-scenarios.jsonl
task-switch-boundary-summary.json
task-switch-boundary-failures.jsonl
task-switch-boundary-friction.csv
task-switch-boundary-report.md
```

JSON and JSONL are evidence.
Markdown explains the evidence.
Markdown is not the evidence.

## Required Scenario Shape

Each scenario records:

```text
old authority supplied
new task requested
required authority
authority relationship
expected verdict
actual verdict
expected block reason
actual block reason
mutation attempted
mutation completed
old authority context/permission usage
memory context/permission usage
workflow state transfer
CLI exit code
receipt usefulness
human next step
friction metrics
pass/fail
```

## Red Rules

The campaign fails if:

```text
mutation completes for a blocked/rejected task switch
old authority becomes permission
memory becomes permission
workflow state transfers as permission
wrong package type is accepted
stale authority is accepted
rollback consideration becomes rollback execution
rollback decision becomes rollback execution
```

## Amber Rules

The campaign reports friction and review problems when:

```text
block reason is generic
safe next step is missing
human-readable reason is missing
human cannot choose the next safe step
manual steps are high
copied identifiers are high
JSON inspection load is high
receipt noise is high
```

## CLI

Allowed command:

```text
irondev task-switch-boundary-campaign run --campaign-id <campaign-id> --scenario-set <default|phase5|full> --out <path> [--json]
```

Read-only commands:

```text
irondev task-switch-boundary-campaign inspect --campaign <campaign-output-dir> [--json]
irondev task-switch-boundary-campaign summary --campaign <campaign-output-dir> [--json]
irondev task-switch-boundary-campaign failures --campaign <campaign-output-dir> [--json]
irondev task-switch-boundary-campaign friction --campaign <campaign-output-dir> [--json]
```

Forbidden verbs:

```text
approve
execute
deploy
rollback
release
merge
source-apply
commit
push
publish
publish-package
promote-memory
continue
continue-workflow
dispatch
trigger-pipeline
mutate
```

## Review Line

Block BG runs a task-switch boundary campaign that tests whether old receipts, old approvals, memory, workflow state, and wrong package types can leak authority into a new task. It produces structured evidence and reports. It does not mutate source, release, deploy, rollback, publish packages, promote memory, continue workflow, or approve anything.

## Killjoy

BG is not here to make IronDev look smart.

BG is here to catch the machine cheating.

If old authority can cross into a new task, Phase 5 did not build governance. It built a better-shaped bypass.
