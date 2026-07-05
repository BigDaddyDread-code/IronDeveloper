# REL-2 Deterministic Applied Smoke Receipt

## Purpose

REL-2 extends the deterministic BookSeller alpha smoke from a human approval halt to a service-level `Applied` state.

The slice proves one ticket can move through:

1. deterministic builder proposal
2. real disposable workspace build/test
3. hash-sealed critic package
4. deterministic clean critic review evidence
5. explicit phrase-bound accepted approval record
6. continuation request
7. governed copy-only apply spine
8. final report reconstruction

## Boundary

REL-2 does not create hidden approval.

Applied mode requires an explicit operator phrase:

```text
I approve continuation for run <runId> package <hash>
```

The smoke binds that phrase to the generated run id and critic package hash before recording the in-memory accepted approval.

## Important Correction

REL-2 exposed a lifecycle mismatch in `TicketSkeletonRunService.ApplyAsync(...)`.

The service was trying to transition directly from `Completed` to `Applied`, while the canonical lifecycle requires:

```text
Completed -> Promoted -> Applied
```

This PR updates the service to emit a `SkeletonApplyPromoted` event and transition through `Promoted` before `Applied`.

That is not new authority. It aligns the existing apply service with the existing lifecycle contract.

## Files Changed

- `Scripts/smoke/alpha-smoke.ps1`
- `IronDev.Infrastructure/Services/TicketSkeletonRunService.cs`
- `IronDev.IntegrationTests/AlphaLoopSmokeTests.cs`
- `IronDev.IntegrationTests/Smoke/AlphaSmokeScriptContractTests.cs`
- `Docs/alpha-smoke/README.md`
- `Docs/alpha-smoke/book-seller-single-ticket.md`
- `Docs/alpha-smoke/reason-codes.md`
- `Docs/release/v0.1-local-alpha/READINESS_INVENTORY.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/receipts/REL2_DETERMINISTIC_APPLIED_SMOKE.md`

## Validation

- `AlphaLoopSmokeTests.AlphaSmoke_OneTicket_ReachesHumanGate_WithADeterministicBuilder`: passed
- `AlphaLoopSmokeTests.AlphaSmoke_OneTicket_ReachesApplied_WithDeterministicApproval`: passed
- `Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Applied -RecordHumanApproval -ApprovalPhrase "I approve continuation for run <runId> package <hash>"`: passed
- `SkeletonRunTests.ApplyAsync_FullLoop_AppliesThroughTheGovernedSpine_IntoARealRepo`: passed
- `AlphaSmokeScriptContractTests`: 9/9 passed
- `IntegrationTestCategoryContractTests`: 7/7 passed
- `BlockC11SecretScanningRegressionTests`: 9/9 passed
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with existing warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Not Proven

REL-2 does not prove:

- SQL/API persisted ticket, run, approval, receipt, or report state
- UI approval recording
- live model behavior
- external critic service execution
- commit
- push
- pull request creation
- merge
- release
- deployment

`-RequireExistingAcceptedApproval` is intentionally blocked in REL-2 with `AcceptedApprovalRequired`; REL-3 owns the persisted SQL/API proof.

## Review Line

Applied is a governed copy-only source mutation. It is not commit, push, release, deployment, or alpha readiness.

## Killjoy

A run can reach Applied only after the gate that owns each inch has spoken.
