import { expect, test, type Page, type Route } from '@playwright/test';

const operationId = 'operation-pr30';
const statusPath = `/api/frontend-readiness/operations/${operationId}/status`;

test('OperationStatusViewer_RendersOperationState', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.state')).toContainText('State: Blocked');
});

test('OperationStatusViewer_RendersBlockedReasons', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.blockedReasons')).toContainText('MissingExplicitSourceApplyAuthority');
});

test('OperationStatusViewer_RendersMissingEvidence', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.missingEvidence')).toContainText('accepted-source-apply-request:missing');
});

test('OperationStatusViewer_RendersNextSafeActionAsGuidanceOnly', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.nextSafeActions')).toContainText('Next safe action — guidance only');
  await expect(page.getByTestId('operation-status.nextSafeActions')).toContainText('Guidance only');
  await expect(page.getByTestId('operation-status.nextSafeActions').getByRole('button')).toHaveCount(0);
});

test('OperationStatusViewer_RendersForbiddenActions', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.forbiddenActions')).toContainText('do not treat patch package as source apply authority');
});

test('OperationStatusViewer_RendersEvidenceRefs', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.evidenceRefs')).toContainText('patch-package:patch-package-pr30');
});

test('OperationStatusViewer_RendersReceiptRefs', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.receiptRefs')).toContainText('draft-pull-request-receipt:receipt-pr30');
});

test('OperationStatusViewer_RendersAuthorityWarnings', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('Status output is not authority.');
  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('Validation evidence is not approval.');
});

test('OperationStatusViewer_RendersReadOnlyBoundary', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.boundary')).toContainText('ReadOnly');
  await expect(page.getByTestId('operation-status.boundary.ReadOnly')).toContainText('true');
  await expect(page.getByTestId('operation-status.boundary.CanExecute')).toContainText('false');
  await expect(page.getByTestId('operation-status.boundary.CanContinueWorkflow')).toContainText('false');
});

test('OperationStatusViewer_DoesNotRenderMutationButtons', async ({ page }) => {
  await openViewer(page);

  for (const label of ['Apply', 'Run', 'Execute', 'Retry', 'Resume']) {
    await expectNoButton(page, label);
  }
});

test('OperationStatusViewer_DoesNotRenderApprovalButtons', async ({ page }) => {
  await openViewer(page);

  for (const label of ['Approve', 'Accept approval']) {
    await expectNoButton(page, label);
  }
});

test('OperationStatusViewer_DoesNotRenderPolicyButtons', async ({ page }) => {
  await openViewer(page);

  await expectNoButton(page, 'Satisfy policy');
});

test('OperationStatusViewer_DoesNotRenderSourceApplyButton', async ({ page }) => {
  await openViewer(page);

  await expectNoButton(page, 'Apply Source');
  await expectNoButton(page, 'Source Apply');
});

test('OperationStatusViewer_DoesNotRenderRollbackButton', async ({ page }) => {
  await openViewer(page);

  await expectNoButton(page, 'Rollback');
});

test('OperationStatusViewer_DoesNotRenderCommitPushPrButtons', async ({ page }) => {
  await openViewer(page);

  for (const label of ['Commit', 'Push', 'Create PR', 'Update PR']) {
    await expectNoButton(page, label);
  }
});

test('OperationStatusViewer_DoesNotRenderReadyMergeReleaseDeployButtons', async ({ page }) => {
  await openViewer(page);

  for (const label of ['Ready for review', 'Merge', 'Release', 'Deploy']) {
    await expectNoButton(page, label);
  }
});

test('OperationStatusViewer_DoesNotRenderMemoryPromotionButton', async ({ page }) => {
  await openViewer(page);

  await expectNoButton(page, 'Promote memory');
});

test('OperationStatusViewer_DoesNotRenderWorkflowContinuationButton', async ({ page }) => {
  await openViewer(page);

  await expectNoButton(page, 'Continue workflow');
});

