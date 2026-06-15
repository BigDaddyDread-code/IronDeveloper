import { expect, test, type Page, type Route } from '@playwright/test';

const projectReferenceId = '42';
const workflowRunId = 'workflow-run-155';
const workflowStepId = 'workflow-step-155';
const approvalPackageId = 'approval-package-155';
const traceId = 'trace-approval-155';
const correlationId = 'correlation-155';
const causationId = 'causation-155';

test('ApprovalPackageReviewPage_RendersReadOnlyBanner', async ({ page }) => {
  await openApprovalPackagePage(page);

  await expect(page.getByRole('heading', { name: 'Approval Package Review' })).toBeVisible();
  await expect(page.getByTestId('approval-packages.readonlyBanner')).toContainText('Read-only view');
  await expect(page.getByTestId('approval-packages.readonlyBanner')).toContainText('Approval package is not accepted approval');
  await expect(page.getByTestId('approval-packages.readonlyBanner')).toContainText('Approval package review is not approval');
  await expect(page.getByTestId('approval-packages.readonlyBanner')).toContainText('Requested decision is not decision made');
  await expect(page.getByTestId('approval-packages.readonlyBanner')).toContainText('Policy evidence is not policy satisfaction');
});

test('ApprovalPackageReviewPage_RendersSearchFilters', async ({ page }) => {
  await openApprovalPackagePage(page);

  for (const label of ['Project reference', 'Workflow run', 'Workflow step', 'Approval package id', 'Correlation', 'Approval scope', 'Package status']) {
    await expect(page.getByLabel(label)).toBeVisible();
  }
});

test('ApprovalPackageReviewPage_RendersApprovalPackageList', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page);

  await expect(page.getByTestId('approval-packages.item')).toHaveCount(1);
  await expect(page.getByTestId('approval-packages.list')).toContainText(approvalPackageId);
  await expect(page.getByTestId('approval-packages.list')).toContainText('Approval package recorded for human review.');
});

test('ApprovalPackageReviewPage_RendersSelectedPackageSummary', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page);

  await expect(page.getByTestId('approval-packages.safeDetail')).toContainText(approvalPackageId);
  await expect(page.getByTestId('approval-packages.safeDetail')).toContainText(workflowRunId);
  await expect(page.getByTestId('approval-packages.safeDetail')).toContainText(workflowStepId);
  await expect(page.getByTestId('approval-packages.safeDetail')).toContainText(correlationId);
});

test('ApprovalPackageReviewPage_OpenPackageRendersEvidenceReferences', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page);
  await page.getByRole('button', { name: 'Open Package' }).click();

  await expect(page.getByTestId('approval-packages.evidenceRefs')).toContainText('tool-gate-155');
  await expect(page.getByTestId('approval-packages.evidenceRefs')).toContainText('policy-decision-155');
  await expect(page.getByTestId('approval-packages.boundaryWarnings')).toContainText('Human review remains required.');
});

test('ApprovalPackageReviewPage_RendersBoundaryFooter', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page);

  await expect(page.getByTestId('approval-packages.boundaryFooter')).toContainText(
    'This UI cannot approve, reject, accept approvals, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software.'
  );
});

test('ApprovalPackageReviewPage_RendersEmptyState', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page, { empty: true });

  await expect(page.getByRole('heading', { name: 'No approval package evidence' })).toBeVisible();
  await expect(page.getByTestId('approval-packages.status')).toContainText('No approval package evidence matched those filters.');
});

test('ApprovalPackageReviewPage_RendersValidationErrorState', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page, { validationError: true });

  await expect(page.getByTestId('approval-packages.validationError')).toBeVisible();
  await expect(page.getByTestId('approval-packages.validationError')).toContainText('projectReferenceId');
});

test('ApprovalPackageReviewPage_ClientValidationRequiresSearchBasis', async ({ page }) => {
  await openApprovalPackagePage(page);
  await page.getByRole('button', { name: 'Search' }).click();

  await expect(page.getByTestId('approval-packages.validationError')).toBeVisible();
  await expect(page.getByTestId('approval-packages.status')).toContainText('Approval package review needs a project reference');
});

