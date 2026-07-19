import { expect, test, type Page, type Route } from '@playwright/test';

const projectReferenceId = 'project-pr158';
const workflowRunId = 'workflow-run-pr158';
const workflowStepId = 'workflow-step-pr158';
const memoryProposalId = 'memory-proposal-pr158';
const traceId = 'trace-memory-proposal-pr158';
const correlationId = 'correlation-pr158';
const causationId = 'causation-pr158';

test('MemoryProposalReviewPage_RendersReadOnlyBanner', async ({ page }) => {
  await openMemoryProposalPage(page);

  await expect(page.getByRole('heading', { name: 'Memory Proposal Review' })).toBeVisible();
  await expect(page.getByTestId('memory-proposals.readonly-banner')).toContainText('Read-only view');
  await expect(page.getByTestId('memory-proposals.readonly-banner')).toContainText('Memory proposal is not accepted memory');
  await expect(page.getByTestId('memory-proposals.readonly-banner')).toContainText('Proposed memory summary is not memory');
  await expect(page.getByTestId('memory-proposals.readonly-banner')).toContainText('Memory review is not memory promotion');
  await expect(page.getByTestId('memory-proposals.readonly-banner')).toContainText('Retrieval candidate is not retrieval activation');
});

test('MemoryProposalReviewPage_RendersMemoryProposals', async ({ page }) => {
  await openMemoryProposalPageWithSearch(page);

  await expect(page.getByTestId('memory-proposals.item')).toHaveCount(1);
  await expect(page.getByTestId('memory-proposals.list')).toContainText(memoryProposalId);
  await expect(page.getByTestId('memory-proposals.list')).toContainText('Review deterministic memory proposal evidence.');
});

test('MemoryProposalReviewPage_RendersProposalDetail', async ({ page }) => {
  await openProposalDetail(page);

  await expect(page.getByTestId('memory-proposals.safe-detail')).toContainText(memoryProposalId);
  await expect(page.getByTestId('memory-proposals.safe-detail')).toContainText('cross-project-candidate');
  await expect(page.getByTestId('memory-proposals.safe-detail')).toContainText('review-required');
});

test('MemoryProposalReviewPage_RendersSafeSummaryOnly', async ({ page }) => {
  await openProposalDetail(page, { unsafeFixture: true });

  await expect(page.getByTestId('memory-proposals.workspace')).toContainText('[redacted memory proposal review text]');
  await expect(page.getByTestId('memory-proposals.workspace')).not.toContainText('RawMemoryText');
});

test('MemoryProposalReviewPage_RendersEvidenceReferences', async ({ page }) => {
  await openProposalDetail(page);

  await expect(page.getByTestId('memory-proposals.evidence')).toContainText('memory.proposal.staged');
  await expect(page.getByTestId('memory-proposals.evidence')).toContainText('Evidence supports human review only.');
});

test('MemoryProposalReviewPage_RendersConfidentialityWarnings', async ({ page }) => {
  await openProposalDetail(page);

  await expect(page.getByTestId('memory-proposals.confidentiality')).toContainText('Confidentiality review remains required');
});

test('MemoryProposalReviewPage_RendersPortabilityWarnings', async ({ page }) => {
  await openProposalDetail(page);

  await expect(page.getByTestId('memory-proposals.portability')).toContainText('candidate-only');
});

test('MemoryProposalReviewPage_RendersWorkflowAndCorrelationReferences', async ({ page }) => {
  await openProposalDetail(page);

  await expect(page.getByTestId('memory-proposals.safe-detail')).toContainText(workflowRunId);
  await expect(page.getByTestId('memory-proposals.safe-detail')).toContainText(workflowStepId);
  await expect(page.getByTestId('memory-proposals.safe-detail')).toContainText(correlationId);
});

test('MemoryProposalReviewPage_RendersEmptyState', async ({ page }) => {
  await openMemoryProposalPageWithSearch(page, { empty: true });

  await expect(page.getByRole('heading', { name: 'No memory proposal evidence found' })).toBeVisible();
  await expect(page.getByTestId('memory-proposals.message')).toContainText('No memory proposal evidence found for the selected filters.');
});

