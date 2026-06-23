# Block P0 to Y Authority Roadmap

> [!IMPORTANT]
> **Status: Historical / Fully Completed**
> This roadmap was the plan for rolling out the backend authority chain. All Blocks (P through Y) have been implemented and validated via regression testing. The current active plans are tracked in [ROADMAP.md](file:///c:/Users/bob/source/repos/AIDeveloper/Docs/ROADMAP.md).

## Purpose

This document records the post-PR166 backend authority rollout plan.

It is a planning document only.

It does not create accepted approvals, satisfy policy, run dry-runs, create patch artifacts, apply source, execute rollback, continue workflow, decide release readiness, or add UI authority.

## Current position

PR166 defines the release gate finish line. It does not cross it.

The next work starts by locking the validation lanes before real backend authority records appear.

## Global invariants

- Approval package is not accepted approval.
- Human-looking approval text is not accepted approval.
- Accepted approval must be a backend-owned durable record.
- Policy satisfaction is not approval.
- Approval is an input, not policy itself.
- Policy text is not policy satisfaction.
- Dry-run requirement is not dry-run execution.
- Apply preview is not dry-run execution.
- Patch proposal evidence is not a patch artifact.
- Patch artifact must be content-addressed.
- Patch artifact must be base-bound.
- Rollback plan is not rollback execution.
- Real apply must require rollback support.
- No accepted approval means no apply.
- No policy satisfaction means no apply.
- No dry-run proof means no apply.
- No patch artifact means no apply.
- No rollback plan means no apply.
- Workflow transition record is not runner permission.
- One workflow transition needs one reason and one scope.
- Release readiness report is not release readiness decision.
- Dogfood pass is not release readiness.
- Health check is not release readiness.
- Validation summary is not release readiness.
- UI displays authority records only after backend authority exists.
- UI does not become authority.

## Required backend authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

The release readiness gate is last.

Nothing before release readiness gate is release readiness.

## Block P0 - Authority Validation Baseline

### PR167 - Block P Authority Validation Baseline

Purpose: lock the validation lanes before real authority records start appearing.

Must prove:

- L4 matrix, invariants, failure, and readiness pass.
- UI cannot own backend authority.
- Dogfood, governance, and correlation evidence pass.
- API/CLI and ThoughtLedger pass.
- Build and diff-check pass.
- Known noisy lanes are documented.

Review line:

> PR167 paints the authority lanes. It does not drive in them.

## Block P - Accepted Approval

Purpose: first real backend authority brick.

Planned PRs:

- PR168 - Accepted Approval Record Contract
- PR169 - Accepted Approval SQL Store
- PR170 - Accepted Approval Read API
- PR171 - Governed Accepted Approval Create API
- PR172 - Accepted Approval Receipt and Regression Tests
- PR173 - Approval Satisfaction Evaluator

Hard line:

- Approval package is not accepted approval.
- Human-looking approval text is not accepted approval.
- Accepted approval must be a backend-owned durable record.

Review line:

> Block P creates the approval brick. It does not build the whole wall.

## Block Q - Policy Satisfaction

Purpose: prove policy is satisfied only after accepted approval and rule evaluation.

Planned PRs:

- PR174 - Policy Satisfaction Record Contract
- PR175 - Policy Satisfaction SQL Store
- PR176 - Policy Requirement/Satisfaction Evaluator
- PR177 - Policy Satisfaction Read API
- PR178 - Governed Policy Satisfaction Create API
- PR179 - Policy Satisfaction Receipt and Regression Tests

Hard line:

- Policy satisfaction is not approval.
- Approval is an input, not policy itself.
- Policy text is not policy satisfaction.

Review line:

> Block Q checks the rules. It does not grant the approval.

## Block R - Controlled Dry-run

Purpose: create real controlled dry-run proof, not preview text.

Planned PRs:

- PR180 - Controlled Dry-run Request Contract
- PR181 - Disposable Workspace Dry-run Boundary Receipt
- PR182 - Disposable Workspace Dry-run Executor
- PR183 - Dry-run Execution Audit
- PR184 - Dry-run Receipt Store
- PR185 - Dry-run Receipt Write Integration
- PR186 - Dry-run Failure Regression Tests

Hard line:

- Dry-run requirement is not dry-run execution.
- Apply preview is not dry-run execution.
- No real source mutation.

Review line:

> Block R runs the rehearsal. It does not touch the stage.

## Block S - Patch Artifact

Purpose: turn patch proposal evidence into an immutable, base-bound artifact.

Planned PRs:

- PR187 - Patch Artifact Contract
- PR188 - Patch Artifact Store
- PR189 - Patch Base/Hash Validation
- PR190 - Patch Artifact Read API
- PR191 - Patch Artifact Creation Integration
- PR192 - Patch Artifact Regression Tests

Hard line:

- Patch proposal evidence is not a patch artifact.
- Patch artifact must be content-addressed.
- Patch artifact must be base-bound.

Review line:

> Block S bottles the patch. It does not pour it into source.

## Block T0 - Source Apply Threat Boundary

### PR193 - Source Apply Threat Model and Boundary Receipt

Purpose: record the source-apply footguns before executor work.

Must name:

- wrong branch
- dirty worktree
- stale base
- missing rollback
- partial apply
- silent mutation
- validation bypass
- approval/policy drift

Review line:

> PR193 names the dragon. It does not fight it.

## Block U - Rollback Foundation

Purpose: make rollback a precondition, not an afterthought.

Planned PRs:

- PR194 - Rollback Plan Contract
- PR195 - Rollback Gate Evaluator
- PR196 - Rollback Receipt Store
- PR197 - Rollback Read API
- PR198 - Rollback Regression Tests

Hard line:

- Rollback plan is not rollback execution.
- Real apply must require rollback support.

Review line:

> Block U builds the escape hatch before opening the door.

## Block T - Controlled Source Apply

Purpose: first controlled backend source mutation.

Planned PRs:

- PR199 - Source Apply Gate Evaluator
- PR200 - Source Apply Request Contract
- PR201 - Source Apply Executor Dry-run Mode
- PR202 - Source Apply Receipt Store
- PR203 - Source Apply Read API
- PR204 - Source Apply Narrow Real Apply Path
- PR205 - Source Apply Regression Tests

Hard line:

- No accepted approval means no apply.
- No policy satisfaction means no apply.
- No dry-run proof means no apply.
- No patch artifact means no apply.
- No rollback plan means no apply.

Review line:

> Block T touches source only when the whole chain says yes.

## Block U2 - Rollback Execution

Purpose: prove rollback can actually execute and be audited.

Planned PRs:

- PR206 - Rollback Executor
- PR207 - Rollback Execution Audit
- PR208 - Rollback Receipt Write Integration
- PR209 - Rollback Failure Regression Tests

Hard line:

- Rollback receipt is not rollback success unless execution proof exists.

Review line:

> Block U2 proves the escape hatch opens.

## Block V - Workflow Continuation

Purpose: allow one governed workflow transition after authority and evidence are satisfied.

Planned PRs:

- PR210 - Workflow Continuation Gate
- PR211 - Workflow Transition Record Contract
- PR212 - Workflow Transition Store
- PR213 - Workflow Continuation Read API
- PR214 - Governed Continuation API/CLI
- PR215 - Workflow Continuation Regression Tests

Hard line:

- Workflow transition record is not runner permission.
- One transition.
- One reason.
- One scope.

Review line:

> Block V moves the workflow one governed step.

## Block W - Release Readiness

Purpose: final backend release readiness decision.

Planned PRs:

- PR216 - Release Readiness Report
- PR217 - Release Readiness Decision Record Contract
- PR218 - Release Readiness Store
- PR219 - Release Readiness Gate Evaluator
- PR220 - Release Gate Read API
- PR221 - Governed Release Gate API/CLI
- PR222 - Release Readiness Regression Tests

Hard line:

- Release readiness report is not release readiness decision.
- Dogfood pass is not release readiness.
- Health check is not release readiness.
- Validation summary is not release readiness.

Review line:

> Block W decides readiness. It does not confuse evidence with shipping.

## Block X - Backend Dogfood Hardening

Purpose: prove the authority chain survives repeated real use and failure cases.

Planned PRs:

- PR223 - Repeated Governed Dogfood Campaigns
- PR224 - Stale Authority Detection
- PR225 - Authority Expiry Regression Tests
- PR226 - Failed Apply Recovery Campaign
- PR227 - Failed Continuation Recovery Campaign
- PR228 - Release Gate Negative Campaigns

Review line:

> Block X makes the machine boring under pressure.

## Block Y - New UI Over Real Authority

Purpose: build the cockpit over real backend authority records only after those records exist.

Planned PRs:

- PR229 - Accepted Approval UI
- PR230 - Policy Satisfaction UI
- PR231 - Dry-run Receipt UI
- PR232 - Patch Artifact UI
- PR233 - Source Apply Review UI
- PR234 - Rollback UI
- PR235 - Workflow Continuation UI
- PR236 - Release Readiness UI
- PR237 - UI Authority Firewall Update

Hard line:

- UI displays authority records.
- UI does not become authority.

Review line:

> Block Y builds the cockpit after the engine exists.

## Immediate next slices

- PR167 - Block P Authority Validation Baseline
- PR168 - Accepted Approval Record Contract

PR167 should be docs/tests/validation receipt work only.

PR168 starts the first backend authority contract but must not add SQL, API, workflow continuation, source apply, or release readiness.
