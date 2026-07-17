import { expect, test, type Page, type Route } from '@playwright/test';
import { projectWorkSessionRequiredReadiness, readyToRunReadiness, runConfigurationRequiredReadiness, setupIncompleteRunReadiness } from './helpers/mockBoard';

test('Board renders backend-owned attention, waiting, assignment, state, and event truth', async ({ page }) => {
  await mockBoardEntry(page, boardResponse());
  await page.goto('/projects/7/board');

  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await expect(page.getByTestId('flow.cockpit.primary.review')).toHaveText('Review waiting item');
  await expect(page.getByTestId('flow.board.attention.41')).toContainText('Acceptance findings need disposition.');
  await expect(page.getByTestId('flow.board.attention.41')).toContainText('Next: Review findings and record a disposition.');
  await expect(page.getByTestId('flow.board.attention.41')).toContainText('Human review');

  const reviewCard = page.getByTestId('flow.board.item.41');
  await expect(reviewCard).toContainText('PausedForApproval');
  await expect(reviewCard).toContainText('Waiting on Human review');
  await expect(reviewCard).toContainText('Assigned to Bob Reviewer');
  await expect(page.getByTestId('flow.board.column.done')).toContainText('Applied catalog change');

  await page.getByTestId('flow.board.filter.attention').click();
  await expect(page.getByTestId('flow.board.item.41')).toBeVisible();
  await expect(page.getByTestId('flow.board.item.42')).toHaveCount(0);
});

test('project readiness is concise and switches the primary action to setup', async ({ page }) => {
  const board = boardResponse();
  board.items = [];
  board.readiness = {
    ...board.readiness,
    isReady: false,
    blockedCount: 2,
    nextAction: {
      kind: 'ConfirmTestCommand',
      checkCode: 'TestCommand',
      allowed: true,
      reasonCode: 'BlockedMissingTestCommand',
      label: 'Confirm test command',
      nextSafeAction: 'Confirm the detected test command before running governed work.'
    }
  };
  board.runReadiness = setupIncompleteRunReadiness(7, board.readiness);
  await mockBoardEntry(page, board);
  await page.goto('/projects/7/board');

  await expect(page.getByTestId('flow.cockpit.badge')).toContainText('Setup incomplete · 2 blocker(s)');
  await expect(page.getByTestId('flow.cockpit.primary.setup')).toHaveText('Complete project setup');
  await expect(page.getByTestId('flow.cockpit.setup')).toContainText('Confirm the detected test command');
});

test('provisioning green with Fake agents shows four execution blockers and opens project Agents', async ({ page }) => {
  const board = boardResponse();
  board.items = [];
  board.runReadiness = runConfigurationRequiredReadiness(7);
  await mockBoardEntry(page, board);
  await page.goto('/projects/7/board');

  await expect(page.getByTestId('flow.cockpit.badge')).toContainText('Run configuration required · 4 agent blockers');
  await expect(page.getByTestId('flow.cockpit.runReadiness')).toContainText('Project setup complete.');
  await expect(page.getByTestId('flow.cockpit.runReadiness.blockers').getByRole('listitem')).toHaveCount(4);
  await page.getByTestId('flow.cockpit.primary.configureRunAgents').click();
  await expect(page).toHaveURL(/\/projects\/7\/library\/settings\/agents$/);
  await expect(page.getByTestId('flow.settings.section.agents')).toHaveAttribute('aria-selected', 'true');
});

test('project-work capability block dominates the Board and exposes one copyable supported restart', async ({ page }) => {
  const board = boardResponse();
  board.items = [];
  board.runReadiness = projectWorkSessionRequiredReadiness(7);
  await mockBoardEntry(page, board);
  await page.goto('/projects/7/board');

  await expect(page.getByTestId('flow.cockpit.badge')).toHaveText('Project-work session required');
  await expect(page.getByTestId('flow.cockpit.projectWorkSession')).toContainText('This session can build, test and review work');
  await expect(page.getByTestId('flow.cockpit.projectWorkSession.command')).toHaveText('.\\tools\\localtest\\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply');
  await expect(page.getByTestId('flow.cockpit.primary.copyProjectWorkRestart')).toHaveText('Copy supported restart');
  await expect(page.getByTestId('flow.board.new')).toHaveCount(0);
});

