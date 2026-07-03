# Phase 0 — Walking Skeleton Seam Map

Status: exploration complete, 2026-07-03. This document maps where the golden path
(ticket → criteria → build → test → critic → human gate → apply → receipt) works today,
where it is evaluation-only, and where the thread breaks. The gap list at the bottom is
the Phase 0 backlog.

## Method

Three code traces over the real controllers and services (not the docs): the
ticket→build-run→proposal path, the tester→critic→approval path, and the
apply→receipt→trace path — plus the existing E2E proofs under `tools/dogfood/proofs/`.

## Headline finding

> Every role of the five-role model exists as a working, tested service.
> Nothing connects them. The walking skeleton is missing exactly one thing:
> orchestration glue.

The second finding matters just as much: the **back half already works end-to-end**.
The governed workspace copy-only apply spine (prepare → validate → diff →
promotion-package → promotion-approval → apply-preflight → apply-dry-run → apply-copy →
apply-verify → post-apply-validate → source-report) is E2E-proven through the product
CLI against a real disposable repository (`tools/dogfood/proofs/workspace-copy-apply-e2e`,
test filter `WorkspaceCopyOnlyApply`). Phase 0 is not "build execution" — it is
"connect the front half to a back half that already exists, through the API".

## Golden path status

| Stage | Status | What runs today | Seam |
| --- | --- | --- | --- |
| Shape → ticket (BA) | ✅ works | `tickets/draft`, `draft/plan`, `draft/tests` call the live model with real project context; ticket persists; readiness gate is real | — |
| Builder: proposal | ✅ works | `POST /api/tickets/{id}/proposal` → `BuilderProposalService` → live model → real `ProposedFileChange` diffs; readiness is a hard precondition (BuilderProposalService.cs:67–84) | Proposal object is ephemeral — not persisted with an id, nothing downstream consumes it |
| Builder: apply (sandbox) | 🟡 partial | `ApplyProposalAsync` writes real files inside SafeWriteRoot, then runs build/test via `IDotNetBuildService` (BuilderProposalService.cs:213–233) | Stops after build/test (lines 254–259): no receipts, no controlled executor handoff, no run-event persistence for the apply itself |
| Sandbox build/test run | ✅ works | `POST .../build-runs` and `/disposable` → `TicketBuildRunService.StartDisposableAsync` → real disposable workspace, real `dotnet build` + `dotnet test`, `RunRecord` + run events in SQL, evidence on disk | — |
| Tester: test intents | 🟡 partial | `draft/tests` generates intents via live model | Intents never become test files; nothing executes them from the ticket flow |
| Tester: test execution | ❌ disconnected | `TesterAgent` runs `dotnet test` via PowerShell | Dogfood/CLI only — no API route, no tie to `TicketBuildRunService` |
| Critic | 🟡 isolated | `POST /api/v1/manual-critic/reviews` executes and persists `CriticReviewResult` (advisory-only, requires-human hardcoded) | Not fed by build outputs; the run `review-package` is a separate evidence stream never handed to the critic |
| Human approval: record | ✅ works | `POST /api/v1/projects/{id}/accepted-approvals` persists immutable, boundary-checked `AcceptedApprovalRecord` | — |
| Human approval: consumption | ❌ disconnected | `ApprovalSatisfactionEvaluator` and `WorkflowApprovalHaltEvaluator` exist and are tested | No caller in the ticket flow: `TicketBuildRunService` never queries the approval store; `ControlledApplyPlanWorkflow` checks request flags, not live approval records |
| Controlled apply/commit/push + receipts | 🟡 orphaned | Executors and receipt persistence services exist and are integration-tested; the CLI workspace spine is E2E-proven | Not registered in API DI (`Program.cs`); no route or service invokes them; `apply-and-build` returns "Phase 4B required" (TicketBuildOrchestrator.cs:72–78) |
| Trace | 🟡 partial | `GET /api/runs/{id}/report` and `/events` reconstruct run state from SQL events | No mutation/receipt chain in the trace — receipts and run events are unlinked |

## The five seams, precisely

1. **Proposal → nothing.** The generated proposal is returned to the client and
   forgotten. It needs an id, persistence, and a consumer.
2. **Test intents → nothing.** `DraftTicket.TestIntent` is never written as files or
   executed. `TesterAgent` exists but is CLI-only.
3. **Build outputs → critic.** The run review package and the critic input contract
   are two systems that have never met.
4. **Approval → unblock.** Approvals are recorded and evaluators exist, but no code
   path in the ticket flow asks "is there an accepted approval for this?" before
   proceeding. Halt is evaluated nowhere it matters.
5. **Apply → receipts.** The builder's file-write path and the governed
   executor/receipt tier are parallel universes. The proven CLI apply spine is not
   reachable from the API.

## Phase 0 backlog

Ordered, one PR each, every step usable the day it lands:

- **P0-1 — Skeleton orchestrator (the glue).** A `TicketSkeletonRunService` +
  `POST .../tickets/{id}/skeleton-runs` that chains what already works: readiness
  check → generate proposal (persist it with an id) → materialize disposable
  workspace → apply proposal files in-workspace → `dotnet build` + `dotnet test` →
  persist run events. No new capability — composition only.
- **P0-2 — Package → critic.** On skeleton-run completion, assemble the review
  package (ticket + diff + build/test results + evidence refs) and create the critic
  review via the existing manual-critic service. Persist the link run ↔ review.
- **P0-3 — Approval consumption.** The skeleton run halts after critic review with
  `ApprovalRequiredHalt` (call the existing halt evaluator). A recorded
  `AcceptedApprovalRecord` targeting the run is the only thing that lets the next
  step proceed — checked by querying the live store, not request flags.
- **P0-4 — Controlled apply + receipts.** Register the controlled executors in DI.
  Post-approval, hand the workspace diff to the governed apply path (converge with
  the proven CLI workspace spine or invoke `ControlledSourceApplyExecutor` directly)
  and persist the source-apply receipt. Copy-only, sandbox repo only.
- **P0-5 — Test intents → test files.** Generate real test files from the ticket's
  criteria into the workspace before the build/test step, so the criterion→test
  matrix has real cells. (Tester independence: generation prompt sees criteria, not
  the diff.)
- **P0-6 — Trace completeness.** Link receipts and the critic review into the run
  report so `GET /runs/{id}/report` reconstructs the whole loop.
- **P0-7 — UI: Build and Review stages.** The work-item spine consumes
  skeleton-runs: live run view (Build), critic findings + disposition + human gate
  (Review). The flow shell is already shaped for this.

## Decision points

- **Which apply path wins:** `BuilderProposalService.ApplyProposalAsync` (API-wired,
  receiptless) vs the CLI workspace spine (receipted, proven, not API-wired).
  Recommendation: P0-1 uses the disposable-workspace path for speed; P0-4 converges
  on the workspace spine as the only mutation path, and the builder's direct
  file-write is then retired.
- **Critic model calls:** the manual critic service supports live-model execution;
  the skeleton should start with it as-is (advisory-only, human-required) — the
  advisory boundary is correct until the finding→disposition invariant lands.

## Boundary

The skeleton adds no authority. Every step it chains already exists behind its own
gates; the orchestrator coordinates work — it does not bless work. Approval remains
the only unblock, source mutation remains copy-only inside sandbox repositories, and
every mutating step must leave a receipt. Evidence is not approval. Dry-run is not
execution. Receipt is not capability.
