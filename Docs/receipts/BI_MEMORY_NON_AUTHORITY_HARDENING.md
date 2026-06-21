# BI - Memory Non-Authority Hardening

## Purpose

Block BI hardens memory as context only.

BI proves that memory, ThoughtLedger text, prior run summaries, old receipts, workflow history, cross-project memory, and portable engineering memory cannot satisfy current approval, policy, execution, mutation, release, deployment, rollback, workflow continuation, or memory promotion authority.

Core rule:

```text
Memory may explain context.
Memory must not authorize action.
```

## Boundary

BI does not promote memory.
BI does not write memory to the memory store.
BI does not approve.
BI does not satisfy policy.
BI does not execute.
BI does not retry.
BI does not release.
BI does not deploy.
BI does not rollback.
BI does not source-apply.
BI does not mutate source.
BI does not mutate environments.
BI does not continue workflow.
BI does not dispatch pipelines.

BI does not infer authority from memory.
BI does not infer authority from ThoughtLedger text.
BI does not infer authority from prior runs.
BI does not infer authority from old receipts.
BI does not infer authority from workflow history.
BI does not infer authority from cross-project memory.
BI does not infer authority from cross-repository memory.

Portable engineering memory may carry sanitized lessons.
Portable engineering memory must not carry project authority.

## Evidence Model

BI records memory authority attempts as sanitized evidence:

```text
source kind
source id
memory scope
memory kind
current project/repository
memory project/repository
requested action
required authority
supplied authority
sanitized authority phrase
claim hash
context/authority flags
cross-project and cross-repository flags
claim language flags
```

BI must not write raw memory payloads by default.

Claim text must stay short and sanitized.
The hash is accountability, not authority.

## Outputs

BI writes:

```text
memory-non-authority-decisions.jsonl
memory-non-authority-summary.json
memory-non-authority-report.md
memory-non-authority-red-findings.jsonl
memory-non-authority-amber-findings.jsonl
```

JSON and JSONL are evidence.
Markdown explains the evidence.
Markdown is not authority.

## Built-In Campaign

The built-in scenario set covers:

```text
memory as approval
memory as policy satisfaction
memory as execution request
memory as source mutation authority
memory as release authority
memory as deployment authority
memory as rollback decision authority
memory as rollback execution authority
memory as workflow continuation authority
memory as memory promotion authority
ThoughtLedger text as approval
prior run summary as authority
prior receipt as current authority
memory refreshing stale authority
cross-project memory as authority
cross-repository memory as authority
portable engineering memory as project authority
memory used only as planning context
```

Only memory used as planning context is non-blocking.
Even then, it grants no authority.

## CLI

Allowed report creation:

```text
irondev memory-non-authority evaluate-scenarios --scenario-set <default|full> --report-id <report-id> --out <path> [--json]
irondev memory-non-authority evaluate --attempts <memory-authority-attempts.jsonl> --report-id <report-id> --out <path> [--json]
```

Read-only commands:

```text
irondev memory-non-authority inspect --report <path> [--json]
irondev memory-non-authority red-findings --report <path> [--json]
irondev memory-non-authority amber-findings --report <path> [--json]
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
write-memory
promote
remember-as-authority
```

## Review Line

Block BI hardens memory non-authority. Memory can inform context, but it cannot approve, satisfy policy, execute, mutate, promote itself, continue workflow, refresh stale authority, or cross project/repository boundaries as current authority.

## Killjoy

BI is not about making memory smarter.

BI is about making memory less dangerous.

The moment memory can approve, refresh, continue, deploy, rollback, or mutate, the governance chain has a hidden authority bypass.
