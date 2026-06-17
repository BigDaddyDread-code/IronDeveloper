export const patchArtifactBoundaryBanner =
  'Patch artifact evidence is display only. It does not create patch artifacts, edit patch artifacts, execute dry-run, apply source, approve release, refresh authority, reissue evidence, or mutate state. Human review remains required.';

export const patchArtifactBoundaryRules = [
  'Patch artifact UI is not patch artifact creation.',
  'Patch artifact evidence is not patch approval.',
  'Patch artifact evidence is not dry-run.',
  'Patch artifact evidence is not source apply.',
  'Patch artifact stored is not source mutation.',
  'Patch artifact hash is not approval authority.',
  'Patch artifact file summary is not raw patch payload.',
  'Patch artifact display is not release approval.',
  'Patch artifact display is not deployment approval.',
  'Patch artifact display is not merge approval.',
  'Patch artifact display is not workflow continuation.',
  'UI state is not authority.',
  'Fixture data is not backend truth.',
  'Copy buttons are not execution controls.',
  'Human review remains required.'
];

export const patchArtifactUnsafeMarkers = [
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

export const patchArtifactAuthorityClaimMarkers = [
  'patch approved',
  'dry-run approved',
  'source apply approved',
  'release approved',
  'deployment approved',
  'merge approved',
  'safe to deploy',
  'safe to merge',
  'ready to release',
  'can execute',
  'green to ship',
  'patch created by ui',
  'patch edited by ui',
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
  'not patch approval',
  'not dry-run',
  'not source apply',
  'does not create patch artifact',
  'does not edit patch artifact',
  'does not apply patch',
  'does not apply source',
  'does not execute',
  'does not continue workflow',
  'does not approve release',
  'does not approve merge',
  'does not mutate state',
  'not backend truth',
  'not authority',
  'human review required'
];

export function patchArtifactSafeText(value: unknown, fallback = 'Unavailable') {
  const text = value === undefined || value === null ? '' : `${value}`.trim();
  if (!text) {
    return fallback;
  }

  const normalized = normalizePatchArtifactText(text);
  if (patchArtifactUnsafeMarkers.some((marker) => normalized.includes(normalizePatchArtifactText(marker)))) {
    return '[redacted patch artifact evidence]';
  }

  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizePatchArtifactText(phrase)));
  if (!allowedNegative && patchArtifactAuthorityClaimMarkers.some((marker) => normalized.includes(normalizePatchArtifactText(marker)))) {
    return '[authority claim redacted]';
  }

  return text;
}

export function containsPatchArtifactUnsafeText(value: unknown) {
  const normalized = normalizePatchArtifactText(value);
  return patchArtifactUnsafeMarkers.some((marker) => normalized.includes(normalizePatchArtifactText(marker)));
}

export function containsPatchArtifactAuthorityClaim(value: unknown) {
  const normalized = normalizePatchArtifactText(value);
  const allowedNegative = allowedNegativeAuthorityPhrases.some((phrase) => normalized.includes(normalizePatchArtifactText(phrase)));
  return !allowedNegative && patchArtifactAuthorityClaimMarkers.some((marker) => normalized.includes(normalizePatchArtifactText(marker)));
}

function normalizePatchArtifactText(value: unknown) {
  return `${value ?? ''}`.toLowerCase().replace(/[^a-z0-9 -]/g, '');
}
