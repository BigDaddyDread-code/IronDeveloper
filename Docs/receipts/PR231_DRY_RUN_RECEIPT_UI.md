# PR231 - Dry-run Receipt UI

## What landed

Read-only TauriShell source-apply dry-run receipt evidence panel.

The UI displays supplied dry-run receipt id/hash, source-apply request binding, subject/workflow binding, validation outcome, planned file/action summary, warnings, stale/expired/incomplete states, unsafe-material warnings, authority-claim warnings, boundary maxims, and copy-only inspection actions.

## What did not land

No dry-run execution.
No source apply execution.
No backend mutation.
No API mutation endpoint.
No CLI mutation command.
No source apply approval.
No release approval.
No deployment approval.
No merge approval.
No rollback execution.
No workflow continuation.
No authority refresh.
No evidence reissue.
No scheduler.
No autonomous loop.
No git operation.
No agent dispatch.
No model call.
No tool invocation.
No memory promotion.
No retrieval activation.

## What authority was not granted

Dry-run receipt display does not grant source apply authority.
Dry-run receipt display does not prove source was applied.
Dry-run receipt display does not approve source apply.
Dry-run receipt display does not approve release, deployment, or merge.
Dry-run receipt display does not mutate workflow state.
Dry-run receipt display does not permit execution.
Dry-run receipt display does not refresh authority.
Dry-run receipt display does not reissue evidence.
Copying ids, hashes, or evidence references is inspection only.
Human review remains required.

## Validation run

- `npm run build` from `IronDev.TauriShell`
- `npx playwright test tests/source-apply-dry-run-receipt-panel.spec.ts --reporter=list --workers=1`
- `dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "SourceApplyDryRunReceiptReadApi|SourceApplyReadApi|ApplyPreview|GovernanceTrace|Operational|AcceptedApproval|PolicySatisfaction" --no-restore --logger "console;verbosity=minimal"`
- `dotnet build IronDev.slnx --no-restore -v:minimal`
- `git diff --check`

## Known caveats

Fixture or supplied UI data is display-only unless a future read API wiring slice explicitly connects backend read data.
UI display is not backend truth.
Receipt present does not mean receipt valid.
Receipt valid does not mean source apply approved.
Dry-run passed does not mean source applied.
Human review remains required.

## Review line

PR231 shows dry-run receipt evidence. It does not apply source.
