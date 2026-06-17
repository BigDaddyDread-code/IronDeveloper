import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { AcceptedApprovalPanel } from './AcceptedApprovalPanel';
import type { AcceptedApprovalEvidenceViewModel } from './AcceptedApprovalTypes';

interface AcceptedApprovalPanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function AcceptedApprovalPanelRoute({ onRouteReady }: AcceptedApprovalPanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Accepted approval evidence', testId: 'accepted-approvals.chip.evidence' },
      { label: 'Read-only', testId: 'accepted-approvals.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'empty';

  if (fixture === 'loading') {
    return <AcceptedApprovalPanel isLoading />;
  }

  if (fixture === 'error') {
    return <AcceptedApprovalPanel errorMessage="Unable to load accepted approval evidence." />;
  }

  if (fixture === 'empty') {
    return <AcceptedApprovalPanel evidence={null} />;
  }

  return <AcceptedApprovalPanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): AcceptedApprovalEvidenceViewModel {
  const current: AcceptedApprovalEvidenceViewModel = {
    acceptedApprovalId: 'accepted-approval-229',
    acceptedApprovalHash: 'sha256:accepted-approval-hash-229',
    projectId: 'project-229',
    subjectKind: 'source_apply_request',
    subjectId: 'source-apply-request-229',
    subjectHash: 'sha256:subject-hash-229',
    workflowRunId: 'workflow-run-229',
    workflowStepId: 'workflow-step-229',
    acceptedBy: 'human-reviewer-229',
    acceptedAtUtc: '2026-06-18T00:00:00Z',
    evidenceReferences: ['approval-package-229', 'policy-satisfaction-229', 'source-apply-gate-229'],
    boundaryMaxims: ['Accepted approval evidence is display only.', 'Copying ids does not create approval.'],
    humanReviewRequired: true,
    releaseApproved: false,
    deploymentApproved: false,
    mergeApproved: false,
    releaseExecuted: false,
    sourceApplyExecuted: false,
    rollbackExecuted: false,
    workflowContinued: false,
    workflowMutated: false,
    gitOperationExecuted: false,
    authorityRefreshed: false,
    evidenceReissued: false
  };

  if (fixture === 'stale') {
    return { ...current, isStale: true, staleReasonCodes: ['authority_expired'] };
  }

  if (fixture === 'expired') {
    return { ...current, isExpired: true, staleReasonCodes: ['approval_window_expired'] };
  }

  if (fixture === 'missing-hash') {
    return { ...current, acceptedApprovalHash: '' };
  }

  if (fixture === 'missing-subject') {
    return { ...current, subjectKind: '', subjectId: '', subjectHash: '' };
  }

  if (fixture === 'missing-workflow') {
    return { ...current, workflowRunId: '', workflowStepId: '' };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      evidenceReferences: ['private reasoning raw prompt should redact', 'approval-package-229'],
      boundaryMaxims: ['hidden reasoning should redact']
    };
  }

  return current;
}
