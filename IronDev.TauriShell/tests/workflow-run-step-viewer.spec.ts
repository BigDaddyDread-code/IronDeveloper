import { expect, test, type Page, type Route } from '@playwright/test';

const projectReferenceId = '10000000-0000-0000-0000-000000000157';
const workflowRunId = '20000000-0000-0000-0000-000000000157';
const workflowStepId = '30000000-0000-0000-0000-000000000157';
const correlationId = '40000000-0000-0000-0000-000000000157';
const causationId = '50000000-0000-0000-0000-000000000157';

test('WorkflowRunStepViewerPage_RendersReadOnlyBanner', async ({ page }) => {
  await openWorkflowViewerPage(page);

  await expect(page.getByRole('heading', { name: 'Workflow Run/Step Viewer' })).toBeVisible();
  await expect(page.getByTestId('workflow-runs.boundary-banner')).toContainText('Read-only view');
  await expect(page.getByTestId('workflow-runs.boundary-banner')).toContainText(
    'Workflow visibility is not workflow authority'
  );
  await expect(page.getByTestId('workflow-runs.boundary-banner')).toContainText(
    'Workflow status is not transition permission'
  );
  await expect(page.getByTestId('workflow-runs.boundary-banner')).toContainText('Step status is not execution permission');
  await expect(page.getByTestId('workflow-runs.boundary-banner')).toContainText('Refresh is not retry');
});

test('WorkflowRunStepViewerPage_RendersSearchFilters', async ({ page }) => {
  await openWorkflowViewerPage(page);

  for (const label of [
    'Project reference',
    'Workflow run ID',
    'Workflow step ID',
    'Correlation ID',
    'Workflow status',
    'Step status',
    'Workflow kind',
    'From UTC',
    'To UTC',
    'Take'
  ]) {
    await expect(page.getByLabel(label)).toBeVisible();
  }
});

test('WorkflowRunStepViewerPage_SearchRequiresProjectReference', async ({ page }) => {
  await openWorkflowViewerPage(page);
  await page.getByTestId('workflow-runs.search').click();

  await expect(page.getByTestId('workflow-runs.message')).toContainText('Project reference is required');
});

test('WorkflowRunStepViewerPage_RendersWorkflowRunList', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);

  await expect(page.getByTestId('workflow-runs.run-card')).toHaveCount(1);
  await expect(page.getByTestId('workflow-runs.list')).toContainText(workflowRunId);
  await expect(page.getByTestId('workflow-runs.list')).toContainText('Test Failure Review Candidate');
});

test('WorkflowRunStepViewerPage_RendersWorkflowStepList', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);

  await expect(page.getByTestId('workflow-runs.step-card')).toHaveCount(1);
  await expect(page.getByTestId('workflow-runs.steps')).toContainText(workflowStepId);
  await expect(page.getByTestId('workflow-runs.steps')).toContainText('Critic review package');
});

test('WorkflowRunStepViewerPage_OpenRunShowsSafeEvidence', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByTestId('workflow-runs.open-run').click();

  await expect(page.getByTestId('workflow-runs.evidence')).toContainText('workflow evidence');
  await expect(page.getByTestId('workflow-runs.grounding')).toContainText('workflow grounding');
});

test('WorkflowRunStepViewerPage_OpenStepShowsSafeSummary', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByTestId('workflow-runs.open-step').click();

  await expect(page.getByTestId('workflow-runs.step-detail')).toContainText('Safe critic package summary.');
});

test('WorkflowRunStepViewerPage_RendersBoundaryFooter', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);

  await expect(page.getByTestId('workflow-runs.footer')).toContainText(
    'This UI cannot start, continue, transition, retry, repair, execute workflow, invoke tools, dispatch agents, apply source, or release software.'
  );
});

