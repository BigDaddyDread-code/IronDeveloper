# PR233 - Source Apply Review UI

## What landed

Read-only TauriShell source-apply review evidence panel.

The UI displays supplied source-apply review id/hash, source-apply request binding, patch artifact binding, dry-run receipt binding, subject/workflow binding, review status, planned file/action summaries, warnings, stale/expired/incomplete states, unsafe/private-material warnings, authority-claim warnings, boundary maxims, and copy-only inspection actions.

## What did not land

No source apply approval.
No source apply execution.
No dry-run execution.
No patch artifact creation.
No patch artifact editing.
No backend mutation.
No API mutation endpoint.
No CLI mutation command.
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

Source-apply review display does not grant source apply authority.
Source-apply review display does not prove source was applied.
Source-apply review display does not approve release, deployment, or merge.
Source-apply review display does not mutate workflow state.
Source-apply review display does not permit execution.
Source-apply review display does not refresh authority.
Source-apply review display does not reissue evidence.
Copying ids, hashes, file summaries, or evidence references is inspection only.
Human review remains required.

## Validation run

- `npm run build` from `IronDev.TauriShell`
- `npx playwright test tests/source-apply-review-panel.spec.ts --reporter=list --workers=1`
- `dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "SourceApplyReadApi|SourceApplyDryRunReceiptReadApi|PatchArtifactReadApi|ApplyPreview|GovernanceTrace|Operational|AcceptedApproval|PolicySatisfaction" --no-restore --logger "console;verbosity=minimal"`
- `dotnet build IronDev.slnx --no-restore -v:minimal`
- `git diff --check`

## Known caveats

Fixture or supplied UI data is display-only unless a future read API wiring slice explicitly connects backend read data.
UI display is not backend truth.
Patch artifact present does not mean patch artifact valid.
Dry-run receipt present does not mean source apply approved.
Source-apply review complete does not mean source apply.
Human review remains required.

## Review line

PR233 reviews source-apply evidence. It does not apply source.
