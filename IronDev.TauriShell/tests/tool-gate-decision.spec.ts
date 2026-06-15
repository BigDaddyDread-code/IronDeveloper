import { expect, test, type Page, type Route } from '@playwright/test';

const projectReferenceId = '42';
const workflowRunId = 'workflow-run-154';
const workflowStepId = 'workflow-step-154';
const toolRequestId = 'tool-request-154';
const gateDecisionId = 'tool-gate-154';
const correlationId = 'correlation-154';
const causationId = 'causation-154';

test('ToolGateDecisionPage_RendersReadOnlyBanner', async ({ page }) => {
  await openToolGateDecisionPage(page);

  await expect(page.getByRole('heading', { name: 'Tool Requests and Gate Decisions' })).toBeVisible();
  await expect(page.getByTestId('tool-gates.readonlyBanner')).toContainText('Read-only view');
  await expect(page.getByTestId('tool-gates.readonlyBanner')).toContainText('Tool request visibility is not tool execution');
  await expect(page.getByTestId('tool-gates.readonlyBanner')).toContainText('Gate decision visibility is not gate authority');
  await expect(page.getByTestId('tool-gates.readonlyBanner')).toContainText('Approval requirement is not approval');
  await expect(page.getByTestId('tool-gates.readonlyBanner')).toContainText('Policy evidence is not policy satisfaction');
});

test('ToolGateDecisionPage_RendersToolRequests', async ({ page }) => {
  await openToolGateDecisionPageWithSearch(page);

  await expect(page.getByTestId('tool-gates.requestItem')).toHaveCount(1);
  await expect(page.getByTestId('tool-gates.requests')).toContainText('workspace.diff');
  await expect(page.getByTestId('tool-gates.requests')).toContainText('Tool request recorded as evidence.');
});

test('ToolGateDecisionPage_RendersGateDecisions', async ({ page }) => {
  await openToolGateDecisionPageWithSearch(page);

  await expect(page.getByTestId('tool-gates.decisionItem')).toHaveCount(1);
  await expect(page.getByTestId('tool-gates.decisions')).toContainText('Gate decision recorded as evidence.');
  await expect(page.getByTestId('tool-gates.decisions')).toContainText('human_review_required');
});

test('ToolGateDecisionPage_RendersSafeSummaryOnly', async ({ page }) => {
  await openToolGateDecisionPageWithSearch(page, { unsafeFixture: true });

  await expect(page.getByTestId('tool-gates.requests')).toContainText('[redacted tool gate text]');
  await expect(page.locator('body')).not.toContainText('private reasoning leaked');
});

test('ToolGateDecisionPage_RendersWorkflowAndCorrelationReferences', async ({ page }) => {
  await openToolGateDecisionPageWithSearch(page);

  await expect(page.getByTestId('tool-gates.safeDetail')).toContainText(workflowRunId);
  await expect(page.getByTestId('tool-gates.safeDetail')).toContainText(workflowStepId);
  await expect(page.getByTestId('tool-gates.safeDetail')).toContainText(correlationId);
  await expect(page.getByTestId('tool-gates.safeDetail')).toContainText(causationId);
});

test('ToolGateDecisionPage_RendersEmptyState', async ({ page }) => {
  await openToolGateDecisionPageWithSearch(page, { empty: true });

  await expect(page.getByRole('heading', { name: 'No tool request evidence' })).toBeVisible();
  await expect(page.getByTestId('tool-gates.status')).toContainText('No tool request or gate decision evidence found for the selected filters.');
});

test('ToolGateDecisionPage_RendersValidationErrorState', async ({ page }) => {
  await openToolGateDecisionPageWithSearch(page, { validationError: true });

  await expect(page.getByTestId('tool-gates.validationError')).toBeVisible();
  await expect(page.getByTestId('tool-gates.validationError')).toContainText('projectId');
});

