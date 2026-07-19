import { expect, test, type Page, type Route } from '@playwright/test';

const projectReferenceId = '42';
const dogfoodLoopId = 'dogfood-loop-156';
const dogfoodReceiptId = 'dogfood-receipt-156';
const evidenceId = 'evidence-156';
const traceId = 'trace-dogfood-156';
const workflowRunId = 'workflow-run-156';
const workflowStepId = 'workflow-step-156';
const correlationId = 'correlation-156';
const causationId = 'causation-156';

test('DogfoodReceiptViewerPage_RendersReadOnlyBanner', async ({ page }) => {
  await openDogfoodReceiptPage(page);

  await expect(page.getByRole('heading', { name: 'Dogfood Receipt Viewer' })).toBeVisible();
  await expect(page.getByTestId('dogfood-receipts.readonlyBanner')).toContainText('Read-only view');
  await expect(page.getByTestId('dogfood-receipts.readonlyBanner')).toContainText('Dogfood receipt is not release approval');
  await expect(page.getByTestId('dogfood-receipts.readonlyBanner')).toContainText('Dogfood pass is not release readiness');
  await expect(page.getByTestId('dogfood-receipts.readonlyBanner')).toContainText('Dogfood evidence is not policy satisfaction');
  await expect(page.getByTestId('dogfood-receipts.readonlyBanner')).toContainText('Receipt viewer is not dogfood execution');
});

test('DogfoodReceiptViewerPage_RendersSearchFilters', async ({ page }) => {
  await openDogfoodReceiptPage(page);

  for (const label of ['Project reference', 'Dogfood loop id', 'Dogfood receipt id', 'Workflow run', 'Workflow step', 'Correlation', 'Source component']) {
    await expect(page.getByLabel(label)).toBeVisible();
  }
});

test('DogfoodReceiptViewerPage_RendersReceiptList', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page);

  await expect(page.getByTestId('dogfood-receipts.item')).toHaveCount(1);
  await expect(page.getByTestId('dogfood-receipts.list')).toContainText(dogfoodReceiptId);
  await expect(page.getByTestId('dogfood-receipts.list')).toContainText('Dogfood receipt recorded for human review.');
});

test('DogfoodReceiptViewerPage_RendersSelectedReceiptSummary', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page);

  await expect(page.getByTestId('dogfood-receipts.safeDetail')).toContainText(dogfoodReceiptId);
  await expect(page.getByTestId('dogfood-receipts.safeDetail')).toContainText(dogfoodLoopId);
  await expect(page.getByTestId('dogfood-receipts.safeDetail')).toContainText(workflowRunId);
  await expect(page.getByTestId('dogfood-receipts.safeDetail')).toContainText(evidenceId);
});

test('DogfoodReceiptViewerPage_OpenReceiptRendersEvidenceReferences', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page);
  await page.getByRole('button', { name: 'Open Receipt' }).click();

  await expect(page.getByTestId('dogfood-receipts.evidenceRefs')).toContainText('approval-package-156');
  await expect(page.getByTestId('dogfood-receipts.gateRefs')).toContainText('tool-gate-156');
  await expect(page.getByTestId('dogfood-receipts.boundaryWarnings')).toContainText('Dogfood receipt is not release approval.');
});

test('DogfoodReceiptViewerPage_RendersBoundaryFooter', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page);

  await expect(page.getByTestId('dogfood-receipts.boundaryFooter')).toContainText(
    'This UI cannot create dogfood receipts, mark dogfood passed, approve release, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software.'
  );
});

test('DogfoodReceiptViewerPage_RendersEmptyState', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page, { empty: true, useTraceSearch: true });

  await expect(page.getByRole('heading', { name: 'No dogfood receipt evidence' })).toBeVisible();
  await expect(page.getByTestId('dogfood-receipts.status')).toContainText('No dogfood receipt evidence matched those filters.');
});

test('DogfoodReceiptViewerPage_RendersValidationErrorState', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page, { validationError: true });

  await expect(page.getByTestId('dogfood-receipts.validationError')).toBeVisible();
  await expect(page.getByTestId('dogfood-receipts.validationError')).toContainText('projectId');
});

