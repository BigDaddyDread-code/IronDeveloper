import { expect, test, type Page, type Route } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

test('PatchArtifactPanel_renders_complete_supplied_evidence', async ({ page }) => {
  await openPatchArtifactPanel(page, 'current');

  await expect(page.getByRole('heading', { name: 'Patch Artifact Evidence' })).toBeVisible();
  await expect(page.getByTestId('patch-artifact.identity')).toContainText('patch-artifact-232');
  await expect(page.getByTestId('patch-artifact.identity')).toContainText('sha256:patch-artifact-hash-232');
  await expect(page.getByTestId('patch-artifact.sourceBinding')).toContainText('implementation-proposal-232');
  await expect(page.getByTestId('patch-artifact.subjectBinding')).toContainText('source-apply-request-232');
  await expect(page.getByTestId('patch-artifact.subjectBinding')).toContainText('workflow-run-232');
  await expect(page.getByTestId('patch-artifact.files')).toContainText('src/apply/Widget.cs');
});

test('PatchArtifactPanel_renders_missing_patch_artifact_id_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing-artifact-id');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('patchArtifactId');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_renders_missing_patch_artifact_hash_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing-artifact-hash');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('patchArtifactHash');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_renders_missing_source_binding_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing-source');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('sourceKind');
  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('sourceId');
  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('sourceHash');
});

test('PatchArtifactPanel_renders_missing_subject_binding_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing-subject');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('subjectKind');
  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('subjectId');
  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('subjectHash');
});

test('PatchArtifactPanel_renders_missing_workflow_binding_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing-workflow');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('workflowRunId');
  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('workflowStepId');
});

test('PatchArtifactPanel_renders_invalid_timestamp_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'invalid-timestamp');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('invalid timestamp');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_renders_empty_evidence_refs_as_missing_evidence', async ({ page }) => {
  await openPatchArtifactPanel(page, 'empty-refs');

  await expect(page.getByTestId('patch-artifact.noEvidenceRefs')).toContainText('Missing evidence does not permit dry-run or source apply.');
  await expect(page.getByTestId('patch-artifact.missingEvidenceWarning')).toContainText('cannot permit dry-run or source apply');
  await expect(page.getByTestId('patch-artifact.state')).toContainText('Supplied false');
});

test('PatchArtifactPanel_renders_missing_boundary_maxims_as_incomplete', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing-boundary');

  await expect(page.getByTestId('patch-artifact.incompleteWarning')).toContainText('boundaryMaxims');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_renders_loading_state_with_boundary', async ({ page }) => {
  await openPatchArtifactPanel(page, 'loading');

  await expect(page.getByRole('heading', { name: 'Loading patch artifact evidence...' })).toBeVisible();
  await expect(page.getByTestId('patch-artifact.loading')).toContainText('UI loading does not create patch artifacts, run dry-run, or apply source.');
  await expectBoundaryWarning(page);
});

test('PatchArtifactPanel_renders_error_state_without_mutation_language', async ({ page }) => {
  await openPatchArtifactPanel(page, 'error');

  await expect(page.getByRole('heading', { name: 'Unable to load patch artifact evidence.' })).toBeVisible();
  await expect(page.getByTestId('patch-artifact.error')).toContainText('No patch artifact, dry-run, approval, source mutation, rollback, or workflow state changed.');
  await expectBoundaryWarning(page);
});

test('PatchArtifactPanel_renders_missing_evidence_without_authority', async ({ page }) => {
  await openPatchArtifactPanel(page, 'missing');

  await expect(page.getByRole('heading', { name: 'No patch artifact evidence selected.' })).toBeVisible();
  await expect(page.getByTestId('patch-artifact.empty')).toContainText('Missing patch artifact evidence does not permit dry-run or source apply.');
  await expectBoundaryWarning(page);
});

test('PatchArtifactPanel_happy_path_keeps_human_review_and_boundaries_visible', async ({ page }) => {
  await openPatchArtifactPanel(page, 'current');

  await expect(page.getByTestId('patch-artifact.statusBanner')).toContainText('Human review required');
  await expect(page.getByTestId('patch-artifact.boundaryRules')).toContainText('Patch artifact evidence is not source apply.');
  await expect(page.getByTestId('patch-artifact.boundaryRules')).toContainText('UI state is not authority.');
});