test('Board failure refuses to infer pipeline state from the legacy ticket list', async ({ page }) => {
  await mockBoardEntry(page, boardResponse(), 503);
  await page.route('**/irondev-api/api/projects/7/tickets', (route) =>
    json(route, [{ id: 999, projectId: 7, title: 'Do not infer me', status: 'Review' }])
  );
  await page.goto('/projects/7/board');

  await expect(page.getByTestId('flow.board.error')).toBeVisible();
  await expect(page.getByTestId('flow.board.error')).toContainText('will not reconstruct pipeline state');
  await expect(page.getByText('Do not infer me')).toHaveCount(0);
  await expect(page.getByTestId('flow.board.retry')).toBeEnabled();
});

test('Board remains readable without horizontal overflow on a narrow viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockBoardEntry(page, boardResponse());
  await page.goto('/projects/7/board');

  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth
  }));
  expect(dimensions.content).toBeLessThanOrEqual(dimensions.viewport);
  await expect(page.getByTestId('flow.board.attention.41')).toContainText('Human review');
});

function boardResponse() {
  return {
    projectId: 7,
    projectName: 'BookSeller',
    generatedUtc: '2026-07-11T01:00:00Z',
    readiness: {
      projectId: 7,
      isReady: true,
      blockedCount: 0,
      blockedStates: [] as string[],
      checks: [] as unknown[],
      nextAction: {
        kind: 'OpenBoard',
        checkCode: null as string | null,
        allowed: true,
        reasonCode: null as string | null,
        label: 'Open Board',
        nextSafeAction: 'Open the project Board.'
      },
      proposedProfile: null,
      boundary: 'Backend readiness truth.'
    },
    runReadiness: readyToRunReadiness(7),
    items: [
      {
        workItemId: 41,
        title: 'Search books by author',
        stage: 'Review',
        state: 'PausedForApproval',
        priority: 'High',
        needsAttention: true,
        attentionReason: 'Acceptance findings need disposition.',
        nextSafeAction: 'Review findings and record a disposition.',
        waitingOn: { kind: 'Human', label: 'Human review' },
        assignee: { userId: 7, displayName: 'Bob Reviewer' },
        lastMeaningfulEventUtc: '2026-07-11T00:58:00Z',
        latestRun: {
          runId: 'run-41',
          status: 'PausedForApproval',
          summary: 'Waiting for human review.',
          failureReason: null,
          requiresHumanAction: true,
          updatedUtc: '2026-07-11T00:58:00Z'
        }
      },
      {
        workItemId: 42,
        title: 'Applied catalog change',
        stage: 'Done',
        state: 'Applied',
        priority: 'Medium',
        needsAttention: false,
        attentionReason: null,
        nextSafeAction: 'Inspect the applied outcome and its receipts.',
        waitingOn: null,
        assignee: null,
        lastMeaningfulEventUtc: '2026-07-11T00:30:00Z',
        latestRun: null
      }
    ]
  };
}

async function mockBoardEntry(page: Page, board: ReturnType<typeof boardResponse>, boardStatus = 200) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/environment', (route) =>
    json(route, { environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    json(route, { userId: 7, email: 'bob@irondev.local', displayName: 'Bob Reviewer', selectedTenantId: 3 })
  );
  await page.route('**/irondev-api/api/tenants', (route) => json(route, [{ id: 3, name: 'IronDev Local' }]));
  await page.route('**/irondev-api/api/projects', (route) =>
    json(route, [{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
  );
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) => json(route, { projectId: 7 }));
  await page.route('**/irondev-api/api/projects/7/board', (route) => json(route, board, boardStatus));
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
