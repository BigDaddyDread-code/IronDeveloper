import { expect, test } from '@playwright/test';

test.describe('login-smoke future-shell contract', () => {
  test.skip(true, 'Pending future UI shell. Requires stable login.* data-testid selectors.');

  test('user can log in and reach tenant-aware shell', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('login.email').fill('single-tenant-user@irondev.test');
    await page.getByTestId('login.password').fill('correct-horse-battery-staple');
    await page.getByTestId('login.submit').click();

    await expect(page.getByTestId('shell.root')).toBeVisible();
    await expect(page.getByTestId('shell.tenant.active')).toBeVisible();
  });
});
