import { expect, test, type Page } from '@playwright/test';

// P0-7: Build and Review stages consume the walking-skeleton loop through its
// governed endpoints. These tests mock the backend and assert the UI's side of
// the boundary: it renders what the backend recorded, requests through governed
// surfaces, shows refusals honestly, and never invents an unblocked state.

const RUN_ID = 'run-777';
const PACKAGE_HASH = 'a'.repeat(64);
const APPROVAL_PROJECT_GUID = '00000007-0000-0000-0000-000000000000';

test('build stage renders the halted run and review renders the matrix and gate', async ({ page }) => {
  await mockTicketWorkspace(page);
  await mockSkeletonRun(page, { continuationUnblocked: false });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();

  await expect(page.getByTestId('flow.build.status')).toContainText('PausedForApproval');
  await expect(page.getByTestId('flow.build.testAuthoring')).toContainText('1 test(s) authored');
  await expect(page.getByTestId('flow.build.timeline')).toContainText('ApprovalRequiredHalt');
  await expect(page.getByTestId('flow.build.gate')).toContainText('Halt is not approval');

  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.matrix')).toContainText('Catalog sorts by title ascending');
  await expect(page.getByTestId('flow.review.matrix')).toContainText('tests/skeleton/SortTests.cs');
  await expect(page.getByTestId('flow.review.requirement')).toContainText('skeleton-run.continue');
  await expect(page.getByTestId('flow.review.gate')).toContainText('Human gate: locked');
  await expect(page.getByTestId('flow.review.requestApply')).toBeDisabled();
});

test('recording an approval posts to the governed surface and continuation consumes it', async ({ page }) => {
  await mockTicketWorkspace(page);
  const state = await mockSkeletonRun(page, { continuationUnblocked: false });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await page.getByTestId('flow.review.recordApproval').click();
  await expect(page.getByTestId('flow.review.gate')).toContainText('Recording is not continuation');
  expect(state.approvalRequestBody.approvalTargetHash).toBe(PACKAGE_HASH);
  expect(state.approvalRequestBody.capabilityCode).toBe('skeleton-run.continue');

  state.continuationUnblocked = true;
  await page.getByTestId('flow.review.requestContinuation').click();

  await expect(page.getByTestId('flow.review.gate')).toContainText('Continuation allowed');
  await expect(page.getByTestId('flow.review.requestApply')).toBeEnabled();
});

test('a refused apply is shown honestly and the loop stays incomplete', async ({ page }) => {
  await mockTicketWorkspace(page);
  const state = await mockSkeletonRun(page, { continuationUnblocked: true, applyRefusedReason: 'ApplyDisabled' });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.requestApply')).toBeEnabled();
  await page.getByTestId('flow.review.requestApply').click();

  await expect(page.getByTestId('flow.review.gate')).toContainText('Skeleton apply is disabled');
  expect(state.applyRequested).toBe(true);
});

async function openTicketStage(page: Page) {
  await page.goto('/');
  await page.getByText('Add book sorting to catalog').click();
  await expect(page.getByTestId('flow.ticket.startRun')).toBeEnabled();
}

interface SkeletonMockState {
  continuationUnblocked: boolean;
  applyRefusedReason?: string;
  applyRequested: boolean;
  approvalRequestBody: Record<string, unknown>;
}

