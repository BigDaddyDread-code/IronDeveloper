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
  await expect(page.getByTestId('auth.email')).toHaveValue('bob@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('auth.flowHint')).toHaveText('Sign in, then select a project to continue.');
  await expect(page.getByTestId('auth.submit')).toBeVisible();
  await expect(page.getByTestId('auth.apiStatusChip')).toContainText('LocalTest');
  await expect(page.getByTestId('app.authState.configureToken')).toHaveCount(0);
  await expect(page.getByTestId('tenant.selector')).toHaveCount(0);
  await expect(page.getByTestId('project.selector')).toHaveCount(0);
});

test('invalid credentials render one inline sign-in error', async ({ page }) => {
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/login', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'Invalid email or password.' })
    });
  });
  await page.goto('/');

  await page.getByTestId('auth.password').fill('wrong-password');
  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('auth.error')).toHaveText('LocalTest sign in failed. Reset the LocalTest data and retry.');
  await expect(page.getByTestId('auth.error')).toHaveCount(1);
  await expect(page.locator('body')).not.toContainText('TOKEN REJECTED');
  await expect(page.locator('body')).not.toContainText('Authentication failed');
});

test('valid LocalTest login auto-selects one tenant and lands on project chooser', async ({ page }) => {
  await mockHealthyApi(page);
  await mockLoginToSingleTenantChooser(page);
  await page.goto('/');

  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('flow.tenantChooser')).toHaveCount(0);
  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.chooser.project.7')).toContainText('BookSeller');
});

test('multiple tenants render a separate tenant chooser', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
  });
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Robert', selectedTenantId: null })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 3, name: 'Local Test Tenant', slug: 'local-test' },
        { id: 4, name: 'Client Tenant', slug: 'client' }
      ])
    });
  });
  await page.goto('/');

  await expect(page.getByTestId('flow.tenantChooser')).toBeVisible();
  await expect(page.getByText('Welcome, Robert')).toBeVisible();
  await expect(page.getByTestId('auth.form')).toHaveCount(0);
  await expect(page.getByTestId('flow.chooser')).toHaveCount(0);
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

  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await expect(page.getByTestId('flow.settings.banner')).toContainText('Role assignment decides visibility, never mutation authority');
  await expect(page.getByText('Viewer User')).toBeVisible();
  await expect(page.getByTestId('flow.settings.savePolicy')).toBeVisible();
});

test('settings tolerates the singleton tenant-user response used by LocalTest', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/tenants/3/users', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ id: 7, email: 'dev@iron.dev', displayName: 'Dev User', role: 'Owner', isActive: true })
    });
  });
  await page.goto('/');

  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await expect(page.getByTestId('flow.library').getByText('Dev User', { exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.library').getByText('dev@iron.dev')).toBeVisible();
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

async function mockLoginToSingleTenantChooser(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/api/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'base-token', userId: 7, displayName: 'Local Test User' })
    });
  });
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    const authorization = route.request().headers().authorization ?? '';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        userId: 7,
        email: 'bob@irondev.local',
        displayName: 'Local Test User',
        selectedTenantId: authorization.includes('tenant-token') ? 3 : null
      })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'Local Test Tenant', slug: 'local-test' }])
    });
  });
  await page.route('**/irondev-api/api/tenants/select', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'tenant-token', userId: 7, displayName: 'Local Test User' })
    });
  });
  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: 7,
        isReady: true,
        blockedCount: 0,
        blockedStates: [],
        checks: [],
        nextAction: { kind: 'OpenBoard', checkCode: null, allowed: true, reasonCode: null, label: 'Open Board', nextSafeAction: 'Open the project Board.' },
        proposedProfile: null,
        boundary: 'Readiness is computed from stored truth and scan evidence.'
      })
    });
  });
}
