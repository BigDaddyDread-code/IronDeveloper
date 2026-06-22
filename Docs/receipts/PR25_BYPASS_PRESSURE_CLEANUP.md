# PR25 Bypass Pressure Cleanup

## Purpose

PR25 reduces dogfood bypass pressure by making governed blocked states easier to understand.

It adds a read-only user-message formatter for governed operation status output and focused tests proving the formatter improves wording without changing authority.

Review line:

> If governance is annoying, users will route around it. Fix friction without weakening the gate.

Killjoy:

> Make the locked door readable. Do not make it easier to pick.

## Dogfood Findings

Findings live at `Docs/dogfood/PR25_BYPASS_PRESSURE_FINDINGS.md`.

Summary:

- no-approval output was safe but needed clearer review guidance
- ask-before-mutation stopped correctly but needed exact next-action scope
- freshness output must say freshness is not authority
- bounded-authority lane succeeded but needed clearer stop-before explanation
- draft PR output must say draft PR is not ready-for-review
- PR URL output must say PR URL is not a release candidate reference

## Wording Improvements

Added `GovernedStatusUserMessageFormatter`, a pure presentation/read-model layer.

It:

- maps reason codes to plain-language explanations
- preserves canonical evidence refs
- preserves receipt refs
- de-duplicates repeated next-safe-action lines
- de-duplicates repeated forbidden-action lines
- keeps missing evidence visible
- adds authority warnings
- exposes boundary flags from the existing status validator

It does not:

- modify the original status
- change status state
- change eligibility
- infer authority
- hide forbidden actions
- hide missing evidence
- approve
- satisfy policy
- execute
- mutate source
- continue workflow

## Before And After

Before:

```text
Blocked
MissingExplicitSourceApplyAuthority
NoBoundedAuthorityGrantForSourceApply
Request approval
```

After:

```text
Source apply is blocked because no accepted source-apply request or bounded SourceApply authority exists for repo BigDaddyDread-code/IronDeveloper, branch dogfood/ask-before-mutation-boundary-lane, run run-pr23, patch <hash>, and file scope Docs/receipts/...

Review the patch package, then create a governed source-apply request bound to repo BigDaddyDread-code/IronDeveloper, branch dogfood/ask-before-mutation-boundary-lane, run run-pr23, patch hash <hash>, and file scope Docs/receipts/...
```

## Authority Proof

Clearer blocked reason is not approval.

Better next safe action is not execution.

Fewer duplicate prompts is not fewer authority checks.

Plain language is not weaker policy.

Status output is not source apply.

Validation evidence is not approval.

Freshness evidence is not authority.

Patch package evidence is not source apply authority.

Draft PR evidence is not ready-for-review authority.

PR URL is not release candidate evidence.

Memory, UI, receipt, and hostile status text cannot approve, execute, mutate, or continue workflow.

## Boundary Proof

The formatter is read-only and pure.

It does not call:

- executors
- provider gateways
- Git
- GitHub
- shell/process APIs
- frontend/UI APIs
- memory promotion APIs
- release/deploy APIs
- workflow continuation APIs

The tests prove:

- status state does not change
- eligibility does not change
- boundary flags do not change
- canonical evidence refs remain visible
- receipt refs remain visible
- forbidden actions remain visible
- hostile friendly wording remains non-authoritative

## Validation

- Focused PR25: passed 38/38.
- PR24 focused lane: passed 44/44.
- PR23 focused lane: passed 41/41.
- PR22 focused lane: passed 30/30.
- PR21 focused lane: passed 44/44.
- PR20 focused lane: passed 31/31.
- CA focused lane: passed 16/16.
- BJ through PR25 authority corridor: passed 649/649.
- Build: passed with 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed before staging with no cached changes.
- `git diff --check HEAD~1 HEAD`: passed.