test('MemoryProposalReviewPage_RendersValidationErrorState', async ({ page }) => {
  await openMemoryProposalPage(page);
  await page.getByLabel('Project reference').fill('');
  await page.getByLabel('Memory proposal id').fill('');
  await page.getByTestId('memory-proposals.search').click();

  await expect(page.getByTestId('memory-proposals.message')).toContainText('Memory Proposal Review needs');
});

test('MemoryProposalReviewPage_SearchUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openMemoryProposalPageWithSearch(page, { methods });

  expect(methods).toContain('GET');
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('MemoryProposalReviewPage_RefreshUsesGetOnly', async ({ page }) => {
  const methods: string[] = [];
  await openMemoryProposalPageWithSearch(page, { methods });
  await page.getByTestId('memory-proposals.refresh').click();

  expect(methods.length).toBeGreaterThanOrEqual(2);
  expect(methods.every((method) => method === 'GET')).toBe(true);
});

test('MemoryProposalReviewPage_DoesNotRenderAcceptMemoryButton', async ({ page }) => {
  await expectNoButton(page, 'Accept Memory');
});

test('MemoryProposalReviewPage_DoesNotRenderPromoteMemoryButton', async ({ page }) => {
  await expectNoButton(page, 'Promote Memory');
});

test('MemoryProposalReviewPage_DoesNotRenderWriteMemoryButton', async ({ page }) => {
  await expectNoButton(page, 'Write Memory');
});

test('MemoryProposalReviewPage_DoesNotRenderActivateRetrievalButton', async ({ page }) => {
  await expectNoButton(page, 'Activate Retrieval');
});

test('MemoryProposalReviewPage_DoesNotRenderApproveCrossProjectLearningButton', async ({ page }) => {
  await expectNoButton(page, 'Approve Cross-project Learning');
});

test('MemoryProposalReviewPage_DoesNotRenderContinueWorkflowButton', async ({ page }) => {
  await expectNoButton(page, 'Continue Workflow');
});

test('MemoryProposalReviewPage_DoesNotRenderInvokeToolButton', async ({ page }) => {
  await expectNoButton(page, 'Invoke Tool');
});

test('MemoryProposalReviewPage_DoesNotRenderDispatchAgentButton', async ({ page }) => {
  await expectNoButton(page, 'Dispatch Agent');
});

test('MemoryProposalReviewPage_DoesNotRenderApplySourceButton', async ({ page }) => {
  await expectNoButton(page, 'Apply Source');
});

test('MemoryProposalReviewPage_DoesNotExposePayloadJson', async ({ page }) => {
  await expectNoUnsafeText(page, 'PayloadJson');
});

test('MemoryProposalReviewPage_DoesNotExposeMemoryPayloadJson', async ({ page }) => {
  await expectNoUnsafeText(page, 'MemoryPayloadJson');
});

test('MemoryProposalReviewPage_DoesNotExposeRawMemoryText', async ({ page }) => {
  await expectNoUnsafeText(page, 'RawMemoryText');
});

test('MemoryProposalReviewPage_DoesNotExposePrivateReasoning', async ({ page }) => {
  await expectNoUnsafeText(page, 'PrivateReasoning');
});

test('MemoryProposalReviewPage_DoesNotExposeRawPrompt', async ({ page }) => {
  await expectNoUnsafeText(page, 'RawPrompt');
});

test('MemoryProposalReviewPage_DoesNotExposeSourceContent', async ({ page }) => {
  await expectNoUnsafeText(page, 'SourceContent');
});

test('MemoryProposalReviewPage_DoesNotExposePatchPayload', async ({ page }) => {
  await expectNoUnsafeText(page, 'PatchPayload');
});

test('MemoryProposalReviewPage_DoesNotExposeSecrets', async ({ page }) => {
  await expectNoUnsafeText(page, 'Secret Bearer Token ApiKey Credential');
});

test('MemoryProposalReviewPage_DoesNotExposeConfidentialClientDetail', async ({ page }) => {
  await expectNoUnsafeText(page, 'ConfidentialClientDetail');
});

test('MemoryProposalReviewPage_CopyProposalIdIsNotAcceptance', async ({ page }) => {
  await openMemoryProposalPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Proposal ID' }).click();

  await expect(page.getByTestId('memory-proposals.message')).toContainText('Copy proposal id is not acceptance');
});

test('MemoryProposalReviewPage_CopyCorrelationIdIsNotMemoryAuthority', async ({ page }) => {
  await openMemoryProposalPageWithSearch(page);
  await page.getByRole('button', { name: 'Copy Correlation ID' }).click();

  await expect(page.getByTestId('memory-proposals.message')).toContainText('Copy proposal id is not acceptance');
});

test('MemoryProposalReviewPage_OpenTraceIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Trace', '/governance/timeline?correlationId=');
});

