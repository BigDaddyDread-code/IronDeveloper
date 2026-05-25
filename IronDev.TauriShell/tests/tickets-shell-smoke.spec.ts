import { expect, test } from '@playwright/test';

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

async function mockHealthyApi(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'healthy' })
    });
  });
}

async function seedToken(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
  });
}

test('tickets shell exposes cockpit regions and auth state', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('app.header')).toBeVisible();
  await expect(page.getByTestId('app.apiStatus')).toBeVisible();
  await expect(page.getByTestId('shell.nav.tickets')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('tickets.header')).toBeVisible();
  await expect(page.getByTestId('ticket.list')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByTestId('ticket.command.refresh')).toBeVisible();
  await expect(page.getByTestId('app.authState')).toBeVisible();
  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toBeVisible();
  await expect(page.getByTestId('auth.password')).toBeVisible();
  await expect(page.getByTestId('auth.submit')).toBeVisible();
  await expect(page.getByTestId('app.authState.configureToken')).toBeVisible();
  await expect(page.getByTestId('app.authState.retry')).toBeVisible();
  await expect(page.getByTestId('api.status.authRequired')).toBeVisible();
  await expect(page.getByTestId('api.status.connected')).toBeVisible();

  await page.getByTestId('app.authState.configureToken').click();
  await expect(page.getByTestId('auth.tokenInput')).toBeVisible();
  await expect(page.getByTestId('auth.saveToken')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows offline state and does not overflow in a narrow desktop window', async ({ page }) => {
  await page.route('**/irondev-api/health', async (route) => {
    await route.abort('connectionrefused');
  });

  await page.setViewportSize({ width: 920, height: 760 });
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByText('IronDev.Api is offline', { exact: true })).toBeVisible();
  await expect(page.getByText('dotnet run --project IronDev.Api', { exact: true })).toBeVisible();
  await expect(page.getByTestId('api.status.disconnected')).toBeVisible();
  expect(await page.getByTestId('app.authState.configureToken').count()).toBe(0);
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows tenant required state after token auth', async ({ page }) => {
  await seedToken(page);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: null })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    });
  });

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Tenant required' })).toBeVisible();
  await expect(page.getByTestId('tenant.selector')).toBeVisible();
  await expect(page.getByTestId('tenant.option')).toHaveCount(1);
  await expect(page.getByTestId('api.status.connected')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows project required state when no projects are available', async ({ page }) => {
  await seedToken(page);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Project required' })).toBeVisible();
  await expect(page.getByTestId('project.selector')).toBeVisible();
  await expect(page.getByTestId('app.header').getByTestId('project.status.missing')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('tickets shell loads mocked project ticket data', async ({ page }) => {
  await seedToken(page);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 101,
          projectId: 7,
          title: 'Make tickets cockpit real',
          status: 'Ready',
          priority: 'High',
          summary: 'Render ticket data through the Tauri API client.'
        },
        {
          id: 102,
          projectId: 7,
          title: 'Add project selection',
          status: 'Draft',
          priority: 'Medium',
          summary: 'Pick active project before loading tickets.'
        }
      ])
    });
  });

  await page.goto('/');

  await expect(page.getByTestId('project.status.selected')).toBeVisible();
  await expect(page.getByTestId('ticket.row')).toHaveCount(2);
  await expect(page.getByTestId('ticket.detail')).toContainText('Make tickets cockpit real');
  await expect(page.getByTestId('ticket.detail')).toContainText('Render ticket data through the Tauri API client.');
  await expectNoHorizontalOverflow(page);
});
