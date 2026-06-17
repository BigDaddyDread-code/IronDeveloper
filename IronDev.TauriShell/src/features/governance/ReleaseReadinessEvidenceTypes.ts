export type ReleaseReadinessEvidenceFinding = {
  code: string;
  severity: string;
  field: string;
  safeSummary: string;
};

export type ReleaseReadinessEvidence = {
  releaseReadinessEvidenceId: string;
  releaseReadinessEvidenceHash: string;
  releaseReadinessReportId: string;
  releaseReadinessReportHash: string;
  releaseReadinessDecisionRecordId?: string;
  releaseReadinessDecisionRecordHash?: string;
  projectId: string;
  subjectKind: string;
  subjectId: string;
  subjectHash: string;
  workflowRunId: string;
  workflowStepId: string;
  acceptedApprovalId: string;
  acceptedApprovalHash: string;
  policySatisfactionId: string;
  policySatisfactionHash: string;
  sourceApplyReviewId: string;
  sourceApplyReviewHash: string;
  rollbackEvidenceId?: string;
  rollbackEvidenceHash?: string;
  workflowContinuationEvidenceId: string;
  workflowContinuationEvidenceHash: string;
  reviewedBy: string;
  reviewedAtUtc: string;
  expiresAtUtc?: string;
  readinessStatus: string;
  approvalEvidencePresent: boolean;
  policyEvidencePresent: boolean;
  sourceApplyEvidencePresent: boolean;
  rollbackEvidencePresent: boolean;
  workflowContinuationEvidencePresent: boolean;
  releaseReadinessReportPresent: boolean;
  releaseReadinessReportSatisfied: boolean;
  releaseReadinessDecisionPresent: boolean;
  releaseReadyClaimed: boolean;
  releaseBlocked: boolean;
  releaseFailed: boolean;
  releasePartial: boolean;
  findings: ReleaseReadinessEvidenceFinding[];
  warnings: string[];
  evidenceRefs: string[];
  boundaryMaxims: string[];
  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  displayState: ReleaseReadinessEvidenceDisplayState;
};

export type ReleaseReadinessEvidenceDisplayState = {
  evidencePresent: boolean;
  evidenceSatisfied: boolean;
  recordStored: boolean;
  humanReviewRequired: boolean;

  approvalCreated: false;
  releaseReadinessDecided: false;
  releaseApproved: false;
  deploymentApproved: false;
  mergeApproved: false;
  releaseExecuted: false;

  dryRunExecuted: false;
  sourceApplyExecuted: false;
  rollbackExecuted: false;
  workflowContinued: false;
  workflowMutated: false;
  releaseDecisionRecordCreated: false;
  gitOperationExecuted: false;

  authorityRefreshed: false;
  evidenceReissued: false;
  mutationPerformed: false;
};

export type ReleaseReadinessEvidencePanelProps = {
  evidence?: ReleaseReadinessEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const releaseReadinessEvidenceRequiredFields: Array<keyof ReleaseReadinessEvidence> = [
  'releaseReadinessEvidenceId',
  'releaseReadinessEvidenceHash',
  'releaseReadinessReportId',
  'releaseReadinessReportHash',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'acceptedApprovalId',
  'acceptedApprovalHash',
  'policySatisfactionId',
  'policySatisfactionHash',
  'sourceApplyReviewId',
  'sourceApplyReviewHash',
  'workflowContinuationEvidenceId',
  'workflowContinuationEvidenceHash',
  'reviewedBy',
  'reviewedAtUtc',
  'readinessStatus'
];

export const releaseReadinessEvidenceDefaultDisplayState: ReleaseReadinessEvidenceDisplayState = {
  evidencePresent: false,
  evidenceSatisfied: false,
  recordStored: false,
  humanReviewRequired: true,
  approvalCreated: false,
  releaseReadinessDecided: false,
  releaseApproved: false,
  deploymentApproved: false,
  mergeApproved: false,
  releaseExecuted: false,
  dryRunExecuted: false,
  sourceApplyExecuted: false,
  rollbackExecuted: false,
  workflowContinued: false,
  workflowMutated: false,
  releaseDecisionRecordCreated: false,
  gitOperationExecuted: false,
  authorityRefreshed: false,
  evidenceReissued: false,
  mutationPerformed: false
};

export const releaseReadinessEvidenceAuthorityFlags: Array<keyof Pick<
  ReleaseReadinessEvidenceDisplayState,
  | 'approvalCreated'
  | 'releaseReadinessDecided'
  | 'releaseApproved'
  | 'deploymentApproved'
  | 'mergeApproved'
  | 'releaseExecuted'
  | 'dryRunExecuted'
  | 'sourceApplyExecuted'
  | 'rollbackExecuted'
  | 'workflowContinued'
  | 'workflowMutated'
  | 'releaseDecisionRecordCreated'
  | 'gitOperationExecuted'
  | 'authorityRefreshed'
  | 'evidenceReissued'
  | 'mutationPerformed'
>> = [
  'approvalCreated',
  'releaseReadinessDecided',
  'releaseApproved',
  'deploymentApproved',
  'mergeApproved',
  'releaseExecuted',
  'dryRunExecuted',
  'sourceApplyExecuted',
  'rollbackExecuted',
  'workflowContinued',
  'workflowMutated',
  'releaseDecisionRecordCreated',
  'gitOperationExecuted',
  'authorityRefreshed',
  'evidenceReissued',
  'mutationPerformed'
];

export function missingReleaseReadinessEvidenceFields(evidence: ReleaseReadinessEvidence | null | undefined) {
  if (!evidence) {
    return releaseReadinessEvidenceRequiredFields;
  }

  return releaseReadinessEvidenceRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasReleaseReadinessEvidenceAuthorityFlags(evidence: ReleaseReadinessEvidence | null | undefined) {
  return Boolean(evidence && releaseReadinessEvidenceAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidReleaseReadinessEvidenceTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
