import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { workflowContinuationEvidenceBoundaryRules } from './WorkflowContinuationEvidenceBoundary';
import { WorkflowContinuationEvidencePanel } from './WorkflowContinuationEvidencePanel';
import type { WorkflowContinuationEvidence } from './WorkflowContinuationEvidenceTypes';
import { workflowContinuationEvidenceDefaultDisplayState } from './WorkflowContinuationEvidenceTypes';

interface WorkflowContinuationEvidencePanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function WorkflowContinuationEvidencePanelRoute({ onRouteReady }: WorkflowContinuationEvidencePanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Workflow continuation evidence', testId: 'workflow-continuation-evidence.chip.evidence' },
      { label: 'Read-only', testId: 'workflow-continuation-evidence.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <WorkflowContinuationEvidencePanel isLoading />;
  }

  if (fixture === 'error') {
    return <WorkflowContinuationEvidencePanel errorMessage="Unable to load workflow continuation evidence." />;
  }

  if (fixture === 'missing') {
    return <WorkflowContinuationEvidencePanel evidence={null} />;
  }

  return <WorkflowContinuationEvidencePanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): WorkflowContinuationEvidence {
  const current: WorkflowContinuationEvidence = {
    workflowContinuationEvidenceId: 'workflow-continuation-evidence-235',
    workflowContinuationEvidenceHash: 'sha256:workflow-continuation-evidence-hash-235',
    projectId: 'project-7',
    subjectKind: 'WorkflowContinuationGateEvaluation',
    subjectId: 'continuation-gate-evaluation-235',
    subjectHash: 'sha256:continuation-gate-evaluation-hash-235',
    workflowRunId: 'workflow-run-235',
    workflowStepId: 'workflow-step-235',
    continuationGateEvaluationId: 'continuation-gate-evaluation-235',
    continuationGateEvaluationHash: 'sha256:continuation-gate-evaluation-hash-235',
    workflowTransitionRecordId: 'workflow-transition-record-235',
    workflowTransitionRecordHash: 'sha256:workflow-transition-record-hash-235',
    sourceApplyReceiptId: 'source-apply-receipt-235',
    sourceApplyReceiptHash: 'sha256:source-apply-receipt-hash-235',
    rollbackExecutionReceiptId: 'rollback-execution-receipt-235',
    rollbackExecutionReceiptHash: 'sha256:rollback-execution-receipt-hash-235',
    reviewedBy: 'human-reviewer-235',
    reviewedAtUtc: '2026-06-18T00:00:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    continuationStatus: 'EvidenceReadyForHumanReview',
    continuationGatePresent: true,
    continuationGateSatisfied: true,
    transitionRecordPresent: true,
    transitionRecordValid: true,
    workflowContinuedElsewhere: true,
    workflowContinuationFailed: false,
    workflowContinuationPartial: false,
    workflowMutationDetected: false,
    stepSummaries: [
      {
        stepId: 'workflow-step-235',
        stepName: 'Apply review',
        status: 'TransitionEvidencePresent',
        safeSummary: 'Workflow continuation evidence says the transition has gate and transition-record evidence.'
      },
      {
        stepId: 'workflow-step-236',
        stepName: 'Post-continuation review',
        status: 'PendingHumanReview',
        safeSummary: 'Workflow continuation evidence says the next step remains review-bound.'
      }
    ],
    warnings: ['Workflow continuation display does not continue workflow.'],
    evidenceRefs: ['continuation-gate-evaluation-235', 'workflow-transition-record-235', 'workflow-run-235'],
    boundaryMaxims: workflowContinuationEvidenceBoundaryRules,
    displayState: {
      ...workflowContinuationEvidenceDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-evidence-id') {
    return { ...current, workflowContinuationEvidenceId: '', incomplete: true };
  }

  if (fixture === 'missing-evidence-hash') {
    return { ...current, workflowContinuationEvidenceHash: '', incomplete: true };
  }

  if (fixture === 'missing-gate') {
    return { ...current, continuationGateEvaluationId: '', continuationGateEvaluationHash: '', continuationGatePresent: false, continuationGateSatisfied: false, incomplete: true };
  }

  if (fixture === 'missing-subject') {
    return { ...current, subjectKind: '', subjectId: '', subjectHash: '', incomplete: true };
  }

  if (fixture === 'missing-workflow') {
    return { ...current, workflowRunId: '', workflowStepId: '', incomplete: true };
  }

  if (fixture === 'invalid-timestamp') {
    return { ...current, reviewedAtUtc: 'not-a-date', incomplete: true };
  }

  if (fixture === 'empty-refs') {
    return { ...current, evidenceRefs: [], displayState: { ...current.displayState, evidencePresent: false, evidenceSatisfied: false } };
  }

  if (fixture === 'missing-boundary') {
    return { ...current, boundaryMaxims: [], incomplete: true };
  }

  if (fixture === 'stale') {
    return { ...current, stale: true, warnings: ['stale workflow continuation evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired workflow continuation evidence'] };
  }

  if (fixture === 'partial') {
    return { ...current, workflowContinuedElsewhere: false, workflowContinuationPartial: true, continuationStatus: 'PartialContinuationEvidence' };
  }

  if (fixture === 'failed') {
    return { ...current, workflowContinuedElsewhere: false, workflowContinuationFailed: true, continuationStatus: 'FailedContinuationEvidence' };
  }

  if (fixture === 'mutation-detected') {
    return { ...current, workflowMutationDetected: true, continuationStatus: 'WorkflowMutationDetected' };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      stepSummaries: [{ ...current.stepSummaries[0], safeSummary: 'raw prompt private reasoning should redact' }],
      evidenceRefs: ['raw diff should redact', 'workflow-run-235'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['workflow continuation approved and safe to release by fixture data'],
      authorityClaimsDetected: true
    };
  }

  if (fixture === 'contradictory') {
    return {
      ...current,
      displayState: {
        ...current.displayState,
        workflowContinuationApproved: true,
        workflowContinued: true,
        workflowMutated: true,
        workflowTransitionRecordCreated: true,
        mutationPerformed: true
      }
    } as unknown as WorkflowContinuationEvidence;
  }

  return current;
}