test('ApprovalPackageReviewPage_SearchUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openApprovalPackagePageWithSearch(page, { methods });

  expect(methods).toContain('GET');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('ApprovalPackageReviewPage_OpenPackageUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openApprovalPackagePageWithSearch(page, { methods });
  await page.getByRole('button', { name: 'Open Package' }).click();

  expect(methods.length).toBeGreaterThanOrEqual(2);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('ApprovalPackageReviewPage_RefreshUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openApprovalPackagePageWithSearch(page, { methods });
  await page.getByTestId('approval-packages.filters').getByRole('button', { name: 'Refresh' }).click();

  expect(methods.length).toBeGreaterThanOrEqual(2);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('ApprovalPackageReviewPage_ClearFiltersDoesNotCallApi', async ({ page }) => {
  const methods: string[] = [];
  await openApprovalPackagePageWithSearch(page, { methods });
  const countAfterSearch = methods.length;
  await page.getByRole('button', { name: 'Clear Filters' }).click();

  expect(methods.length).toBe(countAfterSearch);
  await expect(page.getByTestId('approval-packages.status')).toContainText('Filters cleared.');
});

test('ApprovalPackageReviewPage_CopyPackageIdIsInspectionOnly', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Package ID' }).click();

  await expect(page.getByTestId('approval-packages.copyStatus')).toContainText('Copy package id is not approval.');
});

test('ApprovalPackageReviewPage_CopyCorrelationIdIsInspectionOnly', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Correlation ID' }).click();

  await expect(page.getByTestId('approval-packages.copyStatus')).toContainText('Copy correlation id is not workflow continuation.');
});

test('ApprovalPackageReviewPage_OpenTraceIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Trace', '/api/v1/governance/traces/'));
test('ApprovalPackageReviewPage_OpenTimelineIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Timeline', '/governance/timeline?correlationId='));
test('ApprovalPackageReviewPage_OpenCorrelationReportIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Correlation Report', '/api/v1/governance/correlation-reports/approval-gate-dogfood'));
test('ApprovalPackageReviewPage_OpenToolGateLedgerIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Tool Gate Ledger', '/governance/tool-gates?workflowRunId='));

test('ApprovalPackageReviewPage_RendersSafeSummaryOnly', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page, { unsafeFixture: true });

  await expect(page.getByTestId('approval-packages.list')).toContainText('[redacted approval package text]');
  await expect(page.locator('body')).not.toContainText('private reasoning leaked');
});

test('ApprovalPackageReviewPage_RedactsUnsafeDetailText', async ({ page }) => {
  await openApprovalPackagePageWithSearch(page, { unsafeFixture: true });
  await page.getByRole('button', { name: 'Open Package' }).click();

  await expect(page.getByTestId('approval-packages.evidenceRefs')).toContainText('[redacted approval package text]');
  await expect(page.locator('body')).not.toContainText('raw approval text leaked');
});

