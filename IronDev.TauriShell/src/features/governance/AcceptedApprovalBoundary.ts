import type { AcceptedApprovalEvidenceViewModel } from './AcceptedApprovalTypes';

export const acceptedApprovalBoundaryBanner =
  'Accepted approval evidence is not release approval. This screen does not approve, execute, continue workflow, refresh authority, or reissue evidence. Human review remains required.';

export const acceptedApprovalBoundaryMaxims = [
  'Accepted approval UI is not accepted approval.',
  'Accepted approval UI is not release approval.',
  'Accepted approval UI is not release readiness.',
  'Accepted approval UI is not execution permission.',
  'Human review remains required.'
];

export const acceptedApprovalUnsafeTextMarkers = [
  'raw' + 'prompt',
  'raw prompt',
  'raw' + 'completion',
  'raw completion',
  'raw' + 'tooloutput',
  'raw tool output',
  'private' + 'reasoning',
  'private reasoning',
  'hidden' + 'reasoning',
  'hidden reasoning',
  'chain' + 'ofthought',
  'chain-of-thought',
  'scratchpad',
  'source' + 'content',
  'source content',
  'patch' + 'payload',
  'patch payload',
  'approval' + 'payload',
  'approval payload',
  'connection' + 'string',
  'password',
  'secret',
  'api' + 'key',
  'credential',
  'bearer '
];

export const acceptedApprovalActionFlags: Array<keyof Pick<
  AcceptedApprovalEvidenceViewModel,
  | 'releaseApproved'
  | 'deploymentApproved'
  | 'mergeApproved'
  | 'releaseExecuted'
  | 'sourceApplyExecuted'
  | 'rollbackExecuted'
  | 'workflowContinued'
  | 'workflowMutated'
  | 'gitOperationExecuted'
  | 'authorityRefreshed'
  | 'evidenceReissued'
>> = [
  'releaseApproved',
  'deploymentApproved',
  'mergeApproved',
  'releaseExecuted',
  'sourceApplyExecuted',
  'rollbackExecuted',
  'workflowContinued',
  'workflowMutated',
  'gitOperationExecuted',
  'authorityRefreshed',
  'evidenceReissued'
];

export function acceptedApprovalHasAuthorityFlag(evidence: AcceptedApprovalEvidenceViewModel | null | undefined) {
  return Boolean(evidence && acceptedApprovalActionFlags.some((flag) => evidence[flag] !== false));
}

export function acceptedApprovalSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = text.toLowerCase().replace(/[^a-z0-9 -]/g, '');
  return acceptedApprovalUnsafeTextMarkers.some((marker) => normalized.includes(marker))
    ? '[redacted accepted approval evidence]'
    : text;
}