test('PatchArtifactPanel_display_valid_does_not_grant_patch_dry_run_source_apply_or_release', async ({ page }) => {
  await openPatchArtifactPanel(page, 'current');

  await expect(page.getByTestId('patch-artifact.statusBanner')).toContainText('Supplied evidence claims artifact satisfaction');
  await expect(page.locator('body')).not.toContainText('Patch approved');
  await expect(page.locator('body')).not.toContainText('Dry-run approved');
  await expect(page.locator('body')).not.toContainText('Source apply approved');
  await expect(page.locator('body')).not.toContainText('Ready to release');
});

test('PatchArtifactPanel_renders_stale_warning_without_current_badge', async ({ page }) => {
  await openPatchArtifactPanel(page, 'stale');

  await expect(page.getByTestId('patch-artifact.staleWarning')).toContainText('stale');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_renders_expired_warning_without_current_badge', async ({ page }) => {
  await openPatchArtifactPanel(page, 'expired');

  await expect(page.getByTestId('patch-artifact.expiredWarning')).toContainText('expired');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_shows_raw_patch_payload_present_without_rendering_payload', async ({ page }) => {
  await openPatchArtifactPanel(page, 'raw-patch');

  await expect(page.getByTestId('patch-artifact.rawPatchWarning')).toContainText('intentionally not rendered');
  await expect(page.locator('body')).not.toContainText('@@');
  await expect(page.locator('body')).not.toContainText('diff --git');
});

test('PatchArtifactPanel_redacts_unsafe_private_raw_material', async ({ page }) => {
  await openPatchArtifactPanel(page, 'unsafe');

  await expect(page.getByTestId('patch-artifact.unsafeWarning')).toContainText('Unsafe or private material was detected');
  await expect(page.getByTestId('patch-artifact.files')).toContainText('[redacted patch artifact evidence]');
  await expect(page.getByTestId('patch-artifact.evidenceRefs')).toContainText('[redacted patch artifact evidence]');
  await expect(page.locator('body')).not.toContainText('raw patch private reasoning should redact');
  await expect(page.locator('body')).not.toContainText('secret bearer token should redact');
});

