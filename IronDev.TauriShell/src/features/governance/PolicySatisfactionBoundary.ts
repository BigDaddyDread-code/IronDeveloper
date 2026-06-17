export const policySatisfactionBoundaryBanner =
  'Policy satisfaction evidence is display only. It does not approve, execute, continue workflow, refresh authority, reissue evidence, or mutate state. Human review remains required.';

export const policySatisfactionBoundaryRules = [
  'Policy satisfaction UI is not policy satisfaction.',
  'Policy evidence is not approval.',
  'Policy evidence display is not policy evaluation.',
  'Policy satisfaction is not release approval.',
  'Policy satisfaction is not dry-run approval.',
  'Policy satisfaction is not source apply.',
  'Policy satisfaction is not rollback execution.',
  'Policy satisfaction is not workflow continuation.',
  'Policy satisfaction is not deployment approval.',
  'Policy satisfaction is not merge approval.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review required remains true.'
];

export const policySatisfactionUnsafeMarkers = [
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
  'bearer'
];

export const policySatisfactionAuthorityClaimMarkers = [
  'release approved',
  'deployment approved',
  'merge approved',
  'safe to deploy',
  'safe to merge',
  'ready to release',
  'can execute',
  'green to ship',
  'source applied',
  'rollback executed',
  'workflow continued',
  'authority refreshed',
  'evidence reissued'
];

const allowedNegativeAuthorityPhrases = [
  'not release approval',
  'does not execute',
  'does not continue workflow',
  'does not satisfy policy',
  'does not mutate state',
  'human review required'
];

export function policySatisfactionSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizePolicyText(text);
  if (policySatisfactionUnsafeMarkers.some((marker) => normalized.includes(normalizePolicyText(marker)))) {
    return '[redacted policy satisfaction evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizePolicyText(phrase)));
  if (!allowedNegative && policySatisfactionAuthorityClaimMarkers.some((marker) => normalized.includes(normalizePolicyText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsPolicySatisfactionUnsafeText(value: unknown) {
  const normalized = normalizePolicyText(value);
  return policySatisfactionUnsafeMarkers.some((marker) => normalized.includes(normalizePolicyText(marker)));
}

export function containsPolicySatisfactionAuthorityClaim(value: unknown) {
  const normalized = normalizePolicyText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizePolicyText(phrase)));
  return !allowedNegative && policySatisfactionAuthorityClaimMarkers.some((marker) => normalized.includes(normalizePolicyText(marker)));
}

function normalizePolicyText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
