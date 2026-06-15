import type { GovernanceTraceDetail, GovernanceTraceSummary } from '../../api/types';

export type ApprovalPackageLoadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'validation' | 'error';

export interface ApprovalPackageReviewFilters {
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  approvalPackageId: string;
  correlationId: string;
  approvalScope: string;
  packageStatus: string;
  sourceComponent: string;
  fromUtc: string;
  toUtc: string;
  take: string;
}

export interface ApprovalPackageReviewItem {
  approvalPackageId: string;
  traceId: string;
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  approvalScope: string;
  requestedDecision: string;
  packageStatus: string;
  sourceComponent: string;
  createdUtc: string;
  safeSummary: string;
}

export interface ApprovalPackageEvidenceReferenceView {
  referenceKind: string;
  referenceId: string;
  safeSummary: string;
}

export interface ApprovalPackageReviewDetail {
  item: ApprovalPackageReviewItem;
  evidenceReferences: ApprovalPackageEvidenceReferenceView[];
  boundaryWarnings: string[];
  safeTimelineSummaries: string[];
  trace: GovernanceTraceDetail | null;
}

export function fromTraceSummary(
  trace: GovernanceTraceSummary,
  filters: ApprovalPackageReviewFilters,
  safeText: (value: string | number | null | undefined, fallback?: string) => string
): ApprovalPackageReviewItem {
  return {
    approvalPackageId: safeText(trace.subjectReferenceId || trace.traceId, 'approval package unavailable'),
    traceId: safeText(trace.traceId),
    projectReferenceId: safeText(trace.projectReferenceId || filters.projectReferenceId),
    workflowRunId: safeText(trace.workflowRunId || filters.workflowRunId),
    workflowStepId: safeText(trace.workflowStepId || filters.workflowStepId),
    correlationId: safeText(trace.correlationId || filters.correlationId),
    approvalScope: safeText(filters.approvalScope, 'Human review'),
    requestedDecision: safeText(trace.eventKind, 'Review requested'),
    packageStatus: safeText(filters.packageStatus, 'Ready for review'),
    sourceComponent: safeText(trace.sourceComponent || filters.sourceComponent, 'Governance evidence'),
    createdUtc: safeText(trace.recordedUtc),
    safeSummary: safeText(trace.safeSummary, 'No safe summary returned.')
  };
}
