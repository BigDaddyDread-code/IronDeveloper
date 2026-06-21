# BV — Commit Package Under Authority

## Review Line

Apply authority is not commit authority.

## Boundary

This PR adds a commit package under authority only.

It does not create a commit.
It does not stage files.
It does not run git.
It does not execute commands.
It does not mutate durable source.
It does not push.
It does not create PRs.
It does not merge.
It does not release.
It does not deploy.
It does not create approvals.
It does not issue or store grants.
It does not satisfy policy.
It does not run validation.
It does not promote memory.
It does not continue workflow.

Source apply receipt is required but is not commit authority.
Source apply authority is not commit authority.
Patch proposal is not commit authority.
Patch package is not commit authority.
Validation passed is not commit authority.
Clean expected diff evidence is not commit authority.
Commit message evidence is not commit authority.
Commit operation authority is required separately.
Commit operation authority must bind the same repository, branch, run id, patch hash, and file set as the source-apply receipt and expected diff evidence.

## Authority Chain

Source apply receipt proves source was applied under authority.
It does not prove the system may commit.

Clean expected diff evidence proves the expected changed-file envelope.
It does not prove the system may commit.

Commit operation eligibility must be for `Commit`, must be eligible, and must carry no blocked reasons or missing evidence.
Any `SourceApply`, `PatchPackageWrite`, `Push`, or other operation eligibility decision fails closed.
Commit operation eligibility must be wrapped in scoped commit authority evidence before BV can consume it.

Validation must be satisfied or explicitly blocked.
Explicitly blocked validation maps the package to `Blocked`, not `Eligible`.

An eligible commit package is ready for controlled commit executor review only.
It still cannot create the commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Killjoy

Patch applied is not commit allowed. Commit allowed is not commit executed.