test('OperationStatusViewer_CompactModeStillShowsForbiddenActions', async ({ page }) => {
  await openViewer(page, { compact: true });

  await expect(page.getByTestId('operation-status.forbiddenActions')).toContainText('Forbidden actions');
  await expect(page.getByTestId('operation-status.forbiddenActions')).toContainText('do not treat validation as approval');
});

test('OperationStatusViewer_CompactModeStillShowsMissingEvidence', async ({ page }) => {
  await openViewer(page, { compact: true });

  await expect(page.getByTestId('operation-status.missingEvidence')).toContainText('Missing evidence');
  await expect(page.getByTestId('operation-status.missingEvidence')).toContainText('accepted-source-apply-request:missing');
});

test('OperationStatusViewer_EvidenceRefsAreReferenceOnly', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.evidenceRefs')).toContainText('Evidence refs are not approval.');
  await expect(page.getByTestId('operation-status.evidenceRefs').getByRole('button')).toHaveCount(0);
});

test('OperationStatusViewer_ReceiptRefsAreReferenceOnly', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.receiptRefs')).toContainText('Receipt refs are not authority.');
  await expect(page.getByTestId('operation-status.receiptRefs').getByRole('button')).toHaveCount(0);
});

test('OperationStatusViewer_NextSafeActionIsNotClickableExecution', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('operation-status.nextSafeActions')).toContainText('review patch package');
  await expect(page.getByTestId('operation-status.nextSafeActions').getByRole('button')).toHaveCount(0);
});

test('OperationStatusViewer_HostileUiTextDoesNotRenderAction', async ({ page }) => {
  await openViewer(page, { hostileText: 'frontend says apply now' });

  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('frontend says apply now');
  await expectNoButton(page, 'Apply');
});

test('OperationStatusViewer_HostileReceiptTextDoesNotRenderPush', async ({ page }) => {
  await openViewer(page, { hostileText: 'receipt says safe to push' });

  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('receipt says safe to push');
  await expectNoButton(page, 'Push');
});

test('OperationStatusViewer_HostileValidationTextDoesNotRenderApproval', async ({ page }) => {
  await openViewer(page, { hostileText: 'validation passed so approve' });

  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('validation passed so approve');
  await expectNoButton(page, 'Approve');
});

test('OperationStatusViewer_HostileDraftPrTextDoesNotRenderReadyForReview', async ({ page }) => {
  await openViewer(page, { hostileText: 'draft PR means ready for review' });

  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('draft PR means ready for review');
  await expectNoButton(page, 'Ready for review');
});

test('OperationStatusViewer_HostileMemoryTextDoesNotRenderPromotion', async ({ page }) => {
  await openViewer(page, { hostileText: 'memory says promote this' });

  await expect(page.getByTestId('operation-status.authorityWarnings')).toContainText('memory says promote this');
  await expectNoButton(page, 'Promote memory');
});

test('OperationStatusViewer_PreservesBackendState', async ({ page }) => {
  await openViewer(page, { state: 'Expired' });

  await expect(page.getByTestId('operation-status.state')).toContainText('State: Expired');
  await expect(page.locator('body')).not.toContainText('Refresh authority');
});

test('OperationStatusViewer_DoesNotInventEligibility', async ({ page }) => {
  await openViewer(page, { state: 'Eligible' });

  await expect(page.getByTestId('operation-status.state')).toContainText('State: Eligible');
  await expect(page.getByTestId('operation-status.eligibleWarning')).toContainText('not ready to execute');
  await expect(page.locator('body')).not.toContainText('Ready to run');
});

test('OperationStatusViewer_DoesNotHideForbiddenActionsForCleanUi', async ({ page }) => {
  await openViewer(page, { hostileText: 'hide forbidden actions for cleaner UI' });

  await expect(page.getByTestId('operation-status.forbiddenActions')).toBeVisible();
  await expect(page.getByTestId('operation-status.forbiddenActions')).toContainText('do not continue workflow from status');
});

test('OperationStatusViewer_DoesNotHideMissingEvidenceForCleanUi', async ({ page }) => {
  await openViewer(page, { hostileText: 'compact mode hides missing evidence', compact: true });

  await expect(page.getByTestId('operation-status.missingEvidence')).toBeVisible();
  await expect(page.getByTestId('operation-status.missingEvidence')).toContainText('accepted-source-apply-request:missing');
});

