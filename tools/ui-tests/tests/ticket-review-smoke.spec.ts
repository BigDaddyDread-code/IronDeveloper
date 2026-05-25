import { expect, test } from '@playwright/test';

test.describe('ticket-review-smoke future-shell contract', () => {
  test.skip(true, 'Pending future UI shell. Requires seeded ticket review queue and ticket.review.* selectors.');

  test('user can open generated ticket review and import selected ticket', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('shell.nav.tickets').click();
    await page.getByTestId('ticket.review.queue').click();

    await expect(page.getByTestId('ticket.review.list')).toBeVisible();
    await page.getByTestId('ticket.review.row.seed-draft-auth-boundary').click();
    await page.getByTestId('ticket.review.importSelected').click();

    await expect(page.getByTestId('ticket.row.seed-draft-auth-boundary')).toBeVisible();
  });
});
