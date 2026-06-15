import type {
  GovernanceTraceDetail,
  GovernanceTraceRelatedReference,
  GovernanceTraceSummary,
  GovernanceTraceTimelineItem
} from '../../api/types';

export type MemoryProposalReviewLoadStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error';

export interface MemoryProposalReviewFilters {
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  memoryProposalId: string;
  correlationId: string;
  proposalStatus: string;
  memoryKind: string;
  scope: string;
  sourceComponent: string;
  fromUtc: string;
  toUtc: string;
  take: string;
}

export interface MemoryProposalListItem {
  memoryProposalId: string;
  traceId: string;
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  memoryKind: string;
  proposalStatus: string;
  proposedScope: string;
  sourceComponent: string;
  createdUtc: string;
  safeSummary: string;
}

export interface MemoryProposalEvidenceReference {
  referenceKind: string;
  referenceId: string;
  safeSummary: string;
}

export interface MemoryProposalDetail {
  memoryProposalId: string;
  memoryKind: string;
  proposalStatus: string;
  proposedScope: string;
  confidence: string;
  safeSummary: string;
  safeRationale: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  sourceComponent: string;
  evidenceReferences: MemoryProposalEvidenceReference[];
  relatedTraces: MemoryProposalEvidenceReference[];
  confidentialityWarnings: string[];
  portabilityWarnings: string[];
  missingEvidenceWarnings: string[];
  boundaryWarnings: string[];
}

export interface MemoryProposalReviewViewModel {
  isReadOnly: true;
  mutationOccurred: false;
  canAcceptMemory: false;
  canPromoteMemory: false;
  canWriteMemory: false;
  canActivateRetrieval: false;
  canApproveCrossProjectLearning: false;
  canTransitionWorkflow: false;
  canInvokeTool: false;
  canDispatchAgent: false;
  canCallModel: false;
  canApplySource: false;
  canApproveRelease: false;
  proposals: MemoryProposalListItem[];
  selectedProposal?: MemoryProposalDetail;
  warnings: string[];
  errors: string[];
}

export type SafeText = (value: unknown, fallback?: string) => string;

export function proposalItemFromTrace(trace: GovernanceTraceSummary, safeText: SafeText): MemoryProposalListItem {
  const eventKind = safeText(trace.eventKind, 'memory.proposal.review');
  const summary = safeText(trace.safeSummary, 'Safe memory proposal summary unavailable.');

  return {
    memoryProposalId: safeText(trace.subjectReferenceId ?? trace.traceId, 'memory-proposal-unavailable'),
    traceId: safeText(trace.traceId, ''),
    projectReferenceId: safeText(trace.projectReferenceId, ''),
    workflowRunId: safeText(trace.workflowRunId, ''),
    workflowStepId: safeText(trace.workflowStepId, ''),
    correlationId: safeText(trace.correlationId, ''),
    memoryKind: deriveMemoryKind(eventKind, summary),
    proposalStatus: deriveProposalStatus(eventKind, summary),
    proposedScope: deriveProposalScope(summary),
    sourceComponent: safeText(trace.sourceComponent, 'source component unavailable'),
    createdUtc: safeText(trace.recordedUtc, ''),
    safeSummary: summary
  };
}

export function proposalDetailFromTrace(
  item: MemoryProposalListItem,
  trace: GovernanceTraceDetail | null | undefined,
  safeText: SafeText
): MemoryProposalDetail {
  const timeline = trace?.timeline ?? [];
  const related = trace?.relatedReferences ?? [];
  const boundaryWarnings = (trace?.boundaryWarnings ?? []).map((warning) => safeText(warning)).filter(Boolean);

  return {
    memoryProposalId: item.memoryProposalId,
    memoryKind: item.memoryKind,
    proposalStatus: item.proposalStatus,
    proposedScope: item.proposedScope,
    confidence: deriveConfidence(timeline),
    safeSummary: item.safeSummary,
    safeRationale: deriveRationale(timeline, item.safeSummary, safeText),
    workflowRunId: item.workflowRunId,
    workflowStepId: item.workflowStepId,
    correlationId: item.correlationId,
    sourceComponent: item.sourceComponent,
    evidenceReferences: evidenceReferencesFromTrace(timeline, related, safeText),
    relatedTraces: relatedTraceReferences(related, safeText),
    confidentialityWarnings: deriveConfidentialityWarnings(timeline, item.safeSummary, safeText),
    portabilityWarnings: derivePortabilityWarnings(timeline, item.safeSummary, safeText),
    missingEvidenceWarnings: deriveMissingEvidenceWarnings(timeline, related, safeText),
    boundaryWarnings: [
      'Memory proposal is not accepted memory',
      'Memory review is not memory promotion',
      'Retrieval candidate is not retrieval activation',
      ...boundaryWarnings
    ]
  };
}

