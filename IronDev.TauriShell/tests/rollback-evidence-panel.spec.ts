import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('RollbackEvidencePanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Rollback Evidence' })).toBeVisible();
  await expect(page.getByTestId('rollback-evidence.identity')).toContainText('rollback-evidence-234');
  await expect(page.getByTestId('rollback-evidence.identity')).toContainText('sha256:rollback-evidence-hash-234');
  await expect(page.getByTestId('rollback-evidence.sourceApplyBinding')).toContainText('source-apply-receipt-234');
  await expect(page.getByTestId('rollback-evidence.planBinding')).toContainText('rollback-plan-234');
  await expect(page.getByTestId('rollback-evidence.supportBinding')).toContainText('rollback-support-receipt-234');
  await expect(page.getByTestId('rollback-evidence.executionBinding')).toContainText('rollback-execution-receipt-234');
  await expect(page.getByTestId('rollback-evidence.auditBinding')).toContainText('rollback-audit-report-234');
  await expect(page.getByTestId('rollback-evidence.subjectBinding')).toContainText('workflow-run-234');
  await expect(page.getByTestId('rollback-evidence.affectedFiles')).toContainText('src/apply/Widget.cs');
});

test('RollbackEvidencePanel_renders_missing_rollback_evidence_id_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-evidence-id');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('rollbackEvidenceId');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_missing_rollback_evidence_hash_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-evidence-hash');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('rollbackEvidenceHash');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_missing_source_apply_receipt_binding_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-source-apply-receipt');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('sourceApplyReceiptId');
  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('sourceApplyReceiptHash');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_missing_rollback_plan_binding_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-rollback-plan');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('rollbackPlanId');
  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('rollbackPlanHash');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_missing_support_receipt_binding_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-support-receipt');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('rollbackSupportReceiptId');
  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('rollbackSupportReceiptHash');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-subject');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('subjectHash');
});

test('RollbackEvidencePanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-workflow');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('workflowStepId');
});

test('RollbackEvidencePanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'empty-refs');

  await expect(page.getByTestId('rollback-evidence.noEvidenceRefs')).toContainText('Missing evidence does not permit rollback execution.');
  await expect(page.getByTestId('rollback-evidence.missingEvidenceWarning')).toContainText('cannot permit rollback execution');
  await expect(page.getByTestId('rollback-evidence.state')).toContainText('Supplied false');
});

test('RollbackEvidencePanel_renders_missing_boundary_maxims_as_incomplete', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing-boundary');

  await expect(page.getByTestId('rollback-evidence.incompleteWarning')).toContainText('boundaryMaxims');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading rollback evidence...' })).toBeVisible();
  await expect(page.getByTestId('rollback-evidence.loading')).toContainText('UI loading does not approve rollback, execute rollback, or continue workflow.');
  await expectBoundaryWarning(page);
});

test('RollbackEvidencePanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load rollback evidence.' })).toBeVisible();
  await expect(page.getByTestId('rollback-evidence.error')).toContainText('No approval, rollback, source mutation, recovery, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('RollbackEvidencePanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No rollback evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('rollback-evidence.empty')).toContainText('Missing rollback evidence does not permit rollback execution, retry, recovery, or workflow continuation.');
  await expectBoundaryWarning(page);
});

test('RollbackEvidencePanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  await expect(page.getByTestId('rollback-evidence.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('rollback-evidence.boundaryRules')).toContainText('Rollback evidence is not rollback approval.');
  await expect(page.getByTestId('rollback-evidence.boundaryRules')).toContainText('Rollback UI is not rollback execution.');
  await expect(page.getByTestId('rollback-evidence.boundaryRules')).toContainText('UI state is not authority.');
});

test('RollbackEvidencePanel_display_valid_does_not_grant_rollback_or_release', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  await expect(page.getByTestId('rollback-evidence.statusBanner')).toContainText('Supplied evidence claims rollback satisfaction');
  await expect(page.locator('body')).not.toContainText('Rollback approved');
  await expect(page.locator('body')).not.toContainText('Release approved');
  await expect(page.locator('body')).not.toContainText('Deployment approved');
  await expect(page.locator('body')).not.toContainText('Merge approved');
});

test('RollbackEvidencePanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'stale');

  await expect(page.getByTestId('rollback-evidence.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'expired');

  await expect(page.getByTestId('rollback-evidence.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_renders_partial_warning_without_retry_or_recovery', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'partial');

  await expect(page.getByTestId('rollback-evidence.partialWarning')).toContainText('partial');
  await expect(page.getByTestId('rollback-evidence.partialWarning')).toContainText('will not retry rollback');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(1);
  await expectNoButton(page, 'Retry Rollback');
  await expectNoButton(page, 'Start Recovery');
});

test('RollbackEvidencePanel_renders_failure_warning_without_retry_recovery_or_continuation', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'failed');

  await expect(page.getByTestId('rollback-evidence.failureWarning')).toContainText('failed');
  await expect(page.getByTestId('rollback-evidence.failureWarning')).toContainText('will not start recovery');
  await expectNoButton(page, 'Retry Rollback');
  await expectNoButton(page, 'Continue Workflow');
});

