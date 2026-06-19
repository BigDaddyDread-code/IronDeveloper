# AS Controlled PR Branch Update Executor

## Review Line

Block AS executes an eligible PR update package against the expected PR branch and writes an execution receipt. It does not approve, mark ready, request reviewers, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Boundary

PR branch update is not review transition.

AS may read an AR PR update package, verify package eligibility, verify repository/branch/head evidence, stage only expected files, create the expected commit, re-observe the worktree after commit, push only to the package target remote and target PR branch, re-observe the remote after push, and write a branch update execution receipt.

It does not push to unexpected branches.
It does not force-push by default.
It does not commit unrelated files.
It does not mark ready.
It does not request reviewers.
It does not resolve review threads.
It does not approve.
It does not enable auto-merge.
It does not merge.
It does not release.
It does not deploy.
It does not tag.
It does not publish.
It does not promote memory.
It does not continue workflow.

## AS Map

AS1 AR package required.

AS requires a `ControlledPrUpdatePackage` with `ExecutionEligibility = Eligible`, `Verdict = PackageReadyForExecutor`, `CanExecuteBranchUpdate = true`, source-apply evidence, validation evidence, `CommitAllowed = true`, `PushAllowed = true`, and an expected diff hash. Missing or ineligible packages fail closed.

AS2 branch and head guard.

AS verifies repository identity, PR number, target branch, local head SHA, and remote branch head SHA against the package before mutation. Wrong branch, wrong PR number, stale local head, or stale remote head blocks execution.

AS3 file and diff guard.

AS requires the dirty worktree file set to match the package expected files exactly. Undeclared file changes, generated restore artifacts, staged pre-existing changes, missing expected changes, or diff hash mismatch block execution.

AS4 controlled commit.

AS stages only expected files and verifies the staged set still matches the package envelope before committing. The commit uses package-declared commit text. After commit, AS re-observes the repository and requires `HEAD` to equal the commit SHA, staged files to be empty, dirty files to be empty, and the actual committed file set to equal the package expected files. Commit failure or post-commit residue writes a failed receipt and does not push.

AS5 controlled push.

AS pushes only `HEAD` to the package target branch on the package target remote. Request-level remote overrides must either be absent or match the package target remote exactly. Force push is disabled by default. Non-fast-forward or unexpected branch pushes fail closed. After push, AS re-observes the remote and requires the remote branch head to equal the commit SHA.

AS6 execution receipt and bypass tests.

AS writes `pr-branch-update-execution-receipt.json` and a Markdown receipt with before/after SHAs, commit SHA, push target, source-apply evidence, validation evidence, dirty-worktree state from post-mutation observation, expected/actual files, rollback instructions, verdict, failure classification, and boundary statements. Focused AS tests prove forbidden CLI verbs fail closed, request remote overrides block before mutation, post-commit dirty residue fails before push, and successful branch update does not mark ready, request reviewers, resolve comments, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Killjoy

Updating a PR branch is not asking for review. It is only moving the workbench to the next tested state.