test('MemoryProposalReviewPage_OpenTimelineIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Timeline', '/governance/timeline?correlationId=');
});

test('MemoryProposalReviewPage_OpenWorkflowIsReadOnlyNavigation', async ({ page }) => {
  await expectRelatedPath(page, 'Open Workflow', '/workflows/runs?workflowRunId=');
});

test('MemoryProposalReviewPage_RendersBoundaryFooter', async ({ page }) => {
  await openMemoryProposalPageWithSearch(page);

  await expect(page.getByTestId('memory-proposals.footer')).toContainText(
    'This UI cannot accept memory, promote memory, write memory, activate retrieval, approve cross-project learning, transition workflow, invoke tools, dispatch agents, apply source, or release software.'
  );
});

test('MemoryProposalReviewPage_ClearFiltersDoesNotCallApi', async ({ page }) => {
  const methods: string[] = [];
  await openMemoryProposalPageWithSearch(page, { methods });
  const countAfterSearch = methods.length;
  await page.getByTestId('memory-proposals.clear').click();

  expect(methods.length).toBe(countAfterSearch);
  await expect(page.getByTestId('memory-proposals.message')).toContainText('Filters cleared');
});

async function expectNoButton(page: Page, name: string) {
  await openMemoryProposalPageWithSearch(page);
  await expect(page.getByRole('button', { name })).toHaveCount(0);
}

async function expectNoUnsafeText(page: Page, text: string) {
  await openProposalDetail(page, { unsafeFixture: true });
  await expect(page.getByTestId('memory-proposals.workspace')).not.toContainText(text);
}

async function expectRelatedPath(page: Page, button: string, expected: string) {
  await openProposalDetail(page);
  await page.getByRole('button', { name: button }).click();

  await expect(page.getByTestId('memory-proposals.message')).toContainText(expected);
  await expect(page.getByTestId('memory-proposals.message')).toContainText('Navigation is not memory activation');
}

async function openProposalDetail(page: Page, options: MockMemoryProposalOptions = {}) {
  await openMemoryProposalPageWithSearch(page, options);
  await page.getByTestId('memory-proposals.open-proposal').click();
  await expect(page.getByTestId('memory-proposals.safe-detail')).toBeVisible();
}

async function openMemoryProposalPageWithSearch(page: Page, options: MockMemoryProposalOptions = {}) {
  await openMemoryProposalPage(page, options);
  await page.getByLabel('Project reference').fill(projectReferenceId);
  await page.getByLabel('Memory proposal id').fill(options.empty ? '' : memoryProposalId);
  await page.getByTestId('memory-proposals.search').click();
  await expect(page.getByTestId('memory-proposals.message')).toBeVisible();
}

