export type RollbackEvidenceFileSummary = {
  path: string;
  action: string;
  safeSummary: string;
  beforeHash?: string;
  afterHash?: string;
};

export type RollbackEvidence = {
  rollbackEvidenceId: string;
  rollbackEvidenceHash: string;
  projectId: string;
  subjectKind: string;
  subjectId: string;
  subjectHash: string;
  workflowRunId: string;
  workflowStepId: string;
  sourceApplyReceiptId: string;
  sourceApplyReceiptHash: string;
  rollbackPlanId: string;
  rollbackPlanHash: string;
  rollbackSupportReceiptId: string;
  rollbackSupportReceiptHash: string;
  rollbackExecutionReceiptId?: string;
  rollbackExecutionReceiptHash?: string;
  rollbackAuditReportId?: string;
  rollbackAuditReportHash?: string;
  reviewedBy: string;
  reviewedAtUtc: string;
  expiresAtUtc?: string;
  rollbackStatus: string;
  rollbackPlanPresent: boolean;
  rollbackSupportReceiptPresent: boolean;
  rollbackExecutionReceiptPresent: boolean;
  rollbackAuditReportPresent: boolean;
  rollbackSucceeded: boolean;
  rollbackPartial: boolean;
  rollbackFailed: boolean;
  rollbackAuditConsistent: boolean;
  affectedFileCount: number;
  affectedFiles: RollbackEvidenceFileSummary[];
  warnings: string[];
  evidenceRefs: string[];
  boundaryMaxims: string[];
  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  displayState: RollbackEvidenceDisplayState;
};

export type RollbackEvidenceDisplayState = {
  evidencePresent: boolean;
  evidenceSatisfied: boolean;
  recordStored: boolean;
  humanReviewRequired: boolean;

  approvalCreated: false;
  rollbackApproved: false;
  releaseApproved: false;
  deploymentApproved: false;
  mergeApproved: false;

  dryRunExecuted: false;
  sourceApplyExecuted: false;
  rollbackExecuted: false;
  rollbackRetried: false;
  rollbackRecoveryStarted: false;
  workflowContinued: false;
  workflowMutated: false;
  gitOperationExecuted: false;

  authorityRefreshed: false;
  evidenceReissued: false;
  mutationPerformed: false;
};

export type RollbackEvidencePanelProps = {
  evidence?: RollbackEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const rollbackEvidenceRequiredFields: Array<keyof RollbackEvidence> = [
  'rollbackEvidenceId',
  'rollbackEvidenceHash',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'sourceApplyReceiptId',
  'sourceApplyReceiptHash',
  'rollbackPlanId',
  'rollbackPlanHash',
  'rollbackSupportReceiptId',
  'rollbackSupportReceiptHash',
  'reviewedBy',
  'reviewedAtUtc',
  'rollbackStatus'
];

export const rollbackEvidenceDefaultDisplayState: RollbackEvidenceDisplayState = {
  evidencePresent: false,
  evidenceSatisfied: false,
  recordStored: false,
  humanReviewRequired: true,
  approvalCreated: false,
  rollbackApproved: false,
  releaseApproved: false,
  deploymentApproved: false,
  mergeApproved: false,
  dryRunExecuted: false,
  sourceApplyExecuted: false,
  rollbackExecuted: false,
  rollbackRetried: false,
  rollbackRecoveryStarted: false,
  workflowContinued: false,
  workflowMutated: false,
  gitOperationExecuted: false,
  authorityRefreshed: false,
  evidenceReissued: false,
  mutationPerformed: false
};

export const rollbackEvidenceAuthorityFlags: Array<keyof Pick<
  RollbackEvidenceDisplayState,
  | 'approvalCreated'
  | 'rollbackApproved'
  | 'releaseApproved'
  | 'deploymentApproved'
  | 'mergeApproved'
  | 'dryRunExecuted'
  | 'sourceApplyExecuted'
  | 'rollbackExecuted'
  | 'rollbackRetried'
  | 'rollbackRecoveryStarted'
  | 'workflowContinued'
  | 'workflowMutated'
  | 'gitOperationExecuted'
  | 'authorityRefreshed'
  | 'evidenceReissued'
  | 'mutationPerformed'
>> = [
  'approvalCreated',
  'rollbackApproved',
  'releaseApproved',
  'deploymentApproved',
  'mergeApproved',
  'dryRunExecuted',
  'sourceApplyExecuted',
  'rollbackExecuted',
  'rollbackRetried',
  'rollbackRecoveryStarted',
  'workflowContinued',
  'workflowMutated',
  'gitOperationExecuted',
  'authorityRefreshed',
  'evidenceReissued',
  'mutationPerformed'
];

export function missingRollbackEvidenceFields(evidence: RollbackEvidence | null | undefined) {
  if (!evidence) {
    return rollbackEvidenceRequiredFields;
  }

  return rollbackEvidenceRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasRollbackEvidenceAuthorityFlags(evidence: RollbackEvidence | null | undefined) {
  return Boolean(evidence && rollbackEvidenceAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidRollbackEvidenceTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
