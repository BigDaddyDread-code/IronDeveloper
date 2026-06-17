import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { sourceApplyReviewBoundaryRules } from './SourceApplyReviewBoundary';
import { SourceApplyReviewPanel } from './SourceApplyReviewPanel';
import type { SourceApplyReviewEvidence } from './SourceApplyReviewTypes';
import { sourceApplyReviewDefaultDisplayState } from './SourceApplyReviewTypes';

interface SourceApplyReviewPanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function SourceApplyReviewPanelRoute({ onRouteReady }: SourceApplyReviewPanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Source apply review evidence', testId: 'source-apply-review.chip.evidence' },
      { label: 'Read-only', testId: 'source-apply-review.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <SourceApplyReviewPanel isLoading />;
  }

  if (fixture === 'error') {
    return <SourceApplyReviewPanel errorMessage="Unable to load source-apply review evidence." />;
  }

  if (fixture === 'missing') {
    return <SourceApplyReviewPanel evidence={null} />;
  }

  return <SourceApplyReviewPanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): SourceApplyReviewEvidence {
  const current: SourceApplyReviewEvidence = {
    reviewId: 'source-apply-review-233',
    reviewHash: 'sha256:source-apply-review-hash-233',
    projectId: 'project-7',
    subjectKind: 'SourceApplyRequest',
    subjectId: 'source-apply-request-233',
    subjectHash: 'sha256:source-apply-request-hash-233',
    workflowRunId: 'workflow-run-233',
    workflowStepId: 'workflow-step-233',
    sourceApplyRequestId: 'source-apply-request-233',
    sourceApplyRequestHash: 'sha256:source-apply-request-hash-233',
    patchArtifactId: 'patch-artifact-233',
    patchArtifactHash: 'sha256:patch-artifact-hash-233',
    dryRunReceiptId: 'dry-run-receipt-233',
    dryRunReceiptHash: 'sha256:dry-run-receipt-hash-233',
    reviewedBy: 'human-reviewer-233',
    reviewedAtUtc: '2026-06-18T00:00:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    patchArtifactPresent: true,
    dryRunReceiptPresent: true,
    requestBindingPresent: true,
    patchArtifactSatisfied: true,
    dryRunReceiptSatisfied: true,
    sourceApplyReviewStatus: 'ReadyForHumanReview',
    plannedChangeCount: 2,
    plannedFileSummaries: [
      {
        path: 'src/apply/Widget.cs',
        action: 'Modify',
        safeSummary: 'Source-apply review evidence says Widget.cs has patch and dry-run evidence.'
      },
      {
        path: 'tests/apply/WidgetTests.cs',
        action: 'Create',
        safeSummary: 'Source-apply review evidence says a test file has patch and dry-run evidence.'
      }
    ],
    warnings: ['Source-apply review display does not apply source.'],
    evidenceRefs: ['source-apply-request-233', 'patch-artifact-233', 'dry-run-receipt-233'],
    boundaryMaxims: sourceApplyReviewBoundaryRules,
    displayState: {
      ...sourceApplyReviewDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-review-id') {
    return { ...current, reviewId: '', incomplete: true };
  }

  if (fixture === 'missing-review-hash') {
    return { ...current, reviewHash: '', incomplete: true };
  }

  if (fixture === 'missing-request') {
    return { ...current, sourceApplyRequestId: '', sourceApplyRequestHash: '', requestBindingPresent: false, incomplete: true };
  }

  if (fixture === 'missing-patch-artifact') {
    return { ...current, patchArtifactId: '', patchArtifactHash: '', patchArtifactPresent: false, patchArtifactSatisfied: false, incomplete: true };
  }

  if (fixture === 'missing-dry-run-receipt') {
    return { ...current, dryRunReceiptId: '', dryRunReceiptHash: '', dryRunReceiptPresent: false, dryRunReceiptSatisfied: false, incomplete: true };
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
    return { ...current, stale: true, warnings: ['stale source-apply review evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired source-apply review evidence'] };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      plannedFileSummaries: [{ ...current.plannedFileSummaries[0], safeSummary: 'raw patch private reasoning should redact' }],
      evidenceRefs: ['raw diff should redact', 'source-apply-request-233'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['source apply approved and safe to merge by fixture data'],
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
        workflowContinued: true,
        mutationPerformed: true
      }
    } as unknown as SourceApplyReviewEvidence;
  }

  return current;
}
