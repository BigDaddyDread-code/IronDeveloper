export type AcceptedApprovalPanelStatus = 'empty' | 'loading' | 'loaded' | 'error';

export type AcceptedApprovalEvidenceViewModel = {
  acceptedApprovalId: string;
  acceptedApprovalHash: string;

  projectId: string;

  subjectKind: string;
  subjectId: string;
  subjectHash: string;

  workflowRunId: string;
  workflowStepId: string;

  acceptedBy: string;
  acceptedAtUtc: string;

  evidenceReferences: string[];
  boundaryMaxims: string[];

  isStale?: boolean;
  isExpired?: boolean;
  staleReasonCodes?: string[];

  humanReviewRequired: boolean;

  releaseApproved: false;
  deploymentApproved: false;
  mergeApproved: false;
  releaseExecuted: false;
  sourceApplyExecuted: false;
  rollbackExecuted: false;
  workflowContinued: false;
  workflowMutated: false;
  gitOperationExecuted: false;
  authorityRefreshed: false;
  evidenceReissued: false;
};

export type AcceptedApprovalPanelProps = {
  evidence?: AcceptedApprovalEvidenceViewModel | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const acceptedApprovalRequiredFields: Array<keyof AcceptedApprovalEvidenceViewModel> = [
  'acceptedApprovalId',
  'acceptedApprovalHash',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'acceptedBy',
  'acceptedAtUtc'
];

export function missingAcceptedApprovalFields(evidence: AcceptedApprovalEvidenceViewModel | null | undefined) {
  if (!evidence) {
    return acceptedApprovalRequiredFields;
  }

  return acceptedApprovalRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}