async function openMemoryProposalPage(page: Page, options: MockMemoryProposalOptions = {}) {
  await seedShellContext(page);
  await mockMemoryProposalApi(page, options);
  await page.goto('/governance/memory-proposals');
  await expect(page.getByTestId('memory-proposals.workspace')).toBeVisible();
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
      version: 'PR158',
      features: ['memory-proposal-review']
    });
  });

  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await fulfillJson(route, 200, { userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 });
  });

  await page.route('**/irondev-api/api/tenants**', async (route) => {
    await fulfillJson(route, 200, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]);
  });

  await page.route('**/irondev-api/api/projects', async (route) => {
    await fulfillJson(route, 200, [{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Memory proposal project' }]);
  });

  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => {
    await fulfillJson(route, 200, { projectId: 7 });
  });
}

interface MockMemoryProposalOptions {
  empty?: boolean;
  unsafeFixture?: boolean;
  methods?: string[];
}

async function mockMemoryProposalApi(page: Page, options: MockMemoryProposalOptions) {
  await page.route('**/irondev-api/api/v1/governance/traces**', async (route) => {
    options.methods?.push(route.request().method());
    const pathname = new URL(route.request().url()).pathname;

    if (pathname.endsWith(`/${traceId}`) || pathname.includes('/by-correlation/')) {
      await fulfillJson(route, 200, traceDetailEnvelope(options));
      return;
    }

    await fulfillJson(route, 200, traceListEnvelope(options));
  });
}

async function fulfillJson(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function traceListEnvelope(options: MockMemoryProposalOptions) {
  return {
    status: 'governance_traces_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyTraceBoundary(),
    warnings: ['Memory proposal review uses governance trace evidence only.'],
    errors: [],
    data: {
      traces: options.empty ? [] : [summaryTrace(options.unsafeFixture)],
      issues: [],
      boundaryWarnings: ['Memory proposal is not accepted memory.']
    }
  };
}

function traceDetailEnvelope(options: MockMemoryProposalOptions) {
  return {
    status: 'governance_trace_found',
    mutationOccurred: false,
    durable: true,
    boundary: readOnlyTraceBoundary(),
    warnings: ['Memory proposal trace detail is read-only.'],
    errors: [],
    data: {
      trace: {
        summary: summaryTrace(options.unsafeFixture),
        timeline: [
          {
            eventId: 'event-memory-proposal-pr158',
            eventKind: 'memory.proposal.staged',
            sourceComponent: 'MemoryProposalStagingStore',
            safeSummary: options.unsafeFixture
              ? 'RawMemoryText PayloadJson MemoryPayloadJson RawPrompt PrivateReasoning SourceContent PatchPayload Secret Bearer Token ApiKey Credential ConfidentialClientDetail'
              : 'Rationale: Evidence supports human review only. Confidential portable candidate needs scope review.',
            recordedUtc: '2026-06-15T04:00:00Z',
            correlationId,
            causationId,
            subjectReferenceId: memoryProposalId
          }
        ],
        relatedReferences: [
          {
            referenceKind: 'evidence_reference',
            referenceId: 'evidence-memory-proposal-pr158',
            safeSummary: options.unsafeFixture
              ? 'RawCompletion RawToolOutput HiddenReasoning ChainOfThought Scratchpad EmployerDetail'
              : 'Evidence supports human review only.'
          },
          {
            referenceKind: 'governance_trace',
            referenceId: traceId,
            safeSummary: 'Trace reference is evidence only.'
          }
        ],
        boundaryWarnings: [
          'Memory proposal is not accepted memory.',
          'Memory review is not memory promotion.',
          'Retrieval candidate is not retrieval activation.'
        ]
      },
      issues: [],
      boundaryWarnings: ['Cross-project learning suggestion is not cross-project authority.']
    }
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
    subjectReferenceId: memoryProposalId,
    eventKind: 'memory.proposal.staged',
    sourceComponent: 'MemoryProposalStagingStore',
    safeSummary: unsafeFixture
      ? 'PayloadJson RawMemoryText MemoryPayloadJson PrivateReasoning ConfidentialClientDetail'
      : 'Review deterministic memory proposal evidence. Confidential portable candidate needs review.',
    recordedUtc: '2026-06-15T04:00:00Z'
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
