import { expect, test } from '@playwright/test';

test.describe('build-run-review-smoke future-shell contract', () => {
  test.skip(true, 'Pending future UI shell. Requires seeded build/run report fixture and runReport.* selectors.');

  test('user can start a build run and review the run report', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('shell.nav.builds').click();
    await page.getByTestId('build.run.start').click();

    await expect(page.getByTestId('build.run.status')).toBeVisible();
    await page.getByTestId('runReport.openLatest').click();

    await expect(page.getByTestId('runReport.detail')).toBeVisible();
  });
});
