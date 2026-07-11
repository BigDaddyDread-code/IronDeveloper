import { expect, test, type Page, type Route } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';
import { mockProjectWorkItem, workItemProjection } from './helpers/mockWorkItem';

test('Work Item renders backend stage, gate, contract, and primary action truth', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, {
    stage: 'Build',
    state: 'Failed',
    statusSummary: 'The latest governed run failed during test execution.',
    gateState: 'Blocked',
    gateReason: 'Tests failed in the disposable workspace.',
    nextSafeAction: 'Inspect failure evidence before deciding whether retry is safe.',
    technicalDetails: ['dotnet test returned exit code 1'],
    primaryActionKind: 'RepairOrRetry',
    primaryActionLabel: 'Review failure',
    criterionCount: 2,
    affectedFiles: ['src/Catalog.cs', 'tests/CatalogTests.cs']
  });

  await page.goto('/projects/7/work-items/42');

  await expect(page.getByTestId('flow.workItem.state')).toContainText('Build');
  await expect(page.getByTestId('flow.workItem.state')).toContainText('Failed');
  await expect(page.getByTestId('flow.workItem.gate')).toContainText('Tests failed in the disposable workspace.');
  await expect(page.getByTestId('flow.workItem.gate')).toContainText('Inspect failure evidence');
  await expect(page.getByTestId('flow.workItem.primaryAction')).toHaveText('Review failure');
  await expect(page.getByTestId('flow.contract.summary')).toContainText('2 criteria · 2 affected files');

  await page.getByText('Technical details', { exact: true }).click();
  await expect(page.getByText('dotnet test returned exit code 1')).toBeVisible();
});

test('Work Item projection failure offers retry and never reconstructs lifecycle truth', async ({ page }) => {
  await mockWorkspace(page);
  let attempts = 0;
  let allowSuccess = false;
  await page.route('**/irondev-api/api/projects/7/work-items/42', (route) => {
    attempts += 1;
    if (!allowSuccess) {
      return json(route, { error: 'Projection unavailable' }, 503);
    }
    return json(route, workItemProjection());
  });

  await page.goto('/projects/7/work-items/42');

  await expect(page.getByTestId('flow.workItemProjection.error')).toBeVisible();
  await expect(page.getByTestId('flow.stagerail')).toHaveCount(0);
  allowSuccess = true;
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByTestId('flow.workItem')).toBeVisible();
  expect(attempts).toBeGreaterThanOrEqual(2);
});

test('Discuss in Chat routes to the exact backend-linked session', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, { linkedChatSessionId: 9007 });

  await page.goto('/projects/7/work-items/42');
  await page.getByRole('button', { name: 'Discuss in Chat' }).click();

  await expect(page).toHaveURL('/projects/7/chat/sessions/9007');
});

async function mockWorkspace(page: Page) {
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
    json(route, { userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    json(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    json(route, [{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
  );
  await page.route('**/irondev-api/api/projects/7/select', (route) => json(route, { projectId: 7 }));

  const ticket = {
    id: 42,
    tenantId: 3,
    projectId: 7,
    title: 'Add book sorting to catalog',
    status: 'Draft',
    acceptanceCriteria: 'Catalog sorts by title ascending\nPaging preserves the selected order.',
    linkedFilePaths: 'src/Catalog.cs\ntests/CatalogTests.cs'
  };
  await page.route('**/irondev-api/api/projects/7/tickets/42', (route) => json(route, ticket));
  await page.route('**/irondev-api/api/projects/7/tickets', (route) => json(route, [ticket]));
  await mockProjectBoard(page, { projectName: 'BookSeller', tickets: [ticket] });
  await page.route('**/irondev-api/api/projects/7/tickets/42/build-readiness', (route) =>
    json(route, { isReady: true, message: 'Ready to build.', blockingIssues: [] })
  );
  await page.route('**/irondev-api/api/projects/7/tickets/42/evidence-summary', (route) =>
    json(route, {
      ticketId: 42,
      status: 'loaded',
      message: 'No linked execution evidence is available yet.',
      latestRun: null,
      blockedActions: [],
      nextSafeAction: 'Start governed run'
    })
  );
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => json(route, []));
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