test('ToolGateDecisionPage_SearchUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openToolGateDecisionPageWithSearch(page, { methods });

  expect(methods).toContain('GET');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('ToolGateDecisionPage_RefreshUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openToolGateDecisionPageWithSearch(page, { methods });
  await page.getByTestId('tool-gates.filters').getByRole('button', { name: 'Refresh' }).click();

  expect(methods.length).toBeGreaterThanOrEqual(4);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('ToolGateDecisionPage_DoesNotRenderApproveButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Approve'));
test('ToolGateDecisionPage_DoesNotRenderRejectButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Reject'));
test('ToolGateDecisionPage_DoesNotRenderAllowButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Allow'));
test('ToolGateDecisionPage_DoesNotRenderDenyButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Deny'));
test('ToolGateDecisionPage_DoesNotRenderOverrideButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Override'));
test('ToolGateDecisionPage_DoesNotRenderReopenGateButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Reopen Gate'));
test('ToolGateDecisionPage_DoesNotRenderExecuteToolButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Execute Tool'));
test('ToolGateDecisionPage_DoesNotRenderInvokeToolButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Invoke Tool'));
test('ToolGateDecisionPage_DoesNotRenderDispatchAgentButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Dispatch Agent'));
test('ToolGateDecisionPage_DoesNotRenderContinueWorkflowButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Continue Workflow'));
test('ToolGateDecisionPage_DoesNotRenderSatisfyPolicyButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Satisfy Policy'));
test('ToolGateDecisionPage_DoesNotRenderApplySourceButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Apply Source'));
test('ToolGateDecisionPage_DoesNotRenderCleanupButton', async ({ page }) => await expectForbiddenButtonNotRendered(page, 'Cleanup'));

