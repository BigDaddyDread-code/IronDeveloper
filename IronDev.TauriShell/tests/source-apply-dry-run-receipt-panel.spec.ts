import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('SourceApplyDryRunReceiptPanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Source Apply Dry-run Receipt Evidence' })).toBeVisible();
  await expect(page.getByTestId('dry-run-receipt.identity')).toContainText('dry-run-receipt-231');
  await expect(page.getByTestId('dry-run-receipt.identity')).toContainText('sha256:dry-run-receipt-hash-231');
  await expect(page.getByTestId('dry-run-receipt.requestBinding')).toContainText('source-apply-request-231');
  await expect(page.getByTestId('dry-run-receipt.subjectBinding')).toContainText('patch-artifact-231');
  await expect(page.getByTestId('dry-run-receipt.subjectBinding')).toContainText('workflow-run-231');
  await expect(page.getByTestId('dry-run-receipt.plannedFiles')).toContainText('src/apply/Widget.cs');
});

test('SourceApplyDryRunReceiptPanel_renders_missing_receipt_id_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing-receipt-id');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('dryRunReceiptId');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_renders_missing_receipt_hash_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing-receipt-hash');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('dryRunReceiptHash');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_renders_missing_source_apply_request_binding_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing-request');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('sourceApplyRequestId');
  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('sourceApplyRequestHash');
});

test('SourceApplyDryRunReceiptPanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing-subject');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('subjectHash');
});

test('SourceApplyDryRunReceiptPanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing-workflow');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('workflowStepId');
});

test('SourceApplyDryRunReceiptPanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'empty-refs');

  await expect(page.getByTestId('dry-run-receipt.noEvidenceRefs')).toContainText('Missing evidence does not permit source apply.');
  await expect(page.getByTestId('dry-run-receipt.missingEvidenceWarning')).toContainText('cannot permit source apply');
  await expect(page.getByTestId('dry-run-receipt.state')).toContainText('Supplied false');
});

test('SourceApplyDryRunReceiptPanel_renders_missing_boundary_maxims_as_incomplete', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing-boundary');

  await expect(page.getByTestId('dry-run-receipt.incompleteWarning')).toContainText('boundaryMaxims');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading dry-run receipt evidence...' })).toBeVisible();
  await expect(page.getByTestId('dry-run-receipt.loading')).toContainText('UI loading does not execute dry-run or apply source.');
  await expectBoundaryWarning(page);
});

test('SourceApplyDryRunReceiptPanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load dry-run receipt evidence.' })).toBeVisible();
  await expect(page.getByTestId('dry-run-receipt.error')).toContainText('No dry-run, approval, source mutation, rollback, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('SourceApplyDryRunReceiptPanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No dry-run receipt evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('dry-run-receipt.empty')).toContainText('Missing dry-run receipt evidence does not permit source apply.');
  await expectBoundaryWarning(page);
});

test('SourceApplyDryRunReceiptPanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'current');

  await expect(page.getByTestId('dry-run-receipt.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('dry-run-receipt.boundaryRules')).toContainText('Dry-run receipt evidence is not source apply approval.');
  await expect(page.getByTestId('dry-run-receipt.boundaryRules')).toContainText('UI state is not authority.');
});

test('SourceApplyDryRunReceiptPanel_display_valid_does_not_grant_source_apply_or_release', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'current');

  await expect(page.getByTestId('dry-run-receipt.statusBanner')).toContainText('Supplied evidence claims receipt satisfaction');
  await expect(page.locator('body')).not.toContainText('Release approved');
  await expect(page.locator('body')).not.toContainText('Source apply approved');
  await expect(page.locator('body')).not.toContainText('Ready to release');
});

test('SourceApplyDryRunReceiptPanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'stale');

  await expect(page.getByTestId('dry-run-receipt.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'expired');

  await expect(page.getByTestId('dry-run-receipt.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'unsafe');

  await expect(page.getByTestId('dry-run-receipt.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('dry-run-receipt.plannedFiles')).toContainText('[redacted dry-run receipt evidence]');
  await expect(page.getByTestId('dry-run-receipt.evidenceRefs')).toContainText('[redacted dry-run receipt evidence]');
  await expect(page.locator('body')).not.toContainText('raw prompt private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('SourceApplyDryRunReceiptPanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'authority-claim');

  await expect(page.getByTestId('dry-run-receipt.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('safe to merge and source apply executed by fixture data');
});

test('SourceApplyDryRunReceiptPanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'contradictory');

  await expect(page.getByTestId('dry-run-receipt.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('dry-run-receipt.currentBadge')).toHaveCount(0);
});

test('SourceApplyDryRunReceiptPanel_does_not_render_source_apply_or_dry_run_buttons', async ({ page }) => {
  for (const label of ['Approve Source Apply', 'Run Dry-run', 'Apply Source']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyDryRunReceiptPanel_does_not_render_rollback_workflow_or_release_buttons', async ({ page }) => {
  for (const label of ['Execute Rollback', 'Continue Workflow', 'Approve Release', 'Approve Deployment', 'Approve Merge']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyDryRunReceiptPanel_does_not_render_authority_or_git_buttons', async ({ page }) => {
  for (const label of ['Refresh Authority', 'Reissue Evidence', 'Run Git', 'Create Pull Request']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyDryRunReceiptPanel_does_not_render_agent_model_tool_buttons', async ({ page }) => {
  for (const label of ['Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyDryRunReceiptPanel_allows_copy_receipt_id_for_inspection_only', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Dry-run Receipt ID' }).click();

  await expect(page.getByTestId('dry-run-receipt.copyStatus')).toContainText('Dry-run receipt id copied for inspection only.');
});

test('SourceApplyDryRunReceiptPanel_allows_copy_hashes_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openDryRunReceiptPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Dry-run Receipt Hash' }).click();
  await expect(page.getByTestId('dry-run-receipt.copyStatus')).toContainText('Dry-run receipt hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Source Apply Request Hash' }).click();
  await expect(page.getByTestId('dry-run-receipt.copyStatus')).toContainText('Source apply request hash copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('SourceApplyDryRunReceiptPanel_allows_copy_evidence_refs_for_inspection_only', async ({ page }) => {
  await openDryRunReceiptPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('dry-run-receipt.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('SourceApplyDryRunReceiptPanel_static_ui_files_do_not_contain_forbidden_dependencies_or_action_labels', async () => {
  const files = [
    'src/features/governance/SourceApplyDryRunReceiptTypes.ts',
    'src/features/governance/SourceApplyDryRunReceiptBoundary.ts',
    'src/features/governance/SourceApplyDryRunReceiptPanel.tsx',
    'src/features/governance/SourceApplyDryRunReceiptPanelRoute.tsx'
  ];
  const forbidden = [
    'ControlledSourceApplyExecutor',
    'SourceApplyExecutor',
    'SourceApplyRunner',
    'ControlledRollbackExecutor',
    'GovernedWorkflowContinuationService',
    'ReleaseApproval',
    'DeploymentApproval',
    'MergeApproval',
    'IHostedService',
    'BackgroundService',
    'Scheduler',
    'AgentDispatch',
    'ModelProvider',
    'ToolInvoker',
    'PromoteMemory',
    'ActivateRetrieval',
    'SqlConnection',
    'IDbConnection',
    'Dapper',
    'HttpClient',
    'fetch(',
    'axios',
    'post(',
    'git commit',
    'git push',
    'gh pr',
    'approveSourceApply(',
    'executeDryRun(',
    'executeSourceApply(',
    'executeRollback(',
    'continueWorkflow(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    '"Approve Source Apply"',
    '"Run Dry-run"',
    '"Apply Source"',
    '"Execute Rollback"',
    '"Continue Workflow"',
    '"Approve Release"',
    '"Approve Deployment"',
    '"Approve Merge"',
    '"Refresh Authority"',
    '"Reissue Evidence"'
  ];

  for (const file of files) {
    const content = readFileSync(join(process.cwd(), file), 'utf8');
    for (const marker of forbidden) {
      expect(content, `${file} should not contain ${marker}`).not.toContain(marker);
    }
  }
});

test('SourceApplyDryRunReceiptPanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR231_DRY_RUN_RECEIPT_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR231 shows dry-run receipt evidence. It does not apply source.');
});

async function expectNoButton(page: Page, label: string) {
  await openDryRunReceiptPanel(page, 'current');
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('dry-run-receipt.boundaryBanner')).toContainText('Source apply dry-run receipt evidence is display only.');
  await expect(page.getByTestId('dry-run-receipt.boundaryBanner')).toContainText('Human review remains required.');
}

async function openDryRunReceiptPanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/source-apply-dry-run-receipts?fixture=${fixture}`);
  await expect(page.getByTestId('dry-run-receipt.workspace')).toBeVisible();
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
