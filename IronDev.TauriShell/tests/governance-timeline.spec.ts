import { expect, test, type Page, type Route } from '@playwright/test';

const projectReferenceId = '11111111-1111-1111-1111-111111111111';
const correlationId = '22222222-2222-2222-2222-222222222222';
const causationId = '33333333-3333-3333-3333-333333333333';

test('GovernanceTimelinePage_RendersReadOnlyBanner', async ({ page }) => {
  await openGovernanceTimeline(page);

  await expect(page.getByRole('heading', { name: 'Governance Timeline' })).toBeVisible();
  await expect(page.getByTestId('governance-timeline.readonlyBanner')).toContainText('Read-only view');
  await expect(page.getByTestId('governance-timeline.readonlyBanner')).toContainText('Timeline is not authority');
  await expect(page.getByTestId('governance-timeline.readonlyBanner')).toContainText('Observation is not approval');
  await expect(page.getByTestId('governance-timeline.readonlyBanner')).toContainText('Traceability is not mutation permission');
});

test('GovernanceTimelinePage_RendersTimelineItems', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);

  await expect(page.getByTestId('governance-timeline.item')).toHaveCount(2);
  await expect(page.getByTestId('governance-timeline.items')).toContainText('Approval decision recorded as evidence.');
  await expect(page.getByTestId('governance-timeline.items')).toContainText('Gate decision recorded as evidence.');
});

test('GovernanceTimelinePage_RendersSafeSummaryOnly', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page, { unsafeFixture: true });

  await expect(page.getByTestId('governance-timeline.items')).toContainText('[redacted timeline text]');
  await expect(page.locator('body')).not.toContainText('private reasoning leaked');
});

test('GovernanceTimelinePage_RendersCorrelationAndCausationReferences', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);

  await expect(page.getByTestId('governance-timeline.detail')).toContainText(correlationId);
  await expect(page.getByTestId('governance-timeline.detail')).toContainText(causationId);
});

test('GovernanceTimelinePage_RendersEmptyState', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page, { empty: true });

  await expect(page.getByRole('heading', { name: 'No governance traces' })).toBeVisible();
  await expect(page.getByTestId('governance-timeline.status')).toContainText('No governance traces matched those filters.');
});

test('GovernanceTimelinePage_RendersValidationErrorState', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page, { validationError: true });

  await expect(page.getByTestId('governance-timeline.validationError')).toBeVisible();
  await expect(page.getByTestId('governance-timeline.validationError')).toContainText('projectReferenceId');
});

test('GovernanceTimelinePage_SearchUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openGovernanceTimelineWithSearch(page, { methods });

  expect(methods).toContain('GET');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('GovernanceTimelinePage_RefreshUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openGovernanceTimelineWithSearch(page, { methods });
  await page.getByTestId('governance-timeline.filters').getByRole('button', { name: 'Refresh' }).click();

  expect(methods.length).toBeGreaterThanOrEqual(2);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('GovernanceTimelinePage_DoesNotRenderApproveButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Approve$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderRejectButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Reject$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderRetryButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Retry$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderRepairButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Repair$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderContinueWorkflowButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Continue Workflow$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderInvokeToolButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Invoke Tool$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderDispatchAgentButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Dispatch Agent$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderApplySourceButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Apply Source$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderCleanupButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Cleanup$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotRenderDeleteButton', async ({ page }) => {
  await openGovernanceTimelineWithSearch(page);
  await expect(page.getByRole('button', { name: /^Delete$/i })).toHaveCount(0);
});

test('GovernanceTimelinePage_DoesNotExposePayloadJson', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'PayloadJson');
});

test('GovernanceTimelinePage_DoesNotExposePrivateReasoning', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'PrivateReasoning');
});

test('GovernanceTimelinePage_DoesNotExposeRawPrompt', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'RawPrompt');
});

test('GovernanceTimelinePage_DoesNotExposeRawCompletion', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'RawCompletion');
});

test('GovernanceTimelinePage_DoesNotExposeRawToolOutput', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'RawToolOutput');
});

test('GovernanceTimelinePage_DoesNotExposeSourceContent', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'SourceContent');
});

test('GovernanceTimelinePage_DoesNotExposePatchPayload', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'PatchPayload');
});

test('GovernanceTimelinePage_DoesNotExposeSecrets', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'Secret');
  await expect(page.locator('body')).not.toContainText('Bearer');
});

async function expectForbiddenTextNotRendered(page: Page, marker: string) {
  await openGovernanceTimelineWithSearch(page, { unsafeFixture: true });
  await expect(page.locator('body')).not.toContainText(marker);
}

async function openGovernanceTimelineWithSearch(page: Page, options: MockGovernanceOptions = {}) {
  await openGovernanceTimeline(page, options);
  await page.getByLabel('Project reference').fill(projectReferenceId);
  await page.getByRole('button', { name: 'Search' }).click();
  await expect(page.getByTestId('governance-timeline.status')).not.toContainText('Loading governance timeline');
}

