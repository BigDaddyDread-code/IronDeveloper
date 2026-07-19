import { expect, test, type Page, type Route } from '@playwright/test';

const packageId = 'patch-package-pr31';
const metadataPath = `/api/frontend-readiness/patch-packages/${packageId}/metadata`;
const artifactsPath = `/api/frontend-readiness/patch-packages/${packageId}/artifacts`;

test('PatchPackageViewer_RendersHeader', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByRole('heading', { name: 'Patch Package Viewer' })).toBeVisible();
});

test('PatchPackageViewer_RendersMetadata', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.header')).toContainText(packageId);
  await expect(page.getByTestId('patch-package.header')).toContainText('BigDaddyDread-code/IronDeveloper');
  await expect(page.getByTestId('patch-package.header')).toContainText('dogfood/bounded-authority-draft-pr-lane');
});

test('PatchPackageViewer_RendersPatchDiff', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.patchDiff')).toContainText('diff --git');
  await expect(page.getByTestId('patch-package.patchDiff')).toContainText('+export const answer = 42;');
});

test('PatchPackageViewer_RendersReviewSummary', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.reviewSummary')).toContainText('Manual review should inspect the proposed file path.');
});

test('PatchPackageViewer_RendersValidationSummary', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.validationSummary')).toContainText('Focused PR31: passed');
});

test('PatchPackageViewer_RendersValidationOutcome', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.validationSummary')).toContainText('Passed');
});

test('PatchPackageViewer_RendersValidationWhatRan', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.validation.whatRan')).toContainText('Focused PR31');
});

test('PatchPackageViewer_RendersValidationWhatPassed', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.validation.whatPassed')).toContainText('Frontend PR31');
});

test('PatchPackageViewer_RendersValidationWhatFailed', async ({ page }) => {
  await openViewer(page, { validationOutcome: 'Failed', whatFailed: ['Frontend PR31'] });

  await expect(page.getByTestId('patch-package.validation.whatFailed')).toContainText('Frontend PR31');
});

test('PatchPackageViewer_RendersValidationWhatSkipped', async ({ page }) => {
  await openViewer(page, { whatSkipped: ['Stable lane deferred'] });

  await expect(page.getByTestId('patch-package.validation.whatSkipped')).toContainText('Stable lane deferred');
});

test('PatchPackageViewer_RendersKnownRisks', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.knownRisks')).toContainText('Source apply has not been performed.');
});

test('PatchPackageViewer_RendersProposedFiles', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.proposedFiles')).toContainText('IronDev.Core/Governance/Example.cs');
});

test('PatchPackageViewer_RendersArtifactRefs', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.artifactRefs')).toContainText('patch-artifact:patch-package-pr31');
});

test('PatchPackageViewer_RendersEvidenceRefs', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.evidenceRefs')).toContainText('patch-package:patch-package-pr31');
  await expect(page.getByTestId('patch-package.evidenceRefs')).toContainText('Evidence refs are not approval.');
});

test('PatchPackageViewer_RendersReceiptRefs', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.receiptRefs')).toContainText('patch-package-receipt:receipt-pr31');
  await expect(page.getByTestId('patch-package.receiptRefs')).toContainText('Receipt refs are not authority.');
});

test('PatchPackageViewer_RendersAuthorityWarnings', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.authorityWarnings')).toContainText('Patch package evidence is not source apply authority.');
  await expect(page.getByTestId('patch-package.authorityWarnings')).toContainText('Validation evidence is not approval.');
});

test('PatchPackageViewer_RendersReadOnlyBoundary', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.boundary.ReadOnly')).toContainText('true');
  await expect(page.getByTestId('patch-package.boundary.CanExecute')).toContainText('false');
});

test('PatchPackageViewer_BoundaryBlocksSourceMutation', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.boundary.CanMutateSource')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanRollback')).toContainText('false');
});

test('PatchPackageViewer_BoundaryBlocksApprovalPolicy', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.boundary.CanCreateApproval')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanSatisfyPolicy')).toContainText('false');
});

