export type WorkflowContinuationEvidenceStepSummary = {
  stepId: string;
  stepName: string;
  status: string;
  safeSummary: string;
};

export type WorkflowContinuationEvidence = {
  workflowContinuationEvidenceId: string;
  workflowContinuationEvidenceHash: string;
  projectId: string;
  subjectKind: string;
  subjectId: string;
  subjectHash: string;
  workflowRunId: string;
  workflowStepId: string;
  continuationGateEvaluationId: string;
  continuationGateEvaluationHash: string;
  workflowTransitionRecordId?: string;
  workflowTransitionRecordHash?: string;
  sourceApplyReceiptId?: string;
  sourceApplyReceiptHash?: string;
  rollbackExecutionReceiptId?: string;
  rollbackExecutionReceiptHash?: string;
  reviewedBy: string;
  reviewedAtUtc: string;
  expiresAtUtc?: string;
  continuationStatus: string;
  continuationGatePresent: boolean;
  continuationGateSatisfied: boolean;
  transitionRecordPresent: boolean;
  transitionRecordValid: boolean;
  workflowContinuedElsewhere: boolean;
  workflowContinuationFailed: boolean;
  workflowContinuationPartial: boolean;
  workflowMutationDetected: boolean;
  stepSummaries: WorkflowContinuationEvidenceStepSummary[];
  warnings: string[];
  evidenceRefs: string[];
  boundaryMaxims: string[];
  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  displayState: WorkflowContinuationEvidenceDisplayState;
};

export type WorkflowContinuationEvidenceDisplayState = {
  evidencePresent: boolean;
  evidenceSatisfied: boolean;
  recordStored: boolean;
  humanReviewRequired: boolean;

  approvalCreated: false;
  workflowContinuationApproved: false;
  releaseApproved: false;
  deploymentApproved: false;
  mergeApproved: false;

  dryRunExecuted: false;
  sourceApplyExecuted: false;
  rollbackExecuted: false;
  workflowContinued: false;
  workflowMutated: false;
  workflowTransitionRecordCreated: false;
  gitOperationExecuted: false;

  authorityRefreshed: false;
  evidenceReissued: false;
  mutationPerformed: false;
};

export type WorkflowContinuationEvidencePanelProps = {
  evidence?: WorkflowContinuationEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const workflowContinuationEvidenceRequiredFields: Array<keyof WorkflowContinuationEvidence> = [
  'workflowContinuationEvidenceId',
  'workflowContinuationEvidenceHash',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'continuationGateEvaluationId',
  'continuationGateEvaluationHash',
  'reviewedBy',
  'reviewedAtUtc',
  'continuationStatus'
];

export const workflowContinuationEvidenceDefaultDisplayState: WorkflowContinuationEvidenceDisplayState = {
  evidencePresent: false,
  evidenceSatisfied: false,
  recordStored: false,
  humanReviewRequired: true,
  approvalCreated: false,
  workflowContinuationApproved: false,
  releaseApproved: false,
  deploymentApproved: false,
  mergeApproved: false,
  dryRunExecuted: false,
  sourceApplyExecuted: false,
  rollbackExecuted: false,
  workflowContinued: false,
  workflowMutated: false,
  workflowTransitionRecordCreated: false,
  gitOperationExecuted: false,
  authorityRefreshed: false,
  evidenceReissued: false,
  mutationPerformed: false
};

export const workflowContinuationEvidenceAuthorityFlags: Array<keyof Pick<
  WorkflowContinuationEvidenceDisplayState,
  | 'approvalCreated'
  | 'workflowContinuationApproved'
  | 'releaseApproved'
  | 'deploymentApproved'
  | 'mergeApproved'
  | 'dryRunExecuted'
  | 'sourceApplyExecuted'
  | 'rollbackExecuted'
  | 'workflowContinued'
  | 'workflowMutated'
  | 'workflowTransitionRecordCreated'
  | 'gitOperationExecuted'
  | 'authorityRefreshed'
  | 'evidenceReissued'
  | 'mutationPerformed'
>> = [
  'approvalCreated',
  'workflowContinuationApproved',
  'releaseApproved',
  'deploymentApproved',
  'mergeApproved',
  'dryRunExecuted',
  'sourceApplyExecuted',
  'rollbackExecuted',
  'workflowContinued',
  'workflowMutated',
  'workflowTransitionRecordCreated',
  'gitOperationExecuted',
  'authorityRefreshed',
  'evidenceReissued',
  'mutationPerformed'
];

export function missingWorkflowContinuationEvidenceFields(evidence: WorkflowContinuationEvidence | null | undefined) {
  if (!evidence) {
    return workflowContinuationEvidenceRequiredFields;
  }

  return workflowContinuationEvidenceRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasWorkflowContinuationEvidenceAuthorityFlags(evidence: WorkflowContinuationEvidence | null | undefined) {
  return Boolean(evidence && workflowContinuationEvidenceAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidWorkflowContinuationEvidenceTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
