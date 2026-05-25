import { expect, test } from '@playwright/test';

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

test('tickets shell exposes cockpit regions and auth state', async ({ page }) => {
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'ok' })
    });
  });

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
  await expect(page.getByText('Authentication required', { exact: true })).toBeVisible();
  await expect(page.getByTestId('app.authState.configureToken')).toBeVisible();
  await expect(page.getByTestId('app.authState.retry')).toBeVisible();
  await expect(page.getByTestId('api.status.authRequired')).toBeVisible();

  await expect(page.getByTestId('api.status.connected')).toBeVisible();
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
  await expectNoHorizontalOverflow(page);
});