test('WorkflowRunStepViewerPage_SearchUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { methods });

  expect(methods).toContain('GET');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('WorkflowRunStepViewerPage_OpenRunUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { methods });
  await page.getByTestId('workflow-runs.open-run').click();

  expect(methods.length).toBeGreaterThanOrEqual(3);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('WorkflowRunStepViewerPage_OpenStepUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { methods });
  await page.getByTestId('workflow-runs.open-step').click();

  expect(methods.length).toBeGreaterThanOrEqual(3);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('WorkflowRunStepViewerPage_RefreshUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { methods });
  await page.getByTestId('workflow-runs.refresh').click();

  expect(methods.length).toBeGreaterThanOrEqual(4);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('WorkflowRunStepViewerPage_ClearFiltersDoesNotCallApi', async ({ page }) => {
  const methods: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { methods });
  const countAfterSearch = methods.length;
  await page.getByTestId('workflow-runs.clear').click();

  expect(methods.length).toBe(countAfterSearch);
  await expect(page.getByTestId('workflow-runs.message')).toContainText('Filters cleared');
});

test('WorkflowRunStepViewerPage_CorrelationSearchUsesCorrelationReadEndpoint', async ({ page }) => {
  const requestedUrls: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { useCorrelationSearch: true, requestedUrls });

  expect(requestedUrls.some((url) => url.includes('/api/v1/workflow/runs/by-correlation/'))).toBe(true);
});

test('WorkflowRunStepViewerPage_RunIdSearchUsesRunDetailEndpoint', async ({ page }) => {
  const requestedUrls: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { requestedUrls });

  expect(requestedUrls.some((url) => url.includes(`/api/v1/workflow/runs/${workflowRunId}?`))).toBe(true);
});

test('WorkflowRunStepViewerPage_StepIdSearchUsesStepDetailEndpoint', async ({ page }) => {
  const requestedUrls: string[] = [];
  await openWorkflowViewerPageWithSearch(page, { includeStepFilter: true, requestedUrls });

  expect(requestedUrls.some((url) => url.includes(`/steps/${workflowStepId}?`))).toBe(true);
});

test('WorkflowRunStepViewerPage_EmptyStateDoesNotOfferControls', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page, { empty: true, useCorrelationSearch: true });

  await expect(page.getByRole('heading', { name: 'No workflow runs loaded' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Start' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry' })).toHaveCount(0);
});

test('WorkflowRunStepViewerPage_StatusFilterIsClientSideOnly', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByLabel('Workflow status').fill('missing-status');
  await page.getByTestId('workflow-runs.search').click();

  await expect(page.getByTestId('workflow-runs.run-card')).toHaveCount(0);
});

test('WorkflowRunStepViewerPage_StepStatusFilterIsClientSideOnly', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByLabel('Step status').fill('missing-step-status');
  await page.getByTestId('workflow-runs.search').click();

  await expect(page.getByTestId('workflow-runs.step-card')).toHaveCount(0);
});

test('WorkflowRunStepViewerPage_WorkflowKindFilterMatchesTypeOrName', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByLabel('Workflow kind').fill('test-failure');
  await page.getByTestId('workflow-runs.search').click();

  await expect(page.getByTestId('workflow-runs.run-card')).toHaveCount(1);
});

test('WorkflowRunStepViewerPage_DateFiltersDoNotCreateMutationCalls', async ({ page }) => {
  const methods: string[] = [];
  await openWorkflowViewerPage(page, { methods });
  await page.getByLabel('Project reference').fill(projectReferenceId);
  await page.getByLabel('From UTC').fill('2026-06-15T00:00:00Z');
  await page.getByLabel('To UTC').fill('2026-06-16T00:00:00Z');
  await page.getByTestId('workflow-runs.search').click();

  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('WorkflowRunStepViewerPage_TakeFilterIsBounded', async ({ page }) => {
  const requestedUrls: string[] = [];
  await openWorkflowViewerPage(page, { requestedUrls });
  await page.getByLabel('Project reference').fill(projectReferenceId);
  await page.getByLabel('Take').fill('5000');
  await page.getByTestId('workflow-runs.search').click();

  expect(requestedUrls.some((url) => url.includes('take=100'))).toBe(true);
});

test('WorkflowRunStepViewerPage_CopyWorkflowIdDoesNotGrantAuthority', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Workflow ID' }).click();

  await expect(page.getByTestId('workflow-runs.message')).toContainText('does not grant workflow authority');
});

test('WorkflowRunStepViewerPage_CopyStepIdDoesNotGrantAuthority', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Step ID' }).click();

  await expect(page.getByTestId('workflow-runs.message')).toContainText('does not grant workflow authority');
});

test('WorkflowRunStepViewerPage_CopyCorrelationIdDoesNotGrantAuthority', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Correlation ID' }).first().click();

  await expect(page.getByTestId('workflow-runs.message')).toContainText('does not grant workflow authority');
});

