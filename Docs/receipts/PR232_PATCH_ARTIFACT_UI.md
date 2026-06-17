# PR232 - Patch Artifact UI

## What landed

Read-only TauriShell patch artifact evidence panel.

The UI displays supplied patch artifact id/hash, artifact status, source binding, subject/workflow binding, safe file/action summaries, warnings, stale/expired/incomplete states, raw-patch-payload warning, unsafe/private-material warnings, authority-claim warnings, boundary maxims, and copy-only inspection actions.

## What did not land

No patch artifact creation.
No patch artifact editing.
No raw patch execution.
No dry-run execution.
No source apply execution.
No backend mutation.
No API mutation endpoint.
No CLI mutation command.
No patch approval.
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

Patch artifact display does not grant patch authority.
Patch artifact display does not approve dry-run.
Patch artifact display does not grant source apply authority.
Patch artifact display does not prove source was applied.
Patch artifact display does not approve release, deployment, or merge.
Patch artifact display does not mutate workflow state.
Patch artifact display does not permit execution.
Patch artifact display does not refresh authority.
Patch artifact display does not reissue evidence.
Copying ids, hashes, file summaries, or evidence references is inspection only.
Human review remains required.

## Validation run

- `npm run build` from `IronDev.TauriShell`
- `npx playwright test tests/patch-artifact-panel.spec.ts --reporter=list --workers=1`
- `dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "PatchArtifactReadApi|SourceApplyDryRunReceiptReadApi|SourceApplyReadApi|ApplyPreview|GovernanceTrace|Operational|AcceptedApproval|PolicySatisfaction" --no-restore --logger "console;verbosity=minimal"`
- `dotnet build IronDev.slnx --no-restore -v:minimal`
- `git diff --check`

## Known caveats

Fixture or supplied UI data is display-only unless a future read API wiring slice explicitly connects backend read data.
UI display is not backend truth.
Patch artifact present does not mean patch artifact valid.
Patch artifact valid does not mean dry-run approved.
Dry-run approved does not mean source apply.
Patch artifact displayed does not mean source was changed.
Raw patch payloads are intentionally not rendered by this slice.
Human review remains required.

## Review line

PR232 shows patch artifact evidence. It does not apply the patch.
