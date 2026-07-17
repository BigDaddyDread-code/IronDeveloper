# Workspace Apply Spine

## Purpose

The workspace apply spine is IronDev's governed path for moving approved changes from a disposable workspace back into a source repository.

It exists to turn agent or workspace-produced changes into controlled, evidence-backed source repository mutation. The spine makes each gate reviewable before the next command can run.

## Release Principle

No source mutation without:

- prepared workspace metadata
- validation
- diff
- promotion package
- immutable approval evidence
- apply preflight
- dry-run plan
- copy-only apply
- apply verification
- post-apply validation
- source report

The release principle is simple: every mutation must be traceable to prior evidence, human approval, a dry-run plan, verification, and a final report.

## Happy Path

| Command | Purpose | Reads | Writes | Can mutate source repo? | Can execute process? | Can be used by agents? | Required prior evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `workspace check` | Inspect whether a disposable workspace path is safe to use. | Source repository path, workspace root. | Standard CLI envelope only. | No | Yes, git status and rev-parse readiness probes. | Yes, as a readiness gate only. | None |
| `workspace prepare` | Create the disposable workspace and copy the source repo into it. | Readiness result through service dependency, source repository files. | `.irondev/workspace.json` inside the workspace. | No | Indirect readiness git check only. | Yes, as a cage creation step only. | Successful readiness check |
| `workspace validate` | Run a controlled validation profile inside the disposable workspace. | Workspace metadata. | `.irondev/runs/<run-id>/validation.json` plus command evidence. | No | Yes, indirectly through allowlisted workspace commands. | Yes, for validation only. | Workspace metadata |
| `workspace diff` | Compare source and workspace files using SHA-256 hashes. | Workspace metadata, source files, workspace files. | `.irondev/runs/<run-id>/diff.json` | No | No | Yes, for reporting only. | Workspace metadata |
| `workspace promotion-package` | Package validation and diff evidence for human review. | Workspace metadata, validation evidence, diff evidence. | `.irondev/runs/<run-id>/promotion-package.json` | No | No | Yes, for packaging only. | Workspace metadata, validation evidence, diff evidence |
| `workspace promotion-approval` | Record immutable human approval or rejection evidence. | Workspace metadata, promotion package. | `.irondev/runs/<run-id>/promotion-approval.json` | No | No | No, human approval input is required. | Workspace metadata, promotion package |
| `workspace apply-preflight` | Verify approval and evidence are complete before any apply planning. | Workspace metadata, promotion package, approval evidence, diff evidence. | `.irondev/runs/<run-id>/apply-preflight.json` | No | No | Yes, as a gate check only. | Workspace metadata, promotion package, approval evidence, diff evidence |
| `workspace apply-dry-run` | Produce the exact add/modify/delete plan that apply would use. | Workspace metadata, diff evidence, promotion package, approval evidence, apply preflight. | `.irondev/runs/<run-id>/apply-dry-run.json` | No | No | Yes, as a plan inspection step only. | Full pre-apply evidence chain |
| `workspace apply-copy` | Retired standalone mutation entry point; fails closed without live project capability context. | Command arguments only. | None. | No | No | No. | Governed project-work apply must call the injected mutation executor instead. |
| `workspace apply-verify` | Recompute hashes after apply-copy and prove source files match workspace files. | Full apply evidence chain. | `.irondev/runs/<run-id>/apply-verify.json` | No | No | Yes, for verification only. | apply-copy evidence plus prior chain |
| `workspace post-apply-validate` | Validate the mutated source state from a fresh disposable workspace. | Full apply and verification evidence chain. | `.irondev/runs/<run-id>/post-apply-validation.json` | No | Yes, indirectly in a fresh validation workspace. | Yes, for validation only. | apply-copy and apply-verify evidence |
| `workspace source-report` | Produce the final human and machine-readable source mutation report. | Full apply, verify, and post-apply validation evidence chain. | `.irondev/runs/<run-id>/source-report.json` | No | No | Yes, as final source truth output. | apply-copy, apply-verify, post-apply validation evidence |

## Failure Path

`workspace failure-package` is used when any stage blocks or fails.

It requires workspace metadata. Later evidence is optional because the failed stage may have stopped before later artifacts existed. It does not repair, roll back, retry, apply, or promote. It classifies post-mutation risk so a human can tell whether the source repo may already have been changed.

