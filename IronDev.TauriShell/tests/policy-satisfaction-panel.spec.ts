import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('PolicySatisfactionPanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Policy Satisfaction Evidence' })).toBeVisible();
  await expect(page.getByTestId('policy-satisfaction.identity')).toContainText('policy-230');
  await expect(page.getByTestId('policy-satisfaction.identity')).toContainText('Controlled source apply policy');
  await expect(page.getByTestId('policy-satisfaction.binding')).toContainText('source-apply-request-230');
  await expect(page.getByTestId('policy-satisfaction.binding')).toContainText('workflow-run-230');
  await expect(page.getByTestId('policy-satisfaction.evidenceRefs')).toContainText('policy-decision-230');
});

test('PolicySatisfactionPanel_renders_missing_policy_id_as_incomplete', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'missing-policy');

  await expect(page.getByTestId('policy-satisfaction.incompleteWarning')).toContainText('policyId');
  await expect(page.getByTestId('policy-satisfaction.currentBadge')).toHaveCount(0);
});

test('PolicySatisfactionPanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'missing-subject');

  await expect(page.getByTestId('policy-satisfaction.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('policy-satisfaction.incompleteWarning')).toContainText('subjectHash');
});

test('PolicySatisfactionPanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'missing-workflow');

  await expect(page.getByTestId('policy-satisfaction.incompleteWarning')).toContainText('workflowId');
});

test('PolicySatisfactionPanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('policy-satisfaction.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('policy-satisfaction.currentBadge')).toHaveCount(0);
});

test('PolicySatisfactionPanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'empty-refs');

  await expect(page.getByTestId('policy-satisfaction.noEvidenceRefs')).toContainText('Missing evidence does not satisfy policy.');
  await expect(page.getByTestId('policy-satisfaction.state')).toContainText('Supplied false');
});

test('PolicySatisfactionPanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading policy satisfaction evidence...' })).toBeVisible();
  await expect(page.getByTestId('policy-satisfaction.loading')).toContainText('UI loading does not satisfy policy.');
  await expectBoundaryWarning(page);
});

test('PolicySatisfactionPanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load policy satisfaction evidence.' })).toBeVisible();
  await expect(page.getByTestId('policy-satisfaction.error')).toContainText('No approval, execution, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('PolicySatisfactionPanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No policy satisfaction evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('policy-satisfaction.empty')).toContainText('Missing evidence does not satisfy policy.');
  await expectBoundaryWarning(page);
});

test('PolicySatisfactionPanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'current');

  await expect(page.getByTestId('policy-satisfaction.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('policy-satisfaction.boundaryRules')).toContainText('Policy evidence is not approval.');
  await expect(page.getByTestId('policy-satisfaction.boundaryRules')).toContainText('UI state is not authority.');
});

test('PolicySatisfactionPanel_display_satisfied_does_not_grant_release_or_execution', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'current');

  await expect(page.getByTestId('policy-satisfaction.statusBanner')).toContainText('Supplied evidence claims satisfaction');
  await expect(page.locator('body')).not.toContainText('Release approved');
  await expect(page.locator('body')).not.toContainText('Can execute');
  await expect(page.locator('body')).not.toContainText('Ready to release');
});

