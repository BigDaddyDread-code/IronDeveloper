# Workspace Command Boundary Inventory

This inventory documents the product workspace commands exposed by `IronDev.Cli`.

The table is a release boundary, not marketing copy. If a workspace command is added, removed, or changes its mutation/process/approval behavior, update this inventory in the same PR.

| Command | Stage | Reads Evidence | Writes Evidence | Mutates Source Repo | Executes Process | Requires Human Approval | Allowed For Agents | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `workspace check` | readiness | None | No persistent evidence | No | Yes, git status/rev-parse via readiness | No | Yes, readiness gate only | Verifies source/workspace isolation before preparation. |
| `workspace prepare` | preparation | Readiness result through service dependency | `workspace.json` | No | Indirect readiness git check | No | Yes, cage creation only | Creates disposable workspace and metadata. |
| `workspace run` | command execution | `workspace.json` | command stdout/stderr and command result evidence | No | Yes, allowlisted workspace commands | No | Yes, controlled execution only | Current allowlist is workspace command IDs such as `dotnet-build` and `dotnet-test`. |
| `workspace validate` | validation | `workspace.json` | `validation.json` plus command evidence | No | Indirect via workspace run/command service | No | Yes, validation only | Runs a validation profile such as `dotnet-build-test`. |
| `workspace diff` | diff | `workspace.json` | `diff.json` | No | No | No | Yes, reporting only | Uses SHA-256 file comparison and ignores workspace evidence. |
| `workspace promotion-package` | promotion packaging | `workspace.json`, `validation.json`, `diff.json` | `promotion-package.json` | No | No | No, but declares required | Yes, packaging only | Creates a human-review package; does not approve or apply. |
| `workspace promotion-approval` | human approval evidence | `workspace.json`, `promotion-package.json` | `promotion-approval.json` | No | No | Yes | No | Immutable approval or rejection evidence bound to package SHA-256. |
| `workspace apply-preflight` | pre-apply gate | `workspace.json`, `promotion-package.json`, `promotion-approval.json`, `diff.json` | `apply-preflight.json` | No | No | Reads approval evidence | Yes, gate check only | Confirms evidence completeness and readiness for a separate apply command. |
| `workspace apply-dry-run` | apply planning | full pre-apply evidence chain | `apply-dry-run.json` | No | No | Reads approval/preflight evidence | Yes, plan inspection only | Plans add/modify/delete operations but does not mutate source. |
| `workspace apply-copy` | source mutation | full approved dry-run evidence chain | `apply-copy.json` | Yes, copy-only add/modify | No | Requires approval evidence chain | No direct autonomous use | Only supported source mutation command; delete is blocked. |
| `workspace apply-verify` | post-mutation verification | full apply evidence chain | `apply-verify.json` | No | No | No | Yes, verification only | Recomputes hashes and proves source matches workspace for applied files. |
| `workspace post-apply-validate` | post-mutation validation | full apply and verification evidence chain | `post-apply-validation.json` | No | Indirect via validation service in fresh workspace | No | Yes, validation only | Prepares a fresh workspace from mutated source and validates there. |
| `workspace source-report` | final report | full apply, verification, and post-apply validation evidence chain | `source-report.json` | No | No | No | Yes, final report only | Summarizes controlled source mutation and remaining human review posture. |
| `workspace failure-package` | failure report | `workspace.json`; later evidence optional | `failure-package.json` | No | No | No | Yes, failure reporting only | Used when any stage blocks or fails; does not repair or rollback. |

## Boundary rules

- `workspace apply-copy` is the only source-mutating workspace command.
- `workspace apply-copy` may only copy planned add/modify files.
- No workspace command currently supports delete apply.
- No workspace command creates git commits, branches, GitHub pull requests, tickets, memory writes, or UI changes.
- Process execution is limited to readiness probes and allowlisted disposable-workspace command execution.
- Agents must not bypass this command spine or create their own source mutation path.