async function mockSkeletonRun(
  page: Page,
  options: { continuationUnblocked: boolean; applyRefusedReason?: string }
): Promise<SkeletonMockState> {
  const state: SkeletonMockState = {
    continuationUnblocked: options.continuationUnblocked,
    applyRefusedReason: options.applyRefusedReason,
    applyRequested: false,
    approvalRequestBody: {}
  };

  const runDto = (status: string, message: string | null = null) => ({
    runId: RUN_ID,
    projectId: 7,
    ticketId: 42,
    status,
    currentNode: 'SkeletonRun',
    requiresHumanApproval: status === 'PausedForApproval',
    message
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(runDto('PausedForApproval')) });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/report`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: RUN_ID,
        projectId: 7,
        ticketId: 42,
        status: state.continuationUnblocked ? 'Completed' : 'PausedForApproval',
        summary: 'Halted for approval.',
        timeline: [
          { timestampUtc: '2026-07-04T10:00:00Z', eventType: 'RunStarted', message: 'Skeleton run started for ticket 42.' },
          { timestampUtc: '2026-07-04T10:01:00Z', eventType: 'TestsAuthored', message: '1 test file(s) authored.' },
          { timestampUtc: '2026-07-04T10:02:00Z', eventType: 'ApprovalRequiredHalt', message: 'Halted for approval. Halt is not approval.' }
        ],
        proposal: { proposalId: `prop-${RUN_ID}`, fileChangeCount: 1, evidenceRef: 'evidence/proposal.json', evidenceExistsOnDisk: true },
        testAuthoring: { authored: true, authoredTestCount: 1, skippedReason: '' },
        criticPackage: {
          packageId: `critic-pkg-${RUN_ID}`,
          packagePath: 'evidence/critic-package.json',
          existsOnDisk: true,
          announcedSha256: PACKAGE_HASH,
          sha256OnDisk: PACKAGE_HASH,
          hashVerified: true
        },
        approval: {
          targetKind: 'workflow-continuation-request',
          targetId: RUN_ID,
          targetHash: PACKAGE_HASH,
          capabilityCode: 'skeleton-run.continue',
          haltObserved: true,
          continuationUnblocked: state.continuationUnblocked,
          acceptedApprovalId: state.continuationUnblocked ? 'b2c3d4e5-0000-0000-0000-000000000001' : ''
        },
        apply: null,
        gaps: [],
        loopComplete: false,
        boundary: 'A report is reconstruction from durable evidence.'
      })
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/critic-package`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        packageId: `critic-pkg-${RUN_ID}`,
        runId: RUN_ID,
        proposalId: `prop-${RUN_ID}`,
        ticketId: 42,
        projectId: 7,
        ticketTitle: 'Add book sorting to catalog',
        acceptanceCriteria: 'Catalog sorts by title ascending',
        proposalSummary: 'Add book sorting.',
        proposalRationale: 'Users need ordered catalogs.',
        changes: [
          {
            filePath: 'src/SortOptions.cs',
            description: 'New sort options enum.',
            isNewFile: true,
            isDeletion: false,
            diff: '+public enum SortOptions { Title }',
            fullContentAfter: 'public enum SortOptions { Title }'
          }
        ],
        authoredTests: [
          {
            relativePath: 'tests/skeleton/SortTests.cs',
            content: 'public class SortTests { }',
            coversCriterion: 'Catalog sorts by title ascending'
          }
        ],
        commandResults: [{ displayName: 'dotnet build', exitCode: 0, timedOut: false, durationMs: 4000 }],
        evidenceRefs: [],
        workspaceRunSucceeded: true,
        boundary: 'This package is review material for the independent critic.'
      })
    });
  });

  await page.route(`**/irondev-api/api/v1/projects/${APPROVAL_PROJECT_GUID}/accepted-approvals`, async (route) => {
    state.approvalRequestBody = route.request().postDataJSON() as Record<string, unknown>;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'created',
        data: { acceptedApprovalId: 'b2c3d4e5-0000-0000-0000-000000000001' },
        acceptedApprovalId: 'b2c3d4e5-0000-0000-0000-000000000001',
        warnings: [],
        errors: []
      })
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/continue`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        state.continuationUnblocked
          ? runDto('Completed', 'Continuation allowed by accepted approval. Approval is not apply permission.')
          : runDto('PausedForApproval', 'No live accepted approval satisfies this requirement. Halt is not approval.')
      )
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/apply`, async (route) => {
    state.applyRequested = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        state.applyRefusedReason
          ? runDto('Completed', 'Apply refused: Skeleton apply is disabled. Set SkeletonApply:Enabled=true for sandbox projects only.')
          : runDto('Applied', 'Applied through the governed workspace spine.')
      )
    });
  });

  return state;
}

async function mockTicketWorkspace(page: Page) {
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
  await page.route('**/irondev-api/api/tenants', async (route) => {
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
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 42,
          tenantId: 3,
          projectId: 7,
          title: 'Add book sorting to catalog',
          status: 'Draft',
          acceptanceCriteria: 'Catalog sorts by title ascending'
        }
      ])
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/42/build-readiness', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isReady: true, message: 'Ready to build.', blockingIssues: [] })
    });
  });
}