test('ToolGateDecisionPage_DoesNotExposePayloadJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PayloadJson'));
test('ToolGateDecisionPage_DoesNotExposeToolInputJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'ToolInputJson'));
test('ToolGateDecisionPage_DoesNotExposeToolOutputJson', async ({ page }) => await expectForbiddenTextNotRendered(page, 'ToolOutputJson'));
test('ToolGateDecisionPage_DoesNotExposePrivateReasoning', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PrivateReasoning'));
test('ToolGateDecisionPage_DoesNotExposeRawPrompt', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawPrompt'));
test('ToolGateDecisionPage_DoesNotExposeRawCompletion', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawCompletion'));
test('ToolGateDecisionPage_DoesNotExposeRawToolOutput', async ({ page }) => await expectForbiddenTextNotRendered(page, 'RawToolOutput'));
test('ToolGateDecisionPage_DoesNotExposeSourceContent', async ({ page }) => await expectForbiddenTextNotRendered(page, 'SourceContent'));
test('ToolGateDecisionPage_DoesNotExposePatchPayload', async ({ page }) => await expectForbiddenTextNotRendered(page, 'PatchPayload'));
test('ToolGateDecisionPage_DoesNotExposeSecrets', async ({ page }) => {
  await expectForbiddenTextNotRendered(page, 'Secret');
  await expect(page.locator('body')).not.toContainText('Bearer');
});

async function expectForbiddenButtonNotRendered(page: Page, label: string) {
  await openToolGateDecisionPageWithSearch(page);
  await expect(page.getByRole('button', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
}

async function expectForbiddenTextNotRendered(page: Page, marker: string) {
  await openToolGateDecisionPageWithSearch(page, { unsafeFixture: true });
  await expect(page.locator('body')).not.toContainText(marker);
}

async function openToolGateDecisionPageWithSearch(page: Page, options: MockToolGateOptions = {}) {
  await openToolGateDecisionPage(page, options);
  await page.getByLabel('Project reference').fill(projectReferenceId);
  await page.getByLabel('Tool request id').fill(options.empty ? '' : toolRequestId);
  await page.getByLabel('Gate decision id').fill(options.empty ? '' : gateDecisionId);
  await page.getByRole('button', { name: 'Search' }).click();
  await expect(page.getByTestId('tool-gates.status')).not.toContainText('Loading tool request and gate decision evidence');
}

async function openToolGateDecisionPage(page: Page, options: MockToolGateOptions = {}) {
  await seedShellContext(page);
  await mockToolGateApi(page, options);
  await page.goto('/governance/tool-gates');
  await expect(page.getByTestId('tool-gates.workspace')).toBeVisible();
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

  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
}

interface MockToolGateOptions {
  empty?: boolean;
  validationError?: boolean;
  unsafeFixture?: boolean;
  methods?: string[];
}

async function mockToolGateApi(page: Page, options: MockToolGateOptions) {
  await page.route('**/irondev-api/api/v1/tool-requests/**', async (route) => {
    options.methods?.push(route.request().method());

    if (options.validationError) {
      await fulfillJson(route, 400, validationErrorEnvelope());
      return;
    }

    await fulfillJson(route, 200, toolRequestEnvelope(options.unsafeFixture));
  });

  await page.route('**/irondev-api/api/v1/tool-gates/evaluations/**', async (route) => {
    options.methods?.push(route.request().method());
    await fulfillJson(route, 200, toolGateEnvelope(options.unsafeFixture));
  });
}

async function fulfillJson(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function toolRequestEnvelope(unsafeFixture = false) {
  return {
    status: 'tool_request_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyBoundary(),
    warnings: ['Tool request visibility is not tool execution.'],
    errors: [],
    data: {
      toolRequest: toolRequest(unsafeFixture),
      issues: []
    }
  };
}

function toolGateEnvelope(unsafeFixture = false) {
  return {
    status: 'tool_gate_decision_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyBoundary(),
    warnings: ['Gate decision visibility is not gate authority.'],
    errors: [],
    data: {
      gateDecision: toolGateDecision(unsafeFixture),
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
    warnings: ['Tool request inspection is read-only.'],
    errors: [
      {
        code: 'missing_project_id',
        field: 'projectId',
        message: 'projectId is required.'
      }
    ],
    data: {
      issues: []
    }
  };
}

function toolRequest(unsafeFixture = false) {
  return {
    toolRequestId,
    projectReferenceId,
    workflowRunId,
    workflowStepId,
    correlationId,
    requestedToolName: 'workspace.diff',
    requestedCapability: 'ReadWorkspaceEvidence',
    requestedOperation: 'diffEvidence',
    requestStatus: 'PendingGate',
    sourceComponent: 'TesterAgent',
    createdUtc: '2026-06-15T01:00:00Z',
    subjectReference: 'apply-preview-154',
    safeSummary: unsafeFixture ? 'private reasoning leaked with PayloadJson ToolInputJson RawPrompt Secret Bearer token' : 'Tool request recorded as evidence.',
    PayloadJson: '{"ToolOutputJson":"hidden"}',
    ToolInputJson: '{"RawPrompt":"hidden"}',
    ToolOutputJson: '{"RawCompletion":"hidden"}',
    PrivateReasoning: 'private reasoning leaked',
    RawPrompt: 'raw prompt leaked',
    RawCompletion: 'raw completion leaked',
    RawToolOutput: 'raw tool output leaked',
    SourceContent: 'source content leaked',
    PatchPayload: 'patch payload leaked',
    Secret: 'Bearer not-for-ui'
  };
}

function toolGateDecision(unsafeFixture = false) {
  return {
    decisionId: gateDecisionId,
    toolRequestId,
    decisionStatus: 'human_review_required',
    policyOutcomeSummary: 'Policy evidence recorded only.',
    approvalRequirementSummary: 'Human approval remains required.',
    safeReason: 'Gate decision requires human review before tool execution.',
    decidedUtc: '2026-06-15T01:05:00Z',
    correlationId,
    causationId,
    subjectReference: 'apply-preview-154',
    safeSummary: unsafeFixture ? 'RawToolOutput PatchPayload SourceContent Password leaked' : 'Gate decision recorded as evidence.',
    DecisionPayloadJson: '{"RawToolOutput":"hidden"}',
    RawToolOutput: 'raw tool output leaked',
    PatchPayload: 'patch payload leaked',
    SourceContent: 'source content leaked',
    Password: 'not-for-ui'
  };
}

function readOnlyBoundary() {
  return {
    readOnly: true,
    durable: true,
    mutationOccurred: false,
    requestVisibilityIsExecutionPermission: false,
    gateDecisionVisibilityIsAuthority: false,
    approvalRequirementIsApproval: false,
    policyEvidenceIsPolicySatisfaction: false,
    gateStatusIsToolInvocation: false,
    canApprove: false,
    canReject: false,
    canOverrideGate: false,
    canReopenGate: false,
    canSatisfyPolicy: false,
    canExecuteTool: false,
    canInvokeTool: false,
    canDispatchAgent: false,
    canTransitionWorkflow: false,
    canApplySource: false,
    canApplyPatch: false
  };
}