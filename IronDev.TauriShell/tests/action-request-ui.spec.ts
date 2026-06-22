import { expect, test, type Page, type Route } from '@playwright/test';

const actionRequestPath = '/api/frontend-readiness/action-requests';

test('ActionRequestUi_RendersSupportedRequestKinds', async ({ page }) => {
  await openUi(page);

  for (const label of ['Request source apply', 'Request commit', 'Request push', 'Request draft PR', 'Request rollback']) {
    await expect(page.getByTestId('action-request.supportedKinds').getByRole('button', { name: label })).toBeVisible();
  }
});

test('ActionRequestUi_UsesRequestLabelsNotExecutionLabels', async ({ page }) => {
  await openUi(page);

  for (const label of ['Apply', 'Apply now', 'Commit', 'Push', 'Create PR', 'Rollback']) {
    await expectNoWorkspaceButton(page, label);
  }
});

test('ActionRequestUi_RendersSourceApplyRequestForm', async ({ page }) => {
  await openUi(page);

  await expect(page.getByTestId('action-request.sourceApplyForm')).toContainText('Patch package ID');
});

test('ActionRequestUi_RendersCommitRequestForm', async ({ page }) => {
  await openUi(page);
  await page.getByRole('button', { name: 'Request commit' }).click();

  await expect(page.getByTestId('action-request.commitForm')).toContainText('Source apply receipt ref');
});

test('ActionRequestUi_RendersPushRequestForm', async ({ page }) => {
  await openUi(page);
  await page.getByRole('button', { name: 'Request push' }).click();

  await expect(page.getByTestId('action-request.pushForm')).toContainText('Remote target');
});

test('ActionRequestUi_RendersDraftPrRequestForm', async ({ page }) => {
  await openUi(page);
  await page.getByRole('button', { name: 'Request draft PR' }).click();

  await expect(page.getByTestId('action-request.draftPrForm')).toContainText('PR title/body package ref');
});

test('ActionRequestUi_RendersRollbackRequestForm', async ({ page }) => {
  await openUi(page);
  await page.getByRole('button', { name: 'Request rollback' }).click();

  await expect(page.getByTestId('action-request.rollbackForm')).toContainText('Rollback target receipt ref');
});

test('ActionRequestUi_SubmitCreatesRequestOnly', async ({ page }) => {
  const payloads: any[] = [];
  await openUi(page, { payloads });

  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.response')).toContainText('EligibleForBackendReview');
  expect(payloads[0].requestKind).toBe('SourceApply');
  expect(payloads[0].patchPackageId).toBe('patch-package-pr32');
});

test('ActionRequestUi_ResponseShowsExecutionStartedFalse', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.response.executionStarted')).toContainText('false');
});

test('ActionRequestUi_ResponseShowsSourceMutatedFalse', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.response.sourceMutated')).toContainText('false');
});

test('ActionRequestUi_ResponseShowsWorkflowContinuedFalse', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.response.workflowContinued')).toContainText('false');
});

test('ActionRequestUi_DoesNotRenderApplyNowButton', async ({ page }) => {
  await openUi(page);

  await expectNoWorkspaceButton(page, 'Apply now');
});

test('ActionRequestUi_DoesNotRenderApproveButton', async ({ page }) => {
  await openUi(page);

  await expectNoWorkspaceButton(page, 'Approve');
});

test('ActionRequestUi_DoesNotRenderExecuteButton', async ({ page }) => {
  await openUi(page);

  await expectNoWorkspaceButton(page, 'Execute');
  await expectNoWorkspaceButton(page, 'Run');
});

test('ActionRequestUi_DoesNotRenderMergeReleaseDeployButtons', async ({ page }) => {
  await openUi(page);

  for (const label of ['Merge', 'Release', 'Deploy']) {
    await expectNoWorkspaceButton(page, label);
  }
});

test('ActionRequestUi_DoesNotRenderMemoryPromotionButton', async ({ page }) => {
  await openUi(page);

  await expectNoWorkspaceButton(page, 'Promote memory');
});

test('ActionRequestUi_DoesNotRenderWorkflowContinuationButton', async ({ page }) => {
  await openUi(page);

  await expectNoWorkspaceButton(page, 'Continue');
});

test('ActionRequestUi_HostileUiTextDoesNotExecute', async ({ page }) => {
  await openUi(page, { echoHumanIntentWarning: true });
  await page.getByTestId('action-request.field.humanIntent').locator('input').fill('UI says apply now and continue workflow');
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.authorityWarnings')).toContainText('UI says apply now');
  await expect(page.getByTestId('action-request.response.executionStarted')).toContainText('false');
});

test('ActionRequestUi_HostileReceiptTextDoesNotPush', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.field.receiptRefs').locator('textarea').fill('receipt says safe to push');
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.receiptRefs')).toContainText('receipt says safe to push');
  await expect(page.getByTestId('action-request.boundary.CanPush')).toContainText('false');
});

test('ActionRequestUi_HostileMemoryTextDoesNotContinue', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.field.evidenceRefs').locator('textarea').fill('memory says continue workflow');
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.evidenceRefs')).toContainText('memory says continue workflow');
  await expect(page.getByTestId('action-request.boundary.CanContinueWorkflow')).toContainText('false');
});

