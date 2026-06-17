export type SourceApplyDryRunPlannedFile = {
  path: string;
  previousPath?: string;
  action: string;
  fileHashBefore?: string;
  fileHashAfter?: string;
  safeSummary: string;
};

export type SourceApplyDryRunReceiptEvidence = {
  dryRunReceiptId: string;
  dryRunReceiptHash: string;
  sourceApplyRequestId: string;
  sourceApplyRequestHash: string;
  projectId: string;
  subjectKind: string;
  subjectId: string;
  subjectHash: string;
  workflowRunId: string;
  workflowStepId: string;
  requestedBy: string;
  dryRunStartedAtUtc: string;
  dryRunCompletedAtUtc: string;
  receiptStoredAtUtc: string;
  expiresAtUtc?: string;
  dryRunStatus: string;
  validationPassed: boolean;
  plannedChangeCount: number;
  plannedFiles: SourceApplyDryRunPlannedFile[];
  warnings: string[];
  evidenceRefs: string[];
  boundaryMaxims: string[];
  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  displayState: SourceApplyDryRunReceiptDisplayState;
};

export type SourceApplyDryRunReceiptDisplayState = {
  evidencePresent: boolean;
  evidenceSatisfied: boolean;
  recordStored: boolean;
  humanReviewRequired: boolean;

  approvalCreated: false;
  sourceApplyApproved: false;
  releaseApproved: false;
  deploymentApproved: false;
  mergeApproved: false;

  dryRunExecuted: false;
  sourceApplyExecuted: false;
  rollbackExecuted: false;
  workflowContinued: false;
  workflowMutated: false;
  gitOperationExecuted: false;

  authorityRefreshed: false;
  evidenceReissued: false;
  mutationPerformed: false;
};

export type SourceApplyDryRunReceiptPanelProps = {
  evidence?: SourceApplyDryRunReceiptEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const sourceApplyDryRunReceiptRequiredFields: Array<keyof SourceApplyDryRunReceiptEvidence> = [
  'dryRunReceiptId',
  'dryRunReceiptHash',
  'sourceApplyRequestId',
  'sourceApplyRequestHash',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'requestedBy',
  'dryRunStartedAtUtc',
  'dryRunCompletedAtUtc',
  'receiptStoredAtUtc',
  'dryRunStatus'
];

export const sourceApplyDryRunReceiptDefaultDisplayState: SourceApplyDryRunReceiptDisplayState = {
  evidencePresent: false,
  evidenceSatisfied: false,
  recordStored: false,
  humanReviewRequired: true,
  approvalCreated: false,
  sourceApplyApproved: false,
  releaseApproved: false,
  deploymentApproved: false,
  mergeApproved: false,
  dryRunExecuted: false,
  sourceApplyExecuted: false,
  rollbackExecuted: false,
  workflowContinued: false,
  workflowMutated: false,
  gitOperationExecuted: false,
  authorityRefreshed: false,
  evidenceReissued: false,
  mutationPerformed: false
};

export const sourceApplyDryRunReceiptAuthorityFlags: Array<keyof Pick<
  SourceApplyDryRunReceiptDisplayState,
  | 'approvalCreated'
  | 'sourceApplyApproved'
  | 'releaseApproved'
  | 'deploymentApproved'
  | 'mergeApproved'
  | 'dryRunExecuted'
  | 'sourceApplyExecuted'
  | 'rollbackExecuted'
  | 'workflowContinued'
  | 'workflowMutated'
  | 'gitOperationExecuted'
  | 'authorityRefreshed'
  | 'evidenceReissued'
  | 'mutationPerformed'
>> = [
  'approvalCreated',
  'sourceApplyApproved',
  'releaseApproved',
  'deploymentApproved',
  'mergeApproved',
  'dryRunExecuted',
  'sourceApplyExecuted',
  'rollbackExecuted',
  'workflowContinued',
  'workflowMutated',
  'gitOperationExecuted',
  'authorityRefreshed',
  'evidenceReissued',
  'mutationPerformed'
];

export function missingSourceApplyDryRunReceiptFields(evidence: SourceApplyDryRunReceiptEvidence | null | undefined) {
  if (!evidence) {
    return sourceApplyDryRunReceiptRequiredFields;
  }

  return sourceApplyDryRunReceiptRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasSourceApplyDryRunReceiptAuthorityFlags(evidence: SourceApplyDryRunReceiptEvidence | null | undefined) {
  return Boolean(evidence && sourceApplyDryRunReceiptAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidSourceApplyDryRunReceiptTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
