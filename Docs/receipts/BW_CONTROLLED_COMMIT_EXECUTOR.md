# BW Controlled Commit Executor

Block BW adds a controlled commit executor.

It can create exactly one commit only after authority and source-state checks pass.

Boundary:

- Commit package is not commit execution.
- Source apply receipt is not commit authority.
- Apply authority is not commit authority.
- Patch proposal is not commit authority.
- Patch package is not commit authority.
- Validation passed is not commit authority.
- Clean diff is not commit authority.
- Commit message is not commit authority.
- Commit authority is not push authority.
- Commit execution receipt is not push authority.
- Commit execution receipt is not workflow continuation.

BW verifies:

- eligible BV commit package
- package manifest identity and file set
- source apply receipt identity and file set
- scoped commit operation authority
- clean expected diff hash and file set
- pre-commit worktree observation
- exact expected files
- empty staged and untracked state before execution
- forbidden file absence
- controlled gateway receipt
- post-commit clean state

It does not push.
It does not create PRs.
It does not merge.
It does not release.
It does not deploy.
It does not promote memory.
It does not continue workflow.
It does not create approvals.
It does not issue or store grants.
It does not satisfy policy.
It does not run validation.
It does not apply patches or source changes.

A commit is a durable mutation and must pass the same gate as every other mutation.