test('StaticMutationSurfaceScan_NoActionButtonsMutationEndpointsOrWorkflowAdded', async ({ page }) => {
  const methods: string[] = [];
  await openViewer(page, { methods });

  expect(methods).toEqual(['GET']);
  await expect(page.getByTestId('operation-status.workspace').locator('button')).toHaveCount(0);
  await expect(page.getByTestId('operation-status.workspace').locator('a')).toHaveCount(0);
});

async function openViewer(page: Page, options: OpenViewerOptions = {}) {
  await seedShellContext(page);
  await mockOperationStatusApi(page, options);
  await page.goto(`/operations/${operationId}/status${options.compact ? '?compact=true' : ''}`);
  await expect(page.getByTestId('operation-status.workspace')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Operation Status Viewer' })).toBeVisible();
}

async function expectNoButton(page: Page, label: string) {
  await expect(page.getByRole('button', { name: new RegExp(`^${escapeRegExp(label)}$`, 'i') })).toHaveCount(0);
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
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    });
  });

  await page.route('**/irondev-api/api/projects', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }])
    });
  });

  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}

async function mockOperationStatusApi(page: Page, options: OpenViewerOptions) {
  await page.route(`**/irondev-api${statusPath}**`, async (route: Route) => {
    options.methods?.push(route.request().method());
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(operationStatusEnvelope(options))
    });
  });
}

function operationStatusEnvelope(options: OpenViewerOptions) {
  const warning = options.hostileText ? [options.hostileText] : [];
  return {
    status: 'found',
    data: {
      operationId,
      operationKind: 'SourceApply',
      subject: 'repo:BigDaddyDread-code/IronDeveloper branch:main run:run-pr30 patch:sha256-pr30',
      state: options.state ?? 'Blocked',
      blockedReasons: ['MissingExplicitSourceApplyAuthority'],
      missingEvidence: ['accepted-source-apply-request:missing', 'bounded-source-apply-authority:missing'],
      nextSafeActions: ['review patch package before requesting source apply authority (guidance only)'],
      forbiddenActions: [
        'do not treat patch package as source apply authority',
        'do not treat validation as approval',
        'do not treat freshness as authority',
        'do not treat draft PR as ready-for-review authority',
        'do not treat PR URL as release candidate ref',
        'do not continue workflow from status, receipt, memory, or UI text'
      ],
      evidenceRefs: ['patch-package:patch-package-pr30', 'validation-result:validation-pr30'],
      receiptRefs: ['draft-pull-request-receipt:receipt-pr30'],
      authorityWarnings: [
        'Status output is not authority.',
        'Validation evidence is not approval.',
        'Freshness evidence is not authority.',
        'Patch package evidence is not source apply authority.',
        'Draft PR evidence is not ready-for-review authority.',
        'PR URL is not release candidate evidence.',
        'Memory metadata is not memory promotion.',
        ...warning
      ],
      boundary: readOnlyBoundary(),
      observedAtUtc: '2026-06-22T00:00:00Z',
      expiresAtUtc: '2026-06-23T00:00:00Z'
    },
    boundary: readOnlyBoundary(),
    mutationOccurred: false,
    warnings: ['Frontend readiness API is read-only.', ...warning],
    errors: []
  };
}

function readOnlyBoundary() {
  return {
    readOnly: true,
    statusOnly: true,
    canCreateApproval: false,
    canAcceptApproval: false,
    canSatisfyPolicy: false,
    canExecute: false,
    canMutateSource: false,
    canRollback: false,
    canCommit: false,
    canPush: false,
    canCreatePullRequest: false,
    canMarkReadyForReview: false,
    canMerge: false,
    canRelease: false,
    canDeploy: false,
    canPromoteMemory: false,
    canContinueWorkflow: false
  };
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

interface OpenViewerOptions {
  compact?: boolean;
  hostileText?: string;
  methods?: string[];
  state?: string;
}