export function looksLikeMemoryProposal(item: MemoryProposalListItem) {
  const haystack = [
    item.memoryProposalId,
    item.memoryKind,
    item.proposalStatus,
    item.proposedScope,
    item.sourceComponent,
    item.safeSummary
  ]
    .join(' ')
    .toLowerCase();

  return haystack.includes('memory') || haystack.includes('proposal') || haystack.includes('learning');
}

function evidenceReferencesFromTrace(
  timeline: GovernanceTraceTimelineItem[],
  related: GovernanceTraceRelatedReference[],
  safeText: SafeText
) {
  const timelineRefs = timeline.map((entry, index) => ({
    referenceKind: safeText(entry.eventKind, 'timeline_event'),
    referenceId: safeText(entry.eventId ?? entry.subjectReferenceId, `timeline-${index + 1}`),
    safeSummary: safeText(entry.safeSummary, 'Safe timeline summary unavailable.')
  }));

  const relatedRefs = related.map((entry, index) => ({
    referenceKind: safeText(entry.referenceKind, 'related_reference'),
    referenceId: safeText(entry.referenceId, `related-${index + 1}`),
    safeSummary: safeText(entry.safeSummary, 'Safe related reference summary unavailable.')
  }));

  return [...timelineRefs, ...relatedRefs];
}

function relatedTraceReferences(related: GovernanceTraceRelatedReference[], safeText: SafeText) {
  return related
    .filter((entry) => safeText(entry.referenceKind).toLowerCase().includes('trace'))
    .map((entry, index) => ({
      referenceKind: safeText(entry.referenceKind, 'trace'),
      referenceId: safeText(entry.referenceId, `trace-${index + 1}`),
      safeSummary: safeText(entry.safeSummary, 'Safe trace summary unavailable.')
    }));
}

function deriveMemoryKind(eventKind: string, summary: string) {
  const text = `${eventKind} ${summary}`.toLowerCase();
  if (text.includes('duplicate')) {
    return 'duplicate-memory-candidate';
  }

  if (text.includes('stale')) {
    return 'stale-memory-candidate';
  }

  if (text.includes('conflict')) {
    return 'conflicting-memory-candidate';
  }

  if (text.includes('cross') || text.includes('portable')) {
    return 'cross-run-learning-candidate';
  }

  return 'memory-proposal-candidate';
}

function deriveProposalStatus(eventKind: string, summary: string) {
  const text = `${eventKind} ${summary}`.toLowerCase();
  if (text.includes('missing') || text.includes('incomplete')) {
    return 'missing-evidence';
  }

  if (text.includes('ready')) {
    return 'ready-for-review';
  }

  return 'review-material';
}

function deriveProposalScope(summary: string) {
  const text = summary.toLowerCase();
  if (text.includes('cross-project') || text.includes('portable')) {
    return 'cross-project-candidate';
  }

  if (text.includes('agent')) {
    return 'agent-local-candidate';
  }

  return 'project-local-candidate';
}

function deriveConfidence(timeline: GovernanceTraceTimelineItem[]) {
  const text = timeline.map((entry) => entry.safeSummary ?? '').join(' ').toLowerCase();
  if (text.includes('high confidence')) {
    return 'high-review-confidence';
  }

  if (text.includes('low confidence')) {
    return 'low-review-confidence';
  }

  return 'review-required';
}

function deriveRationale(timeline: GovernanceTraceTimelineItem[], fallback: string, safeText: SafeText) {
  const rationale = timeline.find((entry) => safeText(entry.safeSummary).toLowerCase().includes('rationale'));
  return safeText(rationale?.safeSummary, fallback);
}

function deriveConfidentialityWarnings(timeline: GovernanceTraceTimelineItem[], summary: string, safeText: SafeText) {
  const text = [summary, ...timeline.map((entry) => safeText(entry.safeSummary))].join(' ').toLowerCase();
  return text.includes('confidential') || text.includes('client') || text.includes('employer')
    ? ['Confidentiality review remains required before any future memory acceptance.']
    : ['No confidentiality warning supplied by the read model. Human review remains required.'];
}

function derivePortabilityWarnings(timeline: GovernanceTraceTimelineItem[], summary: string, safeText: SafeText) {
  const text = [summary, ...timeline.map((entry) => safeText(entry.safeSummary))].join(' ').toLowerCase();
  return text.includes('portable') || text.includes('cross-project')
    ? ['Portable or cross-project learning is candidate-only and grants no cross-project authority.']
    : ['No portability approval exists in this view.'];
}

function deriveMissingEvidenceWarnings(
  timeline: GovernanceTraceTimelineItem[],
  related: GovernanceTraceRelatedReference[],
  safeText: SafeText
) {
  if (timeline.length > 0 || related.length > 0) {
    return [];
  }

  return ['No evidence references were returned for this proposal evidence view.'];
}