test('DogfoodReceiptViewerPage_ClientValidationRequiresSearchBasis', async ({ page }) => {
  await openDogfoodReceiptPage(page);
  await page.getByRole('button', { name: 'Search' }).click();

  await expect(page.getByTestId('dogfood-receipts.validationError')).toBeVisible();
  await expect(page.getByTestId('dogfood-receipts.status')).toContainText('Dogfood receipt viewer needs a project reference');
});

test('DogfoodReceiptViewerPage_SearchUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openDogfoodReceiptPageWithSearch(page, { methods });

  expect(methods).toContain('GET');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('DogfoodReceiptViewerPage_OpenReceiptUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openDogfoodReceiptPageWithSearch(page, { methods });
  await page.getByRole('button', { name: 'Open Receipt' }).click();

  expect(methods.length).toBeGreaterThanOrEqual(2);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('DogfoodReceiptViewerPage_RefreshUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openDogfoodReceiptPageWithSearch(page, { methods });
  await page.getByTestId('dogfood-receipts.filters').getByRole('button', { name: 'Refresh' }).click();

  expect(methods.length).toBeGreaterThanOrEqual(2);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('DogfoodReceiptViewerPage_ClearFiltersDoesNotCallApi', async ({ page }) => {
  const methods: string[] = [];
  await openDogfoodReceiptPageWithSearch(page, { methods });
  const countAfterSearch = methods.length;
  await page.getByRole('button', { name: 'Clear Filters' }).click();

  expect(methods.length).toBe(countAfterSearch);
  await expect(page.getByTestId('dogfood-receipts.status')).toContainText('Filters cleared.');
});

test('DogfoodReceiptViewerPage_CopyReceiptIdIsInspectionOnly', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Receipt ID' }).click();

  await expect(page.getByTestId('dogfood-receipts.copyStatus')).toContainText('Copy receipt id is not release approval.');
});

test('DogfoodReceiptViewerPage_CopyCorrelationIdIsInspectionOnly', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Correlation ID' }).click();

  await expect(page.getByTestId('dogfood-receipts.copyStatus')).toContainText('Copy correlation id is not workflow continuation.');
});

test('DogfoodReceiptViewerPage_OpenTraceIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Trace', '/api/v1/governance/traces/', { useTraceSearch: true }));
test('DogfoodReceiptViewerPage_OpenTimelineIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Timeline', '/governance/timeline?correlationId='));
test('DogfoodReceiptViewerPage_OpenCorrelationReportIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Correlation Report', '/api/v1/governance/correlation-reports/approval-gate-dogfood'));
test('DogfoodReceiptViewerPage_OpenToolGateLedgerIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Tool Gate Ledger', '/governance/tool-gates?workflowRunId='));
test('DogfoodReceiptViewerPage_OpenApprovalPackageIsNavigationOnly', async ({ page }) => await expectRelatedLink(page, 'Open Approval Package', '/governance/approval-packages?correlationId='));

test('DogfoodReceiptViewerPage_RendersSafeSummaryOnly', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page, { unsafeFixture: true });

  await expect(page.getByTestId('dogfood-receipts.list')).toContainText('[redacted dogfood receipt text]');
  await expect(page.locator('body')).not.toContainText('private reasoning leaked');
});

test('DogfoodReceiptViewerPage_RedactsUnsafeDetailText', async ({ page }) => {
  await openDogfoodReceiptPageWithSearch(page, { unsafeFixture: true });
  await page.getByRole('button', { name: 'Open Receipt' }).click();

  await expect(page.getByTestId('dogfood-receipts.evidenceRefs')).toContainText('[redacted dogfood receipt text]');
  await expect(page.locator('body')).not.toContainText('raw dogfood notes leaked');
});

