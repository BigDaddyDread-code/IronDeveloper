export type PatchArtifactFileSummary = {
  path: string;
  previousPath?: string;
  action: string;
  fileHashBefore?: string;
  fileHashAfter?: string;
  safeSummary: string;
};

export type PatchArtifactEvidence = {
  patchArtifactId: string;
  patchArtifactHash: string;
  patchArtifactStatus: string;
  projectId: string;
  subjectKind: string;
  subjectId: string;
  subjectHash: string;
  workflowRunId: string;
  workflowStepId: string;
  createdBy: string;
  createdAtUtc: string;
  storedAtUtc: string;
  expiresAtUtc?: string;
  sourceKind: string;
  sourceId: string;
  sourceHash: string;
  fileCount: number;
  files: PatchArtifactFileSummary[];
  warnings: string[];
  evidenceRefs: string[];
  boundaryMaxims: string[];
  stale?: boolean;
  expired?: boolean;
  incomplete?: boolean;
  unsafeMaterialDetected?: boolean;
  authorityClaimsDetected?: boolean;
  rawPatchPayloadPresent?: boolean;
  rawPatchPayloadRendered: false;
  displayState: PatchArtifactDisplayState;
};

export type PatchArtifactDisplayState = {
  evidencePresent: boolean;
  evidenceSatisfied: boolean;
  recordStored: boolean;
  humanReviewRequired: boolean;

  approvalCreated: false;
  patchArtifactCreated: false;
  patchArtifactEdited: false;
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

export type PatchArtifactPanelProps = {
  evidence?: PatchArtifactEvidence | null;
  isLoading?: boolean;
  errorMessage?: string | null;
};

export const patchArtifactRequiredFields: Array<keyof PatchArtifactEvidence> = [
  'patchArtifactId',
  'patchArtifactHash',
  'patchArtifactStatus',
  'projectId',
  'subjectKind',
  'subjectId',
  'subjectHash',
  'workflowRunId',
  'workflowStepId',
  'createdBy',
  'createdAtUtc',
  'storedAtUtc',
  'sourceKind',
  'sourceId',
  'sourceHash'
];

export const patchArtifactDefaultDisplayState: PatchArtifactDisplayState = {
  evidencePresent: false,
  evidenceSatisfied: false,
  recordStored: false,
  humanReviewRequired: true,
  approvalCreated: false,
  patchArtifactCreated: false,
  patchArtifactEdited: false,
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

export const patchArtifactAuthorityFlags: Array<keyof Pick<
  PatchArtifactDisplayState,
  | 'approvalCreated'
  | 'patchArtifactCreated'
  | 'patchArtifactEdited'
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
  'patchArtifactCreated',
  'patchArtifactEdited',
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

export function missingPatchArtifactFields(evidence: PatchArtifactEvidence | null | undefined) {
  if (!evidence) {
    return patchArtifactRequiredFields;
  }

  return patchArtifactRequiredFields.filter((field) => {
    const value = evidence[field];
    return typeof value !== 'string' || value.trim().length === 0;
  });
}

export function hasPatchArtifactAuthorityFlags(evidence: PatchArtifactEvidence | null | undefined) {
  return Boolean(evidence && patchArtifactAuthorityFlags.some((flag) => evidence.displayState[flag] !== false));
}

export function hasInvalidPatchArtifactTimestamp(value: string | undefined) {
  if (!value?.trim()) {
    return false;
  }

  return Number.isNaN(Date.parse(value));
}
