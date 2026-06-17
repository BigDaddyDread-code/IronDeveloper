export type PolicySatisfactionEvidence = {
  policyId: string;
  policyName: string;
  policyVersion: string;

  subjectId: string;
  subjectHash: string;
  workflowId: string;

  approvalId?: string;
  approvalHash?: string;

  evidenceRefs: string[];

  evaluatedAtUtc: string;
  expiresAtUtc?: string;

  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  warnings: string[];

  displayState: PolicySatisfactionDisplayState;
};

export type PolicySatisfactionDisplayState = {
  evidencePresent: boolean;
  evidenceSatisfied: boolean;
  recordStored: boolean;
  humanReviewRequired: boolean;

  approvalCreated: false;
  releaseApproved: false;
  deploymentApproved: false;
  mergeApproved: false;

  dryRunExecuted: false;
  sourceApplyExecuted: false;
  rollbackExecuted: false;
  workflowContinued: false;

  authorityRefreshed: false;
  authorityReissued: false;
  mutationPerformed: false;
};

export type PolicySatisfactionPanelProps = {
  evidence?: PolicySatisfactionEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const policySatisfactionRequiredFields: Array<keyof PolicySatisfactionEvidence> = [
  'policyId',
  'policyName',
  'policyVersion',
  'subjectId',
  'subjectHash',
  'workflowId',
  'evaluatedAtUtc'
];

export const policySatisfactionDefaultDisplayState: PolicySatisfactionDisplayState = {
  evidencePresent: false,
  evidenceSatisfied: false,
  recordStored: false,
  humanReviewRequired: true,
  approvalCreated: false,
  releaseApproved: false,
  deploymentApproved: false,
  mergeApproved: false,
  dryRunExecuted: false,
  sourceApplyExecuted: false,
  rollbackExecuted: false,
  workflowContinued: false,
  authorityRefreshed: false,
  authorityReissued: false,
  mutationPerformed: false
};

export const policySatisfactionAuthorityFlags: Array<keyof Pick<
  PolicySatisfactionDisplayState,
  | 'approvalCreated'
  | 'releaseApproved'
  | 'deploymentApproved'
  | 'mergeApproved'
  | 'dryRunExecuted'
  | 'sourceApplyExecuted'
  | 'rollbackExecuted'
  | 'workflowContinued'
  | 'authorityRefreshed'
  | 'authorityReissued'
  | 'mutationPerformed'
>> = [
  'approvalCreated',
  'releaseApproved',
  'deploymentApproved',
  'mergeApproved',
  'dryRunExecuted',
  'sourceApplyExecuted',
  'rollbackExecuted',
  'workflowContinued',
  'authorityRefreshed',
  'authorityReissued',
  'mutationPerformed'
];

export function missingPolicySatisfactionFields(evidence: PolicySatisfactionEvidence | null | undefined) {
  if (!evidence) {
    return policySatisfactionRequiredFields;
  }

  return policySatisfactionRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasPolicySatisfactionAuthorityFlags(evidence: PolicySatisfactionEvidence | null | undefined) {
  return Boolean(evidence && policySatisfactionAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidPolicySatisfactionTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
