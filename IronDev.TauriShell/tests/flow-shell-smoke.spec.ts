import { expect, test } from '@playwright/test';

// Flow-shell smoke: sign-in gate, board, shape stage, settings users, and a
// governance deep link rendering inside the Library. Replaces the old
// tickets-shell-smoke, which asserted the eight-workspace shell.

test('normal sign-in appears before any product surface', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('auth.route')).toBeVisible();
  await expect(page.getByTestId('flow.shell')).toHaveCount(0);
  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('localtest@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('auth.flowHint')).toHaveText('Sign in, then select a project to continue.');
  await expect(page.getByTestId('auth.submit')).toBeVisible();
});

test('board renders pipeline columns with project tickets', async ({ page }) => {
  await mockSelectedProject(page);
  await page.goto('/');

  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toContainText('Shape');
  await expect(page.getByTestId('flow.board.columns')).toContainText('Done');
  await expect(page.getByTestId('flow.board.columns')).toContainText('Add book sorting to catalog');
});

test('shape stage earns promotion through the readiness gate', async ({ page }) => {
  await mockSelectedProject(page);
  await page.goto('/');

  await page.getByTestId('flow.board.new').click();
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.getByTestId('flow.shape.gate')).toContainText('blocked');
  await expect(page.getByTestId('flow.shape.promote')).toBeDisabled();

  await page.getByTestId('flow.shape.prompt').fill('Users need to sort the catalog by title.');
  await page.getByTestId('flow.shape.prompt').press('Enter');
  await expect(page.getByTestId('flow.contract')).toBeVisible();

  await page.getByTestId('flow.shape.addCriterion').fill('Catalog sorts by title ascending');
  await page.getByTestId('flow.shape.addCriterion').press('Enter');
  await page.getByRole('button', { name: 'Confirm' }).click();

  await expect(page.getByTestId('flow.shape.gate')).toContainText('satisfied');
  await expect(page.getByTestId('flow.shape.promote')).toBeEnabled();
});

test('settings lists real tenant users and labels the policy draft honestly', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/tenants/3/users', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 7, email: 'dev@iron.dev', displayName: 'Dev User', role: 'Owner', isActive: true },
        { id: 8, email: 'viewer@iron.dev', displayName: 'Viewer User', role: 'Viewer', isActive: true }
      ])
    });
  });
  await page.goto('/');

  await page.getByTestId('flow.nav.settings').click();
  await expect(page.getByTestId('flow.settings.banner')).toContainText('Role assignment decides visibility, never mutation authority');
  await expect(page.getByText('Viewer User')).toBeVisible();
  await expect(page.getByTestId('flow.settings.savePolicy')).toBeVisible();
});

test('governance deep link renders the timeline viewer inside the Library', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/governance/**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ data: { items: [], totalCount: 0 }, errors: [] })
    });
  });
  await page.goto('/governance/timeline');

  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.governanceHost')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Governance Timeline' })).toBeVisible();
});

async function mockHealthyApi(page: import('@playwright/test').Page) {
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
}

async function mockSelectedProject(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await mockHealthyApi(page);
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
        { id: 42, tenantId: 3, projectId: 7, title: 'Add book sorting to catalog', status: 'Draft', acceptanceCriteria: null }
      ])
    });
  });
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: 'Proposed criteria are ready — confirm them in the contract.',
        contextSummary: 'Shaping context',
        linkedFilePaths: 'src/Catalog/CatalogService.cs'
      })
    });
  });
}
