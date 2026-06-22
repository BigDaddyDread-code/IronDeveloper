import { useEffect, useMemo, useState } from 'react';
import type { ControlledActionRequestCreateRequest } from '../../api/types';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import { useSessionContext } from '../../state/useSessionContext';
import { ControlledActionRequestUi } from './ControlledActionRequestUi';
import type { ActionRequestDraft, ActionRequestUiLoadStatus, ActionRequestUiModel } from './ControlledActionRequestTypes';

interface ControlledActionRequestRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

const initialDraft: ActionRequestDraft = {
  requestKind: 'SourceApply',
  requestId: 'action-request-pr32',
  operationId: 'operation-pr32',
  repository: 'BigDaddyDread-code/IronDeveloper',
  branch: 'frontend/controlled-action-request-ui',
  runId: 'run-pr32',
  humanIntent: 'Request backend review for the selected governed action.',
  evidenceRefsText: 'patch-package:patch-package-pr32\nvalidation-result:validation-pr32',
  receiptRefsText: 'patch-package-receipt:receipt-pr32',
  patchPackageId: 'patch-package-pr32',
  patchHash: 'sha256:patch-pr32',
  proposedFilePathsText: 'IronDev.Core/Governance/Example.cs',
  sourceApplyReceiptRef: 'source-apply-receipt:receipt-pr32',
  commitPackageId: 'commit-package-pr32',
  commitMessageEvidenceRef: 'commit-message:message-pr32',
  commitSha: 'commit-sha-pr32',
  remoteTarget: 'origin',
  pushIntent: 'request push of governed commit only',
  headBranch: 'frontend/controlled-action-request-ui',
  baseBranch: 'main',
  pushedCommitSha: 'commit-sha-pr32',
  draftPullRequestPackageId: 'draft-pr-package-pr32',
  pullRequestTextPackageRef: 'draft-pr-text-package:pr32',
  rollbackTargetReceiptRef: 'rollback-target-receipt:pr32',
  rollbackScopePathsText: 'IronDev.Core/Governance/Example.cs'
};

export function ControlledActionRequestRoute({ onRouteReady }: ControlledActionRequestRouteProps) {
  const session = useSessionContext();
  const [draft, setDraft] = useState<ActionRequestDraft>(initialDraft);
  const [status, setStatus] = useState<ActionRequestUiLoadStatus>('idle');
  const [model, setModel] = useState<ActionRequestUiModel>({
    response: null,
    message: 'No action request has been submitted.'
  });
  const canRequest = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: [],
      workspaceBlockReason: canRequest ? null : 'Controlled action requests require API connection and authentication.',
      workspaceSummaryChips: [
        { label: 'Action request', testId: 'action-request.chip.surface' },
        { label: draft.requestKind, testId: 'action-request.chip.kind' },
        { label: 'Request only', testId: 'action-request.chip.boundary' }
      ]
    }),
    [canRequest, draft.requestKind]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  async function submit() {
    if (!canRequest) {
      setStatus('error');
      setModel({
        response: null,
        message: 'Controlled action requests require API connection and authentication.'
      });
      return;
    }

    setStatus('submitting');
    setModel((current) => ({
      ...current,
      message: 'Creating request-only backend record. No action is executed from this UI.'
    }));

    try {
      const response = await session.client.createFrontendControlledActionRequest(toRequest(draft));
      setStatus(response.requestCreated ? 'submitted' : 'rejected');
      setModel({
        response,
        message: response.requestCreated
          ? 'Request record created. Backend eligibility still decides.'
          : 'Request was rejected before backend eligibility review.'
      });
    } catch (error: unknown) {
      setStatus('error');
      setModel({
        response: null,
        message: error instanceof IronDevApiError ? error.message : 'Request creation failed without executing any action.'
      });
    }
  }

  return (
    <ControlledActionRequestUi
      draft={draft}
      status={status}
      model={model}
      onDraftChange={setDraft}
      onSubmit={submit}
    />
  );
}

function toRequest(draft: ActionRequestDraft): ControlledActionRequestCreateRequest {
  return {
    requestId: draft.requestId,
    operationId: draft.operationId,
    requestKind: draft.requestKind,
    repository: draft.repository,
    branch: draft.branch,
    runId: draft.runId,
    patchPackageId: draft.patchPackageId,
    patchHash: draft.patchHash,
    proposedFilePaths: splitLines(draft.proposedFilePathsText),
    sourceApplyReceiptRef: draft.sourceApplyReceiptRef,
    commitPackageId: draft.commitPackageId,
    commitMessageEvidenceRef: draft.commitMessageEvidenceRef,
    commitSha: draft.commitSha,
    remoteTarget: draft.remoteTarget,
    pushIntent: draft.pushIntent,
    headBranch: draft.headBranch,
    baseBranch: draft.baseBranch,
    pushedCommitSha: draft.pushedCommitSha,
    draftPullRequestPackageId: draft.draftPullRequestPackageId,
    pullRequestTextPackageRef: draft.pullRequestTextPackageRef,
    rollbackTargetReceiptRef: draft.rollbackTargetReceiptRef,
    rollbackScopePaths: splitLines(draft.rollbackScopePathsText),
    humanIntent: draft.humanIntent,
    evidenceRefs: splitLines(draft.evidenceRefsText),
    receiptRefs: splitLines(draft.receiptRefsText),
    requestedAtUtc: new Date().toISOString()
  };
}

function splitLines(value: string) {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}
