import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('ReleaseReadinessEvidencePanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Release Readiness Evidence' })).toBeVisible();
  await expect(page.getByTestId('release-readiness-evidence.identity')).toContainText('release-readiness-evidence-236');
  await expect(page.getByTestId('release-readiness-evidence.identity')).toContainText('sha256:release-readiness-evidence-hash-236');
  await expect(page.getByTestId('release-readiness-evidence.reportBinding')).toContainText('release-readiness-report-236');
  await expect(page.getByTestId('release-readiness-evidence.approvalBinding')).toContainText('accepted-approval-236');
  await expect(page.getByTestId('release-readiness-evidence.policyBinding')).toContainText('policy-satisfaction-236');
  await expect(page.getByTestId('release-readiness-evidence.sourceApplyBinding')).toContainText('source-apply-review-236');
  await expect(page.getByTestId('release-readiness-evidence.workflowContinuationBinding')).toContainText('workflow-continuation-evidence-236');
  await expect(page.getByTestId('release-readiness-evidence.subjectBinding')).toContainText('release-readiness-report-236');
});

test('ReleaseReadinessEvidencePanel_renders_missing_evidence_id_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-evidence-id');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('releaseReadinessEvidenceId');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_renders_missing_evidence_hash_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-evidence-hash');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('releaseReadinessEvidenceHash');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_renders_missing_report_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-report');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('releaseReadinessReportId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('releaseReadinessReportHash');
});

test('ReleaseReadinessEvidencePanel_renders_missing_accepted_approval_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-accepted-approval');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('acceptedApprovalId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('acceptedApprovalHash');
});

test('ReleaseReadinessEvidencePanel_renders_missing_policy_satisfaction_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-policy');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('policySatisfactionId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('policySatisfactionHash');
});

test('ReleaseReadinessEvidencePanel_renders_missing_source_apply_review_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-source-apply-review');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('sourceApplyReviewId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('sourceApplyReviewHash');
});

test('ReleaseReadinessEvidencePanel_renders_missing_workflow_continuation_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-workflow-continuation');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('workflowContinuationEvidenceId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('workflowContinuationEvidenceHash');
});

test('ReleaseReadinessEvidencePanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-subject');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('subjectHash');
});

test('ReleaseReadinessEvidencePanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-workflow');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('workflowStepId');
});

test('ReleaseReadinessEvidencePanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'empty-refs');

  await expect(page.getByTestId('release-readiness-evidence.noEvidenceRefs')).toContainText('Missing evidence does not approve release.');
  await expect(page.getByTestId('release-readiness-evidence.missingEvidenceWarning')).toContainText('cannot approve release');
  await expect(page.getByTestId('release-readiness-evidence.state')).toContainText('Supplied false');
});

test('ReleaseReadinessEvidencePanel_renders_missing_boundary_maxims_as_incomplete', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing-boundary');

  await expect(page.getByTestId('release-readiness-evidence.incompleteWarning')).toContainText('boundaryMaxims');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading release readiness evidence...' })).toBeVisible();
  await expect(page.getByTestId('release-readiness-evidence.loading')).toContainText('UI loading does not decide readiness, approve release, or execute release.');
  await expectBoundaryWarning(page);
});

test('ReleaseReadinessEvidencePanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load release readiness evidence.' })).toBeVisible();
  await expect(page.getByTestId('release-readiness-evidence.error')).toContainText('No release approval, deployment approval, merge approval, execution, recovery, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('ReleaseReadinessEvidencePanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No release readiness evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('release-readiness-evidence.empty')).toContainText('Missing release readiness evidence does not approve release');
  await expectBoundaryWarning(page);
});

test('ReleaseReadinessEvidencePanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');

  await expect(page.getByTestId('release-readiness-evidence.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('release-readiness-evidence.boundaryRules')).toContainText('Release readiness evidence is not release approval.');
  await expect(page.getByTestId('release-readiness-evidence.boundaryRules')).toContainText('Release readiness display is not release execution.');
  await expect(page.getByTestId('release-readiness-evidence.boundaryRules')).toContainText('UI state is not authority.');
});

test('ReleaseReadinessEvidencePanel_display_ready_claim_does_not_grant_release_deploy_or_merge', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');

  await expect(page.getByTestId('release-readiness-evidence.readyClaim')).toContainText('evidence only');
  await expect(page.locator('body')).not.toContainText('Release approved');
  await expect(page.locator('body')).not.toContainText('Deployment approved');
  await expect(page.locator('body')).not.toContainText('Merge approved');
});

test('ReleaseReadinessEvidencePanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'stale');

  await expect(page.getByTestId('release-readiness-evidence.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'expired');

  await expect(page.getByTestId('release-readiness-evidence.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_renders_blocked_warning_without_approval_controls', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'blocked');

  await expect(page.getByTestId('release-readiness-evidence.blockedWarning')).toContainText('blocked');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
  await expectNoButton(page, 'Approve Release');
  await expectNoButton(page, 'Run Release Gate');
});

test('ReleaseReadinessEvidencePanel_renders_failure_warning_without_retry_or_release_controls', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'failed');

  await expect(page.getByTestId('release-readiness-evidence.failureWarning')).toContainText('failed');
  await expect(page.getByTestId('release-readiness-evidence.failureWarning')).toContainText('will not retry, recover, approve, deploy, merge, execute, or continue workflow');
  await expectNoButton(page, 'Approve Release');
  await expectNoButton(page, 'Execute Release');
});