## Evidence Artifacts

| Artifact | Producer | Purpose | Required by |
| --- | --- | --- | --- |
| `workspace.json` | `workspace prepare` | Binds run ID, source repo, workspace path, creation time, and preparation method. | All later workspace commands |
| `validation.json` | `workspace validate` | Records controlled validation profile status and command evidence. | `workspace promotion-package` |
| `diff.json` | `workspace diff` | Records added, modified, deleted, and unchanged file evidence. | `workspace promotion-package`, `workspace apply-preflight`, `workspace apply-dry-run` |
| `promotion-package.json` | `workspace promotion-package` | Packages validation and diff evidence for human review. | `workspace promotion-approval`, `workspace apply-preflight`, `workspace apply-dry-run`, governed apply-copy stage |
| `promotion-approval.json` | `workspace promotion-approval` | Immutable approval or rejection evidence bound to the promotion package hash. | `workspace apply-preflight`, `workspace apply-dry-run`, governed apply-copy stage |
| `apply-preflight.json` | `workspace apply-preflight` | Confirms evidence completeness and readiness for a separate apply stage. | `workspace apply-dry-run`, governed apply-copy stage |
| `apply-dry-run.json` | `workspace apply-dry-run` | Records the planned apply operations without mutating source. | Governed apply-copy stage, `workspace apply-verify` |
| `apply-copy.json` | Governed project-work apply-copy stage | Records structured authority, path, hash, and copy-only add/modify mutation results. | `workspace apply-verify`, `workspace post-apply-validate`, `workspace source-report` |
| `apply-verify.json` | `workspace apply-verify` | Verifies source files match approved workspace files after apply-copy. | `workspace post-apply-validate`, `workspace source-report` |
| `post-apply-validation.json` | `workspace post-apply-validate` | Records fresh-workspace validation of the mutated source repo. | `workspace source-report` |
| `source-report.json` | `workspace source-report` | Final report for the controlled copy-only apply path. | Human review and downstream reporting |
| `failure-package.json` | `workspace failure-package` | Failure report for blocked or failed stages. | Human triage |

## Mutation Boundary

No standalone CLI command may mutate the source repository. The injected governed project-work spine is the sole mutation corridor: it carries run-start capability evidence into apply-copy, and `IControlledSourceMutationExecutor` re-evaluates live authority, resolves both workspace and destination paths without following links, performs the write through verified handles, and verifies the result hash. The public authority check and the write are not separable operations.

It may only copy planned add/modify files. It must not delete files. It must not run git. It must not run build or test commands. It must not call agents.

All other workspace commands either inspect, prepare a disposable workspace, validate inside a disposable workspace, produce evidence, or report.

## Human Approval Boundary

`workspace promotion-approval` records approval evidence.

It does not apply. It does not allow automatic apply. It does not grant broad permission beyond the exact promotion package hash. Approval evidence is immutable; an existing approval artifact must not be silently overwritten.

Approval is not execution. Approval only permits later commands to continue through their own gates.

## Validation Boundary

`workspace validate` runs controlled commands inside disposable workspaces.

`workspace post-apply-validate` prepares a fresh workspace from the mutated source repo and validates there. It must not run build or test directly inside the source repo.

Validation evidence is required before promotion packaging and after source mutation.

## Agent Boundary

Agents may request or use the workspace apply spine.

Agents must not bypass the spine. Agents must not copy files directly into the source repository. Agents must not create their own apply path. Agents must consume `source-report.json` for success outcomes and `failure-package.json` for blocked or failed outcomes.

Agent integration must use the product commands or typed application services behind these commands. It must not reintroduce ReplayRunner as a product interface.

## Non-Goals

- delete apply
- rollback
- git commit
- branch creation
- GitHub PR creation
- autonomous repair loop
- ticket creation
- memory writes
- UI

## Current Supported Apply Mode

The current supported apply mode is:

- copy-only add/modify
- no delete
- human-approved
- evidence-backed
- verified
- post-apply validated
- source-report summarized

## Future Extension Points

These are future extension points, not current behavior:

- delete apply
- rollback
- git commit, branch, or PR package
- agent integration
- plan binding
- source report UI
- memory ingestion
