export const workflowContinuationEvidenceBoundaryBanner =
  'Workflow continuation evidence is display only. It does not approve continuation, continue workflow, create transition records, mutate workflow state, execute rollback, apply source, refresh authority, reissue evidence, or approve release. Human review remains required.';

export const workflowContinuationEvidenceBoundaryRules = [
  'Workflow continuation UI is not workflow continuation.',
  'Workflow continuation evidence is not continuation approval.',
  'Workflow continuation gate evaluation is not workflow continuation.',
  'Workflow transition record is transition evidence.',
  'Workflow transition record is not workflow continuation.',
  'Workflow transition store is not workflow transition.',
  'Workflow continuation display is not workflow mutation.',
  'Workflow continuation display is not source apply.',
  'Workflow continuation display is not rollback execution.',
  'Workflow continuation display is not release approval.',
  'Workflow continuation display is not deployment approval.',
  'Workflow continuation display is not merge approval.',
  'Workflow continuation failure evidence is not automatic recovery.',
  'Workflow continuation partial evidence is not automatic retry.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review remains required.'
];

export const workflowContinuationEvidenceUnsafeMarkers = [
  'raw' + 'prompt',
  'raw prompt',
  'raw' + 'completion',
  'raw completion',
  'raw' + 'tooloutput',
  'raw tool output',
  'chain' + 'ofthought',
  'chain of thought',
  'private' + 'reasoning',
  'private reasoning',
  'scratchpad',
  'password',
  'secret',
  'api' + 'key',
  'private' + 'key',
  'bearer',
  'entire patch',
  'patch payload',
  'raw patch',
  'raw diff',
  'full diff'
];

export const workflowContinuationEvidenceAuthorityClaimMarkers = [
  'workflow continuation approved',
  'continuation approved',
  'workflow continued',
  'workflow transition created',
  'workflow transition record created',
  'workflow mutated',
  'continuation complete',
  'retry continuation',
  'recovery complete',
  'rollback approved',
  'rollback executed',
  'source apply approved',
  'source approved',
  'dry-run approved',
  'patch approved',
  'release approved',
  'deployment approved',
  'merge approved',
  'safe to deploy',
  'safe to merge',
  'safe to release',
  'ready to release',
  'can execute',
  'green to ship',
  'dry-run executed',
  'source applied',
  'source apply executed',
  'authority refreshed',
  'evidence reissued',
  'git' + ' committed',
  'git' + ' pushed',
  'tag created',
  'pull request created'
];

const allowedNegativeAuthorityPhrases = [
  'not workflow continuation',
  'not continuation approval',
  'not workflow mutation',
  'does not continue workflow',
  'does not create transition record',
  'does not mutate workflow state',
  'does not execute rollback',
  'does not apply source',
  'does not approve release',
  'does not approve merge',
  'not backend truth',
  'not authority',
  'human review required'
];

export function workflowContinuationEvidenceSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizeWorkflowContinuationEvidenceText(text);
  if (workflowContinuationEvidenceUnsafeMarkers.some((marker) => normalized.includes(normalizeWorkflowContinuationEvidenceText(marker)))) {
    return '[redacted workflow continuation evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeWorkflowContinuationEvidenceText(phrase)));
  if (!allowedNegative && workflowContinuationEvidenceAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeWorkflowContinuationEvidenceText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsWorkflowContinuationEvidenceUnsafeText(value: unknown) {
  const normalized = normalizeWorkflowContinuationEvidenceText(value);
  return workflowContinuationEvidenceUnsafeMarkers.some((marker) => normalized.includes(normalizeWorkflowContinuationEvidenceText(marker)));
}

export function containsWorkflowContinuationEvidenceAuthorityClaim(value: unknown) {
  const normalized = normalizeWorkflowContinuationEvidenceText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeWorkflowContinuationEvidenceText(phrase)));
  return !allowedNegative && workflowContinuationEvidenceAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeWorkflowContinuationEvidenceText(marker)));
}

function normalizeWorkflowContinuationEvidenceText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