test('PatchArtifactPanel_treats_authority_claims_as_warnings', async ({ page }) => {
  await openPatchArtifactPanel(page, 'authority-claim');

  await expect(page.getByTestId('patch-artifact.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.locator('body')).toContainText('[authority claim redacted]');
  await expect(page.locator('body')).not.toContainText('patch approved and safe to merge by fixture data');
});

test('PatchArtifactPanel_rejects_contradictory_authority_flags_from_current_badge', async ({ page }) => {
  await openPatchArtifactPanel(page, 'contradictory');

  await expect(page.getByTestId('patch-artifact.authorityWarning')).toContainText('Authority claims were detected');
  await expect(page.getByTestId('patch-artifact.currentBadge')).toHaveCount(0);
});

test('PatchArtifactPanel_does_not_render_patch_artifact_creation_or_edit_buttons', async ({ page }) => {
  for (const label of ['Create Patch Artifact', 'Edit Patch Artifact', 'Approve Patch']) {
    await expectNoButton(page, label);
  }
});

test('PatchArtifactPanel_does_not_render_dry_run_or_source_apply_buttons', async ({ page }) => {
  for (const label of ['Approve Dry-run', 'Run Dry-run', 'Approve Source Apply', 'Apply Patch', 'Apply Source']) {
    await expectNoButton(page, label);
  }
});

test('PatchArtifactPanel_does_not_render_rollback_workflow_or_release_buttons', async ({ page }) => {
  for (const label of ['Execute Rollback', 'Continue Workflow', 'Approve Release', 'Approve Deployment', 'Approve Merge']) {
    await expectNoButton(page, label);
  }
});

test('PatchArtifactPanel_does_not_render_authority_or_git_buttons', async ({ page }) => {
  for (const label of ['Refresh Authority', 'Reissue Evidence', 'Run Git', 'Create Pull Request']) {
    await expectNoButton(page, label);
  }
});

test('PatchArtifactPanel_does_not_render_agent_model_tool_buttons', async ({ page }) => {
  for (const label of ['Run Agent', 'Call Model', 'Run Tool']) {
    await expectNoButton(page, label);
  }
});

test('PatchArtifactPanel_allows_copy_artifact_id_for_inspection_only', async ({ page }) => {
  await openPatchArtifactPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Patch Artifact ID' }).click();

  await expect(page.getByTestId('patch-artifact.copyStatus')).toContainText('Patch artifact id copied for inspection only.');
});

test('PatchArtifactPanel_allows_copy_hashes_without_mutation_api', async ({ page }) => {
  const methods: string[] = [];
  await page.route('**/irondev-api/**', async (route) => {
    methods.push(route.request().method());
    await route.fallback();
  });

  await openPatchArtifactPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Patch Artifact Hash' }).click();
  await expect(page.getByTestId('patch-artifact.copyStatus')).toContainText('Patch artifact hash copied for inspection only.');

  await page.getByRole('button', { name: 'Copy Source Hash' }).click();
  await expect(page.getByTestId('patch-artifact.copyStatus')).toContainText('Source hash copied for inspection only.');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('PatchArtifactPanel_allows_copy_evidence_refs_for_inspection_only', async ({ page }) => {
  await openPatchArtifactPanel(page, 'current');
  await page.getByRole('button', { name: 'Copy Evidence References' }).click();

  await expect(page.getByTestId('patch-artifact.copyStatus')).toContainText('Evidence references copied for inspection only.');
});

test('PatchArtifactPanel_static_ui_files_do_not_contain_forbidden_dependencies_or_action_labels', async () => {
  const files = [
    'src/features/governance/PatchArtifactTypes.ts',
    'src/features/governance/PatchArtifactBoundary.ts',
    'src/features/governance/PatchArtifactPanel.tsx',
    'src/features/governance/PatchArtifactPanelRoute.tsx'
  ];
  const forbidden = [
    'PatchArtifactCreator',
    'PatchArtifactWriter',
    'PatchArtifactStoreWrite',
    'PatchGenerationService',
    'DiffGenerationService',
    'ControlledSourceApplyExecutor',
    'SourceApplyExecutor',
    'SourceApplyRunner',
    'SourceApplyDryRunExecutor',
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
    'createPatchArtifact(',
    'editPatchArtifact(',
    'approvePatch(',
    'executeDryRun(',
    'executeSourceApply(',
    'executeRollback(',
    'continueWorkflow(',
    'refreshAuthority(',
    'reissueEvidence(',
    'dispatchAgent(',
    'callModel(',
    'runTool(',
    '"Create Patch Artifact"',
    '"Edit Patch Artifact"',
    '"Approve Patch"',
    '"Approve Dry-run"',
    '"Run Dry-run"',
    '"Apply Patch"',
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

test('PatchArtifactPanel_receipt_records_boundary_and_validation_sections', async () => {
  const receipt = readFileSync(join(process.cwd(), '..', 'Docs/receipts/PR232_PATCH_ARTIFACT_UI.md'), 'utf8');

  expect(receipt).toContain('What landed');
  expect(receipt).toContain('What did not land');
  expect(receipt).toContain('What authority was not granted');
  expect(receipt).toContain('Validation run');
  expect(receipt).toContain('Known caveats');
  expect(receipt).toContain('PR232 shows patch artifact evidence. It does not apply the patch.');
});

async function expectNoButton(page: Page, label: string) {
  await openPatchArtifactPanel(page, 'current');
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectBoundaryWarning(page: Page) {
  await expect(page.getByTestId('patch-artifact.boundaryBanner')).toContainText('Patch artifact evidence is display only.');
  await expect(page.getByTestId('patch-artifact.boundaryBanner')).toContainText('Human review remains required.');
}

async function openPatchArtifactPanel(page: Page, fixture: string) {
  await seedShellContext(page);
  await page.goto(`/governance/patch-artifacts?fixture=${fixture}`);
  await expect(page.getByTestId('patch-artifact.workspace')).toBeVisible();
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