async function openGovernanceTimeline(page: Page, options: MockGovernanceOptions = {}) {
  await seedShellContext(page);
  await mockGovernanceTraceApi(page, options);
  await page.goto('/governance/timeline');
  await expect(page.getByTestId('governance-timeline.workspace')).toBeVisible();
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
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    });
  });

  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }])
    });
  });

  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}

interface MockGovernanceOptions {
  empty?: boolean;
  validationError?: boolean;
  unsafeFixture?: boolean;
  methods?: string[];
}

async function mockGovernanceTraceApi(page: Page, options: MockGovernanceOptions) {
  await page.route('**/irondev-api/api/v1/governance/traces**', async (route) => {
    options.methods?.push(route.request().method());

    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/trace-001')) {
      await fulfillJson(route, 200, governanceTraceDetailEnvelope(options.unsafeFixture));
      return;
    }

    if (options.validationError) {
      await fulfillJson(route, 400, validationErrorEnvelope());
      return;
    }

    await fulfillJson(route, 200, governanceTraceListEnvelope(options));
  });
}

async function fulfillJson(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function governanceTraceListEnvelope(options: MockGovernanceOptions) {
  return {
    status: 'trace_list_returned',
    mutationOccurred: false,
    boundary: readOnlyBoundary(),
    warnings: ['Governance trace explorer is read-only.'],
    errors: [],
    data: {
      status: 'trace_list_returned',
      traces: options.empty ? [] : [traceSummary(options.unsafeFixture), secondTraceSummary()],
      issues: [],
      boundaryWarnings: ['Trace output is not approval.']
    }
  };
}

function governanceTraceDetailEnvelope(unsafeFixture = false) {
  return {
    status: 'trace_found',
    mutationOccurred: false,
    boundary: readOnlyBoundary(),
    warnings: ['Governance trace explorer is read-only.'],
    errors: [],
    data: {
      status: 'trace_found',
      trace: {
        summary: traceSummary(unsafeFixture),
        timeline: [
          {
            eventId: 'event-001',
            eventKind: 'approval.decision.recorded',
            sourceComponent: 'approval-ledger',
            safeSummary: unsafeFixture ? 'rawPrompt leaked in event summary' : 'Approval decision recorded as evidence.',
            recordedUtc: '2026-06-15T01:00:00Z',
            correlationId,
            causationId,
            subjectReferenceId: 'tool-request-123',
            RawCompletion: 'RawCompletion should not render'
          }
        ],
        relatedReferences: [
          {
            referenceKind: 'dogfood_receipt',
            referenceId: 'dogfood-123',
            safeSummary: 'Dogfood receipt was recorded as evidence.'
          }
        ],
        boundaryWarnings: ['Trace output is not workflow transition.']
      },
      issues: [],
      boundaryWarnings: ['Trace output is not approval.']
    }
  };
}

function validationErrorEnvelope() {
  return {
    status: 'validation_error',
    mutationOccurred: false,
    boundary: readOnlyBoundary(),
    warnings: ['Governance trace explorer is read-only.'],
    errors: [
      {
        code: 'missing_project_reference_id',
        field: 'projectReferenceId',
        message: 'projectReferenceId, correlationId, or causationId is required.'
      }
    ],
    data: {
      status: 'validation_error',
      traces: [],
      issues: [],
      boundaryWarnings: []
    }
  };
}

function traceSummary(unsafeFixture = false) {
  return {
    traceId: 'trace-001',
    projectReferenceId,
    workflowRunId: 'workflow-run-001',
    workflowStepId: 'workflow-step-001',
    correlationId,
    causationId,
    subjectReferenceId: 'tool-request-123',
    eventKind: 'approval.decision.recorded',
    sourceComponent: 'approval-ledger',
    safeSummary: unsafeFixture ? 'private reasoning leaked with RawPrompt PayloadJson SourceContent PatchPayload Secret Bearer token' : 'Approval decision recorded as evidence.',
    recordedUtc: '2026-06-15T01:00:00Z',
    isReadOnlyTrace: true,
    isAuthorityDecision: false,
    isApproval: false,
    isPolicySatisfaction: false,
    isWorkflowTransition: false,
    canApprove: false,
    canReject: false,
    canSatisfyPolicy: false,
    canTransitionWorkflow: false,
    canInvokeTool: false,
    canDispatchAgent: false,
    canCallModel: false,
    canPromoteMemory: false,
    canApplySource: false,
    PayloadJson: '{"RawToolOutput":"hidden"}',
    PrivateReasoning: 'private reasoning leaked',
    RawPrompt: 'raw prompt leaked',
    RawToolOutput: 'raw tool output leaked',
    SourceContent: 'source file content leaked',
    PatchPayload: 'patch payload leaked',
    Secret: 'Bearer not-for-ui'
  };
}

function secondTraceSummary() {
  return {
    ...traceSummary(false),
    traceId: 'trace-002',
    eventKind: 'tool_gate.decision.recorded',
    safeSummary: 'Gate decision recorded as evidence.'
  };
}

function readOnlyBoundary() {
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
