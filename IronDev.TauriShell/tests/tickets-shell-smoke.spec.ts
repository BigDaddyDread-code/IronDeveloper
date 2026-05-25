import { expect, test } from '@playwright/test';

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

test('tickets shell exposes cockpit regions and auth state', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('app.header')).toBeVisible();
  await expect(page.getByTestId('app.apiStatus')).toBeVisible();
  await expect(page.getByTestId('shell.nav.tickets')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('ticket.list')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByTestId('ticket.command.refresh')).toBeVisible();
  await expect(page.getByTestId('app.authState')).toBeVisible();
  await expect(page.getByTestId('app.authState.configureToken')).toBeVisible();
  await expect(page.getByTestId('app.authState.retry')).toBeVisible();
  await expect(page.getByTestId('api.status.authRequired')).toBeVisible();

  await expect(page.getByTestId(/api\.status\.(connected|disconnected|loading|error)/)).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('tickets shell does not overflow in a narrow desktop window', async ({ page }) => {
  await page.setViewportSize({ width: 920, height: 760 });
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});
