import type { DogfoodReceiptDetailData, DogfoodLoopReferenceData, GovernanceTraceDetail, GovernanceTraceSummary } from '../../api/types';

export type DogfoodReceiptLoadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'validation' | 'error';

export interface DogfoodReceiptViewerFilters {
  projectReferenceId: string;
  dogfoodLoopId: string;
  dogfoodReceiptId: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  sourceComponent: string;
  fromUtc: string;
  toUtc: string;
  take: string;
}

export interface DogfoodReceiptViewerItem {
  dogfoodLoopId: string;
  dogfoodReceiptId: string;
  evidenceId: string;
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  traceId: string;
  sourceComponent: string;
  createdUtc: string;
  durable: boolean;
  containsNonDurableReferences: boolean;
  safeSummary: string;
}

export interface DogfoodReceiptReferenceView {
  referenceKind: string;
  referenceId: string;
  safeSummary: string;
  durable: boolean;
  backendRecorded: boolean;
  source: string;
}

export interface DogfoodReceiptViewerDetail {
  item: DogfoodReceiptViewerItem;
  goal: string;
  observations: string[];
  blockedReasons: string[];
  referencedAgentRuns: DogfoodReceiptReferenceView[];
  referencedCriticReviews: DogfoodReceiptReferenceView[];
  referencedMemoryImprovements: DogfoodReceiptReferenceView[];
  referencedToolRequests: DogfoodReceiptReferenceView[];
  referencedGateDecisions: DogfoodReceiptReferenceView[];
  evidenceReferences: DogfoodReceiptReferenceView[];
  durabilityWarnings: string[];
  knownLimitations: string[];
  boundaryWarnings: string[];
  safeTimelineSummaries: string[];
  trace: GovernanceTraceDetail | null;
}

export function itemFromTraceSummary(
  trace: GovernanceTraceSummary,
  filters: DogfoodReceiptViewerFilters,
  safeText: (value: unknown, fallback?: string) => string
): DogfoodReceiptViewerItem {
  const subject = safeText(trace.subjectReferenceId || filters.dogfoodReceiptId || filters.dogfoodLoopId, 'dogfood receipt unavailable');

  return {
    dogfoodLoopId: safeText(filters.dogfoodLoopId, ''),
    dogfoodReceiptId: subject,
    evidenceId: safeText(trace.traceId, 'evidence unavailable'),
    projectReferenceId: safeText(trace.projectReferenceId || filters.projectReferenceId),
    workflowRunId: safeText(trace.workflowRunId || filters.workflowRunId),
    workflowStepId: safeText(trace.workflowStepId || filters.workflowStepId),
    correlationId: safeText(trace.correlationId || filters.correlationId),
    traceId: safeText(trace.traceId),
    sourceComponent: safeText(trace.sourceComponent || filters.sourceComponent, 'Governance evidence'),
    createdUtc: safeText(trace.recordedUtc),
    durable: true,
    containsNonDurableReferences: false,
    safeSummary: safeText(trace.safeSummary, 'No safe dogfood receipt summary returned.')
  };
}

export function itemFromReceiptData(
  receipt: DogfoodReceiptDetailData,
  filters: DogfoodReceiptViewerFilters,
  safeText: (value: unknown, fallback?: string) => string
): DogfoodReceiptViewerItem {
  return {
    dogfoodLoopId: safeText(receipt.dogfoodLoopId || filters.dogfoodLoopId || filters.dogfoodReceiptId),
    dogfoodReceiptId: safeText(receipt.receiptId || filters.dogfoodReceiptId || receipt.dogfoodLoopId),
    evidenceId: safeText(receipt.evidenceId),
    projectReferenceId: safeText(receipt.projectId || filters.projectReferenceId),
    workflowRunId: safeText(receipt.runId || filters.workflowRunId),
    workflowStepId: safeText(filters.workflowStepId),
    correlationId: safeText(filters.correlationId),
    traceId: '',
    sourceComponent: 'Dogfood Loop API v1',
    createdUtc: safeText(receipt.createdAtUtc),
    durable: receipt.durable === true,
    containsNonDurableReferences: receipt.containsNonDurableReferences === true,
    safeSummary: safeText(receipt.summary, 'No safe dogfood receipt summary returned.')
  };
}

export function referencesFromReceipt(
  references: DogfoodLoopReferenceData[] | null | undefined,
  safeText: (value: unknown, fallback?: string) => string
): DogfoodReceiptReferenceView[] {
  return (references ?? []).map((reference) => ({
    referenceKind: safeText(reference.refType, 'reference'),
    referenceId: safeText(reference.refId),
    safeSummary: safeText(reference.summary, 'No safe reference summary returned.'),
    durable: reference.durable === true,
    backendRecorded: reference.backendRecorded === true,
    source: safeText(reference.source, 'unknown')
  }));
}