test('WorkflowRunStepViewerPage_OpenTraceIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Trace', '/governance/timeline?workflowRunId=');
});

test('WorkflowRunStepViewerPage_OpenTimelineIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Timeline', '/governance/timeline?workflowRunId=');
});

test('WorkflowRunStepViewerPage_OpenDiagnosisIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Diagnosis', '/workflow/diagnosis?workflowRunId=');
});

test('WorkflowRunStepViewerPage_OpenAgentHealthIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Agent Health', '/governance/agent-health?workflowRunId=');
});

test('WorkflowRunStepViewerPage_OpenToolGateLedgerIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Tool Gate Ledger', '/governance/tool-gates?workflowRunId=');
});

test('WorkflowRunStepViewerPage_OpenDogfoodReceiptsIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Dogfood Receipts', '/governance/dogfood-receipts?workflowRunId=');
});

test('WorkflowRunStepViewerPage_OpenApprovalPackagesIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Approval Packages', '/governance/approval-packages?workflowRunId=');
});

test('WorkflowRunStepViewerPage_UnsafeWorkflowMaterialIsRedacted', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page, { unsafeFixture: true });
  await page.getByTestId('workflow-runs.open-run').click();

  await expect(page.getByTestId('workflow-runs.workspace')).not.toContainText('RawPrompt');
  await expect(page.getByTestId('workflow-runs.workspace')).not.toContainText('PrivateReasoning');
  await expect(page.getByTestId('workflow-runs.workspace')).not.toContainText('PayloadJson');
  await expect(page.getByTestId('workflow-runs.workspace')).toContainText('[redacted workflow viewer text]');
});

test('WorkflowRunStepViewerPage_DoesNotRenderMutationControls', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);

  for (const label of ['Start', 'Continue', 'Transition', 'Execute', 'Retry', 'Repair', 'Approve', 'Apply', 'Release']) {
    await expect(page.getByRole('button', { name: label })).toHaveCount(0);
  }
});

test('WorkflowRunStepViewerPage_RendersApiBoundaryWarnings', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByTestId('workflow-runs.open-run').click();

  await expect(page.getByTestId('workflow-runs.boundary')).toContainText('Workflow visibility is not workflow authority');
  await expect(page.getByTestId('workflow-runs.boundary')).toContainText('Workflow read API is inspection only.');
});

test('WorkflowRunStepViewerPage_DisplaysApiIssuesAsReadIssues', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page, { withIssue: true });

  await expect(page.getByTestId('workflow-runs.issues')).toContainText('workflow_read_warning');
});

test('WorkflowRunStepViewerPage_NoWorkflowAuthorityLanguageAppearsInStatusMessage', async ({ page }) => {
  await openWorkflowViewerPageWithSearch(page);

  await expect(page.getByTestId('workflow-runs.message')).not.toContainText('approved');
  await expect(page.getByTestId('workflow-runs.message')).not.toContainText('permission granted');
});

async function expectRelatedPath(page: Page, button: string, expected: string) {
  await openWorkflowViewerPageWithSearch(page);
  await page.getByTestId('workflow-runs.open-run').click();
  await page.getByRole('button', { name: button }).click();

  await expect(page.getByTestId('workflow-runs.message')).toContainText(expected);
  await expect(page.getByTestId('workflow-runs.message')).toContainText('does not move workflow state');
}

async function openWorkflowViewerPageWithSearch(page: Page, options: MockWorkflowViewerOptions = {}) {
  await openWorkflowViewerPage(page, options);
  await page.getByLabel('Project reference').fill(projectReferenceId);

  if (options.useCorrelationSearch) {
    await page.getByLabel('Correlation ID').fill(correlationId);
  } else {
    await page.getByLabel('Workflow run ID').fill(options.empty ? '' : workflowRunId);
  }

  if (options.includeStepFilter) {
    await page.getByLabel('Workflow step ID').fill(workflowStepId);
  }

  await page.getByTestId('workflow-runs.search').click();
  await expect(page.getByTestId('workflow-runs.message')).toBeVisible();
}

async function openWorkflowViewerPage(page: Page, options: MockWorkflowViewerOptions = {}) {
  await seedShellContext(page);
  await mockWorkflowViewerApi(page, options);
  await page.goto('/workflows/runs');
  await expect(page.getByTestId('workflow-runs.workspace')).toBeVisible();
}

