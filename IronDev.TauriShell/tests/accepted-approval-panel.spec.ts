import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('AcceptedApprovalPanel_renders_empty_state_without_authority', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'empty');

  await expect(page.getByRole('heading', { name: 'No accepted approval evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('accepted-approvals.empty')).toContainText('No authority is granted by this view.');
  await expectBoundaryWarning(page);
});

test('AcceptedApprovalPanel_renders_loading_state_without_authority', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading accepted approval evidence...' })).toBeVisible();
  await expect(page.getByTestId('accepted-approvals.loading')).toContainText('No approval state changed.');
  await expectBoundaryWarning(page);
});

test('AcceptedApprovalPanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load accepted approval evidence.' })).toBeVisible();
  await expect(page.getByTestId('accepted-approvals.error')).toContainText('No approval state changed.');
  await expect(page.locator('body')).not.toContainText('approval state changed successfully');
});

test('AcceptedApprovalPanel_renders_loaded_evidence_fields', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Accepted Approval Evidence' })).toBeVisible();
  await expect(page.getByTestId('accepted-approvals.summary')).toContainText('accepted-approval-229');
  await expect(page.getByTestId('accepted-approvals.summary')).toContainText('sha256:accepted-approval-hash-229');
  await expect(page.getByTestId('accepted-approvals.binding')).toContainText('source_apply_request');
  await expect(page.getByTestId('accepted-approvals.binding')).toContainText('workflow-run-229');
  await expect(page.getByTestId('accepted-approvals.evidenceRefs')).toContainText('policy-satisfaction-229');
});

test('AcceptedApprovalPanel_always_renders_authority_boundary_warning', async ({ page }) => {
  for (const fixture of ['empty', 'loading', 'error', 'current']) {
    await openAcceptedApprovalPanel(page, fixture);
    await expectBoundaryWarning(page);
  }
});

test('AcceptedApprovalPanel_renders_human_review_required', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');

  await expect(page.getByTestId('accepted-approvals.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('accepted-approvals.summary')).toContainText('Human review required');
});

test('AcceptedApprovalPanel_does_not_render_release_approval_wording', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');

  for (const text of ['Release approved', 'Ready to release', 'Approved for release']) {
    await expect(page.locator('body')).not.toContainText(text);
  }
});

test('AcceptedApprovalPanel_does_not_render_deploy_merge_execute_wording', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');

  for (const text of ['Deployment approved', 'Merge approved', 'Safe to deploy', 'Safe to merge', 'Can deploy', 'Can merge', 'Can execute']) {
    await expect(page.locator('body')).not.toContainText(text);
  }
});

test('AcceptedApprovalPanel_renders_stale_warning', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'stale');

  await expect(page.getByTestId('accepted-approvals.staleWarning')).toContainText('Accepted approval evidence is stale or expired.');
});

test('AcceptedApprovalPanel_renders_expired_warning', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'expired');

  await expect(page.getByTestId('accepted-approvals.expiredWarning')).toContainText('Accepted approval evidence is stale or expired.');
});

test('AcceptedApprovalPanel_stale_evidence_does_not_render_current_badge', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'stale');

  await expect(page.getByTestId('accepted-approvals.currentBadge')).toHaveCount(0);
});

test('AcceptedApprovalPanel_expired_evidence_does_not_render_current_badge', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'expired');

  await expect(page.getByTestId('accepted-approvals.currentBadge')).toHaveCount(0);
});

test('AcceptedApprovalPanel_missing_hash_renders_incomplete_warning', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'missing-hash');

  await expect(page.getByTestId('accepted-approvals.incompleteWarning')).toContainText('acceptedApprovalHash');
  await expect(page.getByTestId('accepted-approvals.currentBadge')).toHaveCount(0);
});

test('AcceptedApprovalPanel_missing_subject_binding_renders_incomplete_warning', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'missing-subject');

  await expect(page.getByTestId('accepted-approvals.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('accepted-approvals.incompleteWarning')).toContainText('subjectHash');
});

test('AcceptedApprovalPanel_missing_workflow_binding_renders_incomplete_warning', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'missing-workflow');

  await expect(page.getByTestId('accepted-approvals.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('accepted-approvals.incompleteWarning')).toContainText('workflowStepId');
});

test('AcceptedApprovalPanel_does_not_render_accept_approval_button', async ({ page }) => {
  await expectNoButton(page, 'Accept Approval');
});

