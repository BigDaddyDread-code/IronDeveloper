# PR238-243 Manual Patch Proposal Product Receipt

## Purpose

Block Z adds the first usable manual patch proposal product loop:

```text
irondev patch start --repo <repo-path> --task <task-file> --test "<test-command>"
irondev patch finish --run <run-id>
```

The loop creates a disposable workspace, lets a human or agent edit inside that workspace, then files a review package for manual inspection.

## Artifacts

Each finished run writes:

```text
task.md
run.json
patch.diff
changed-files.txt
test-results.txt
review-summary.md
known-risks.md
manual-apply-instructions.md
```

The default run root is outside the source repository. A caller may pass `--runs-root`, but the CLI rejects a runs root inside the source repository.

## Boundary

This is a manual patch proposal product loop.

It does not modify the real source repository.
It does not apply source.
It does not create a commit.
It does not push.
It does not create or update pull requests.
It does not grant approval.
It does not satisfy policy.
It does not approve release.
It does not continue workflow.
It does not promote memory.
It does not dispatch agents.
It does not call models.
It does not add API, SQL, UI, scheduler, worker, or autonomous runtime behavior.

`patch.diff` is review evidence only. Human manual application or a future governed source-apply path remains required before source changes can be applied. This PR performs neither.

## Validation intent

The Block Z tests prove:

- `patch start` creates a disposable workspace and run metadata.
- `patch finish` exports patch diff, changed-file list, test results, review summary, known risks, and manual apply instructions.
- failing tests still produce review artifacts.
- the source repository remains unchanged.
- run roots inside the source repository are rejected.
- forbidden source-control/release test commands are rejected before package creation.
- `patch status` is read-only inspection.

## Review line

PR238-243 creates the patch proposal workbench. It does not apply the patch.
