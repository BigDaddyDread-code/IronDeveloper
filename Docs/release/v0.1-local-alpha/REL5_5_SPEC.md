# REL-5.5 - Governed Run UI Journey to Applied

## Purpose

Wire the existing governance surfaces into one continuous product-path journey: a persisted run halts at the gate, a human reviews critic evidence, dispositions findings, records the phrase-bound approval, requests continuation, requests controlled apply, and opens the final report, all through the UI against live API/SQL state.

The governance feature is already further along than the inventory implied. Panels and routes exist for accepted approval, approval package review, controlled action request, continuation evidence, source apply review, patch artifacts, and authority firewall behavior. REL-5.5 is therefore a wiring-and-honesty PR: the panels exist as isolated routes; what remains unproven is one continuous journey through them backed by live API state.

## Review Line

The UI is a window onto backend state and a place to perform explicit human acts. It renders authority; it never manufactures it.

## Killjoy Line

A gate journey that works panel-by-panel with hand-pasted IDs is a demo, not a product path. The journey is real when the halted run leads the human to the next screen itself.

## Suggested PR

Title:

```text
flow(governance): wire governed run UI journey from gate to Applied
```

Branch:

```text
flow/rel5-5-governed-run-ui-journey
```

## Required User Flow

```text
Run reaches PausedForApproval.
Ticket/run view shows gate status with reason, not a generic spinner.
User opens critic package/review evidence for that run.
If findings exist, user dispositions each required finding.
User opens approval recording for the run.
UI displays run ID, approval target hash, and critic review reference read from the backend.
User performs the explicit approval act.
User requests continuation; UI shows backend accept/refuse with reason code.
User requests controlled apply; UI shows backend accept/refuse with reason code.
User opens the final report; report names consumed approval and apply receipt.
```

## Approval Interaction

The approval screen must display these values fetched from the backend:

- Run ID
- Critic package hash
- Approval target hash
- Critic review ID

The human act is typing or explicitly confirming the phrase bound to that run and hash:

```text
I approve continuation for run <runId> package <hash>
```

The frontend submits the phrase and identifiers. The backend validates phrase, hash match, and critic review existence.

Rules:

- The frontend must not pre-fill the phrase as a one-click submission.
- The frontend must not compute, guess, or cache the target hash from anything except the backend response for this run.
- A backend refusal, including mismatch, missing review, or undispositioned findings, renders verbatim with its reason code.

## Required Deterministic Finding Fixture

Add one BookSeller ticket variant, or critic mode, that deterministically produces exactly one required finding, so the disposition surface is exercised on the release path instead of shipping unused.

If descoped, known limitations must state that disposition UI is unexercised in v0.1.

## Required UI Surfaces

- BookSeller fixture project visible/selectable, folded in from REL-5 if not already landed
- Ticket detail with run state and gate reason
- Critic package/review evidence view
- Finding disposition view
- Approval recording view
- Continuation request with backend result
- Apply request with backend result
- Final report view with apply receipt path/hash and consumed approval ID
- Model mode indicator, Deterministic or Live, visible during the journey
- Root safety status visible or one click away

## Required State-Honesty Rules

- `PausedForApproval` renders as awaiting human approval, never as `Approved` or `Ready`.
- Validation passed renders as evidence, never as approval.
- Accepted approval renders as approval, never as policy satisfaction or apply permission.
- `Completed` renders as completed, never as `Applied`.
- `Applied` renders only when the backend reports `Applied`.
- Disabled actions state the backend reason code, not a generic tooltip.

## Required Reason Codes

These are rendered, not invented:

```text
AcceptedApprovalRequired
ApprovalPhraseMismatch
ApprovalTargetHashMismatch
ContinuationRequiresCriticReview
ContinuationRequiresFindingDisposition
ContinuationRefused
ApplyRequiresContinuation
ApplyRefused
RootSafetyBlocked
FinalReportMissing
```

## Allowed Files

