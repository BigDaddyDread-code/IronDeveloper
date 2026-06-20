# BH - Authority UX / Receipt Interpretability

## Purpose

Block BH makes governed outcomes readable enough for a human to act safely.

BH converts existing governed evidence, starting with BG task-switch campaign rows, into deterministic machine-readable and human-readable authority explanations.

Each explanation records:

```text
supplied authority
required authority
authority relationship
source verdict
source block reason
block reason category
mutation attempted/completed state
old-authority permission leakage
memory permission leakage
workflow transfer
human summary
safe next step
red and amber flags
```

## Boundary

BH explains authority state.

BH does not create authority.
BH does not approve.
BH does not satisfy policy.
BH does not execute.
BH does not retry.
BH does not release.
BH does not deploy.
BH does not rollback.
BH does not source-apply.
BH does not mutate source.
BH does not mutate environments.
BH does not promote memory.
BH does not continue workflow.
BH does not dispatch pipelines.

Explanation is not permission.
Interpretability is not authority.
Safe next step is not execution.

Memory may explain context but cannot authorize action.
Workflow history may explain context but cannot continue action.
Rollback consideration may explain risk but cannot execute rollback.

## Inputs

The first BH adapter reads BG campaign scenario rows:

```text
task-switch-boundary-scenarios.jsonl
```

BH may read BG campaign output.
BH must not rewrite BG campaign output.

## Outputs

BH writes a separate authority UX report:

```text
authority-ux-explanations.jsonl
authority-ux-summary.json
authority-ux-report.md
authority-ux-red-findings.jsonl
authority-ux-amber-findings.jsonl
```

JSON and JSONL are evidence.
Markdown explains the evidence.
Markdown is not authority.

## Required Interpretability

BH preserves the source verdict and source block reason.

BH must not convert `Blocked` into `Success`.
BH must not convert `NeedsAuthority` into `Success`.
BH must not mark permission granted.

Unknown authority state must remain non-authoritative:

```text
Authority state could not be interpreted safely.
No mutation should proceed from this explanation.
```

## Red Flags

BH raises red flags for:

```text
MutationCompleted
OldAuthorityUsedAsPermission
MemoryUsedAsPermission
WorkflowStateTransferred
ExplanationChangedVerdict
ExplanationGrantedAuthority
RollbackConsiderationTreatedAsExecution
SuccessVerdictFromBlockedAuthority
UnsafeNextStepWouldMutate
```

## Amber Flags

BH raises amber flags for:

```text
GenericBlockReason
MissingSafeNextStep
HumanCannotChooseNextStep
UnsupportedReceiptKind
UnknownAuthorityRelationship
UnclassifiedBlockReason
HighReceiptNoise
HighManualSteps
HighJsonInspectionLoad
```

## CLI

Allowed report creation:

```text
irondev authority-ux explain-campaign --campaign <campaign-output-dir> --out <authority-ux-output-dir> [--json]
```

Read-only commands:

```text
irondev authority-ux inspect --report <authority-ux-output-dir> [--json]
irondev authority-ux red-findings --report <authority-ux-output-dir> [--json]
irondev authority-ux amber-findings --report <authority-ux-output-dir> [--json]
```

Forbidden verbs:

```text
approve
satisfy-policy
execute
retry
release
deploy
rollback
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
mutate-source
mutate-environment
```

## Review Line

Block BH adds an authority UX and receipt interpretability layer that converts existing governed receipts and campaign rows into deterministic human-readable and machine-readable explanations of supplied authority, required authority, block reason, mutation status, and next safe step. It does not change verdicts, create approval, satisfy policy, execute, retry, release, deploy, rollback, mutate source, mutate environments, promote memory, continue workflow, or infer authority.

## Killjoy

BH is not here to make receipts pretty.

BH is here to stop humans bypassing governance because the machine speaks in useless receipts.

If a blocked result cannot tell a human what authority is missing and what the safe next step is, the boundary will eventually be worked around.