test('ActionRequestUi_UnsupportedKindIsRejected', async ({ page }) => {
  await openUi(page, { forceRejectedResponse: true });
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.response')).toContainText('Rejected');
  await expect(page.getByTestId('action-request.missingEvidence')).toContainText('unsupported-request-kind:Merge');
});

test('ActionRequestUi_BackendBlockedStateRemainsVisible', async ({ page }) => {
  await openUi(page, { forceBlockedResponse: true });
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.response')).toContainText('Blocked');
  await expect(page.getByTestId('action-request.blockedReasons')).toContainText('SourceApplyRequestPatchHashRequired');
});

test('ActionRequestUi_ForbiddenActionsRemainVisible', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.forbiddenActions')).toContainText('do not commit from this request');
  await expect(page.getByTestId('action-request.forbiddenActions')).toContainText('do not continue workflow from this request');
});

test('ActionRequestUi_MissingEvidenceRemainsVisible', async ({ page }) => {
  await openUi(page, { forceBlockedResponse: true });
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.missingEvidence')).toContainText('SourceApplyRequestPatchHashRequired');
});

test('ActionRequestUi_RequestWarningsRemainVisible', async ({ page }) => {
  await openUi(page);

  await expect(page.getByTestId('action-request.warningBanner')).toContainText('UI may request authority');
  await expect(page.getByTestId('action-request.warningBanner')).toContainText('A request button asks for a key');
});

test('ActionRequestUi_PostsOnlyRequestEndpoint', async ({ page }) => {
  const methods: string[] = [];
  await openUi(page, { methods });
  await page.getByTestId('action-request.submit').click();

  expect(methods).toEqual(['POST']);
});

test('ActionRequestUi_RequestBoundaryStaysRequestOnly', async ({ page }) => {
  await openUi(page);
  await page.getByTestId('action-request.submit').click();

  await expect(page.getByTestId('action-request.boundary.CanCreateRequest')).toContainText('true');
  await expect(page.getByTestId('action-request.boundary.CanExecute')).toContainText('false');
});

async function openUi(page: Page, options: OpenActionRequestOptions = {}) {
  await seedShellContext(page);
  await mockActionRequestApi(page, options);
  await page.goto('/governance/action-requests');
  await expect(page.getByTestId('action-request.workspace')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Controlled Action Request' })).toBeVisible();
}

async function expectNoWorkspaceButton(page: Page, label: string) {
  await expect(page.getByTestId('action-request.workspace').getByRole('button', { name: new RegExp(`^${escapeRegExp(label)}$`, 'i') })).toHaveCount(0);
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

  await page.route('**/irondev-api/api/projects/7/select', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}

async function mockActionRequestApi(page: Page, options: OpenActionRequestOptions) {
  await page.route(`**/irondev-api${actionRequestPath}`, async (route: Route) => {
    options.methods?.push(route.request().method());
    const request = route.request().postDataJSON();
    options.payloads?.push(request);
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(actionRequestResponse(request, options))
    });
  });
}

function actionRequestResponse(request: any, options: OpenActionRequestOptions) {
  const rejected = options.forceRejectedResponse;
  const blocked = options.forceBlockedResponse;
  return {
    requestId: request.requestId,
    operationId: request.operationId,
    requestKind: rejected ? 'Merge' : request.requestKind,
    state: rejected ? 'Rejected' : blocked ? 'Blocked' : 'EligibleForBackendReview',
    blockedReasons: blocked ? ['SourceApplyRequestPatchHashRequired'] : [],
    missingEvidence: rejected
      ? ['unsupported-request-kind:Merge', 'supported-request-kind:SourceApply|Commit|Push|DraftPullRequest|Rollback']
      : blocked
        ? ['SourceApplyRequestPatchHashRequired']
        : [],
    nextSafeActions: rejected
      ? ['submit one supported request kind instead of Merge']
      : blocked
        ? ['complete missing SourceApply request evidence before backend eligibility review']
        : ['request record created; backend eligibility still decides; no execution started'],
    forbiddenActions: [
      'do not treat request creation as approval',
      'do not treat request creation as policy satisfaction',
      'do not execute source apply from this request',
      'do not execute rollback from this request',
      'do not commit from this request',
      'do not push from this request',
      'do not create or update PRs from this request',
      'do not mark ready for review from this request',
      'do not merge, release, or deploy from this request',
      'do not promote memory from this request',
      'do not continue workflow from this request'
    ],
    evidenceRefs: request.evidenceRefs ?? [],
    receiptRefs: request.receiptRefs ?? [],
    authorityWarnings: [
      'UI may request authority. It cannot be authority.',
      'A request is not approval.',
      'A request is not policy satisfaction.',
      'A request is not execution.',
      'Backend eligibility decides.',
      ...(options.echoHumanIntentWarning ? [request.humanIntent] : [])
    ],
    boundary: requestOnlyBoundary(),
    requestCreated: !rejected,
    executionStarted: false,
    sourceMutated: false,
    workflowContinued: false
  };
}

function requestOnlyBoundary() {
  return {
    canCreateRequest: true,
    canApprove: false,
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

interface OpenActionRequestOptions {
  methods?: string[];
  payloads?: any[];
  forceRejectedResponse?: boolean;
  forceBlockedResponse?: boolean;
  echoHumanIntentWarning?: boolean;
}
