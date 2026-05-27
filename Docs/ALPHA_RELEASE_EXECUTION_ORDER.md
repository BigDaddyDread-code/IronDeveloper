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

## Locked Execution Order

### IRONDEV-017 - Harden first governed tool: `code_standards.analyse_patch`

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

Goal: generate the first reviewable patch inside the safe disposable loop.

Acceptance criteria:

- Patch generated without touching active tree.
- Code standards tool runs as part of workflow.
- Build/test evidence attached.
- Failed validation blocks promotion.
- Human-readable proposal produced.

Hard rule: patch proposal is evidence-first, not model-confidence-first.

### IRONDEV-022 - Promotion Package + Human Approval

Goal: separate "valid patch exists" from "patch may be applied."

Acceptance criteria:

- Promotion package contains patch, evidence, run history, and risk summary.
- Approval record references exact package identity/hash.
- Approval cannot apply a different patch than the reviewed one.
- Rejected packages cannot be applied.
- Approval/rejection is logged.

Hard rule: no approval, no apply.

### IRONDEV-023 - Controlled Worktree Apply

Goal: apply only approved patches, safely.

Acceptance criteria:

- Dry-run succeeds before apply.
- Dirty tree blocks by default.
- Missing approval blocks apply.
- Mismatched patch hash blocks apply.
- Successful apply is logged against RunId, approval ID, patch hash, and target branch/worktree.

Hard rule: the apply step is boring, paranoid, and auditable.