test('ReleaseReadinessEvidencePanel_renders_partial_warning_without_recovery_controls', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'partial');

  await expect(page.getByTestId('release-readiness-evidence.partialWarning')).toContainText('partial');
  await expect(page.getByTestId('release-readiness-evidence.partialWarning')).toContainText('will not normalize partial evidence into release readiness');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
  await expectNoButton(page, 'Start Recovery');
});

test('ReleaseReadinessEvidencePanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'unsafe');

  await expect(page.getByTestId('release-readiness-evidence.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('release-readiness-evidence.findings')).toContainText('[redacted release readiness evidence]');
  await expect(page.getByTestId('release-readiness-evidence.evidenceRefs')).toContainText('[redacted release readiness evidence]');
  await expect(page.locator('body')).not.toContainText('raw prompt private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('ReleaseReadinessEvidencePanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'authority-claim');

  await expect(page.getByTestId('release-readiness-evidence.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('release approved and safe to deploy by fixture data');
});

test('ReleaseReadinessEvidencePanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'contradictory');

  await expect(page.getByTestId('release-readiness-evidence.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('release-readiness-evidence.currentBadge')).toHaveCount(0);
});

test('ReleaseReadinessEvidencePanel_does_not_render_release_or_deployment_buttons', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');

  for (const label of ['Approve Release', 'Approve Deployment', 'Approve Merge', 'Execute Release', 'Mark Release Ready', 'Create Release Decision', 'Run Release Gate']) {
    await expectNoButton(page, label);
  }
});

test('ReleaseReadinessEvidencePanel_does_not_render_source_apply_rollback_or_continuation_buttons', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');

  for (const label of ['Approve Source Apply', 'Run Dry-run', 'Apply Source', 'Apply Patch', 'Approve Rollback', 'Execute Rollback', 'Continue Workflow', 'Create Transition Record']) {
    await expectNoButton(page, label);
  }
});

test('ReleaseReadinessEvidencePanel_does_not_render_authority_git_agent_model_or_tool_buttons', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');

  for (const label of ['Refresh Authority', 'Reissue Evidence', 'Run Git', 'Create Pull Request', 'Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('ReleaseReadinessEvidencePanel_allows_copy_release_readiness_evidence_id_for_inspection_only', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Release Readiness Evidence ID' }).click();

  await expect(page.getByTestId('release-readiness-evidence.copyStatus')).toContainText('Release readiness evidence id copied for inspection only.');
  await expect(page.getByTestId('release-readiness-evidence.copyStatus')).toContainText('does not approve release');
});

test('ReleaseReadinessEvidencePanel_allows_copy_hashes_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openReleaseReadinessEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Release Readiness Evidence Hash' }).click();
  await expect(page.getByTestId('release-readiness-evidence.copyStatus')).toContainText('Release readiness evidence hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Release Readiness Report Hash' }).click();
  await expect(page.getByTestId('release-readiness-evidence.copyStatus')).toContainText('Release readiness report hash copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('ReleaseReadinessEvidencePanel_allows_copy_evidence_refs_for_inspection_only', async ({ page }) => {
  await openReleaseReadinessEvidencePanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('release-readiness-evidence.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('ReleaseReadinessEvidencePanel_static_ui_files_do_not_contain_forbidden_dependencies_or_action_labels', async () => {
  const files = [
    'src/features/governance/ReleaseReadinessEvidenceTypes.ts',
    'src/features/governance/ReleaseReadinessEvidencePanel.tsx',
    'src/features/governance/ReleaseReadinessEvidencePanelRoute.tsx'
  ];
  const forbidden = [
    'ReleaseReadinessGateEvaluator',
    'ReleaseReadinessDecisionRecordStore',
    'ReleaseReadinessDecisionWriter',
    'GovernedReleaseGateService',
    'ReleaseApproval',
    'DeploymentApproval',
    'MergeApproval',
    'ReleaseExecutor',
    'GovernedWorkflowContinuationService',
    'WorkflowContinuationRunner',
    'WorkflowTransitionRecordStore',
    'ControlledRollbackExecutor',
    'RollbackExecutor',
    'RollbackRecoveryRunner',
    'ControlledSourceApplyExecutor',
    'SourceApplyExecutor',
    'SourceApplyRunner',
    'SourceApplyDryRunExecutor',
    'PatchArtifactCreator',
    'PatchArtifactWriter',
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
    'approveRelease(',
    'approveDeployment(',
    'approveMerge(',
    'executeRelease(',
    'markReleaseReady(',
    'createReleaseDecision(',
    'runReleaseGate(',
    'approveSourceApply(',
    'executeDryRun(',
    'executeSourceApply(',
    'executeRollback(',
    'continueWorkflow(',
    'createTransitionRecord(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    '"Approve Release"',
    '"Approve Deployment"',
    '"Approve Merge"',
    '"Execute Release"',
    '"Mark Release Ready"',
    '"Create Release Decision"',
    '"Run Release Gate"',
    '"Approve Source Apply"',
    '"Run Dry-run"',
    '"Apply Source"',
    '"Apply Patch"',
    '"Approve Rollback"',
    '"Execute Rollback"',
    '"Continue Workflow"',
    '"Create Transition Record"',
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

test('ReleaseReadinessEvidencePanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR236_RELEASE_READINESS_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR236 shows release readiness evidence. It does not approve release.');
});

async function expectNoButton(page: Page, label: string) {
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('release-readiness-evidence.boundaryBanner')).toContainText('Release readiness evidence is display only.');
  await expect(page.getByTestId('release-readiness-evidence.boundaryBanner')).toContainText('Human review remains required.');
}

async function openReleaseReadinessEvidencePanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/release-readiness-evidence?fixture=${fixture}`);
  await expect(page.getByTestId('release-readiness-evidence.workspace')).toBeVisible();
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