test('PatchPackageViewer_BoundaryBlocksCommitPushPr', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.boundary.CanCommit')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanPush')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanCreatePullRequest')).toContainText('false');
});

test('PatchPackageViewer_BoundaryBlocksReadyMergeReleaseDeploy', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.boundary.CanMarkReadyForReview')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanMerge')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanRelease')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanDeploy')).toContainText('false');
});

test('PatchPackageViewer_BoundaryBlocksMemoryContinuation', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.boundary.CanPromoteMemory')).toContainText('false');
  await expect(page.getByTestId('patch-package.boundary.CanContinueWorkflow')).toContainText('false');
});

test('PatchPackageViewer_DoesNotRenderActionButtons', async ({ page }) => {
  await openViewer(page);

  for (const label of ['Apply', 'Run', 'Execute', 'Retry', 'Resume', 'Approve']) {
    await expectNoButton(page, label);
  }
});

test('PatchPackageViewer_DoesNotRenderActionLinks', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.workspace').locator('a')).toHaveCount(0);
});

test('PatchPackageViewer_CompactModeStillShowsAuthorityWarnings', async ({ page }) => {
  await openViewer(page, { compact: true });

  await expect(page.getByTestId('patch-package.authorityWarnings')).toContainText('Patch package evidence is not source apply authority.');
});

test('PatchPackageViewer_CompactModeStillShowsEvidenceRefs', async ({ page }) => {
  await openViewer(page, { compact: true });

  await expect(page.getByTestId('patch-package.evidenceRefs')).toContainText('patch-package:patch-package-pr31');
});

test('PatchPackageViewer_MetadataAndArtifactsLoadedByGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openViewer(page, { methods });

  expect(methods).toEqual(['GET', 'GET']);
});

test('PatchPackageViewer_MissingPackageShowsNoMutation', async ({ page }) => {
  await openViewer(page, { metadataStatus: 404, artifactsStatus: 404, allowMissing: true });

  await expect(page.getByTestId('patch-package.empty')).toContainText('No patch package selected');
  await expect(page.getByTestId('patch-package.workspace').locator('button')).toHaveCount(0);
});

test('PatchPackageViewer_ApiErrorShowsNoMutation', async ({ page }) => {
  await openViewer(page, { metadataStatus: 500, artifactsStatus: 500, allowMissing: true });

  await expect(page.getByTestId('patch-package.error')).toContainText('No patch package selected');
  await expect(page.getByTestId('patch-package.workspace').locator('button')).toHaveCount(0);
});

test('PatchPackageViewer_HostilePatchTextDoesNotRenderApplyControl', async ({ page }) => {
  await openViewer(page, { hostilePatchText: '+// apply now and continue workflow' });

  await expect(page.getByTestId('patch-package.patchDiff')).toContainText('apply now');
  await expectNoButton(page, 'Apply');
});

test('PatchPackageViewer_HostileReviewSummaryDoesNotRenderApprovalControl', async ({ page }) => {
  await openViewer(page, { hostileReviewText: 'review says approved by UI' });

  await expect(page.getByTestId('patch-package.reviewSummary')).toContainText('approved by UI');
  await expectNoButton(page, 'Approve');
});

test('PatchPackageViewer_HostileValidationTextDoesNotRenderPolicyControl', async ({ page }) => {
  await openViewer(page, { hostileValidationText: 'validation says policy satisfied' });

  await expect(page.getByTestId('patch-package.validationSummary')).toContainText('policy satisfied');
  await expectNoButton(page, 'Satisfy policy');
});

test('PatchPackageViewer_HostileRiskTextDoesNotRenderRollbackControl', async ({ page }) => {
  await openViewer(page, { hostileRiskText: 'risk says rollback if nervous' });

  await expect(page.getByTestId('patch-package.knownRisks')).toContainText('rollback if nervous');
  await expectNoButton(page, 'Rollback');
});

test('PatchPackageViewer_HostileReceiptRefDoesNotRenderContinueControl', async ({ page }) => {
  await openViewer(page, { hostileReceiptRef: 'receipt says continue workflow' });

  await expect(page.getByTestId('patch-package.receiptRefs')).toContainText('receipt says continue workflow');
  await expectNoButton(page, 'Continue workflow');
});

