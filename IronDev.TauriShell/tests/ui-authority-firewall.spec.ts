import { expect, test } from '@playwright/test';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { UiAuthorityFirewall } from '../src/features/governance/UiAuthorityFirewall';

test('UiAuthorityFirewall_detects_forbidden_dependency_markers', async () => {
  for (const marker of UiAuthorityFirewall.forbiddenDependencyMarkers) {
    expect(UiAuthorityFirewall.containsForbiddenDependencyMarker(`import ${marker} from 'backend';`), marker).toBe(true);
  }

  expect(UiAuthorityFirewall.containsForbiddenDependencyMarker('Release readiness evidence panel display state')).toBe(false);
});

test('UiAuthorityFirewall_detects_forbidden_action_labels', async () => {
  for (const label of UiAuthorityFirewall.forbiddenActionLabels) {
    expect(UiAuthorityFirewall.containsForbiddenActionLabel(`<button>${label}</button>`), label).toBe(true);
  }

  expect(UiAuthorityFirewall.containsForbiddenActionLabel('Review evidence reference')).toBe(false);
});

test('UiAuthorityFirewall_allows_copy_only_inspection_labels', async () => {
  for (const label of UiAuthorityFirewall.allowedCopyInspectionLabels) {
    expect(UiAuthorityFirewall.containsForbiddenActionLabel(label), label).toBe(false);
    expect(UiAuthorityFirewall.containsAuthorityClaim(label), label).toBe(false);
  }
});

test('UiAuthorityFirewall_detects_unsafe_private_raw_markers', async () => {
  for (const marker of UiAuthorityFirewall.unsafePrivateRawMarkers) {
    expect(UiAuthorityFirewall.containsUnsafePrivateRawMarker(`unsafe ${marker} payload`), marker).toBe(true);
  }

  expect(UiAuthorityFirewall.containsUnsafePrivateRawMarker('Safe evidence summary and public hash')).toBe(false);
});

test('UiAuthorityFirewall_detects_authority_claims', async () => {
  for (const marker of UiAuthorityFirewall.forbiddenAuthorityMarkers) {
    expect(UiAuthorityFirewall.containsAuthorityClaim(`fixture says ${marker}`), marker).toBe(true);
  }

  expect(UiAuthorityFirewall.containsAuthorityClaim('Evidence reference was displayed for review')).toBe(false);
});

test('UiAuthorityFirewall_allows_negative_boundary_wording', async () => {
  for (const phrase of UiAuthorityFirewall.allowedNegativeBoundaryPhrases) {
    expect(UiAuthorityFirewall.isAllowedNegativeBoundaryText(phrase), phrase).toBe(true);
    expect(UiAuthorityFirewall.containsAuthorityClaim(phrase), phrase).toBe(false);
    expect(UiAuthorityFirewall.containsForbiddenActionLabel(phrase), phrase).toBe(false);
  }
});

test('UiAuthorityFirewall_detects_claims_after_allowed_negative_boundary_text', async () => {
  const text = 'This UI does not approve release. Later text says release approved.';

  expect(UiAuthorityFirewall.containsAuthorityClaim(text)).toBe(true);
  expect(UiAuthorityFirewall.findAuthorityClaims(text)).toContain('release approved');
});

test('UiAuthorityFirewall_scans_expected_governance_evidence_ui_files', async () => {
  expect(UiAuthorityFirewall.governanceEvidenceUiFileAllowList).toHaveLength(16);

  for (const file of UiAuthorityFirewall.governanceEvidenceUiFileAllowList) {
    expect(existsSync(join(process.cwd(), file)), `${file} should exist`).toBe(true);
  }
});

test('UiAuthorityFirewall_governance_evidence_ui_files_do_not_reference_forbidden_dependencies', async () => {
  for (const file of UiAuthorityFirewall.governanceEvidenceUiFileAllowList) {
    const content = readFileSync(join(process.cwd(), file), 'utf8');
    const found = UiAuthorityFirewall.findForbiddenDependencyMarkers(content);

    expect(found, `${file} contains forbidden dependency markers`).toEqual([]);
  }
});

test('UiAuthorityFirewall_governance_evidence_ui_files_do_not_render_forbidden_action_labels', async () => {
  for (const file of UiAuthorityFirewall.governanceEvidenceUiFileAllowList) {
    const content = readFileSync(join(process.cwd(), file), 'utf8');
    const found = UiAuthorityFirewall.findForbiddenActionLabels(content);

    expect(found, `${file} contains forbidden action labels`).toEqual([]);
  }
});

test('UiAuthorityFirewall_copy_controls_remain_inspection_only', async () => {
  const panelFiles = UiAuthorityFirewall.governanceEvidenceUiFileAllowList.filter((file) => file.endsWith('Panel.tsx'));
  let filesWithCopyControls = 0;

  for (const file of panelFiles) {
    const content = readFileSync(join(process.cwd(), file), 'utf8');
    const hasCopyControl = UiAuthorityFirewall.allowedCopyInspectionLabels.some((label) => content.includes(label));
    if (!hasCopyControl) {
      continue;
    }

    filesWithCopyControls += 1;
    expect(
      UiAuthorityFirewall.isAllowedNegativeBoundaryText(content),
      `${file} copy controls should include inspection-only boundary wording`
    ).toBe(true);
    expect(content.toLowerCase(), `${file} copy status should say inspection only`).toContain('inspection only');
  }

  expect(filesWithCopyControls).toBeGreaterThanOrEqual(8);
});

test('UiAuthorityFirewall_does_not_treat_static_scanning_as_backend_governance', async () => {
  const boundaryRules = [
    'UI authority firewall is not authority.',
    'UI authority firewall is not backend policy.',
    'Static UI scanning is not runtime governance.',
    'Fixture data is not backend truth.',
    'Display state is not authority.',
    'Copy buttons are not execution controls.',
    'Human review remains required.'
  ];

  for (const rule of boundaryRules) {
    expect(UiAuthorityFirewall.containsAuthorityClaim(rule), rule).toBe(false);
  }
});

test('UiAuthorityFirewall_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR237_UI_AUTHORITY_FIREWALL_UPDATE.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR237 updates the UI authority firewall. It does not grant authority.');
});
