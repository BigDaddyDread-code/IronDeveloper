import { expect, test } from '@playwright/test';

test('tickets shell exposes API status and cockpit selectors', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('shell.header')).toBeVisible();
  await expect(page.getByTestId('app.apiStatus')).toBeVisible();
  await expect(page.getByTestId('shell.nav.tickets')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('ticket.list')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByTestId('ticket.command.refresh')).toBeVisible();

  await expect(page.getByTestId(/api\.status\.(connected|disconnected|unauthenticated|checking)/)).toBeVisible();
});
