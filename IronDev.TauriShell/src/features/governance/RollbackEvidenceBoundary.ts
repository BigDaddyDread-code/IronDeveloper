export const rollbackEvidenceBoundaryBanner =
  'Rollback evidence is display only. It does not approve rollback, execute rollback, retry rollback, start recovery, continue workflow, approve release, refresh authority, reissue evidence, or mutate state. Human review remains required.';

export const rollbackEvidenceBoundaryRules = [
  'Rollback UI is not rollback execution.',
  'Rollback evidence is not rollback approval.',
  'Rollback plan is not rollback execution.',
  'Rollback support receipt is not rollback execution.',
  'Rollback execution receipt is evidence of execution, not execution by UI.',
  'Rollback audit report is not workflow continuation.',
  'Rollback success evidence is not release approval.',
  'Rollback failure evidence is not automatic recovery.',
  'Rollback partial evidence is not automatic retry.',
  'Rollback display is not source mutation.',
  'Rollback display is not release approval.',
  'Rollback display is not deployment approval.',
  'Rollback display is not merge approval.',
  'Rollback display is not workflow continuation.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review remains required.'
];

export const rollbackEvidenceUnsafeMarkers = [
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

export const rollbackEvidenceAuthorityClaimMarkers = [
  'rollback approved',
  'rollback authorized',
  'rollback executed',
  'rollback retried',
  'rollback recovered',
  'recovery complete',
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
  'workflow continued',
  'authority refreshed',
  'evidence reissued',
  'git' + ' committed',
  'git' + ' pushed',
  'tag created',
  'pull request created'
];

const allowedNegativeAuthorityPhrases = [
  'not rollback approval',
  'not rollback execution',
  'not source apply',
  'does not execute rollback',
  'does not retry rollback',
  'does not recover automatically',
  'does not apply source',
  'does not continue workflow',
  'does not approve release',
  'does not approve merge',
  'does not mutate state',
  'not backend truth',
  'not authority',
  'human review required'
];

export function rollbackEvidenceSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizeRollbackEvidenceText(text);
  if (rollbackEvidenceUnsafeMarkers.some((marker) => normalized.includes(normalizeRollbackEvidenceText(marker)))) {
    return '[redacted rollback evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeRollbackEvidenceText(phrase)));
  if (!allowedNegative && rollbackEvidenceAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeRollbackEvidenceText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsRollbackEvidenceUnsafeText(value: unknown) {
  const normalized = normalizeRollbackEvidenceText(value);
  return rollbackEvidenceUnsafeMarkers.some((marker) => normalized.includes(normalizeRollbackEvidenceText(marker)));
}

export function containsRollbackEvidenceAuthorityClaim(value: unknown) {
  const normalized = normalizeRollbackEvidenceText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeRollbackEvidenceText(phrase)));
  return !allowedNegative && rollbackEvidenceAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeRollbackEvidenceText(marker)));
}

function normalizeRollbackEvidenceText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
