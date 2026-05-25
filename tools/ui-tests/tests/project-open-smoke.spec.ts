import { expect, test } from '@playwright/test';

test.describe('project-open-smoke future-shell contract', () => {
  test.skip(true, 'Pending future UI shell. Requires deterministic project fixture and project.* selectors.');

  test('user can open a seeded project', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('shell.nav.projects').click();

    await expect(page.getByTestId('project.list')).toBeVisible();
    await page.getByTestId('project.row.irondev-seeded').click();

    await expect(page.getByTestId('project.detail')).toBeVisible();
    await expect(page.getByTestId('project.detail.title')).toContainText('IronDev Seeded Project');
  });
});
