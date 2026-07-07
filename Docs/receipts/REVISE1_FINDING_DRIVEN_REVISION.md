# REVISE-1 Receipt - Finding-Driven Revision

## Purpose

REPAIR-1 answered a red build. REVISE-1 answers the other common gate outcome:
the build is green, the critic raised findings, and the human's honest decision
is "fix it now" — not accept-risk, not defer, not reject. Before REVISE-1 that
decision meant abandoning the run and starting over. Now the human at the gate
may direct ONE bounded, governed answer: revise the proposal under review.

```text
run halts PausedForApproval -> critic review records findings
-> human POSTs .../skeleton-runs/{runId}/revise { findingIds, reason }
-> contract checks: at the gate; instruction present; findings cited; cited findings
   exist on a review OF THE CURRENT PACKAGE and are undispositioned; NO other
   finding is left unanswered; budget remains (SkeletonRevision:MaxAttempts, default 0)
-> SkeletonRevisionAttemptStarted -> Builder revises from the HUMAN's instruction +
   cited finding ids + the proposal read from the hash-sealed package (never critic
   text pasted by the requester, never a blank slate)
-> revision proposal persisted as proposal-revise-N.json; Tester re-authors from the
   unchanged criteria (still blind to the diff); fresh attempt-scoped workspace
   ({runId}-revise-N); real dotnet build/test
-> GREEN: superseded package preserved as critic-package-superseded-N.json, revised
   package becomes canonical (new hash), cited findings get AddressedByRevision
   dispositions, run re-halts at the SAME gate on the NEW hash
-> RED: SkeletonRevisionAttemptFailed named, budget spent, run returns to the gate
   with the PREVIOUS package untouched and the cited findings still blocking
```

## Boundaries

- A revision is human-directed, proposal-shaped work, never authority: it cannot
  approve, continue, or apply anything. The gate after a revision is exactly the
  gate — the revised package needs its OWN critic review, its own finding
  dispositions, and its own hash-bound accepted approval.
- The gate is now hash-scoped (hardening this contract forced): continuation and
  apply require a critic review whose recorded `packageSha256` matches the
  CURRENT package on disk. A review of a superseded pre-revision package
  satisfies nothing — before this change, any recorded review satisfied the
  review check while only the approval was hash-bound.
- Revision is driven by the human's WRITTEN instruction and cited finding ids.
  Critic text never silently becomes Builder input: if the human misstates the
  finding, the critic re-reviews the revised package and re-raises it.
- No finding is left unanswered behind a revision: every undispositioned finding
  must be cited (→ `AddressedByRevision`, recorded ONLY after the revision
  builds green) or already dispositioned. `AddressedByRevision` cannot be
  recorded through the disposition surface — a human cannot claim a revision
  that never ran.
- Bounded by explicit configuration: `SkeletonRevision:MaxAttempts`, default 0
  (off). Hard-clamped to 3. A spent budget refuses by name and leaves the gate's
  disposition/approval path fully available — never a dead run.
- Attempt history is never erased: the superseded package, every revision
  proposal, failed attempts' workspaces, and all events are preserved; the run
  report carries a `RevisionAttempts` trace reconstructed from durable events.

## What is proven (executed locally against real SQL, real dotnet builds)

- `Revise_CitedFinding_RunsRevisionToTheSameGate_AndTheRevisedPackageNeedsItsOwnReview`:
  green first attempt, deterministic critic finding, human-directed revision
  builds green and re-halts; new package hash != superseded hash; approval
  requirement re-bound to the new hash; report's gate proposal is
  `prop-{runId}-revise-1` with the original preserved as InitialProposal;
  superseded package on disk; AddressedByRevision disposition recorded; and
  continuation REFUSES (`CriticReviewMissing`) because the old review does not
  cover the revised package.
- `Revise_OffByDefault_RefusedNamed_AndTheGateIsUnchanged`: no configuration ->
  `RevisionDisabled` refusal, zero Builder revision calls, package hash unchanged.
- `Revise_RefusesToLeaveUncitedFindingsUnanswered`: two findings, one cited ->
  `UndispositionedFindingsNotCited` refusal before any Builder call.
- `Revise_FailedRevisionBuild_LeavesThePreviousGateCanonical_AndSpendsTheBudget`:
  broken revision -> run returns to the gate, previous package still canonical
  and hash-verified, NO disposition recorded (the finding keeps blocking),
  RevisionAttempts trace says Failed/BuildFailed, second request refuses
  `RevisionBudgetExhausted`.
- `Revise_AHumanCannotClaimAddressedByRevisionDirectly`: the disposition surface
  refuses the AddressedByRevision kind with the named reason.

## What is NOT proven

- Live-model revision quality (a deterministic builder proves the plumbing;
  whether a real model revises well is live-walk evidence, gathered per run).
- UI surfacing of revision attempts and the revise action (report carries the
  trace; the flow UI rendering/requesting it is DEMO/UI-journey scope).
- Revision of a repair-produced gate proposal in one run (composes by design —
  the revision reads whatever the sealed canonical package holds — but no
  combined repair-then-revise proof was executed).

## Files Changed

- `IronDev.Core/Builder/SkeletonRevisionModels.cs` (new — request + builder context)
- `IronDev.Core/Builder/SkeletonFindingDisposition.cs` (AddressedByRevision kind)
- `IronDev.Core/Builder/SkeletonRunReport.cs` (RevisionAttempts trace)
- `IronDev.Core/Builder/BuilderDtos.cs` (RevisionDirectives context field)
- `IronDev.Core/Interfaces/IBuilderServices.cs` (GenerateRevisionProposalAsync)
- `IronDev.Core/Interfaces/ITicketSkeletonRunService.cs` (ReviseAsync)
- `IronDev.Infrastructure/Builder/BuilderProposalService.cs` (real revision pipeline)
- `IronDev.Infrastructure/Builder/CodeChangeProposalService.cs` (renders the human's directives)
- `IronDev.Infrastructure/Services/TicketSkeletonRunService.cs` (ReviseAsync; hash-scoped review gate at continue AND apply; report: current package/halt + revision trace)
- `IronDev.Infrastructure/Services/SkeletonFindingDispositionService.cs` (refuses direct AddressedByRevision)
- `IronDev.Infrastructure/Services/SkeletonRunDriftDetector.cs` (staleness measured from the CURRENT package-ready)
- `IronDev.Api/Controllers/TicketsController.cs` (POST .../skeleton-runs/{runId}/revise)
- `IronDev.Client/Tickets/TicketsApiClient.cs` (explicit no-client-revision refusal)
- Test fakes updated; proofs + contract pin added; full SQL CI lane executes the proofs by exact name; quarantine register row added.

## Validation

- Full solution build: 0 errors.
- Revision proofs 5/5 against real SQL with real disposable builds.
- Regression: skeleton-run orchestration, critic review, gate recommendation,
  batch-run, and DemoSeed contract suites re-run green after the hash-scoped
  gate change (the interface pin now names ReviseAsync; fabricated review events
  in tests now carry the honest package hash).

## Review Line

Revision widens what the gate can ask for. It must not widen what the gate lets through.

## Killjoy Line

A revision is not an answer unless the finding shaped the second proposal — and the second proposal is not progress until a fresh critic and the same hard gate have seen it.