async function seedShellContext(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: async () => undefined },
      configurable: true
    });
  });

  await page.route('**/irondev-api/health', async (route) => {
    await fulfillJson(route, 200, { status: 'healthy' });
  });

  await page.route('**/irondev-api/api/environment', async (route) => {
    await fulfillJson(route, 200, {
      environment: 'LocalTest',
      version: 'PR157',
      features: ['workflow-run-step-viewer']
    });
  });

  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await fulfillJson(route, 200, { userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 });
  });

  await page.route('**/irondev-api/api/tenants**', async (route) => {
    await fulfillJson(route, 200, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]);
  });

  await page.route('**/irondev-api/api/projects', async (route) => {
    await fulfillJson(route, 200, [{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Workflow project' }]);
  });

  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => {
    await fulfillJson(route, 200, { projectId: 7 });
  });
}

interface MockWorkflowViewerOptions {
  empty?: boolean;
  unsafeFixture?: boolean;
  withIssue?: boolean;
  useCorrelationSearch?: boolean;
  includeStepFilter?: boolean;
  methods?: string[];
  requestedUrls?: string[];
}

async function mockWorkflowViewerApi(page: Page, options: MockWorkflowViewerOptions) {
  await page.route('**/irondev-api/api/v1/workflow/runs**', async (route) => {
    options.methods?.push(route.request().method());
    options.requestedUrls?.push(route.request().url());
    const pathname = new URL(route.request().url()).pathname;

    if (pathname.includes('/steps/')) {
      await fulfillJson(route, 200, stepDetailEnvelope(options));
      return;
    }

    if (pathname.endsWith('/steps')) {
      await fulfillJson(route, 200, stepListEnvelope(options));
      return;
    }

    if (pathname.includes('/by-correlation/')) {
      await fulfillJson(route, 200, runListEnvelope(options));
      return;
    }

    if (pathname.endsWith(`/runs/${workflowRunId}`)) {
      await fulfillJson(route, 200, runDetailEnvelope(options));
      return;
    }

    await fulfillJson(route, 200, runListEnvelope(options));
  });
}

async function fulfillJson(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function runListEnvelope(options: MockWorkflowViewerOptions) {
  return {
    status: 'workflow_runs_found',
    mutationOccurred: false,
    humanApprovalRequired: true,
    boundary: readOnlyWorkflowBoundary(),
    warnings: ['Workflow read API is inspection only.'],
    errors: options.withIssue ? [{ code: 'workflow_read_warning', message: 'Read warning only.', severity: 'warning' }] : [],
    data: {
      runs: options.empty ? [] : [runSummary(options.unsafeFixture)],
      issues: options.withIssue ? [{ code: 'workflow_read_warning', message: 'Read warning only.', severity: 'warning' }] : []
    }
  };
}

function runDetailEnvelope(options: MockWorkflowViewerOptions) {
  return {
    status: 'workflow_run_found',
    mutationOccurred: false,
    humanApprovalRequired: true,
    boundary: readOnlyWorkflowBoundary(),
    warnings: ['Workflow read API is inspection only.'],
    errors: options.withIssue ? [{ code: 'workflow_read_warning', message: 'Read warning only.', severity: 'warning' }] : [],
    data: runDetail(options.unsafeFixture)
  };
}

function stepListEnvelope(options: MockWorkflowViewerOptions) {
  return {
    status: 'workflow_steps_found',
    mutationOccurred: false,
    humanApprovalRequired: true,
    boundary: readOnlyWorkflowBoundary(),
    warnings: ['Workflow read API is inspection only.'],
    errors: options.withIssue ? [{ code: 'workflow_read_warning', message: 'Read warning only.', severity: 'warning' }] : [],
    data: {
      steps: options.empty ? [] : [stepSummary(options.unsafeFixture)],
      issues: options.withIssue ? [{ code: 'workflow_read_warning', message: 'Read warning only.', severity: 'warning' }] : []
    }
  };
}

function stepDetailEnvelope(options: MockWorkflowViewerOptions) {
  return {
    status: 'workflow_step_found',
    mutationOccurred: false,
    humanApprovalRequired: true,
    boundary: readOnlyWorkflowBoundary(),
    warnings: ['Workflow read API is inspection only.'],
    errors: [],
    data: stepDetail(options.unsafeFixture)
  };
}

function runSummary(unsafeFixture = false) {
  return {
    workflowRunId,
    projectId: projectReferenceId,
    workflowType: 'test-failure-review',
    workflowName: unsafeFixture ? 'RawPrompt PayloadJson PrivateReasoning' : 'Test Failure Review Candidate',
    status: 'blocked_review_required',
    subjectType: 'test-failure',
    subjectId: 'test-failure-157',
    correlationId,
    causationId,
    stepCount: 1,
    evidenceReferenceCount: 1,
    groundingReferenceCount: 1,
    authorityFlags: safeAuthorityFlags(),
    createdUtc: '2026-06-15T03:00:00Z'
  };
}

function runDetail(unsafeFixture = false) {
  return {
    ...runSummary(unsafeFixture),
    subjectSummary: unsafeFixture ? 'RawPrompt leaked with PrivateReasoning PayloadJson' : 'Safe workflow subject summary.',
    steps: [stepSummary(unsafeFixture)],
    evidenceReferences: [evidenceReference(unsafeFixture)],
    groundingReferences: [groundingReference(unsafeFixture)]
  };
}

function stepSummary(unsafeFixture = false) {
  return {
    workflowRunStepId: workflowStepId,
    workflowRunId,
    projectId: projectReferenceId,
    stepKey: 'critic.review.package',
    stepName: unsafeFixture ? 'RawToolOutput PrivateReasoning PayloadJson' : 'Critic review package',
    stepType: 'review-package',
    status: 'ready_for_human_review',
    sequenceNumber: 1,
    agentRole: 'IndependentCriticAgent',
    agentId: 'critic-agent-157',
    subjectType: 'test-failure',
    subjectId: 'test-failure-157',
    correlationId,
    causationId,
    evidenceReferenceCount: 1,
    groundingReferenceCount: 1,
    authorityFlags: safeAuthorityFlags(),
    createdUtc: '2026-06-15T03:01:00Z'
  };
}

function stepDetail(unsafeFixture = false) {
  return {
    ...stepSummary(unsafeFixture),
    safeSummary: unsafeFixture ? 'RawCompletion PrivateReasoning entirePatch' : 'Safe critic package summary.',
    evidenceReferences: [evidenceReference(unsafeFixture)],
    groundingReferences: [groundingReference(unsafeFixture)]
  };
}

function evidenceReference(unsafeFixture = false) {
  return {
    evidenceReferenceId: 'workflow-evidence-157',
    evidenceId: 'evidence-157',
    evidenceType: 'workflow-evidence',
    evidenceLabel: 'workflow evidence',
    safeSummary: unsafeFixture ? 'RawPrompt PrivateReasoning PayloadJson' : 'Evidence says review is required.',
    isApproval: false,
    isExecutionPermission: false,
    isPolicySatisfaction: false,
    isWorkflowTransition: false,
    isMemoryPromotion: false,
    isSourceApply: false
  };
}

function groundingReference(unsafeFixture = false) {
  return {
    groundingReferenceId: 'workflow-grounding-157',
    groundingId: 'grounding-157',
    groundingType: 'workflow-grounding',
    claim: 'workflow grounding',
    safeSummary: unsafeFixture ? 'RawToolOutput PrivateReasoning PayloadJson' : 'Grounding is context only.',
    groundingIsAuthority: false
  };
}

function safeAuthorityFlags() {
  return {
    createsApproval: false,
    satisfiesApproval: false,
    grantsExecutionPermission: false,
    transitionsWorkflow: false,
    invokesTool: false,
    dispatchesAgent: false,
    mutatesSource: false,
    appliesPatch: false,
    promotesMemory: false,
    activatesRetrieval: false,
    releasesSoftware: false,
    createsAuthority: false,
    containsRawPrivateReasoning: false
  };
}

function readOnlyWorkflowBoundary() {
  return {
    readOnlyInspection: true,
    workflowStatusIsAction: false,
    evidenceIsPermission: false,
    groundingIsAuthority: false,
    endpointAccessIsExecutionPermission: false,
    apiResponseStatusIsGovernance: false,
    modelOutputIsAuthority: false,
    sourceApplied: false,
    memoryPromoted: false,
    releaseApproved: false,
    approvalSatisfied: false,
    humanReviewRequiredForSourceApply: true,
    humanReviewRequiredForMemoryPromotion: true
  };
}
