export type SourceApplyReviewFileSummary = {
  path: string;
  previousPath?: string;
  action: string;
  safeSummary: string;
};

export type SourceApplyReviewEvidence = {
  reviewId: string;
  reviewHash: string;
  projectId: string;
  subjectKind: string;
  subjectId: string;
  subjectHash: string;
  workflowRunId: string;
  workflowStepId: string;
  sourceApplyRequestId: string;
  sourceApplyRequestHash: string;
  patchArtifactId: string;
  patchArtifactHash: string;
  dryRunReceiptId: string;
  dryRunReceiptHash: string;
  reviewedBy: string;
  reviewedAtUtc: string;
  expiresAtUtc?: string;
  patchArtifactPresent: boolean;
  dryRunReceiptPresent: boolean;
  requestBindingPresent: boolean;
  patchArtifactSatisfied: boolean;
  dryRunReceiptSatisfied: boolean;
  sourceApplyReviewStatus: string;
  plannedChangeCount: number;
  plannedFileSummaries: SourceApplyReviewFileSummary[];
  warnings: string[];
  evidenceRefs: string[];
  boundaryMaxims: string[];
  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  displayState: SourceApplyReviewDisplayState;
};

export type SourceApplyReviewDisplayState = {
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

export type SourceApplyReviewPanelProps = {
  evidence?: SourceApplyReviewEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const sourceApplyReviewRequiredFields: Array<keyof SourceApplyReviewEvidence> = [
  'reviewId',
  'reviewHash',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'sourceApplyRequestId',
  'sourceApplyRequestHash',
  'patchArtifactId',
  'patchArtifactHash',
  'dryRunReceiptId',
  'dryRunReceiptHash',
  'reviewedBy',
  'reviewedAtUtc',
  'sourceApplyReviewStatus'
];

export const sourceApplyReviewDefaultDisplayState: SourceApplyReviewDisplayState = {
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

export const sourceApplyReviewAuthorityFlags: Array<keyof Pick<
  SourceApplyReviewDisplayState,
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

export function missingSourceApplyReviewFields(evidence: SourceApplyReviewEvidence | null | undefined) {
  if (!evidence) {
    return sourceApplyReviewRequiredFields;
  }

  return sourceApplyReviewRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasSourceApplyReviewAuthorityFlags(evidence: SourceApplyReviewEvidence | null | undefined) {
  return Boolean(evidence && sourceApplyReviewAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidSourceApplyReviewTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
