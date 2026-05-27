# IronDev Alpha Release Execution Order

Status: active
Last updated: 2026-05-27

This document is the working order for the Alpha runway. Keep it updated when a ticket changes scope, lands, or is superseded.

## Guardrails

- No significant TauriShell expansion until controlled apply is complete.
- UI work may only fix bugs, stabilize manual tests, or wire completed backend capability.
- No new agents until disposable workspace execution, patch proposal, approval, and controlled apply exist.
- Every governance PR includes at least one boundary or guard test.
- Run state is the source of truth.
- The active working tree is sacred.
- Human approval is mandatory before any real apply.

## Stabilisation Before More Feature Work

Status: active after IRONDEV-021 through IRONDEV-023 landed.

The Alpha core moved quickly. Consolidate the execution spine before adding more cockpit surface area, endpoints, or agent behavior.

### Day 1 - Run Lifecycle Hardening

Status: done in current branch after `dfcff23`; validate in PR before merge.

Focus:

- RunRecord lifecycle transitions.
- Created -> Running -> PausedForApproval -> Completed/Failed/Cancelled.
- Terminal-state protection.
- Retry behavior.
- Missing run behavior.
- Event ordering.

Acceptance:

- Completed runs cannot move back to Running.
- Failed runs record a failure reason.
- Run status uses durable state before report/event projection.
- Run events remain child evidence, not lifecycle source of truth.
- Missing runs return an honest 404.

### Day 2 - Disposable Workspace Safety

Focus:

- Path safety.
- Command allow-listing.
- Timeout behavior.
- Cleanup behavior.
- Failure evidence.
- No active repository mutation.

Acceptance:

- Reject workspace roots outside approved temp/test roots.
- Reject unsafe source/target path combinations.
- Reject non-allowlisted commands.
- Timeout records failed run evidence.
- Cleanup failure is visible.
- Disposable runs never apply changes to the active repo.

### Day 3 - Governed Tool Regression Guards

Focus:

- `code_standards.analyse_patch` remains read-only.
- No nested tool calls.
- No direct agent-to-agent call path.
- Passive-agent containment remains enforced.

Acceptance:

- Disallowed caller rejected.
- Nested call rejected.
- Tool cannot write tickets, documents, memory, files, or workspaces.
- No `CodeStandardsAgent` type exists.
- No governed tool invokes another governed tool.

### Day 4 - API/Client Slimming

Focus:

- Separate real Alpha endpoints from compatibility and legacy/report endpoints.
- Mark compatibility aliases clearly.
- Ensure Tauri uses product-shaped APIs rather than legacy report-shaped APIs.

Deliverable:

- `Docs/API_ALPHA_CONTRACT.md`.
- Endpoint inventory marked canonical, compatibility, legacy, and internal.

### Day 5 - LocalTest Full Smoke

Focus:

- Reset LocalTest.
- Start API.
- Start Tauri shell.
- Log in.
- Select tenant/project.
- Start disposable run if endpoint is exposed.
- Watch persisted events.
- Review run.
- Confirm no real repo or dev database mutation.

## Locked Execution Order

### IRONDEV-017 - Harden first governed tool: `code_standards.analyse_patch`

Status: done in `87bda94`.

Goal: prove the first governed tool is genuinely read-only and auditable.

Scope:

- Enforce runtime read-only guard at the governed tool registry/policy boundary.
- Block file writes, process mutation, network calls, other tool calls, and workspace mutation.
- Ensure the tool only reads supplied patch/context.
- Log tool invocation into the governed tool ThoughtLedger.
- Add performance guard: normal patch analysis must complete under 8 seconds.
- Preserve existing smoke/replay tests.

Acceptance criteria:

- Boundary tests prove `code_standards.analyse_patch` cannot invoke write-capable services.
- Boundary tests prove it cannot call another governed tool.
- Tool call appears clearly in the governed tool ThoughtLedger.
- Existing smoke/replay tests pass.
- No new agent abstractions added.

Hard rule: Killjoy can analyse the patch. Killjoy cannot touch the steering wheel.

### IRONDEV-018 - Ruthlessly reduce passive agents

Status: done in `87bda94`.

Goal: stop agent sprawl before it becomes permanent architecture debt.

Scope:

- Review ConscienceAgent, CriticAgent, reviewer/passive agents, and governance-like classes.
- Move simple policy/check logic into the governed tool registry, governance policy services, active workflow nodes, or explicit validators.
- Delete or merge at least one passive-agent role from the governed tool path.
- Write an ADR explaining what remains and why.

Acceptance criteria:

- At least one passive-agent role removed or merged from the governed tool path.
- No behaviour loss without explanation.
- ADR created.
- Governance path becomes easier to follow, not cleverer.
- Existing tests pass.

Hard rule: no "agent" should exist just because the concept sounds cool.

### IRONDEV-019 - Durable Run State

Status: done in current branch after `87bda94`; validate in PR before merge.

Goal: replace RunReport as the real source of truth.

Suggested states:

- Created
- Running
- PausedForApproval
- Failed
- Cancelled
- Completed
- Promoted
- Applied

Acceptance criteria:

- Every governed workflow starts with a durable Run.
- Events are tied to RunId.
- RunReport no longer owns truth.
- Tests prove run creation, event append, failure transition, and replay/report projection.
- No disposable workspace work starts before this lands.

Hard rule: if it did not create a Run, it did not happen.

### IRONDEV-020 - Safe Disposable Workspace Execution

Status: done in current branch after `87bda94`; validate in PR before merge.

Goal: prove IronDev can build/test without touching the user's active working tree.

Acceptance criteria:

- Test proves active working tree remains untouched.
- Test proves dirty active tree is not modified.
- Test proves cleanup happens after failure.
- First consumer can run build/test inside disposable workspace.
- Run events capture workspace creation, command execution, cleanup, and failure.

Hard rule: no active tree mutation. Ever.

### Gate - Disposable Command Execution Smoke

Before patch generation, prove this loop is boring and green:

- Create Run.
- Create disposable workspace.
- Run `dotnet build`.
- Run `dotnet test`.
- Capture output.
- Clean workspace.
- Emit final Run state.

Do not let an LLM generate code until this loop is stable.

### IRONDEV-021 - Patch Generation + Validation in Disposable Context

Status: done in current branch after `ab62032`; validate in PR before merge.

Goal: generate the first reviewable patch inside the safe disposable loop.

Implementation notes:

- `PatchProposalService` packages a `CodeChangeProposal` as an exact unified diff with SHA-256 identity.
- `code_standards.analyse_patch` runs through the governed tool registry before promotion packaging.
- Build/test/validation evidence is carried with the proposal.
- This slice does not allow repository writes or UI-side fake success states.

Acceptance criteria:

- Patch generated without touching active tree.
- Code standards tool runs as part of workflow.
- Build/test evidence attached.
- Failed validation blocks promotion.
- Human-readable proposal produced.

Hard rule: patch proposal is evidence-first, not model-confidence-first.

### IRONDEV-022 - Promotion Package + Human Approval

Status: done in current branch after `ab62032`; validate in PR before merge.

Goal: separate "valid patch exists" from "patch may be applied."

Implementation notes:

- `PromotionPackageService` creates a review package with patch hash, unified diff, evidence, files to promote, blocked files, checklist, and risk summary.
- `ControlledWriteApprovalService` creates durable approval/rejection records scoped to one exact package and patch hash.
- Approval remains scoped to controlled worktree apply; it does not approve direct real-repo writes.

Acceptance criteria:

- Promotion package contains patch, evidence, run history, and risk summary.
- Approval record references exact package identity/hash.
- Approval cannot apply a different patch than the reviewed one.
- Rejected packages cannot be applied.
- Approval/rejection is logged.

Hard rule: no approval, no apply.

### IRONDEV-023 - Controlled Worktree Apply

Status: done in current branch after `ab62032`; validate in PR before merge.

Goal: apply only approved patches, safely.

Implementation notes:

- `ControlledWorktreeApplyService` blocks missing/rejected approvals, mismatched patch hashes, blocked files, unsafe branches, dirty active repos, and target paths inside the active repo.
- Apply uses a fresh Git worktree, runs `git apply --check` first, then applies only to the target worktree.
- Run events capture completed/blocked controlled apply attempts with package, approval, patch hash, and target branch/worktree metadata.

Acceptance criteria:

- Dry-run succeeds before apply.
- Dirty tree blocks by default.
- Missing approval blocks apply.
- Mismatched patch hash blocks apply.
- Successful apply is logged against RunId, approval ID, patch hash, and target branch/worktree.

Hard rule: the apply step is boring, paranoid, and auditable.
