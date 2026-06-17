export const sourceApplyDryRunReceiptBoundaryBanner =
  'Source apply dry-run receipt evidence is display only. It does not execute dry-run, apply source, approve source apply, continue workflow, refresh authority, reissue evidence, or mutate state. Human review remains required.';

export const sourceApplyDryRunReceiptBoundaryRules = [
  'Dry-run receipt UI is not dry-run execution.',
  'Dry-run receipt evidence is not source apply approval.',
  'Dry-run receipt evidence is not source apply.',
  'Dry-run passed is not source applied.',
  'Dry-run receipt stored is not source mutation.',
  'Source apply request is not source apply.',
  'Source apply dry-run is not source apply.',
  'Source apply dry-run receipt is not release approval.',
  'Source apply dry-run receipt is not deployment approval.',
  'Source apply dry-run receipt is not merge approval.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review remains required.'
];

export const sourceApplyDryRunReceiptUnsafeMarkers = [
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

export const sourceApplyDryRunReceiptAuthorityClaimMarkers = [
  'release approved',
  'deployment approved',
  'merge approved',
  'source apply approved',
  'dry-run approved source apply',
  'safe to deploy',
  'safe to merge',
  'ready to release',
  'can execute',
  'green to ship',
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
  'not source apply',
  'not source applied',
  'not source mutation',
  'does not apply source',
  'does not execute',
  'does not continue workflow',
  'does not approve release',
  'does not approve deployment',
  'does not approve merge',
  'does not mutate state',
  'does not grant',
  'human review required',
  'not backend truth',
  'not authority'
];

export function sourceApplyDryRunReceiptSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizeDryRunReceiptText(text);
  if (sourceApplyDryRunReceiptUnsafeMarkers.some((marker) => normalized.includes(normalizeDryRunReceiptText(marker)))) {
    return '[redacted dry-run receipt evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeDryRunReceiptText(phrase)));
  if (!allowedNegative && sourceApplyDryRunReceiptAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeDryRunReceiptText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsSourceApplyDryRunReceiptUnsafeText(value: unknown) {
  const normalized = normalizeDryRunReceiptText(value);
  return sourceApplyDryRunReceiptUnsafeMarkers.some((marker) => normalized.includes(normalizeDryRunReceiptText(marker)));
}

export function containsSourceApplyDryRunReceiptAuthorityClaim(value: unknown) {
  const normalized = normalizeDryRunReceiptText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizeDryRunReceiptText(phrase)));
  return !allowedNegative && sourceApplyDryRunReceiptAuthorityClaimMarkers.some((marker) => normalized.includes(normalizeDryRunReceiptText(marker)));
}

function normalizeDryRunReceiptText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
