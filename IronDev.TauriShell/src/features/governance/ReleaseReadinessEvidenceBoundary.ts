export const releaseReadinessEvidenceBoundaryBanner =
  'Release readiness evidence is display only. It does not decide readiness, approve release, approve deployment, approve merge, execute release, apply source, execute rollback, continue workflow, refresh authority, reissue evidence, or mutate state. Human review remains required.';

export const releaseReadinessEvidenceBoundaryRules = [
  'Release readiness UI is not release readiness decision.',
  'Release readiness evidence is not release approval.',
  'Release readiness report is not release approval.',
  'Release readiness decision record is not release execution.',
  'Accepted approval is not release approval.',
  'Policy satisfaction is not release approval.',
  'Source apply review is not source apply.',
  'Rollback evidence is not release approval.',
  'Workflow continuation evidence is not workflow continuation.',
  'Release ready claim is not release approval.',
  'Release blocked evidence is not automatic recovery.',
  'Release failed evidence is not automatic retry.',
  'Release readiness display is not deployment approval.',
  'Release readiness display is not merge approval.',
  'Release readiness display is not release execution.',
  'Release readiness display is not workflow mutation.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review remains required.'
];

export const releaseReadinessEvidenceUnsafeMarkers = [
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

export const releaseReadinessEvidenceAuthorityClaimMarkers = [
  'release approved',
  'release authorized',
  'release executed',
  'release complete',
  'deployment approved',
  'deployment authorized',
  'merge approved',
  'ready to deploy',
  'safe to deploy',
  'safe to merge',
  'safe to release',
  'ready to release',
  'green to ship',
  'can execute',
  'can deploy',
  'can merge',
  'release readiness decided',
  'release gate passed',
  'release gate approved',
  'decision record created',
  'approval created',
  'source apply approved',
  'source applied',
  'source apply executed',
  'rollback approved',
  'rollback executed',
  'workflow continued',
  'workflow mutated',
  'authority refreshed',
  'evidence reissued',
  'git' + ' committed',
  'git' + ' pushed',
  'tag created',
  'pull request created'
];

const allowedNegativeAuthorityPhrases = [
  'not release approval',
  'not deployment approval',
  'not merge approval',
  'not release execution',
  'not release readiness decision',
  'does not approve release',
  'does not approve deployment',
  'does not approve merge',
  'does not execute release',
  'does not apply source',
  'does not execute rollback',
  'does not continue workflow',
  'does not mutate workflow state',
  'not backend truth',
  'not authority',
  'human review required'
];

export function releaseReadinessEvidenceSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizeReleaseReadinessEvidenceText(text);
  if (releaseReadinessEvidenceUnsafeMarkers.some((marker) => normalized.includes(normalizeReleaseReadinessEvidenceText(marker)))) {
    return '[redacted release readiness evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeReleaseReadinessEvidenceText(phrase)));
  if (!allowedNegative && releaseReadinessEvidenceAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeReleaseReadinessEvidenceText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsReleaseReadinessEvidenceUnsafeText(value: unknown) {
  const normalized = normalizeReleaseReadinessEvidenceText(value);
  return releaseReadinessEvidenceUnsafeMarkers.some((marker) => normalized.includes(normalizeReleaseReadinessEvidenceText(marker)));
}

export function containsReleaseReadinessEvidenceAuthorityClaim(value: unknown) {
  const normalized = normalizeReleaseReadinessEvidenceText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeReleaseReadinessEvidenceText(phrase)));
  return !allowedNegative && releaseReadinessEvidenceAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeReleaseReadinessEvidenceText(marker)));
}

function normalizeReleaseReadinessEvidenceText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
