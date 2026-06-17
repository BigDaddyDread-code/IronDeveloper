import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { sourceApplyDryRunReceiptBoundaryRules } from './SourceApplyDryRunReceiptBoundary';
import { SourceApplyDryRunReceiptPanel } from './SourceApplyDryRunReceiptPanel';
import type { SourceApplyDryRunReceiptEvidence } from './SourceApplyDryRunReceiptTypes';
import { sourceApplyDryRunReceiptDefaultDisplayState } from './SourceApplyDryRunReceiptTypes';

interface SourceApplyDryRunReceiptPanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function SourceApplyDryRunReceiptPanelRoute({ onRouteReady }: SourceApplyDryRunReceiptPanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Dry-run receipt evidence', testId: 'dry-run-receipt.chip.evidence' },
      { label: 'Read-only', testId: 'dry-run-receipt.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <SourceApplyDryRunReceiptPanel isLoading />;
  }

  if (fixture === 'error') {
    return <SourceApplyDryRunReceiptPanel errorMessage="Unable to load source apply dry-run receipt evidence." />;
  }

  if (fixture === 'missing') {
    return <SourceApplyDryRunReceiptPanel evidence={null} />;
  }

  return <SourceApplyDryRunReceiptPanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): SourceApplyDryRunReceiptEvidence {
  const current: SourceApplyDryRunReceiptEvidence = {
    dryRunReceiptId: 'dry-run-receipt-231',
    dryRunReceiptHash: 'sha256:dry-run-receipt-hash-231',
    sourceApplyRequestId: 'source-apply-request-231',
    sourceApplyRequestHash: 'sha256:source-apply-request-hash-231',
    projectId: 'project-7',
    subjectKind: 'PatchArtifact',
    subjectId: 'patch-artifact-231',
    subjectHash: 'sha256:patch-artifact-hash-231',
    workflowRunId: 'workflow-run-231',
    workflowStepId: 'workflow-step-231',
    requestedBy: 'human-reviewer-231',
    dryRunStartedAtUtc: '2026-06-18T00:00:00Z',
    dryRunCompletedAtUtc: '2026-06-18T00:01:00Z',
    receiptStoredAtUtc: '2026-06-18T00:02:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    dryRunStatus: 'Passed',
    validationPassed: true,
    plannedChangeCount: 2,
    plannedFiles: [
      {
        path: 'src/apply/Widget.cs',
        action: 'Modify',
        fileHashBefore: 'sha256:before-widget-231',
        fileHashAfter: 'sha256:after-widget-231',
        safeSummary: 'Method signature would change in the disposable dry-run workspace.'
      },
      {
        path: 'tests/apply/WidgetTests.cs',
        action: 'Create',
        fileHashAfter: 'sha256:after-widget-tests-231',
        safeSummary: 'New test file would be created in the disposable dry-run workspace.'
      }
    ],
    warnings: ['Dry-run receipt display does not apply source.'],
    evidenceRefs: ['source-apply-request-231', 'patch-artifact-231', 'apply-dry-run-record-231'],
    boundaryMaxims: sourceApplyDryRunReceiptBoundaryRules,
    displayState: {
      ...sourceApplyDryRunReceiptDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-receipt-id') {
    return { ...current, dryRunReceiptId: '', incomplete: true };
  }

  if (fixture === 'missing-receipt-hash') {
    return { ...current, dryRunReceiptHash: '', incomplete: true };
  }

  if (fixture === 'missing-request') {
    return { ...current, sourceApplyRequestId: '', sourceApplyRequestHash: '', incomplete: true };
  }

  if (fixture === 'missing-subject') {
    return { ...current, subjectKind: '', subjectId: '', subjectHash: '', incomplete: true };
  }

  if (fixture === 'missing-workflow') {
    return { ...current, workflowRunId: '', workflowStepId: '', incomplete: true };
  }

  if (fixture === 'invalid-timestamp') {
    return { ...current, dryRunCompletedAtUtc: 'not-a-date', incomplete: true };
  }

  if (fixture === 'empty-refs') {
    return { ...current, evidenceRefs: [], displayState: { ...current.displayState, evidencePresent: false, evidenceSatisfied: false } };
  }

  if (fixture === 'missing-boundary') {
    return { ...current, boundaryMaxims: [], incomplete: true };
  }

  if (fixture === 'stale') {
    return { ...current, stale: true, warnings: ['stale dry-run receipt evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired dry-run receipt evidence'] };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      plannedFiles: [{ ...current.plannedFiles[0], safeSummary: 'raw prompt private reasoning should redact' }],
      evidenceRefs: ['raw tool output should redact', 'apply-dry-run-record-231'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['safe to merge and source apply executed by fixture data'],
      authorityClaimsDetected: true
    };
  }

  if (fixture === 'contradictory') {
    return {
      ...current,
      displayState: {
        ...current.displayState,
        sourceApplyApproved: true,
        sourceApplyExecuted: true,
        mutationPerformed: true
      }
    } as unknown as SourceApplyDryRunReceiptEvidence;
  }

  return current;
}
