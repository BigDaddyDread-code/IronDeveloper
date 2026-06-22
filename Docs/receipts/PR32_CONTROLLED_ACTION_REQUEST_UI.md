# PR32 — Controlled Action Request UI

## Purpose

PR32 adds the first controlled frontend request surface for governed actions.

The UI may create explicit action request records for:

- SourceApply
- Commit
- Push
- DraftPullRequest
- Rollback

It does not execute actions directly.

## Boundary

UI may request authority. It cannot be authority.

Request creation is not approval. Request creation is not policy satisfaction.
Request creation is not execution. Request creation is not source mutation.
Request creation is not rollback execution. Request creation is not commit,
push, PR creation, ready-for-review, merge, release, deployment, memory
promotion, or workflow continuation authority.

request creation is not approval.

The frontend endpoint is:

```text
POST /api/frontend-readiness/action-requests
```

The endpoint creates request records only. It returns:

- request id, kind, and state
- blocked reasons
- missing evidence
- next safe actions
- forbidden actions
- evidence refs
- receipt refs
- request-only boundary flags
- RequestCreated
- ExecutionStarted = false
- SourceMutated = false
- WorkflowContinued = false

The UI labels stay request-shaped:

- Request source apply
- Request commit
- Request push
- Request draft PR
- Request rollback

Forbidden labels such as Apply, Apply now, Approve, Run, Execute, Commit,
Push, Create PR, Rollback, Continue, Merge, Release, Deploy, and Promote
memory are not exposed as exact action controls.

## Non-Authority Rules

- UI text is not authority.
- Receipt text is not authority.
- Memory text is not authority.
- Evidence refs are not approval.
- Receipt refs are not execution permission.
- Backend eligibility still decides.
- No action is executed from the UI.

## Validation

- ControlledActionRequest focused backend tests: 36/36
- ActionRequestUi Playwright tests: 27/27
- PR29/PR30/PR31/PR32 frontend regression lane: 188/188
- OperationStatus/PatchPackage/ActionRequest Playwright lane: 101/101
- BJ-through-PR32 corridor: 1007/1007
- Frontend build: passed
- .NET build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed
- `git diff --check HEAD~1 HEAD`: passed

## Review Traps

Reject this PR if:

- request creation becomes approval
- request creation becomes policy satisfaction
- request creation starts source apply, rollback, commit, push, PR creation, merge, release, deployment, memory promotion, or workflow continuation
- the UI exposes exact execution labels instead of request labels
- hostile UI, memory, receipt, or evidence text can grant authority
- old read-only viewers gain action buttons
- the action request endpoint is treated as an executor endpoint

## Review Line

UI may request authority. It cannot be authority.

## Killjoy

A request button asks for a key. It is not the key.
