import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('WorkflowContinuationEvidencePanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Workflow Continuation Evidence' })).toBeVisible();
  await expect(page.getByTestId('workflow-continuation-evidence.identity')).toContainText('workflow-continuation-evidence-235');
  await expect(page.getByTestId('workflow-continuation-evidence.identity')).toContainText('sha256:workflow-continuation-evidence-hash-235');
  await expect(page.getByTestId('workflow-continuation-evidence.gateBinding')).toContainText('continuation-gate-evaluation-235');
  await expect(page.getByTestId('workflow-continuation-evidence.transitionBinding')).toContainText('workflow-transition-record-235');
  await expect(page.getByTestId('workflow-continuation-evidence.sourceApplyBinding')).toContainText('source-apply-receipt-235');
  await expect(page.getByTestId('workflow-continuation-evidence.rollbackBinding')).toContainText('rollback-execution-receipt-235');
  await expect(page.getByTestId('workflow-continuation-evidence.subjectBinding')).toContainText('workflow-run-235');
  await expect(page.getByTestId('workflow-continuation-evidence.steps')).toContainText('Apply review');
});

test('WorkflowContinuationEvidencePanel_renders_missing_evidence_id_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing-evidence-id');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('workflowContinuationEvidenceId');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_missing_evidence_hash_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing-evidence-hash');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('workflowContinuationEvidenceHash');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_missing_gate_binding_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing-gate');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('continuationGateEvaluationId');
  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('continuationGateEvaluationHash');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing-subject');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('subjectHash');
});

test('WorkflowContinuationEvidencePanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing-workflow');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('workflowStepId');
});

test('WorkflowContinuationEvidencePanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'empty-refs');

  await expect(page.getByTestId('workflow-continuation-evidence.noEvidenceRefs')).toContainText('Missing evidence does not permit workflow continuation.');
  await expect(page.getByTestId('workflow-continuation-evidence.missingEvidenceWarning')).toContainText('cannot permit workflow continuation');
  await expect(page.getByTestId('workflow-continuation-evidence.state')).toContainText('Supplied false');
});

test('WorkflowContinuationEvidencePanel_renders_missing_boundary_maxims_as_incomplete', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing-boundary');

  await expect(page.getByTestId('workflow-continuation-evidence.incompleteWarning')).toContainText('boundaryMaxims');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading workflow continuation evidence...' })).toBeVisible();
  await expect(page.getByTestId('workflow-continuation-evidence.loading')).toContainText('UI loading does not approve continuation, continue workflow, or create transition records.');
  await expectBoundaryWarning(page);
});

test('WorkflowContinuationEvidencePanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load workflow continuation evidence.' })).toBeVisible();
  await expect(page.getByTestId('workflow-continuation-evidence.error')).toContainText('No approval, workflow transition, source mutation, recovery, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('WorkflowContinuationEvidencePanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No workflow continuation evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('workflow-continuation-evidence.empty')).toContainText('Missing workflow continuation evidence does not permit workflow continuation.');
  await expectBoundaryWarning(page);
});

test('WorkflowContinuationEvidencePanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  await expect(page.getByTestId('workflow-continuation-evidence.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('workflow-continuation-evidence.boundaryRules')).toContainText('Workflow continuation evidence is not continuation approval.');
  await expect(page.getByTestId('workflow-continuation-evidence.boundaryRules')).toContainText('Workflow continuation UI is not workflow continuation.');
  await expect(page.getByTestId('workflow-continuation-evidence.boundaryRules')).toContainText('UI state is not authority.');
});

test('WorkflowContinuationEvidencePanel_display_valid_does_not_grant_continuation_or_release', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  await expect(page.getByTestId('workflow-continuation-evidence.statusBanner')).toContainText('Supplied evidence claims continuation satisfaction');
  await expect(page.locator('body')).not.toContainText('Continuation approved');
  await expect(page.locator('body')).not.toContainText('Release approved');
  await expect(page.locator('body')).not.toContainText('Deployment approved');
  await expect(page.locator('body')).not.toContainText('Merge approved');
});

test('WorkflowContinuationEvidencePanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'stale');

  await expect(page.getByTestId('workflow-continuation-evidence.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'expired');

  await expect(page.getByTestId('workflow-continuation-evidence.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_renders_partial_warning_without_retry_or_recovery', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'partial');

  await expect(page.getByTestId('workflow-continuation-evidence.partialWarning')).toContainText('partial');
  await expect(page.getByTestId('workflow-continuation-evidence.partialWarning')).toContainText('will not retry continuation');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
  await expectNoButton(page, 'Retry Continuation');
  await expectNoButton(page, 'Start Recovery');
});

test('WorkflowContinuationEvidencePanel_renders_failure_warning_without_retry_recovery_or_continuation', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'failed');

  await expect(page.getByTestId('workflow-continuation-evidence.failureWarning')).toContainText('failed');
  await expect(page.getByTestId('workflow-continuation-evidence.failureWarning')).toContainText('will not start recovery');
  await expectNoButton(page, 'Retry Continuation');
  await expectNoButton(page, 'Continue Workflow');
});