test('DogfoodReceiptViewerPage_DoesNotExposeDogfoodPayloadJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'DogfoodPayloadJson'));
test('DogfoodReceiptViewerPage_DoesNotExposeDogfoodOutputJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'DogfoodOutputJson'));
test('DogfoodReceiptViewerPage_DoesNotExposeValidationOutputJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'ValidationOutputJson'));
test('DogfoodReceiptViewerPage_DoesNotExposeRawDogfoodNotes', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawDogfoodNotes'));
test('DogfoodReceiptViewerPage_DoesNotExposePayloadJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PayloadJson'));
test('DogfoodReceiptViewerPage_DoesNotExposePrivateReasoning', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PrivateReasoning'));
test('DogfoodReceiptViewerPage_DoesNotExposeRawPrompt', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawPrompt'));
test('DogfoodReceiptViewerPage_DoesNotExposeRawCompletion', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawCompletion'));
test('DogfoodReceiptViewerPage_DoesNotExposeRawToolOutput', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawToolOutput'));
test('DogfoodReceiptViewerPage_DoesNotExposePatchPayload', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PatchPayload'));
test('DogfoodReceiptViewerPage_DoesNotExposeSecrets', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'Secret');
  await expect(page.locator('body')).not.toContainText('Bearer');
});

test('DogfoodReceiptViewerPage_DoesNotRenderCreateReceiptButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Create Receipt'));
test('DogfoodReceiptViewerPage_DoesNotRenderRecordReceiptButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Record Receipt'));
test('DogfoodReceiptViewerPage_DoesNotRenderMarkPassedButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Mark Passed'));
test('DogfoodReceiptViewerPage_DoesNotRenderMarkDogfoodPassedButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Mark Dogfood Passed'));
test('DogfoodReceiptViewerPage_DoesNotRenderMarkFailedButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Mark Failed'));
test('DogfoodReceiptViewerPage_DoesNotRenderApproveReleaseButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Approve Release'));
test('DogfoodReceiptViewerPage_DoesNotRenderReleaseButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Release'));
test('DogfoodReceiptViewerPage_DoesNotRenderSatisfyPolicyButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Satisfy Policy'));
test('DogfoodReceiptViewerPage_DoesNotRenderTransitionWorkflowButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Transition Workflow'));
test('DogfoodReceiptViewerPage_DoesNotRenderInvokeToolButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Invoke Tool'));
test('DogfoodReceiptViewerPage_DoesNotRenderDispatchAgentButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Dispatch Agent'));
test('DogfoodReceiptViewerPage_DoesNotRenderApplySourceButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Apply Source'));

async function expectRelatedLink(page: Page, button: string, expected: string, options: MockDogfoodReceiptOptions = {}) {
  await openDogfoodReceiptPageWithSearch(page, options);
  await page.getByRole('button', { name: button }).click();
  await expect(page.getByTestId('dogfood-receipts.relatedStatus')).toContainText(expected);
}

