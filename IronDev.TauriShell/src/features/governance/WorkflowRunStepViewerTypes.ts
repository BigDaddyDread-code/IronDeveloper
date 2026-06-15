import type {
  WorkflowEvidenceReferenceData,
  WorkflowGroundingReferenceData,
  WorkflowRunDetailData,
  WorkflowRunSummaryData,
  WorkflowStepDetailData,
  WorkflowStepSummaryData
} from '../../api/types';

export type WorkflowRunStepViewerLoadStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error';

export interface WorkflowRunStepViewerFilters {
  projectReferenceId: string;
  workflowRunId: string;
  workflowStepId: string;
  correlationId: string;
  workflowStatus: string;
  stepStatus: string;
  workflowKind: string;
  fromUtc: string;
  toUtc: string;
  take: string;
}

export interface WorkflowReferenceView {
  id: string;
  type: string;
  label: string;
  summary: string;
  authority: boolean;
}

export interface WorkflowRunViewerItem {
  workflowRunId: string;
  projectReferenceId: string;
  workflowType: string;
  workflowName: string;
  status: string;
  subjectType: string;
  subjectId: string;
  subjectSummary: string;
  correlationId: string;
  causationId: string;
  stepCount: number;
  evidenceReferenceCount: number;
  groundingReferenceCount: number;
  createdUtc: string;
  hasAuthorityFlag: boolean;
}

export interface WorkflowStepViewerItem {
  workflowRunStepId: string;
  workflowRunId: string;
  projectReferenceId: string;
  stepKey: string;
  stepName: string;
  stepType: string;
  status: string;
  sequenceNumber: number;
  agentRole: string;
  agentId: string;
  subjectType: string;
  subjectId: string;
  safeSummary: string;
  correlationId: string;
  causationId: string;
  evidenceReferenceCount: number;
  groundingReferenceCount: number;
  createdUtc: string;
  hasAuthorityFlag: boolean;
}

export interface WorkflowRunStepViewerDetail {
  run: WorkflowRunViewerItem | null;
  step: WorkflowStepViewerItem | null;
  evidenceReferences: WorkflowReferenceView[];
  groundingReferences: WorkflowReferenceView[];
  warnings: string[];
}

export type SafeText = (value: unknown, fallback?: string) => string;

export function runItemFromSummary(data: WorkflowRunSummaryData, safeText: SafeText): WorkflowRunViewerItem {
  return {
    workflowRunId: safeText(data.workflowRunId, 'workflow-run-id-unavailable'),
    projectReferenceId: safeText(data.projectId, 'project-unavailable'),
    workflowType: safeText(data.workflowType, 'workflow-type-unavailable'),
    workflowName: safeText(data.workflowName, 'workflow name unavailable'),
    status: safeText(data.status, 'status unavailable'),
    subjectType: safeText(data.subjectType, 'subject type unavailable'),
    subjectId: safeText(data.subjectId, 'subject unavailable'),
    subjectSummary: '',
    correlationId: safeText(data.correlationId, 'correlation unavailable'),
    causationId: safeText(data.causationId, 'causation unavailable'),
    stepCount: numberOrZero(data.stepCount),
    evidenceReferenceCount: numberOrZero(data.evidenceReferenceCount),
    groundingReferenceCount: numberOrZero(data.groundingReferenceCount),
    createdUtc: safeText(data.createdUtc, 'created time unavailable'),
    hasAuthorityFlag: hasUnsafeAuthorityFlag(data.authorityFlags)
  };
}

export function runItemFromDetail(data: WorkflowRunDetailData, safeText: SafeText): WorkflowRunViewerItem {
  return {
    ...runItemFromSummary(data, safeText),
    subjectSummary: safeText(data.subjectSummary, 'safe subject summary unavailable'),
    stepCount: Array.isArray(data.steps) ? data.steps.length : numberOrZero(data.stepCount),
    evidenceReferenceCount: Array.isArray(data.evidenceReferences)
      ? data.evidenceReferences.length
      : numberOrZero(data.evidenceReferenceCount),
    groundingReferenceCount: Array.isArray(data.groundingReferences)
      ? data.groundingReferences.length
      : numberOrZero(data.groundingReferenceCount)
  };
}