test('WorkflowContinuationEvidencePanel_renders_mutation_detected_warning_without_trusting_it', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'mutation-detected');

  await expect(page.getByTestId('workflow-continuation-evidence.mutationWarning')).toContainText('Workflow mutation was detected');
  await expect(page.getByTestId('workflow-continuation-evidence.mutationWarning')).toContainText('will not normalize it into approval');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'unsafe');

  await expect(page.getByTestId('workflow-continuation-evidence.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('workflow-continuation-evidence.steps')).toContainText('[redacted workflow continuation evidence]');
  await expect(page.getByTestId('workflow-continuation-evidence.evidenceRefs')).toContainText('[redacted workflow continuation evidence]');
  await expect(page.locator('body')).not.toContainText('raw prompt private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('WorkflowContinuationEvidencePanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'authority-claim');

  await expect(page.getByTestId('workflow-continuation-evidence.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('workflow continuation approved and safe to release by fixture data');
});

test('WorkflowContinuationEvidencePanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'contradictory');

  await expect(page.getByTestId('workflow-continuation-evidence.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('workflow-continuation-evidence.currentBadge')).toHaveCount(0);
});

test('WorkflowContinuationEvidencePanel_does_not_render_continuation_or_transition_buttons', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  for (const label of ['Approve Continuation', 'Continue Workflow', 'Create Transition Record', 'Retry Continuation', 'Start Recovery']) {
    await expectNoButton(page, label);
  }
});

test('WorkflowContinuationEvidencePanel_does_not_render_rollback_source_apply_or_dry_run_buttons', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  for (const label of ['Approve Rollback', 'Execute Rollback', 'Approve Source Apply', 'Run Dry-run', 'Apply Source', 'Apply Patch']) {
    await expectNoButton(page, label);
  }
});

test('WorkflowContinuationEvidencePanel_does_not_render_release_or_authority_buttons', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  for (const label of ['Approve Release', 'Approve Deployment', 'Approve Merge', 'Refresh Authority', 'Reissue Evidence']) {
    await expectNoButton(page, label);
  }
});

test('WorkflowContinuationEvidencePanel_does_not_render_git_agent_model_or_tool_buttons', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');

  for (const label of ['Run Git', 'Create Pull Request', 'Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('WorkflowContinuationEvidencePanel_allows_copy_evidence_id_for_inspection_only', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Workflow Continuation Evidence ID' }).click();

  await expect(page.getByTestId('workflow-continuation-evidence.copyStatus')).toContainText('Workflow continuation evidence id copied for inspection only.');
  await expect(page.getByTestId('workflow-continuation-evidence.copyStatus')).toContainText('does not continue workflow');
});

test('WorkflowContinuationEvidencePanel_allows_copy_hashes_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openWorkflowContinuationEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Workflow Continuation Evidence Hash' }).click();
  await expect(page.getByTestId('workflow-continuation-evidence.copyStatus')).toContainText('Workflow continuation evidence hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Continuation Gate Hash' }).click();
  await expect(page.getByTestId('workflow-continuation-evidence.copyStatus')).toContainText('Continuation gate evaluation hash copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('WorkflowContinuationEvidencePanel_allows_copy_evidence_refs_for_inspection_only', async ({ page }) => {
  await openWorkflowContinuationEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('workflow-continuation-evidence.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('WorkflowContinuationEvidencePanel_static_ui_files_do_not_contain_forbidden_dependencies_or_action_labels', async () => {
  const files = [
    'src/features/governance/WorkflowContinuationEvidenceTypes.ts',
    'src/features/governance/WorkflowContinuationEvidencePanel.tsx',
    'src/features/governance/WorkflowContinuationEvidencePanelRoute.tsx'
  ];
  const forbidden = [
    'GovernedWorkflowContinuationService',
    'WorkflowContinuationRunner',
    'WorkflowContinuationExecutor',
    'WorkflowTransitionRecordStore',
    'WorkflowTransitionStore',
    'CreateWorkflowTransitionRecord',
    'ControlledRollbackExecutor',
    'RollbackExecutor',
    'RollbackRecoveryRunner',
    'ControlledSourceApplyExecutor',
    'SourceApplyExecutor',
    'SourceApplyRunner',
    'SourceApplyDryRunExecutor',
    'PatchArtifactCreator',
    'PatchArtifactWriter',
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
    'approveContinuation(',
    'continueWorkflow(',
    'createTransitionRecord(',
    'retryContinuation(',
    'startRecovery(',
    'approveRollback(',
    'executeRollback(',
    'approveSourceApply(',
    'executeDryRun(',
    'executeSourceApply(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    '"Approve Continuation"',
    '"Continue Workflow"',
    '"Create Transition Record"',
    '"Retry Continuation"',
    '"Start Recovery"',
    '"Approve Rollback"',
    '"Execute Rollback"',
    '"Approve Source Apply"',
    '"Run Dry-run"',
    '"Apply Source"',
    '"Apply Patch"',
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

test('WorkflowContinuationEvidencePanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR235_WORKFLOW_CONTINUATION_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR235 shows workflow continuation evidence. It does not continue workflow.');
});

async function expectNoButton(page: Page, label: string) {
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('workflow-continuation-evidence.boundaryBanner')).toContainText('Workflow continuation evidence is display only.');
  await expect(page.getByTestId('workflow-continuation-evidence.boundaryBanner')).toContainText('Human review remains required.');
}

async function openWorkflowContinuationEvidencePanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/workflow-continuation-evidence?fixture=${fixture}`);
  await expect(page.getByTestId('workflow-continuation-evidence.workspace')).toBeVisible();
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
