import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('SourceApplyReviewPanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Source Apply Review Evidence' })).toBeVisible();
  await expect(page.getByTestId('source-apply-review.identity')).toContainText('source-apply-review-233');
  await expect(page.getByTestId('source-apply-review.identity')).toContainText('sha256:source-apply-review-hash-233');
  await expect(page.getByTestId('source-apply-review.requestBinding')).toContainText('source-apply-request-233');
  await expect(page.getByTestId('source-apply-review.patchBinding')).toContainText('patch-artifact-233');
  await expect(page.getByTestId('source-apply-review.dryRunBinding')).toContainText('dry-run-receipt-233');
  await expect(page.getByTestId('source-apply-review.subjectBinding')).toContainText('workflow-run-233');
  await expect(page.getByTestId('source-apply-review.plannedFiles')).toContainText('src/apply/Widget.cs');
});

test('SourceApplyReviewPanel_renders_missing_review_id_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-review-id');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('reviewId');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_missing_review_hash_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-review-hash');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('reviewHash');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_missing_source_apply_request_binding_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-request');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('sourceApplyRequestId');
  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('sourceApplyRequestHash');
  await expect(page.getByTestId('source-apply-review.state')).toContainText('Supplied true');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_missing_patch_artifact_binding_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-patch-artifact');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('patchArtifactId');
  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('patchArtifactHash');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_missing_dry_run_receipt_binding_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-dry-run-receipt');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('dryRunReceiptId');
  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('dryRunReceiptHash');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-subject');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('subjectHash');
});

test('SourceApplyReviewPanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-workflow');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('workflowStepId');
});

test('SourceApplyReviewPanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'empty-refs');

  await expect(page.getByTestId('source-apply-review.noEvidenceRefs')).toContainText('Missing evidence does not permit source apply.');
  await expect(page.getByTestId('source-apply-review.missingEvidenceWarning')).toContainText('cannot permit source apply');
  await expect(page.getByTestId('source-apply-review.state')).toContainText('Supplied false');
});

test('SourceApplyReviewPanel_renders_missing_boundary_maxims_as_incomplete', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing-boundary');

  await expect(page.getByTestId('source-apply-review.incompleteWarning')).toContainText('boundaryMaxims');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading source-apply review evidence...' })).toBeVisible();
  await expect(page.getByTestId('source-apply-review.loading')).toContainText('UI loading does not approve or apply source.');
  await expectBoundaryWarning(page);
});

test('SourceApplyReviewPanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load source-apply review evidence.' })).toBeVisible();
  await expect(page.getByTestId('source-apply-review.error')).toContainText('No approval, dry-run, source mutation, rollback, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('SourceApplyReviewPanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No source-apply review evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('source-apply-review.empty')).toContainText('Missing review evidence does not permit source apply.');
  await expectBoundaryWarning(page);
});

test('SourceApplyReviewPanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  await expect(page.getByTestId('source-apply-review.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('source-apply-review.boundaryRules')).toContainText('Source apply review evidence is not source apply.');
  await expect(page.getByTestId('source-apply-review.boundaryRules')).toContainText('UI state is not authority.');
  await expect(page.getByTestId('source-apply-review.boundaryRules')).toContainText('Human review remains required.');
});

test('SourceApplyReviewPanel_display_valid_does_not_grant_source_apply_or_release', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  await expect(page.getByTestId('source-apply-review.statusBanner')).toContainText('Supplied evidence claims review satisfaction');
  await expect(page.locator('body')).not.toContainText('Source apply approved');
  await expect(page.locator('body')).not.toContainText('Release approved');
  await expect(page.locator('body')).not.toContainText('Deployment approved');
  await expect(page.locator('body')).not.toContainText('Merge approved');
});

test('SourceApplyReviewPanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'stale');

  await expect(page.getByTestId('source-apply-review.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'expired');

  await expect(page.getByTestId('source-apply-review.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'unsafe');

  await expect(page.getByTestId('source-apply-review.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('source-apply-review.plannedFiles')).toContainText('[redacted source apply review evidence]');
  await expect(page.getByTestId('source-apply-review.evidenceRefs')).toContainText('[redacted source apply review evidence]');
  await expect(page.locator('body')).not.toContainText('raw patch private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('SourceApplyReviewPanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'authority-claim');

  await expect(page.getByTestId('source-apply-review.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('source apply approved and safe to merge by fixture data');
});

test('SourceApplyReviewPanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'contradictory');

  await expect(page.getByTestId('source-apply-review.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('source-apply-review.currentBadge')).toHaveCount(0);
});

test('SourceApplyReviewPanel_does_not_render_source_apply_or_dry_run_buttons', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  for (const label of ['Approve Source Apply', 'Run Dry-run', 'Apply Source', 'Apply Patch']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyReviewPanel_does_not_render_rollback_workflow_or_release_buttons', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  for (const label of ['Execute Rollback', 'Continue Workflow', 'Approve Release', 'Approve Deployment', 'Approve Merge']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyReviewPanel_does_not_render_authority_or_git_buttons', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  for (const label of ['Refresh Authority', 'Reissue Evidence', 'Run Git', 'Create Pull Request']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyReviewPanel_does_not_render_agent_model_tool_buttons', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');

  for (const label of ['Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('SourceApplyReviewPanel_allows_copy_review_id_for_inspection_only', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Review ID' }).click();

  await expect(page.getByTestId('source-apply-review.copyStatus')).toContainText('Review id copied for inspection only.');
  await expect(page.getByTestId('source-apply-review.copyStatus')).toContainText('does not apply source');
});

test('SourceApplyReviewPanel_allows_copy_hashes_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openSourceApplyReviewPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Review Hash' }).click();
  await expect(page.getByTestId('source-apply-review.copyStatus')).toContainText('Review hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Source Apply Request Hash' }).click();
  await expect(page.getByTestId('source-apply-review.copyStatus')).toContainText('Source apply request hash copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('SourceApplyReviewPanel_allows_copy_evidence_refs_for_inspection_only', async ({ page }) => {
  await openSourceApplyReviewPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('source-apply-review.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('SourceApplyReviewPanel_static_ui_files_do_not_contain_forbidden_dependencies_or_action_labels', async () => {
  const files = [
    'src/features/governance/SourceApplyReviewTypes.ts',
    'src/features/governance/SourceApplyReviewPanel.tsx',
    'src/features/governance/SourceApplyReviewPanelRoute.tsx'
  ];
  const forbidden = [
    'ControlledSourceApplyExecutor',
    'SourceApplyExecutor',
    'SourceApplyRunner',
    'SourceApplyDryRunExecutor',
    'PatchArtifactCreator',
    'PatchArtifactWriter',
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
    'CLI mutation',
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
    '"Apply Patch"',
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

test('SourceApplyReviewPanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR233_SOURCE_APPLY_REVIEW_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR233 reviews source-apply evidence. It does not apply source.');
});

async function expectNoButton(page: Page, label: string) {
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('source-apply-review.boundaryBanner')).toContainText('Source apply review evidence is display only.');
  await expect(page.getByTestId('source-apply-review.boundaryBanner')).toContainText('Human review remains required.');
}

async function openSourceApplyReviewPanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/source-apply-reviews?fixture=${fixture}`);
  await expect(page.getByTestId('source-apply-review.workspace')).toBeVisible();
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

  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}