test('ApprovalPackageReviewPage_DoesNotExposeRawApprovalPayload', async ({ page }) => await expectForbiddenTextNotRendered(page, 'ApprovalPayloadJson'));
test('ApprovalPackageReviewPage_DoesNotExposeApprovalNotesRaw', async ({ page }) => await expectForbiddenTextNotRendered(page, 'ApprovalNotesRaw'));
test('ApprovalPackageReviewPage_DoesNotExposeRawApprovalText', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawApprovalText'));
test('ApprovalPackageReviewPage_DoesNotExposePayloadJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PayloadJson'));
test('ApprovalPackageReviewPage_DoesNotExposePrivateReasoning', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PrivateReasoning'));
test('ApprovalPackageReviewPage_DoesNotExposeRawPrompt', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawPrompt'));
test('ApprovalPackageReviewPage_DoesNotExposeRawCompletion', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawCompletion'));
test('ApprovalPackageReviewPage_DoesNotExposeRawToolOutput', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawToolOutput'));
test('ApprovalPackageReviewPage_DoesNotExposePatchPayload', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PatchPayload'));
test('ApprovalPackageReviewPage_DoesNotExposeSecrets', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'Secret');
  await expect(page.locator('body')).not.toContainText('Bearer');
});

test('ApprovalPackageReviewPage_DoesNotRenderApproveButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Approve'));
test('ApprovalPackageReviewPage_DoesNotRenderRejectButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Reject'));
test('ApprovalPackageReviewPage_DoesNotRenderAcceptApprovalButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Accept Approval'));
test('ApprovalPackageReviewPage_DoesNotRenderSatisfyPolicyButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Satisfy Policy'));
test('ApprovalPackageReviewPage_DoesNotRenderContinueWorkflowButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Continue Workflow'));
test('ApprovalPackageReviewPage_DoesNotRenderInvokeToolButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Invoke Tool'));
test('ApprovalPackageReviewPage_DoesNotRenderDispatchAgentButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Dispatch Agent'));
test('ApprovalPackageReviewPage_DoesNotRenderApplySourceButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Apply Source'));
test('ApprovalPackageReviewPage_DoesNotRenderReleaseButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Release Software'));

async function expectRelatedLink(page: Page, button: string, expected: string) {
  await openApprovalPackagePageWithSearch(page);
  await page.getByRole('button', { name: button }).click();
  await expect(page.getByTestId('approval-packages.relatedStatus')).toContainText(expected);
}

async function expectForbiddenButtonNotRendered(page: Page, label: string) {
  await openApprovalPackagePageWithSearch(page);
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectForbiddenTextNotRendered(page: Page, marker: string) {
  await openApprovalPackagePageWithSearch(page, { unsafeFixture: true });
  await page.getByRole('button', { name: 'Open Package' }).click();
  await expect(page.locator('body')).not.toContainText(marker);
}

async function openApprovalPackagePageWithSearch(page: Page, options: MockApprovalPackageOptions = {}) {
  await openApprovalPackagePage(page, options);
  await page.getByLabel('Project reference').fill(projectReferenceId);
  await page.getByLabel('Approval package id').fill(options.empty ? '' : approvalPackageId);
  await page.getByLabel('Workflow run').fill(workflowRunId);
  await page.getByLabel('Workflow step').fill(workflowStepId);
  await page.getByLabel('Correlation').fill(correlationId);
  await page.getByLabel('Approval scope').fill('source_apply');
  await page.getByLabel('Package status').fill('ReadyForReview');
  await page.getByRole('button', { name: 'Search' }).click();
  await expect(page.getByTestId('approval-packages.status')).not.toContainText('Loading approval package evidence');
}

async function openApprovalPackagePage(page: Page, options: MockApprovalPackageOptions = {}) {
  await seedShellContext(page);
  await mockApprovalPackageApi(page, options);
  await page.goto('/governance/approval-packages');
  await expect(page.getByTestId('approval-packages.workspace')).toBeVisible();
}

async function seedShellContext(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) });
  });

  await page.route('**/irondev-api/api/environment', async (route) => {
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

  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 })
    });
  });

  await page.route('**/irondev-api/api/tenants**', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]) });
  });

  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }]) });
  });

  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}

interface MockApprovalPackageOptions {
  empty?: boolean;
  validationError?: boolean;
  unsafeFixture?: boolean;
  methods?: string[];
}

async function mockApprovalPackageApi(page: Page, options: MockApprovalPackageOptions) {
  await page.route('**/irondev-api/api/v1/governance/traces**', async (route) => {
    options.methods?.push(route.request().method());

    if (options.validationError) {
      await fulfillJson(route, 400, validationErrorEnvelope());
      return;
    }

    const pathname = new URL(route.request().url()).pathname;
    await fulfillJson(route, 200, pathname.endsWith(`/${traceId}`) ? detailEnvelope(options.unsafeFixture) : listEnvelope(options));
  });
}

