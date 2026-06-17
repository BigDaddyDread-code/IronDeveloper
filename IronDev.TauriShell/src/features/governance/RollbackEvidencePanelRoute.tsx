import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { rollbackEvidenceBoundaryRules } from './RollbackEvidenceBoundary';
import { RollbackEvidencePanel } from './RollbackEvidencePanel';
import type { RollbackEvidence } from './RollbackEvidenceTypes';
import { rollbackEvidenceDefaultDisplayState } from './RollbackEvidenceTypes';

interface RollbackEvidencePanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function RollbackEvidencePanelRoute({ onRouteReady }: RollbackEvidencePanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Rollback evidence', testId: 'rollback-evidence.chip.evidence' },
      { label: 'Read-only', testId: 'rollback-evidence.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <RollbackEvidencePanel isLoading />;
  }

  if (fixture === 'error') {
    return <RollbackEvidencePanel errorMessage="Unable to load rollback evidence." />;
  }

  if (fixture === 'missing') {
    return <RollbackEvidencePanel evidence={null} />;
  }

  return <RollbackEvidencePanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): RollbackEvidence {
  const current: RollbackEvidence = {
    rollbackEvidenceId: 'rollback-evidence-234',
    rollbackEvidenceHash: 'sha256:rollback-evidence-hash-234',
    projectId: 'project-7',
    subjectKind: 'RollbackPlan',
    subjectId: 'rollback-plan-234',
    subjectHash: 'sha256:rollback-plan-hash-234',
    workflowRunId: 'workflow-run-234',
    workflowStepId: 'workflow-step-234',
    sourceApplyReceiptId: 'source-apply-receipt-234',
    sourceApplyReceiptHash: 'sha256:source-apply-receipt-hash-234',
    rollbackPlanId: 'rollback-plan-234',
    rollbackPlanHash: 'sha256:rollback-plan-hash-234',
    rollbackSupportReceiptId: 'rollback-support-receipt-234',
    rollbackSupportReceiptHash: 'sha256:rollback-support-receipt-hash-234',
    rollbackExecutionReceiptId: 'rollback-execution-receipt-234',
    rollbackExecutionReceiptHash: 'sha256:rollback-execution-receipt-hash-234',
    rollbackAuditReportId: 'rollback-audit-report-234',
    rollbackAuditReportHash: 'sha256:rollback-audit-report-hash-234',
    reviewedBy: 'human-reviewer-234',
    reviewedAtUtc: '2026-06-18T00:00:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    rollbackStatus: 'EvidenceReadyForHumanReview',
    rollbackPlanPresent: true,
    rollbackSupportReceiptPresent: true,
    rollbackExecutionReceiptPresent: true,
    rollbackAuditReportPresent: true,
    rollbackSucceeded: true,
    rollbackPartial: false,
    rollbackFailed: false,
    rollbackAuditConsistent: true,
    affectedFileCount: 2,
    affectedFiles: [
      {
        path: 'src/apply/Widget.cs',
        action: 'Restore',
        safeSummary: 'Rollback evidence says Widget.cs has plan, support, execution, and audit evidence.',
        beforeHash: 'sha256:before-widget-234',
        afterHash: 'sha256:after-widget-234'
      },
      {
        path: 'tests/apply/WidgetTests.cs',
        action: 'RemoveCreatedFile',
        safeSummary: 'Rollback evidence says a created test file has rollback evidence.',
        beforeHash: 'sha256:before-test-234',
        afterHash: 'sha256:after-test-234'
      }
    ],
    warnings: ['Rollback display does not execute rollback.'],
    evidenceRefs: ['source-apply-receipt-234', 'rollback-plan-234', 'rollback-support-receipt-234', 'rollback-execution-receipt-234'],
    boundaryMaxims: rollbackEvidenceBoundaryRules,
    displayState: {
      ...rollbackEvidenceDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-evidence-id') {
    return { ...current, rollbackEvidenceId: '', incomplete: true };
  }

  if (fixture === 'missing-evidence-hash') {
    return { ...current, rollbackEvidenceHash: '', incomplete: true };
  }

  if (fixture === 'missing-source-apply-receipt') {
    return { ...current, sourceApplyReceiptId: '', sourceApplyReceiptHash: '', incomplete: true };
  }

  if (fixture === 'missing-rollback-plan') {
    return { ...current, rollbackPlanId: '', rollbackPlanHash: '', rollbackPlanPresent: false, incomplete: true };
  }

  if (fixture === 'missing-support-receipt') {
    return { ...current, rollbackSupportReceiptId: '', rollbackSupportReceiptHash: '', rollbackSupportReceiptPresent: false, incomplete: true };
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
    return { ...current, stale: true, warnings: ['stale rollback evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired rollback evidence'] };
  }

  if (fixture === 'partial') {
    return { ...current, rollbackSucceeded: false, rollbackPartial: true, rollbackStatus: 'PartialRollbackEvidence' };
  }

  if (fixture === 'failed') {
    return { ...current, rollbackSucceeded: false, rollbackFailed: true, rollbackStatus: 'FailedRollbackEvidence' };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      affectedFiles: [{ ...current.affectedFiles[0], safeSummary: 'raw patch private reasoning should redact' }],
      evidenceRefs: ['raw diff should redact', 'rollback-plan-234'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['rollback approved and safe to release by fixture data'],
      authorityClaimsDetected: true
    };
  }

  if (fixture === 'contradictory') {
    return {
      ...current,
      displayState: {
        ...current.displayState,
        rollbackApproved: true,
        rollbackExecuted: true,
        rollbackRetried: true,
        rollbackRecoveryStarted: true,
        workflowContinued: true,
        mutationPerformed: true
      }
    } as unknown as RollbackEvidence;
  }

  return current;
}
