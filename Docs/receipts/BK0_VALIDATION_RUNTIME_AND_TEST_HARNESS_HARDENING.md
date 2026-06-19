# BK0 Validation Runtime and Test Harness Hardening

Review line:

Block BK0 makes validation bounded, scoped, diagnosable, and receipt-backed. It does not approve, merge, release, deploy, mutate source, promote memory, or continue workflow.

Killjoy:

A test suite that can hang silently is not validation. It is theatre with a timeout missing.

## Purpose

Block BK0 adds a validation runtime boundary for IronDev. It gives validation commands explicit timeouts, captured stdout/stderr artifacts, process-tree cleanup, lane planning from changed files, cache policy checks, and receipt-backed reporting.

The boundary is evidence-only. BK0 can report validation state. It cannot approve work, satisfy policy, mutate source, promote memory, merge, release, deploy, tag, publish, or continue workflow.

## BK0 Split Map

- BK0.1 supervised process runner: executable, arguments, working directory, environment overlay, timeout, stdout path, stderr path, and process-tree kill on timeout/cancel.
- BK0.2 failure classification: Passed, ProcessExitNonZero, Timeout, Cancelled, HarnessException, RestoreFailed, BuildFailed, TestFailed, DiffCheckFailed, EnvironmentAccessDenied, DirtyGeneratedArtifacts, InvalidLanePlan, CachePolicyViolation, and UnknownFailure.
- BK0.3 validation receipt: bounded verdicts of Passed, Failed, Incomplete, or Blocked.
- BK0.4 changed-file lane planner: focused lanes are selected from changed source, CLI, receipt, project, and block-specific paths.
- BK0.5 authority invariant lane: every plan includes the fast authority invariant lane.
- BK0.6 cache and parallelism policy: cached pass evidence is rejected for authority, source-apply, rollback, workflow, memory-promotion, CLI mutation, database, dogfood, merge, and release categories.

## CLI Surface

Supported validation commands:

- `irondev validate plan`
- `irondev validate run`
- `irondev validate lanes`
- `irondev validate receipt`
- `irondev validate inventory`

Known validation lanes run their declared command manifest. A caller-provided `--command` cannot be combined with a known lane name. Custom commands must use `irondev validate run --ad-hoc`, and ad-hoc receipts cannot satisfy required named lanes.

Unsupported authority-shaped commands include approve, merge, release, deploy, continue, satisfy-policy, mutate-source, promote-memory, push, commit, tag, publish, request-reviewers, ready, rerun-ci, apply, and rollback.

## Artifact Boundary

Validation receipts include:

- validation run id and validation plan id
- branch and commit SHA
- changed file hash
- required, recommended, and deferred lanes
- command, arguments, working directory, duration, exit code, stdout/stderr paths, and bounded output tails
- failure classifications
- skipped lanes and reasons
- dirty generated/temp file classifications
- worktree cleanliness flags
- cache policy
- non-authority validation boundary

Receipts do not contain approval, merge, release, deployment, workflow continuation, source mutation, memory promotion, or policy satisfaction authority.

## Lane Planning

The lane planner prefers focused validation over broad validation when the changed files make that safe:

- governance source changes select build, impacted governance tests, and fast authority invariants
- CLI changes select build, CLI command-surface tests, impacted governance tests, and fast authority invariants
- BK0 validation runtime changes select build, focused BK0 tests, and fast authority invariants
- AO merge/release separation changes select build, focused AO tests, and fast authority invariants
- receipt-only changes select diff-check, receipt boundary checks, and fast authority invariants
- project/solution metadata changes select restore, build, impacted tests, and fast authority invariants
- phase boundary changes add a phase-gate lane

Generated restore artifacts such as `obj/project.assets.json` and repo-local temporary `NuGet.Config` files are not source evidence. They are classified so the operator can remove or review them explicitly.

A missing changed-files manifest is an invalid lane plan. A dirty worktree after validation, or dirty generated restore artifacts such as `obj/project.assets.json`, cannot produce a passed validation receipt.

## Non-Authority Boundary

BK0 produces validation evidence. Evidence can inform a human or a later governed action, but the validation receipt is not permission to proceed. A passing receipt is not a merge decision, release decision, deployment approval, source mutation approval, policy satisfaction record, memory promotion, or workflow continuation.