```text
IronDev.TauriShell/src/features/governance/*
IronDev.TauriShell/src/features/runReports/*
IronDev.TauriShell/src/features/tickets/*
IronDev.TauriShell/src/components/TicketRunReviewPanel.tsx
IronDev.TauriShell/src/api/*
IronDev.TauriShell/tests/*
IronDev.Api/* read-model/route additions only if a journey step lacks a product route
IronDev.IntegrationTests.Api/*
Docs/receipts/REL5_5_GOVERNED_RUN_UI_JOURNEY.md
Docs/alpha-smoke/*
```

## Forbidden Behavior

- No frontend-computed approval hashes.
- No one-click approval that skips the explicit phrase-bound act.
- No UI state that upgrades backend state, such as halted to approved or completed to applied.
- No hidden auto-continuation after approval.
- No apply button enabled before backend-confirmed continuation.
- No journey step that requires hand-copying IDs between screens.
- No new authority routes; UI consumes the REL-2/REL-3 spine.

## Required Tests

Backend/API:

```text
ApprovalRouteExposesTargetHashAndReviewReference
ContinuationRefusalReturnsNamedReasonCode
ApplyRefusalReturnsNamedReasonCode
FindingFixtureProducesDeterministicRequiredFinding
ContinuationRefusesUndispositionedFindingFromUiPath
```

Frontend contract lane:

```text
GateStateRendersAsAwaitingApprovalNotApproved
ValidationPassedNeverRendersAsApproved
CompletedNeverRendersAsApplied
ApprovalScreenDisplaysBackendHashAndRunId
ApprovalPhraseNotPrefilledForOneClickSubmit
ContinuationButtonDisabledWithoutAcceptedApproval
ApplyButtonDisabledWithoutContinuation
DisabledActionsShowBackendReasonCode
FinalReportViewShowsConsumedApprovalAndApplyReceipt
DispositionRequiredBlocksApprovalPathUntilResolved
```

These frontend state-honesty tests should also be appended to the release-blocking test groups when that section is next touched.

## Receipt

Path:

```text
Docs/receipts/REL5_5_GOVERNED_RUN_UI_JOURNEY.md
```

The receipt must include:

- Commit SHA
- Journey steps walked and the surface used for each
- Run ID, critic package hash, accepted approval ID, and approval target hash
- Finding fixture used and disposition IDs
- Continuation and apply results
- Final report path
- State-honesty tests added
- Known gaps, such as restart-survival of UI session state
- Boundary statement

## Acceptance Criteria

- One deterministic BookSeller ticket goes gate to `Applied` entirely through the UI against SQL/API state.
- The disposition surface is exercised by the deterministic finding fixture, or explicitly descoped.
- Every rendered status maps to exactly one permitted backend state.
- No step requires PowerShell, hand-carried IDs, or author knowledge.

## Review Traps

Block if:

- The frontend computes or caches the approval hash.
- Approval submits without the explicit human act.
- Any surface renders a stronger state than the backend reports.
- Continuation fires automatically after approval is recorded.
- Apply is reachable without backend-confirmed continuation.
- The journey works only with hand-pasted run IDs.
- Tests assert UI labels without asserting the backend state they came from.

## Out Of Scope

- Live model
- Real-repo import
- UI polish beyond honesty and navigability
- Batch surfaces
- Restart-survival of in-flight UI sessions; backend restart-survival is REL-3 proof

## Next PR

REL-6 fresh-machine setup and release doctor.

## Notes

The main REL-5.5 risk is not missing UI. It is disconnected UI: a scavenger hunt where the dogfooder copies run IDs between isolated governance viewers. "No hand-carried IDs" is therefore part of the required flow, traps, and acceptance criteria. It is the difference between passing honestly and passing theatrically.

The finding fixture is in scope here. Disposition UI without a finding to disposition is untestable, and a fixture-only PR has weak dogfood value. Bundling the fixture keeps the review question concrete: can a new user reach `Applied` through the governed product path?

Remaining loose ends:

- Resolve the REL-4 numbering fork between spec and inventory.
- Confirm where fixture-project visibility lands if REL-5 has not already covered it.
