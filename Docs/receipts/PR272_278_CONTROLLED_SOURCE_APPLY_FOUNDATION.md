# PR272-278 Controlled Source Apply Foundation Receipt

This block creates the controlled source-apply foundation.

It does not apply source.
It does not mutate the source repository.
It does not execute rollback.
It does not create git commits.
It does not push.
It does not create pull requests.
It does not merge.
It does not approve release.
It does not approve deployment.
It does not satisfy policy.
It does not continue workflow.
It does not promote memory.
It does not dispatch agents.
It does not add API, SQL, UI, scheduler, worker, or autonomous runtime behavior.

Dry-run apply occurs only in a disposable apply rehearsal workspace.
A successful dry-run is evidence only.
A successful dry-run is not source apply.
A successful dry-run is not approval.
A successful dry-run is not release readiness.
A successful dry-run is not merge readiness.

## What Block AF adds

- Source-apply request, approval evidence, patch verification, gate decision, dry-run plan/result, rollback draft, and readiness report contracts.
- Patch artifact verification for run metadata, patch hash, changed files, manual apply instructions, empty/large patch, and forbidden paths.
- Human approval evidence reading and SourceApplyGate evaluation for dry-run readiness only.
- Disposable apply rehearsal workspace dry-run with source repository mutation proof.
- Rollback plan draft and readiness report artifacts.
- Run-scoped governance JSONL events for source-apply preparation evidence.
- CLI commands: `source-apply approval-template`, `source-apply prepare`, and `source-apply status`.

## Explicitly absent

- No `source-apply apply` command.
- No real source repo mutation.
- No git commit, push, pull request, merge, release, or deploy path.
- No rollback execution.
- No API, SQL, UI, scheduler, worker, or autonomous runtime path.
- No policy, workflow, memory, agent, model, or tool authority is created.

## Review lines

PR272 defines source-apply foundation contracts. It does not apply source.
PR273 verifies patch artifacts. It does not apply them.
PR274 gates source-apply readiness. It does not grant source apply.
PR275 rehearses source apply in a disposable workspace. It does not mutate source.
PR276 drafts rollback evidence. It does not execute rollback.
PR277 reports source-apply readiness. It does not approve apply.
PR278 proves source-apply bypass lanes. It does not apply source.
