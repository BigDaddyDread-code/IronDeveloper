# PR237 - UI Authority Firewall Update

## What landed

Shared TauriShell UI authority firewall marker lists and tests for governance evidence views.

The firewall centralizes forbidden backend/runtime dependency markers, forbidden authority/action labels, unsafe/private/raw material markers, authority-claim markers, allowed negative boundary phrases, copy-only inspection labels, and the current governance evidence UI file allow-list.

The focused test harness covers the read-only governance evidence UI slices from PR229 through PR236:

- Accepted Approval UI
- Policy Satisfaction UI
- Dry-run Receipt UI
- Patch Artifact UI
- Source Apply Review UI
- Rollback UI
- Workflow Continuation UI
- Release Readiness UI

## What did not land

No backend mutation.
No API mutation endpoint.
No CLI mutation command.
No SQL.
No runtime worker.
No scheduler.
No release gate execution.
No release approval.
No deployment approval.
No merge approval.
No release execution.
No source apply execution.
No dry-run execution.
No rollback execution.
No rollback retry.
No rollback recovery.
No workflow continuation.
No workflow transition record creation.
No workflow state mutation.
No patch artifact creation.
No patch artifact editing.
No approval creation.
No authority refresh.
No evidence reissue.
No git operation.
No agent dispatch.
No model call.
No tool invocation.
No memory promotion.
No retrieval activation.

## What authority was not granted

The UI authority firewall does not grant authority.
The UI authority firewall does not replace backend governance.
The UI authority firewall does not decide release readiness.
The UI authority firewall does not approve release, deployment, merge, source apply, rollback, or workflow continuation.
The UI authority firewall does not execute anything.
The UI authority firewall does not mutate workflow state.
The UI authority firewall does not make fixture data backend truth.
The UI authority firewall does not make UI state authoritative.
Human review remains required.

## Validation run

Focused validation for this slice:

- `npm run build`
- `npx playwright test tests/ui-authority-firewall.spec.ts --reporter=list --workers=1`
- neighboring governance evidence UI Playwright specs where useful
- `dotnet build IronDev.slnx --no-restore -v:minimal`
- `git diff --check`

## Known caveats

Static UI scanning is not backend governance.
Static UI scanning does not prove runtime backend behavior.
Fixture data is not backend truth.
Copy buttons remain inspection only.
Backend authority still belongs in governed backend services, not UI.
Human review remains required.

## Review line

PR237 updates the UI authority firewall. It does not grant authority.