export function stepItemFromSummary(data: WorkflowStepSummaryData, safeText: SafeText): WorkflowStepViewerItem {
  return {
    workflowRunStepId: safeText(data.workflowRunStepId, 'workflow-step-id-unavailable'),
    workflowRunId: safeText(data.workflowRunId, 'workflow-run-id-unavailable'),
    projectReferenceId: safeText(data.projectId, 'project-unavailable'),
    stepKey: safeText(data.stepKey, 'step-key-unavailable'),
    stepName: safeText(data.stepName, 'step name unavailable'),
    stepType: safeText(data.stepType, 'step type unavailable'),
    status: safeText(data.status, 'status unavailable'),
    sequenceNumber: numberOrZero(data.sequenceNumber),
    agentRole: safeText(data.agentRole, 'agent role unavailable'),
    agentId: safeText(data.agentId, 'agent unavailable'),
    subjectType: safeText(data.subjectType, 'subject type unavailable'),
    subjectId: safeText(data.subjectId, 'subject unavailable'),
    safeSummary: '',
    correlationId: safeText(data.correlationId, 'correlation unavailable'),
    causationId: safeText(data.causationId, 'causation unavailable'),
    evidenceReferenceCount: numberOrZero(data.evidenceReferenceCount),
    groundingReferenceCount: numberOrZero(data.groundingReferenceCount),
    createdUtc: safeText(data.createdUtc, 'created time unavailable'),
    hasAuthorityFlag: hasUnsafeAuthorityFlag(data.authorityFlags)
  };
}

export function stepItemFromDetail(data: WorkflowStepDetailData, safeText: SafeText): WorkflowStepViewerItem {
  return {
    ...stepItemFromSummary(data, safeText),
    safeSummary: safeText(data.safeSummary, 'safe step summary unavailable'),
    evidenceReferenceCount: Array.isArray(data.evidenceReferences)
      ? data.evidenceReferences.length
      : numberOrZero(data.evidenceReferenceCount),
    groundingReferenceCount: Array.isArray(data.groundingReferences)
      ? data.groundingReferences.length
      : numberOrZero(data.groundingReferenceCount)
  };
}

export function referencesFromEvidence(
  evidenceReferences: WorkflowEvidenceReferenceData[] | undefined,
  safeText: SafeText
): WorkflowReferenceView[] {
  return (evidenceReferences ?? []).map((reference, index) => ({
    id: safeText(reference.evidenceReferenceId ?? reference.evidenceId, `evidence-${index + 1}`),
    type: safeText(reference.evidenceType, 'evidence'),
    label: safeText(reference.evidenceLabel, 'workflow evidence'),
    summary: safeText(reference.safeSummary, 'safe evidence summary unavailable'),
    authority:
      reference.isApproval === true ||
      reference.isExecutionPermission === true ||
      reference.isPolicySatisfaction === true ||
      reference.isWorkflowTransition === true ||
      reference.isMemoryPromotion === true ||
      reference.isSourceApply === true
  }));
}

export function referencesFromGrounding(
  groundingReferences: WorkflowGroundingReferenceData[] | undefined,
  safeText: SafeText
): WorkflowReferenceView[] {
  return (groundingReferences ?? []).map((reference, index) => ({
    id: safeText(reference.groundingReferenceId ?? reference.groundingId, `grounding-${index + 1}`),
    type: safeText(reference.groundingType, 'grounding'),
    label: safeText(reference.claim, 'workflow grounding'),
    summary: safeText(reference.safeSummary, 'safe grounding summary unavailable'),
    authority: reference.groundingIsAuthority === true
  }));
}

function numberOrZero(value: number | undefined) {
  return Number.isFinite(value) ? Number(value) : 0;
}

function hasUnsafeAuthorityFlag(flags: object | undefined) {
  return Object.entries((flags ?? {}) as Record<string, unknown>).some(([key, value]) => {
    const normalized = key.toLowerCase();
    return (
      value === true &&
      (normalized.includes('approval') ||
        normalized.includes('authority') ||
        normalized.includes('execution') ||
        normalized.includes('transition') ||
        normalized.includes('mutates') ||
        normalized.includes('applies') ||
        normalized.includes('promotes') ||
        normalized.includes('activates') ||
        normalized.includes('releases') ||
        normalized.includes('private'))
    );
  });
}