test('PolicySatisfactionPanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'stale');

  await expect(page.getByTestId('policy-satisfaction.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('policy-satisfaction.currentBadge')).toHaveCount(0);
});

test('PolicySatisfactionPanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'expired');

  await expect(page.getByTestId('policy-satisfaction.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('policy-satisfaction.currentBadge')).toHaveCount(0);
});

test('PolicySatisfactionPanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'unsafe');

  await expect(page.getByTestId('policy-satisfaction.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('policy-satisfaction.evidenceRefs')).toContainText('[redacted policy satisfaction evidence]');
  await expect(page.locator('body')).not.toContainText('raw prompt private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('PolicySatisfactionPanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'authority-claim');

  await expect(page.getByTestId('policy-satisfaction.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('release approved by fixture data');
});

test('PolicySatisfactionPanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'contradictory');

  await expect(page.getByTestId('policy-satisfaction.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('policy-satisfaction.currentBadge')).toHaveCount(0);
});

test('PolicySatisfactionPanel_does_not_render_approve_release_deployment_or_merge_buttons', async ({ page }) => {
  for (const label of ['Approve Release', 'Approve Deployment', 'Approve Merge']) {
    await expectNoButton(page, label);
  }
});

test('PolicySatisfactionPanel_does_not_render_execution_or_workflow_buttons', async ({ page }) => {
  for (const label of ['Run Dry-run', 'Apply Source', 'Execute Rollback', 'Continue Workflow']) {
    await expectNoButton(page, label);
  }
});

test('PolicySatisfactionPanel_does_not_render_refresh_or_reissue_buttons', async ({ page }) => {
  for (const label of ['Refresh Authority', 'Reissue Evidence']) {
    await expectNoButton(page, label);
  }
});

test('PolicySatisfactionPanel_does_not_render_agent_model_tool_buttons', async ({ page }) => {
  for (const label of ['Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('PolicySatisfactionPanel_allows_copy_policy_id', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Policy ID' }).click();

  await expect(page.getByTestId('policy-satisfaction.copyStatus')).toContainText('Policy id copied for inspection only.');
});

test('PolicySatisfactionPanel_allows_copy_hashes', async ({ page }) => {
  await openPolicySatisfactionPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Subject Hash' }).click();
  await expect(page.getByTestId('policy-satisfaction.copyStatus')).toContainText('Subject hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Approval Hash' }).click();
  await expect(page.getByTestId('policy-satisfaction.copyStatus')).toContainText('Approval hash copied for inspection only.');
});

test('PolicySatisfactionPanel_allows_copy_evidence_refs_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openPolicySatisfactionPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('policy-satisfaction.copyStatus')).toContainText('Evidence references copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('PolicySatisfactionPanel_static_ui_files_do_not_contain_forbidden_calls_or_labels', async () => {
  const files = [
    'src/features/governance/PolicySatisfactionTypes.ts',
    'src/features/governance/PolicySatisfactionBoundary.ts',
    'src/features/governance/PolicySatisfactionPanel.tsx',
    'src/features/governance/PolicySatisfactionPanelRoute.tsx'
  ];
  const forbidden = [
    'approveRelease(',
    'approveDeployment(',
    'approveMerge(',
    'executeDryRun(',
    'executeSourceApply(',
    'executeRollback(',
    'continueWorkflow(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    'promoteMemory(',
    'activateRetrieval(',
    'git push',
    'gh pr',
    'sqlconnection',
    'POST /policy-satisfaction',
    '"Approve release"',
    '"Approve deployment"',
    '"Approve merge"',
    '"Run dry-run"',
    '"Apply source"',
    '"Execute rollback"',
    '"Continue workflow"',
    '"Refresh authority"',
    '"Reissue evidence"'
  ];

  for (const file of files) {
    const content = readFileSync(join(process.cwd(), file), 'utf8').toLowerCase();
    for (const marker of forbidden) {
      expect(content, `${file} should not contain ${marker}`).not.toContain(marker.toLowerCase());
    }
  }
});

test('PolicySatisfactionPanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR230_POLICY_SATISFACTION_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR230 shows policy satisfaction evidence. It does not satisfy policy.');
});

async function expectNoButton(page: Page, label: string) {
  await openPolicySatisfactionPanel(page, 'current');
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('policy-satisfaction.boundaryBanner')).toContainText('Policy satisfaction evidence is display only.');
  await expect(page.getByTestId('policy-satisfaction.boundaryBanner')).toContainText('Human review remains required.');
}

async function openPolicySatisfactionPanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/policy-satisfaction?fixture=${fixture}`);
  await expect(page.getByTestId('policy-satisfaction.workspace')).toBeVisible();
}

async function seedShellContext(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) });
  });

  await page.route('**/irondev-api/api/environment', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        environment: 'LocalTest',
        database: 'IronDeveloper_Test',
        weaviatePrefix: 'irondev_test',
        isTestEnvironment: true,
        workspaceRoot: 'C:\\IronDevTestWorkspaces\\',
        logsRoot: 'C:\\IronDevTestLogs\\',
        dangerRealRepoWritesEnabled: false
      })
    });
  });

  await page.route('**/irondev-api/api/auth/me**', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 })
    });
  });

  await page.route('**/irondev-api/api/tenants**', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]) });
  });

  await page.route('**/irondev-api/api/projects', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }]) });
  });

  await page.route('**/irondev-api/api/projects/7/select', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}