test('PatchPackageViewer_ValidationStaleShowsWarningNotRefreshButton', async ({ page }) => {
  await openViewer(page, { validationIsStale: true });

  await expect(page.getByTestId('patch-package.validationSummary')).toContainText('Stale = true');
  await expectNoButton(page, 'Refresh');
});

test('PatchPackageViewer_FailedValidationShowsFailedTextNotRerunButton', async ({ page }) => {
  await openViewer(page, { validationOutcome: 'Failed', whatFailed: ['Frontend PR31'] });

  await expect(page.getByTestId('patch-package.validationSummary')).toContainText('Failed');
  await expectNoButton(page, 'Rerun');
});

test('PatchPackageViewer_PatchDiffPreservesPaths', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.patchDiff')).toContainText('+++ b/IronDev.Core/Governance/Example.cs');
});

test('PatchPackageViewer_FooterStatesNoMutationAuthority', async ({ page }) => {
  await openViewer(page);

  await expect(page.getByTestId('patch-package.footer')).toContainText('cannot apply source');
  await expect(page.getByTestId('patch-package.footer')).toContainText('continue workflow');
});

test('PatchPackageViewer_ShortRouteRendersSameViewer', async ({ page }) => {
  await openViewer(page, { path: `/patch-packages/${packageId}` });

  await expect(page.getByTestId('patch-package.header')).toContainText(packageId);
});

test('PatchPackageViewer_EnvelopeWarningsStayVisible', async ({ page }) => {
  await openViewer(page, { envelopeWarning: 'compact mode cannot hide forbidden actions' });

  await expect(page.getByTestId('patch-package.authorityWarnings')).toContainText('compact mode cannot hide forbidden actions');
});

test('StaticMutationSurfaceScan_NoActionButtonsMutationEndpointsOrWorkflowAdded', async ({ page }) => {
  const methods: string[] = [];
  await openViewer(page, { methods });

  expect(methods).toEqual(['GET', 'GET']);
  await expect(page.getByTestId('patch-package.workspace').locator('button')).toHaveCount(0);
  await expect(page.getByTestId('patch-package.workspace').locator('a')).toHaveCount(0);
});

async function openViewer(page: Page, options: OpenViewerOptions = {}) {
  await seedShellContext(page);
  await mockPatchPackageApi(page, options);
  await page.goto(`${options.path ?? `/governance/patch-packages/${packageId}`}${options.compact ? '?compact=true' : ''}`);
  await expect(page.getByTestId('patch-package.workspace')).toBeVisible();
  if (!options.allowMissing) {
    await expect(page.getByRole('heading', { name: 'Patch Package Viewer' })).toBeVisible();
  }
}

