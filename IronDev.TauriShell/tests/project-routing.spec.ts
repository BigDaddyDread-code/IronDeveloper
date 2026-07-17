import { expect, test, type Page } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';
import { mockProjectWorkItem } from './helpers/mockWorkItem';

test.beforeEach(async ({ page }) => {
  await mockRoutedProject(page);
});

test('entry opens the project chooser before resolving to a project-scoped Board', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveURL('/projects');
  await page.getByTestId('flow.chooser.project.7').click();
  await expect(page).toHaveURL('/projects/7/workshop');
  await page.getByTestId('flow.nav.board').click();
  await expect(page).toHaveURL('/projects/7/board');
  const navigation = page.getByRole('navigation', { name: 'Project' });
  await expect(navigation.getByRole('button')).toHaveCount(4);
  await expect(navigation).toContainText('Board');
  await expect(navigation).toContainText('Workshop');
  await expect(navigation).toContainText('Work Item');
  await expect(navigation).toContainText('Library');
  await expect(navigation).not.toContainText('Batch');
  await expect(navigation).not.toContainText('Settings');
  await expect(page.getByTestId('flow.nav.workitem')).toBeDisabled();
});

test('Workshop and Library navigation update history and survive back navigation', async ({ page }) => {
  await page.goto('/projects/7/board');

  await page.getByTestId('flow.nav.workshop').click();
  await expect(page).toHaveURL('/projects/7/workshop');
  await expect(page.getByTestId('chat.route')).toBeVisible();

  await page.getByTestId('flow.nav.library').click();
  await expect(page).toHaveURL('/projects/7/library');
  await expect(page.getByTestId('flow.library')).toBeVisible();

  await page.goBack();
  await expect(page).toHaveURL('/projects/7/workshop');
  await expect(page.getByTestId('chat.route')).toBeVisible();
});

test('opening and refreshing a Work Item hydrates the route from backend truth', async ({ page }) => {
  await page.goto('/projects/7/board');

  await page.getByRole('button', { name: /Routed ticket/ }).click();
  await expect(page).toHaveURL('/projects/7/work-items/41');
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();

  await page.reload();
  await expect(page).toHaveURL('/projects/7/work-items/41');
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.locator('body')).toContainText('Routed ticket');
});

test('Run queue expands inside Board without becoming a destination', async ({ page }) => {
  await page.goto('/projects/7/board');

  await page.getByTestId('flow.board.batch').click();

  await expect(page).toHaveURL('/projects/7/board');
  await expect(page.getByTestId('flow.board.runQueue')).toBeVisible();
  await expect(page.getByTestId('flow.batch')).toBeVisible();
});

test('missing projects and unknown paths do not fall through to Board', async ({ page }) => {
  await page.goto('/projects/999/board');

  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await expect(page.getByRole('heading', { name: 'Project not found' })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome')).toHaveAttribute('role', 'alert');
  await expect(page.getByTestId('flow.routeOutcome')).toBeFocused();
  await expect(page.getByTestId('flow.board.columns')).toHaveCount(0);

  await page.goto('/definitely-not-a-route');
  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await expect(page.getByRole('heading', { name: 'This route does not exist' })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome')).toBeFocused();
});

test('Documents and its upload child are functional project routes', async ({ page }) => {
  await page.goto('/projects/7/library/documents');

  await expect(page).toHaveURL('/projects/7/library/documents');
  await expect(page.getByTestId('flow.documents.empty')).toBeVisible();

  await page.goto('/projects/7/library/documents/upload');
  await expect(page).toHaveURL('/projects/7/library/documents/upload');
  await expect(page.getByTestId('flow.documents.upload')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Upload a document' })).toBeVisible();
});

test('legacy root Chat links require an explicit project choice on clean startup', async ({ page }) => {
  await page.goto('/chat');

  await expect(page).toHaveURL('/projects');
  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('chat.route')).toHaveCount(0);
});

test('legacy project Chat session links redirect to Workshop session routes', async ({ page }) => {
  await page.goto('/projects/7/chat/sessions/9007');

  await expect(page).toHaveURL('/projects/7/workshop/sessions/9007');
  await expect(page.getByTestId('chat.route')).toBeVisible();
});

test('legacy project-connect URL opens project-first entry and returns to projects', async ({ page }) => {
  await page.goto('/projects/connect');

  await expect(page.getByTestId('flow.startProject')).toBeVisible();
  await page.getByTestId('flow.startProject.back').click();
  await expect(page).toHaveURL('/projects');
  await expect(page.getByTestId('flow.chooser')).toBeVisible();
});

async function mockRoutedProject(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) })
  );
  await page.route('**/irondev-api/api/environment', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true })
    })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
    })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    })
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
    })
  );
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: 7,
        tenantId: 3,
        name: 'BookSeller',
        projectLifecyclePhase: 'Shaping',
        executionReadiness: 'Ready',
        repositoryBinding: 'C:\\repos\\BookSeller',
        workbenchSessionId: 7007,
        leaseEpoch: 1,
        wasResumed: false,
        wasTakenOver: false,
        clientOperationId: route.request().postDataJSON().clientOperationId
      })
    })
  );
  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: 7,
        isReady: true,
        blockedCount: 0,
        blockedStates: [],
        checks: [],
        nextAction: { kind: 'OpenBoard', allowed: true, label: 'Open Board', nextSafeAction: 'Open the Board.' },
        proposedProfile: null,
        boundary: 'Backend truth.'
      })
    })
  );

  const ticket = {
    id: 41,
    tenantId: 3,
    projectId: 7,
    title: 'Routed ticket',
    status: 'Draft',
    acceptanceCriteria: 'The route survives refresh.'
  };
  await page.route('**/irondev-api/api/projects/7/tickets/41', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticket) })
  );
  await mockProjectWorkItem(page, {
    workItemId: 41,
    title: ticket.title,
    state: ticket.status,
    ticket
  });
  await page.route('**/irondev-api/api/projects/7/tickets', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([ticket]) })
  );
  await mockProjectBoard(page, { projectName: 'BookSeller', tickets: [ticket] });
  await page.route(/\/irondev-api\/api\/projects\/7\/documents(?:\?[^#]*)?$/, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) })
  );
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => {
    if (route.request().method() === 'GET') {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(9007) });
  });
}
