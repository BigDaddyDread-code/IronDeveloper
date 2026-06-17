export const sourceApplyReviewBoundaryBanner =
  'Source apply review evidence is display only. It does not approve source apply, execute dry-run, apply source, execute rollback, continue workflow, refresh authority, reissue evidence, or mutate state. Human review remains required.';

export const sourceApplyReviewBoundaryRules = [
  'Source apply review UI is not source apply approval.',
  'Source apply review evidence is not source apply.',
  'Patch artifact evidence is not source apply.',
  'Dry-run receipt evidence is not source apply.',
  'Source apply request is not source apply.',
  'Source apply review status is not execution authority.',
  'Source apply review display is not release approval.',
  'Source apply review display is not deployment approval.',
  'Source apply review display is not merge approval.',
  'Source apply review display is not workflow continuation.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review remains required.'
];

export const sourceApplyReviewUnsafeMarkers = [
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

export const sourceApplyReviewAuthorityClaimMarkers = [
  'source apply approved',
  'source approved',
  'dry-run approved',
  'patch approved',
  'release approved',
  'deployment approved',
  'merge approved',
  'safe to deploy',
  'safe to merge',
  'ready to release',
  'can execute',
  'green to ship',
  'dry-run executed',
  'source applied',
  'source apply executed',
  'rollback executed',
  'workflow continued',
  'authority refreshed',
  'evidence reissued',
  'git' + ' committed',
  'git' + ' pushed',
  'tag created',
  'pull request created'
];

const allowedNegativeAuthorityPhrases = [
  'not source apply approval',
  'not source apply',
  'does not apply source',
  'does not execute dry-run',
  'does not execute source apply',
  'does not continue workflow',
  'does not approve release',
  'does not approve merge',
  'does not mutate state',
  'not backend truth',
  'not authority',
  'human review required'
];

export function sourceApplyReviewSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizeSourceApplyReviewText(text);
  if (sourceApplyReviewUnsafeMarkers.some((marker) => normalized.includes(normalizeSourceApplyReviewText(marker)))) {
    return '[redacted source apply review evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeSourceApplyReviewText(phrase)));
  if (!allowedNegative && sourceApplyReviewAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeSourceApplyReviewText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsSourceApplyReviewUnsafeText(value: unknown) {
  const normalized = normalizeSourceApplyReviewText(value);
  return sourceApplyReviewUnsafeMarkers.some((marker) => normalized.includes(normalizeSourceApplyReviewText(marker)));
}

export function containsSourceApplyReviewAuthorityClaim(value: unknown) {
  const normalized = normalizeSourceApplyReviewText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeSourceApplyReviewText(phrase)));
  return !allowedNegative && sourceApplyReviewAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeSourceApplyReviewText(marker)));
}

function normalizeSourceApplyReviewText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