async function expectForbiddenButtonNotRendered(page: Page, label: string) {
  await openDogfoodReceiptPageWithSearch(page);
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectForbiddenTextNotRendered(page: Page, marker: string) {
  await openDogfoodReceiptPageWithSearch(page, { unsafeFixture: true });
  await page.getByRole('button', { name: 'Open Receipt' }).click();
  await expect(page.locator('body')).not.toContainText(marker);
}

async function openDogfoodReceiptPageWithSearch(page: Page, options: MockDogfoodReceiptOptions = {}) {
  await openDogfoodReceiptPage(page, options);
  await page.getByLabel('Project reference').fill(projectReferenceId);
  if (!options.useTraceSearch) {
    await page.getByLabel('Dogfood loop id').fill(options.empty ? '' : dogfoodLoopId);
    await page.getByLabel('Dogfood receipt id').fill(options.empty ? '' : dogfoodReceiptId);
  }
  await page.getByLabel('Workflow run').fill(workflowRunId);
  await page.getByLabel('Workflow step').fill(workflowStepId);
  await page.getByLabel('Correlation').fill(correlationId);
  await page.getByRole('button', { name: 'Search' }).click();
  await expect(page.getByTestId('dogfood-receipts.status')).not.toContainText('Loading dogfood receipt evidence');
}

async function openDogfoodReceiptPage(page: Page, options: MockDogfoodReceiptOptions = {}) {
  await seedShellContext(page);
  await mockDogfoodReceiptApi(page, options);
  await page.goto('/governance/dogfood-receipts');
  await expect(page.getByTestId('dogfood-receipts.workspace')).toBeVisible();
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

  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}

interface MockDogfoodReceiptOptions {
  empty?: boolean;
  validationError?: boolean;
  unsafeFixture?: boolean;
  useTraceSearch?: boolean;
  methods?: string[];
}

async function mockDogfoodReceiptApi(page: Page, options: MockDogfoodReceiptOptions) {
  await page.route('**/irondev-api/api/v1/dogfood-loops/**', async (route) => {
    options.methods?.push(route.request().method());

    if (options.validationError) {
      await fulfillJson(route, 400, validationErrorEnvelope());
      return;
    }

    await fulfillJson(route, 200, dogfoodReceiptEnvelope(options.unsafeFixture));
  });

  await page.route('**/irondev-api/api/v1/governance/traces**', async (route) => {
    options.methods?.push(route.request().method());
    const pathname = new URL(route.request().url()).pathname;
    await fulfillJson(route, 200, pathname.endsWith(`/${traceId}`) ? traceDetailEnvelope(options.unsafeFixture) : traceListEnvelope(options));
  });
}

async function fulfillJson(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function dogfoodReceiptEnvelope(unsafeFixture = false) {
  return {
    status: 'receipt_found',
    mutationOccurred: false,
    humanApprovalRequired: true,
    dogfoodLoopId,
    runId: workflowRunId,
    receiptId: dogfoodReceiptId,
    evidenceId,
    boundary: readOnlyDogfoodBoundary(),
    warnings: ['Dogfood receipt is not release approval.'],
    errors: [],
    data: dogfoodReceiptData(unsafeFixture)
  };
}

function dogfoodReceiptData(unsafeFixture = false) {
  return {
    dogfoodLoopId,
    runId: workflowRunId,
    receiptId: dogfoodReceiptId,
    evidenceId,
    projectId: projectReferenceId,
    summary: unsafeFixture ? 'PrivateReasoning leaked with DogfoodPayloadJson RawPrompt Secret Bearer token' : 'Dogfood receipt recorded for human review.',
    goal: 'Collect dogfood loop evidence.',
    observations: [unsafeFixture ? 'raw dogfood notes leaked with DogfoodOutputJson' : 'Safe dogfood observation.'],
    blockedReasons: ['Missing approval package evidence.'],
    referencedAgentRuns: [{ refType: 'agent_run', refId: 'agent-run-156', summary: 'Agent run evidence only.', durable: true, backendRecorded: true, source: 'backend' }],
    referencedCriticReviews: [{ refType: 'critic_review', refId: 'critic-review-156', summary: 'Critic review evidence only.', durable: true, backendRecorded: true, source: 'backend' }],
    referencedMemoryImprovements: [{ refType: 'memory_improvement', refId: 'memory-improvement-156', summary: 'Memory improvement proposal evidence only.', durable: true, backendRecorded: true, source: 'backend' }],
    referencedToolRequests: [{ refType: 'tool_request', refId: 'tool-request-156', summary: 'Tool request is not execution.', durable: true, backendRecorded: true, source: 'backend' }],
    referencedGateDecisions: [{ refType: 'tool_gate_decision', refId: 'tool-gate-156', summary: 'Gate evidence requires review.', durable: true, backendRecorded: true, source: 'backend' }],
    evidenceRefs: [
      {
        refType: 'approval_package',
        refId: 'approval-package-156',
        summary: unsafeFixture ? 'RawDogfoodNotes ValidationOutputJson PayloadJson hidden' : 'Approval package evidence exists.',
        durable: true,
        backendRecorded: true,
        source: 'backend'
      }
    ],
    durable: true,
    containsNonDurableReferences: false,
    durabilityWarnings: ['Dogfood receipt remains evidence only.'],
    knownLimitations: ['Dogfood pass is not release readiness.'],
    createdAtUtc: '2026-06-15T03:00:00Z',
    warnings: ['Dogfood receipt is not release approval.'],
    DogfoodPayloadJson: '{"RawPrompt":"hidden"}',
    DogfoodOutputJson: '{"RawCompletion":"hidden"}',
    ValidationOutputJson: '{"RawToolOutput":"hidden"}',
    RawDogfoodNotes: 'raw dogfood notes leaked',
    PayloadJson: '{"PatchPayload":"hidden"}',
    PrivateReasoning: 'private reasoning leaked',
    RawPrompt: 'raw prompt leaked',
    RawCompletion: 'raw completion leaked',
    RawToolOutput: 'raw tool output leaked',
    PatchPayload: 'patch payload leaked',
    Secret: 'Bearer not-for-ui'
  };
}

function traceListEnvelope(options: MockDogfoodReceiptOptions) {
  return {
    status: 'governance_traces_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyTraceBoundary(),
    warnings: ['Dogfood receipt trace lookup is read-only.'],
    errors: [],
    data: { traces: options.empty ? [] : [summaryTrace(options.unsafeFixture)], issues: [] }
  };
}

function traceDetailEnvelope(unsafeFixture = false) {
  return {
    status: 'governance_trace_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyTraceBoundary(),
    warnings: ['Dogfood receipt trace detail is evidence only.'],
    errors: [],
    data: {
      trace: {
        summary: summaryTrace(unsafeFixture),
        timeline: [
          {
            eventId: 'event-dogfood-156',
            eventKind: 'dogfood.receipt.recorded',
            sourceComponent: 'DogfoodLoopApi',
            safeSummary: unsafeFixture ? 'raw dogfood notes leaked with PayloadJson' : 'Dogfood receipt was recorded for review.',
            recordedUtc: '2026-06-15T03:00:00Z',
            correlationId,
            causationId,
            subjectReferenceId: dogfoodReceiptId
          }
        ],
        relatedReferences: [
          {
            referenceKind: 'tool_gate_decision',
            referenceId: 'tool-gate-156',
            safeSummary: unsafeFixture ? 'PrivateReasoning RawDogfoodNotes Secret Bearer hidden' : 'Tool gate evidence requires review.'
          }
        ],
        boundaryWarnings: ['Dogfood receipt is not release approval.', 'Dogfood evidence is not policy satisfaction.']
      },
      issues: []
    }
  };
}

function validationErrorEnvelope() {
  return {
    status: 'validation_error',
    mutationOccurred: false,
    boundary: readOnlyDogfoodBoundary(),
    warnings: ['Dogfood receipt viewer is read-only.'],
    errors: [{ code: 'missing_project_id', field: 'projectId', message: 'projectId is required.' }],
    data: null
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
    subjectReferenceId: dogfoodReceiptId,
    eventKind: 'dogfood.receipt.recorded',
    sourceComponent: 'DogfoodLoopApi',
    safeSummary: unsafeFixture ? 'private reasoning leaked with PayloadJson RawPrompt Secret Bearer token' : 'Dogfood receipt recorded for human review.',
    recordedUtc: '2026-06-15T03:00:00Z'
  };
}

function readOnlyDogfoodBoundary() {
  return {
    dogfoodReceiptIsReleaseApproval: false,
    dogfoodLoopIsAutonomousWorkflow: false,
    toolExecuted: false,
    requestApproved: false,
    gateExecuted: false,
    gateIsExecutor: false,
    sourceApplied: false,
    memoryPromoted: false,
    collectiveMemoryWritten: false,
    vectorAuthorityWritten: false,
    auditIsApproval: false,
    modelOutputIsAuthority: false,
    endpointAccessIsExecutionPermission: false,
    apiResponseStatusIsGovernance: false,
    durable: true,
    containsNonDurableReferences: false,
    humanReviewRequiredForSourceApply: true,
    humanReviewRequiredForMemoryPromotion: true
  };
}

function readOnlyTraceBoundary() {
  return {
    readOnlyTrace: true,
    traceabilityIsAuthority: false,
    traceOutputIsApproval: false,
    traceOutputIsPolicySatisfaction: false,
    traceOutputIsWorkflowTransition: false,
    traceOutputIsToolInvocation: false,
    traceOutputIsAgentDispatch: false,
    traceOutputIsModelExecution: false,
    traceOutputIsMemoryPromotion: false,
    traceOutputIsSourceApply: false,
    traceOutputIsPatchApply: false,
    exposesRawPayloadJson: false,
    exposesPrivateReasoning: false
  };
}