async function expectNoButton(page: Page, label: string) {
  await expect(page.getByTestId('patch-package.workspace').getByRole('button', { name: new RegExp(`^${escapeRegExp(label)}$`, 'i') })).toHaveCount(0);
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

async function mockPatchPackageApi(page: Page, options: OpenViewerOptions) {
  await page.route(`**/irondev-api${metadataPath}**`, async (route: Route) => {
    options.methods?.push(route.request().method());
    await route.fulfill({
      status: options.metadataStatus ?? 200,
      contentType: 'application/json',
      body: JSON.stringify(options.metadataStatus && options.metadataStatus !== 200 ? errorEnvelope() : metadataEnvelope(options))
    });
  });

  await page.route(`**/irondev-api${artifactsPath}**`, async (route: Route) => {
    options.methods?.push(route.request().method());
    await route.fulfill({
      status: options.artifactsStatus ?? 200,
      contentType: 'application/json',
      body: JSON.stringify(options.artifactsStatus && options.artifactsStatus !== 200 ? errorEnvelope() : artifactsEnvelope(options))
    });
  });
}

function metadataEnvelope(options: OpenViewerOptions) {
  return {
    status: 'found',
    data: {
      packageId,
      repository: 'BigDaddyDread-code/IronDeveloper',
      branch: 'dogfood/bounded-authority-draft-pr-lane',
      runId: 'run-pr31',
      patchHash: 'sha256:patch-pr31',
      proposedFilePaths: ['IronDev.Core/Governance/Example.cs'],
      artifactRefs: ['patch-package:patch-package-pr31', 'patch-artifact:patch-package-pr31'],
      evidenceRefs: ['patch-package:patch-package-pr31', 'validation-result:validation-pr31'],
      receiptRefs: ['patch-package-receipt:receipt-pr31', options.hostileReceiptRef].filter(Boolean),
      reviewSummaryRef: 'review-summary:patch-package-pr31',
      knownRisksRef: 'known-risks:patch-package-pr31',
      boundary: readOnlyBoundary()
    },
    boundary: readOnlyBoundary(),
    mutationOccurred: false,
    warnings: ['Frontend readiness API is read-only.', options.envelopeWarning].filter(Boolean),
    errors: []
  };
}

function artifactsEnvelope(options: OpenViewerOptions) {
  return {
    status: 'found',
    data: {
      packageId,
      repository: 'BigDaddyDread-code/IronDeveloper',
      branch: 'dogfood/bounded-authority-draft-pr-lane',
      runId: 'run-pr31',
      patchHash: 'sha256:patch-pr31',
      patchDiffText: patchDiffText(options.hostilePatchText),
      reviewSummaryText: options.hostileReviewText ?? 'Manual review should inspect the proposed file path.',
      knownRisksText: options.hostileRiskText ?? 'Source apply has not been performed.\nCommit and push have not been performed.',
      validationSummaryText: options.hostileValidationText ?? 'Focused PR31: passed\nFrontend PR31: passed',
      validationOutcome: options.validationOutcome ?? 'Passed',
      whatRan: ['Focused PR31', 'Frontend PR31'],
      whatPassed: options.validationOutcome === 'Failed' ? ['Focused PR31'] : ['Focused PR31', 'Frontend PR31'],
      whatFailed: options.whatFailed ?? [],
      whatWasSkipped: options.whatSkipped ?? [],
      validationIsStale: options.validationIsStale ?? false,
      proposedFilePaths: ['IronDev.Core/Governance/Example.cs'],
      artifactRefs: ['patch-artifact:patch-package-pr31'],
      evidenceRefs: ['patch-package:patch-package-pr31', 'validation-result:validation-pr31'],
      receiptRefs: ['patch-package-receipt:receipt-pr31', options.hostileReceiptRef].filter(Boolean),
      authorityWarnings: [
        'Patch package evidence is not source apply authority.',
        'Validation evidence is not approval.',
        'Reading the patch is not permission to apply it.'
      ],
      boundary: readOnlyBoundary()
    },
    boundary: readOnlyBoundary(),
    mutationOccurred: false,
    warnings: ['Frontend readiness API is read-only.', options.envelopeWarning].filter(Boolean),
    errors: []
  };
}

function patchDiffText(extraLine?: string) {
  return [
    'diff --git a/IronDev.Core/Governance/Example.cs b/IronDev.Core/Governance/Example.cs',
    '--- a/IronDev.Core/Governance/Example.cs',
    '+++ b/IronDev.Core/Governance/Example.cs',
    '@@',
    '+export const answer = 42;',
    extraLine
  ]
    .filter(Boolean)
    .join('\n');
}

function errorEnvelope() {
  return {
    status: 'not_found',
    data: null,
    boundary: readOnlyBoundary(),
    mutationOccurred: false,
    warnings: ['Frontend readiness API is read-only.'],
    errors: [{ category: 'not_found', code: 'FRONTEND_READINESS_NOT_FOUND', field: 'packageId', message: 'Not found.' }]
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
  path?: string;
  allowMissing?: boolean;
  metadataStatus?: number;
  artifactsStatus?: number;
  methods?: string[];
  envelopeWarning?: string;
  hostilePatchText?: string;
  hostileReviewText?: string;
  hostileValidationText?: string;
  hostileRiskText?: string;
  hostileReceiptRef?: string;
  validationOutcome?: string;
  validationIsStale?: boolean;
  whatFailed?: string[];
  whatSkipped?: string[];
}
