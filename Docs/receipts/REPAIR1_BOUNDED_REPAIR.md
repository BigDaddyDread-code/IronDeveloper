# REPAIR-1 Receipt - Bounded Repair

## Purpose

A red build was a dead end with evidence. HERO-2's live walk made failure the
common case a real user meets — and the loop's only answer was "start over."
REPAIR-1 gives the orchestrator ONE governed answer: within an explicit attempt
budget, direct the Builder to repair its failed proposal with the failure
evidence in context, in a fresh attempt-scoped workspace.

```text
build/test fails
-> deterministic failure classification (BuildFailed / TestsFailed / CommandTimedOut / Unknown,
   bounded excerpt, error lines preferred)
-> budget remaining? NO -> run transitions to Failed with a NAMED reason
   (also fixes a latent defect: a failed build previously left the run silently stuck in Running)
-> budget remaining? YES -> SkeletonRepairAttemptStarted (attempt, kind, command, budget)
-> Builder generates a repair proposal: same context pipeline, PLUS the failure
   excerpt (PastBuildFailures — a designed-but-dead context field, now live) and the
   previous attempt's files (RetrievedSnippets)
-> repair proposal persisted as proposal-repair-N.json (the original is never overwritten)
-> fresh attempt-scoped workspace ({runId}-repair-N) — the failed attempt's preserved
   workspace and evidence are never deleted
-> real dotnet build/test again -> success halts at the SAME human gate; failure
   re-enters the loop until the budget is spent
```

## Boundaries

- A repair attempt is proposal-shaped work, never authority: it cannot approve,
  disposition, continue, or apply anything. The gate after a repaired attempt is
  exactly the gate — a repaired run earns nothing.
- Bounded by explicit configuration: `SkeletonRepair:MaxAttempts`, default 0 (off).
  Hard-clamped to 3 so no configuration can create an unbounded retry loop.
- Attempt history is never erased: every attempt's evidence, proposal file, and
  events are preserved, along with failed attempts' workspaces (successful
  workspaces are cleaned as usual — `CleanWorkspaceOnSuccess`). The run report
  carries a `RepairAttempts` trace reconstructed from durable events. Trust comes
  from seeing the mess.
- Evidence binding (review-hardened): after a successful repair, the repaired
  proposal IS the proposal under review. The critic package's evidence refs and
  proposal id, the report's primary `Proposal` trace, and therefore the approval
  hash all bind to the FINAL proposal that built green; the original failed
  proposal is preserved separately as `InitialProposal` — history, never the
  gate proposal.
- Failure without budget is terminal and named (`BuildFailed` / `RepairBudgetExhausted`),
  never a silently stuck run.

## What is proven (all executed locally against real SQL, real dotnet builds)

- `Repair_FirstAttemptFails_RepairReachesGate_HistoryPreserved` (19s): broken first
  attempt, real compiler failure classified, repair context carries the real error
  excerpt and the previous proposal, repaired attempt builds green and halts
  `PausedForApproval`; both attempts' evidence on disk; continuation still refuses
  without critic review + approval.
- `Repair_BudgetExhausted_RunFailsWithNamedReason` (12s): two broken attempts, exactly
  one repair fired, run `Failed`, `SkeletonRunBlocked.blockedReason = RepairBudgetExhausted`,
  both attempts' evidence preserved.
- `Repair_DisabledByDefault_FailureIsTerminalAndNamed` (7s): no configuration -> zero
  repair calls, run `Failed` with `blockedReason = BuildFailed` (stuck-Running defect fixed).
- `SkeletonBuildFailureClassifierTests` 5/5 (fast unit lane): build/test/timeout/none
  classification, bounded error-preferring excerpts.

## What is NOT proven

- Live-model repair quality (a deterministic two-stage builder proves the plumbing;
  whether a real model repairs well is live-walk evidence, gathered per run).
- Repair for critic findings (this is build/test repair only; finding-driven revision
  is a separate future contract).
- UI surfacing of repair attempts (report carries the trace; the flow UI rendering it
  is DEMO/UI-journey scope).

## Files Changed

- `IronDev.Core/Builder/SkeletonRepairModels.cs` (new — kinds, classifier, repair context)
- `IronDev.Core/Builder/SkeletonRunReport.cs` (RepairAttempts trace)
- `IronDev.Core/Interfaces/IBuilderServices.cs` (GenerateRepairProposalAsync)
- `IronDev.Core/Workspaces/DisposableWorkspaceModels.cs` (AttemptLabel)
- `IronDev.Infrastructure/Builder/BuilderProposalService.cs` (real repair pipeline)
- `IronDev.Infrastructure/Builder/CodeChangeProposalService.cs` (renders PastBuildFailures)
- `IronDev.Infrastructure/Services/TicketSkeletonRunService.cs` (bounded attempt loop, named terminal failure, report trace)
- `IronDev.Infrastructure/Services/Workspaces/DisposableWorkspaceExecutionService.cs` (attempt-scoped paths)
- `IronDev.Client/Tickets/TicketsApiClient.cs` (explicit no-client-repair refusal)
- Test fakes updated; proofs + classifier tests + contract added; CI lane executes the proofs by exact name.

## Validation

- Full solution build: 0 errors.
- Repair proofs 3/3 against real SQL with real disposable builds; classifier 5/5.
- Regression: DemoSeed suite and REL-3/REL-5 alpha smokes re-run green (see PR body).
- Contract suite green including `BoundedRepair_IsOffByDefaultBoundedAndExecutedInCi`.

## Review Line

Repair widens what the loop survives. It must not widen what the loop is allowed to do.

## Killjoy Line

A retry is not a repair unless the failure evidence shaped the second attempt — and a repair is not progress unless the human gate at the end is exactly as hard as it was before.
