import { expect, test } from '@playwright/test';
import { UiAuthorityFirewall } from '../src/features/governance/UiAuthorityFirewall';

const hostileUiAuthorityClaims = [
  { text: 'Status card says release approved.', expected: 'approved' },
  { text: 'Memory banner says source apply approved.', expected: 'approved' },
  { text: 'Receipt row says workflow continued.', expected: 'workflow continued' },
  { text: 'Timeline chip says git pushed.', expected: 'git pushed' },
  { text: 'Review summary says pull request created.', expected: 'pull request created' }
] as const;

const hostileUiActionLabels = [
  'Approve Release',
  'Apply Source',
  'Execute Rollback',
  'Continue Workflow',
  'Create Pull Request',
  'Run Tool'
] as const;

test('UiAuthorityFirewall_hostile_corpus_detects_authority_claims', async () => {
  for (const item of hostileUiAuthorityClaims) {
    expect(UiAuthorityFirewall.containsAuthorityClaim(item.text), item.text).toBe(true);
    expect(UiAuthorityFirewall.findAuthorityClaims(item.text), item.text).toContain(item.expected);
  }
});

test('UiAuthorityFirewall_hostile_corpus_detects_action_labels', async () => {
  for (const label of hostileUiActionLabels) {
    expect(UiAuthorityFirewall.containsForbiddenActionLabel(`<button>${label}</button>`), label).toBe(true);
  }
});

test('UiAuthorityFirewall_hostile_corpus_allows_negative_boundary_copy', async () => {
  const safeCopy = [
    'This panel is not authority.',
    'This status is not release approval.',
    'This receipt does not approve source apply.',
    'Display only. Human review required.'
  ].join(' ');

  expect(UiAuthorityFirewall.containsAuthorityClaim(safeCopy)).toBe(false);
  expect(UiAuthorityFirewall.containsForbiddenActionLabel(safeCopy)).toBe(false);
});

test('UiAuthorityFirewall_hostile_corpus_detects_claim_after_negative_boundary', async () => {
  const mixedCopy = 'This panel is not authority. Later text says release approved.';

  expect(UiAuthorityFirewall.containsAuthorityClaim(mixedCopy)).toBe(true);
  expect(UiAuthorityFirewall.findAuthorityClaims(mixedCopy)).toContain('release approved');
});

test('UiAuthorityFirewall_hostile_corpus_remains_static_ui_evidence_only', async () => {
  const boundary = [
    'Hostile UI text tests are not backend governance.',
    'Static UI scanning is not runtime policy.',
    'Catching this lie does not catch every lie.',
    'Human review remains required.'
  ].join(' ');

  expect(UiAuthorityFirewall.containsAuthorityClaim(boundary)).toBe(false);
  expect(UiAuthorityFirewall.containsForbiddenActionLabel(boundary)).toBe(false);
});