test('RollbackEvidencePanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'unsafe');

  await expect(page.getByTestId('rollback-evidence.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('rollback-evidence.affectedFiles')).toContainText('[redacted rollback evidence]');
  await expect(page.getByTestId('rollback-evidence.evidenceRefs')).toContainText('[redacted rollback evidence]');
  await expect(page.locator('body')).not.toContainText('raw patch private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('RollbackEvidencePanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'authority-claim');

  await expect(page.getByTestId('rollback-evidence.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('rollback approved and safe to release by fixture data');
});

test('RollbackEvidencePanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'contradictory');

  await expect(page.getByTestId('rollback-evidence.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('rollback-evidence.currentBadge')).toHaveCount(0);
});

test('RollbackEvidencePanel_does_not_render_rollback_execution_or_recovery_buttons', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  for (const label of ['Approve Rollback', 'Execute Rollback', 'Retry Rollback', 'Start Recovery']) {
    await expectNoButton(page, label);
  }
});

test('RollbackEvidencePanel_does_not_render_source_apply_or_dry_run_buttons', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  for (const label of ['Approve Source Apply', 'Run Dry-run', 'Apply Source', 'Apply Patch']) {
    await expectNoButton(page, label);
  }
});

test('RollbackEvidencePanel_does_not_render_workflow_release_or_authority_buttons', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  for (const label of ['Continue Workflow', 'Approve Release', 'Approve Deployment', 'Approve Merge', 'Refresh Authority', 'Reissue Evidence']) {
    await expectNoButton(page, label);
  }
});

test('RollbackEvidencePanel_does_not_render_git_agent_model_or_tool_buttons', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');

  for (const label of ['Run Git', 'Create Pull Request', 'Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('RollbackEvidencePanel_allows_copy_evidence_id_for_inspection_only', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Rollback Evidence ID' }).click();

  await expect(page.getByTestId('rollback-evidence.copyStatus')).toContainText('Rollback evidence id copied for inspection only.');
  await expect(page.getByTestId('rollback-evidence.copyStatus')).toContainText('does not execute rollback');
});

test('RollbackEvidencePanel_allows_copy_hashes_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openRollbackEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Rollback Evidence Hash' }).click();
  await expect(page.getByTestId('rollback-evidence.copyStatus')).toContainText('Rollback evidence hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Rollback Plan Hash' }).click();
  await expect(page.getByTestId('rollback-evidence.copyStatus')).toContainText('Rollback plan hash copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('RollbackEvidencePanel_allows_copy_evidence_refs_for_inspection_only', async ({ page }) => {
  await openRollbackEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('rollback-evidence.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('RollbackEvidencePanel_static_ui_files_do_not_contain_forbidden_dependencies_or_action_labels', async () => {
  const files = [
    'src/features/governance/RollbackEvidenceTypes.ts',
    'src/features/governance/RollbackEvidencePanel.tsx',
    'src/features/governance/RollbackEvidencePanelRoute.tsx'
  ];
  const forbidden = [
    'ControlledRollbackExecutor',
    'RollbackExecutor',
    'RollbackRunner',
    'RollbackRecoveryRunner',
    'RollbackAuditExecutor',
    'ControlledSourceApplyExecutor',
    'SourceApplyExecutor',
    'SourceApplyRunner',
    'SourceApplyDryRunExecutor',
    'PatchArtifactCreator',
    'PatchArtifactWriter',
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
    'approveRollback(',
    'executeRollback(',
    'retryRollback(',
    'startRecovery(',
    'approveSourceApply(',
    'executeDryRun(',
    'executeSourceApply(',
    'continueWorkflow(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    '"Approve Rollback"',
    '"Execute Rollback"',
    '"Retry Rollback"',
    '"Start Recovery"',
    '"Approve Source Apply"',
    '"Run Dry-run"',
    '"Apply Source"',
    '"Apply Patch"',
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

test('RollbackEvidencePanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR234_ROLLBACK_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR234 shows rollback evidence. It does not execute rollback.');
});

async function expectNoButton(page: Page, label: string) {
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('rollback-evidence.boundaryBanner')).toContainText('Rollback evidence is display only.');
  await expect(page.getByTestId('rollback-evidence.boundaryBanner')).toContainText('Human review remains required.');
}

async function openRollbackEvidencePanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/rollback-evidence?fixture=${fixture}`);
  await expect(page.getByTestId('rollback-evidence.workspace')).toBeVisible();
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
