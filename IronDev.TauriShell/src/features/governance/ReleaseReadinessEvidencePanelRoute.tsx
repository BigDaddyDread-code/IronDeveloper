import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { releaseReadinessEvidenceBoundaryRules } from './ReleaseReadinessEvidenceBoundary';
import { ReleaseReadinessEvidencePanel } from './ReleaseReadinessEvidencePanel';
import type { ReleaseReadinessEvidence } from './ReleaseReadinessEvidenceTypes';
import { releaseReadinessEvidenceDefaultDisplayState } from './ReleaseReadinessEvidenceTypes';

interface ReleaseReadinessEvidencePanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function ReleaseReadinessEvidencePanelRoute({ onRouteReady }: ReleaseReadinessEvidencePanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Release readiness evidence', testId: 'release-readiness-evidence.chip.evidence' },
      { label: 'Read-only', testId: 'release-readiness-evidence.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <ReleaseReadinessEvidencePanel isLoading />;
  }

  if (fixture === 'error') {
    return <ReleaseReadinessEvidencePanel errorMessage="Unable to load release readiness evidence." />;
  }

  if (fixture === 'missing') {
    return <ReleaseReadinessEvidencePanel evidence={null} />;
  }

  return <ReleaseReadinessEvidencePanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): ReleaseReadinessEvidence {
  const current: ReleaseReadinessEvidence = {
    releaseReadinessEvidenceId: 'release-readiness-evidence-236',
    releaseReadinessEvidenceHash: 'sha256:release-readiness-evidence-hash-236',
    releaseReadinessReportId: 'release-readiness-report-236',
    releaseReadinessReportHash: 'sha256:release-readiness-report-hash-236',
    releaseReadinessDecisionRecordId: 'release-readiness-decision-record-236',
    releaseReadinessDecisionRecordHash: 'sha256:release-readiness-decision-record-hash-236',
    projectId: 'project-7',
    subjectKind: 'ReleaseReadinessReport',
    subjectId: 'release-readiness-report-236',
    subjectHash: 'sha256:release-readiness-report-hash-236',
    workflowRunId: 'workflow-run-236',
    workflowStepId: 'workflow-step-236',
    acceptedApprovalId: 'accepted-approval-236',
    acceptedApprovalHash: 'sha256:accepted-approval-hash-236',
    policySatisfactionId: 'policy-satisfaction-236',
    policySatisfactionHash: 'sha256:policy-satisfaction-hash-236',
    sourceApplyReviewId: 'source-apply-review-236',
    sourceApplyReviewHash: 'sha256:source-apply-review-hash-236',
    rollbackEvidenceId: 'rollback-evidence-236',
    rollbackEvidenceHash: 'sha256:rollback-evidence-hash-236',
    workflowContinuationEvidenceId: 'workflow-continuation-evidence-236',
    workflowContinuationEvidenceHash: 'sha256:workflow-continuation-evidence-hash-236',
    reviewedBy: 'human-reviewer-236',
    reviewedAtUtc: '2026-06-18T00:00:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    readinessStatus: 'EvidenceReadyForHumanReview',
    approvalEvidencePresent: true,
    policyEvidencePresent: true,
    sourceApplyEvidencePresent: true,
    rollbackEvidencePresent: true,
    workflowContinuationEvidencePresent: true,
    releaseReadinessReportPresent: true,
    releaseReadinessReportSatisfied: true,
    releaseReadinessDecisionPresent: true,
    releaseReadyClaimed: true,
    releaseBlocked: false,
    releaseFailed: false,
    releasePartial: false,
    findings: [
      {
        code: 'ReleaseReadinessEvidencePresent',
        severity: 'Info',
        field: 'releaseReadinessReport',
        safeSummary: 'Supplied evidence says release readiness material exists for human review.'
      },
      {
        code: 'HumanReviewRequired',
        severity: 'Warning',
        field: 'humanReview',
        safeSummary: 'Human review remains required before release approval.'
      }
    ],
    warnings: ['Release readiness display does not approve release.'],
    evidenceRefs: ['release-readiness-report-236', 'accepted-approval-236', 'policy-satisfaction-236', 'workflow-run-236'],
    boundaryMaxims: releaseReadinessEvidenceBoundaryRules,
    displayState: {
      ...releaseReadinessEvidenceDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-evidence-id') {
    return { ...current, releaseReadinessEvidenceId: '', incomplete: true };
  }

  if (fixture === 'missing-evidence-hash') {
    return { ...current, releaseReadinessEvidenceHash: '', incomplete: true };
  }

  if (fixture === 'missing-report') {
    return { ...current, releaseReadinessReportId: '', releaseReadinessReportHash: '', releaseReadinessReportPresent: false, releaseReadinessReportSatisfied: false, incomplete: true };
  }

  if (fixture === 'missing-accepted-approval') {
    return { ...current, acceptedApprovalId: '', acceptedApprovalHash: '', approvalEvidencePresent: false, incomplete: true };
  }

  if (fixture === 'missing-policy') {
    return { ...current, policySatisfactionId: '', policySatisfactionHash: '', policyEvidencePresent: false, incomplete: true };
  }

  if (fixture === 'missing-source-apply-review') {
    return { ...current, sourceApplyReviewId: '', sourceApplyReviewHash: '', sourceApplyEvidencePresent: false, incomplete: true };
  }

  if (fixture === 'missing-workflow-continuation') {
    return { ...current, workflowContinuationEvidenceId: '', workflowContinuationEvidenceHash: '', workflowContinuationEvidencePresent: false, incomplete: true };
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
    return { ...current, stale: true, warnings: ['stale release readiness evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired release readiness evidence'] };
  }

  if (fixture === 'blocked') {
    return { ...current, releaseReadyClaimed: false, releaseBlocked: true, readinessStatus: 'ReleaseBlockedEvidence' };
  }

  if (fixture === 'failed') {
    return { ...current, releaseReadyClaimed: false, releaseFailed: true, readinessStatus: 'ReleaseFailedEvidence' };
  }

  if (fixture === 'partial') {
    return { ...current, releaseReadyClaimed: false, releasePartial: true, readinessStatus: 'PartialReadinessEvidence' };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      findings: [{ ...current.findings[0], safeSummary: 'raw prompt private reasoning should redact' }],
      evidenceRefs: ['raw diff should redact', 'release-readiness-report-236'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['release approved and safe to deploy by fixture data'],
      authorityClaimsDetected: true
    };
  }

  if (fixture === 'contradictory') {
    return {
      ...current,
      displayState: {
        ...current.displayState,
        releaseReadinessDecided: true,
        releaseApproved: true,
        deploymentApproved: true,
        mergeApproved: true,
        releaseExecuted: true,
        releaseDecisionRecordCreated: true,
        mutationPerformed: true
      }
    } as unknown as ReleaseReadinessEvidence;
  }

  return current;
}