async function fulfillJson(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function listEnvelope(options: MockApprovalPackageOptions) {
  return {
    status: 'governance_traces_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyBoundary(),
    warnings: ['Approval package review is read-only.'],
    errors: [],
    data: { traces: options.empty ? [] : [summaryTrace(options.unsafeFixture)], issues: [] }
  };
}

function detailEnvelope(unsafeFixture = false) {
  return {
    status: 'governance_trace_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyBoundary(),
    warnings: ['Approval package detail is evidence only.'],
    errors: [],
    data: {
      trace: {
        summary: summaryTrace(unsafeFixture),
        timeline: [
          {
            eventId: 'event-approval-155',
            eventKind: 'human_approval_package',
            sourceComponent: 'HumanApprovalPackageWorkflow',
            safeSummary: unsafeFixture ? 'raw approval text leaked with ApprovalPayloadJson' : 'Approval package was assembled for review.',
            recordedUtc: '2026-06-15T02:00:00Z',
            correlationId,
            causationId,
            subjectReferenceId: approvalPackageId,
            ApprovalPayloadJson: '{"RawApprovalText":"hidden"}',
            RawApprovalText: 'raw approval text leaked'
          }
        ],
        relatedReferences: [
          {
            referenceKind: 'tool_gate_decision',
            referenceId: 'tool-gate-155',
            safeSummary: unsafeFixture ? 'PrivateReasoning ApprovalNotesRaw Secret Bearer hidden' : 'Tool gate decision requires human review.',
            ApprovalNotesRaw: 'private reasoning leaked'
          },
          { referenceKind: 'policy_decision', referenceId: 'policy-decision-155', safeSummary: 'Policy evidence recorded only.' }
        ],
        boundaryWarnings: ['Human review remains required.', 'Approval package review is not approval.']
      },
      issues: []
    }
  };
}

function validationErrorEnvelope() {
  return {
    status: 'validation_error',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyBoundary(),
    warnings: ['Approval package review is read-only.'],
    errors: [{ code: 'missing_project_reference', field: 'projectReferenceId', message: 'projectReferenceId is required.' }],
    data: { issues: [] }
  };
}

function summaryTrace(unsafeFixture = false) {
  return {
    traceId,
    projectReferenceId,
    workflowRunId,
    workflowStepId,
    correlationId,
    causationId,
    subjectReferenceId: approvalPackageId,
    eventKind: 'human_approval_package',
    sourceComponent: 'HumanApprovalPackageWorkflow',
    safeSummary: unsafeFixture ? 'private reasoning leaked with PayloadJson RawPrompt Secret Bearer token' : 'Approval package recorded for human review.',
    recordedUtc: '2026-06-15T02:00:00Z',
    ApprovalPayloadJson: '{"RawPrompt":"hidden"}',
    ApprovalNotesRaw: 'private reasoning leaked',
    RawApprovalText: 'raw approval text leaked',
    PayloadJson: '{"RawCompletion":"hidden"}',
    PrivateReasoning: 'private reasoning leaked',
    RawPrompt: 'raw prompt leaked',
    RawCompletion: 'raw completion leaked',
    RawToolOutput: 'raw tool output leaked',
    PatchPayload: 'patch payload leaked',
    Secret: 'Bearer not-for-ui'
  };
}

function readOnlyBoundary() {
  return {
    readOnly: true,
    durable: true,
    mutationOccurred: false,
    packageVisibilityIsApproval: false,
    packageReviewIsApproval: false,
    requestedDecisionIsDecisionMade: false,
    humanNoteIsAcceptedApproval: false,
    approvalRequirementIsApproval: false,
    policyEvidenceIsPolicySatisfaction: false,
    navigationIsWorkflowContinuation: false,
    copyReferenceIsApproval: false,
    canApprove: false,
    canReject: false,
    canAcceptApproval: false,
    canCreateAcceptedApprovalRecord: false,
    canSatisfyPolicy: false,
    canTransitionWorkflow: false,
    canInvokeTool: false,
    canDispatchAgent: false,
    canApplySource: false,
    canReleaseSoftware: false
  };
}