test('AcceptedApprovalPanel_does_not_render_release_deploy_merge_buttons', async ({ page }) => {
  for (const button of ['Approve Release', 'Approve Deployment', 'Approve Merge', 'Release', 'Deploy', 'Merge']) {
    await expectNoButton(page, button);
  }
});

test('AcceptedApprovalPanel_does_not_render_source_apply_rollback_continue_buttons', async ({ page }) => {
  for (const button of ['Execute Source Apply', 'Execute Rollback', 'Continue Workflow', 'Retry Source Apply']) {
    await expectNoButton(page, button);
  }
});

test('AcceptedApprovalPanel_does_not_render_refresh_reissue_buttons', async ({ page }) => {
  for (const button of ['Refresh Authority', 'Reissue Evidence']) {
    await expectNoButton(page, button);
  }
});

test('AcceptedApprovalPanel_does_not_render_agent_model_tool_buttons', async ({ page }) => {
  for (const button of ['Ask Agent', 'Run Agent', 'Run Model', 'Run Tool']) {
    await expectNoButton(page, button);
  }
});

test('AcceptedApprovalPanel_allows_copy_approval_id', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Accepted Approval ID' }).click();

  await expect(page.getByTestId('accepted-approvals.copyStatus')).toContainText('Accepted approval id copied for inspection only.');
});

test('AcceptedApprovalPanel_allows_copy_approval_hash', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Accepted Approval Hash' }).click();

  await expect(page.getByTestId('accepted-approvals.copyStatus')).toContainText('Accepted approval hash copied for inspection only.');
});

test('AcceptedApprovalPanel_allows_copy_evidence_references', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('accepted-approvals.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('AcceptedApprovalPanel_copy_actions_do_not_call_mutation_apis', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });
  await openAcceptedApprovalPanel(page, 'current');

  await page.getByRole('button', { name: 'Copy Accepted Approval ID' }).click();
  await page.getByRole('button', { name: 'Copy Accepted Approval Hash' }).click();
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('AcceptedApprovalPanel_redacts_unsafe_reference_text', async ({ page }) => {
  await openAcceptedApprovalPanel(page, 'unsafe');

  await expect(page.getByTestId('accepted-approvals.evidenceRefs')).toContainText('[redacted accepted approval evidence]');
  await expect(page.locator('body')).not.toContainText('private reasoning raw prompt should redact');
  await expect(page.locator('body')).not.toContainText('hidden reasoning should redact');
});

test('AcceptedApprovalPanel_static_ui_files_do_not_contain_forbidden_actions_or_labels', async () => {
  const files = [
    'src/features/governance/AcceptedApprovalTypes.ts',
    'src/features/governance/AcceptedApprovalBoundary.ts',
    'src/features/governance/AcceptedApprovalPanel.tsx',
    'src/features/governance/AcceptedApprovalPanelRoute.tsx'
  ];
  const forbidden = [
    'acceptApproval(',
    'approveRelease(',
    'approveDeployment(',
    'approveMerge(',
    'executeRelease(',
    'executeSourceApply(',
    'executeRollback(',
    'continueWorkflow(',
    'mutateWorkflow(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    'promoteMemory(',
    'activateRetrieval(',
    'git push',
    'gh pr',
    '"Approve release"',
    '"Approve deployment"',
    '"Approve merge"',
    '"Execute release"',
    '"Execute source apply"',
    '"Execute rollback"',
    '"Continue workflow"',
    '"Refresh authority"',
    '"Reissue evidence"',
    '"Run agent"',
    '"Run model"',
    '"Run tool"'
  ];

  for (const file of files) {
    const content = readFileSync(join(process.cwd(), file), 'utf8');
    for (const marker of forbidden) {
      expect(content, `${file} should not contain ${marker}`).not.toContain(marker);
    }
  }
});

async function expectNoButton(page: Page, label: string) {
  await openAcceptedApprovalPanel(page, 'current');
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('accepted-approvals.boundaryBanner')).toContainText('Accepted approval evidence is not release approval.');
  await expect(page.getByTestId('accepted-approvals.boundaryBanner')).toContainText('Human review remains required.');
}

async function openAcceptedApprovalPanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/accepted-approvals?fixture=${fixture}`);
  await expect(page.getByTestId('accepted-approvals.workspace')).toBeVisible();
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
