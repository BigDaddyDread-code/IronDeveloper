import { expect, test } from '@playwright/test';

test.describe('document-to-ticket-journey future-shell contract', () => {
  test.skip(true, 'Pending future UI shell. Requires seeded document fixture and document/ticket selectors.');

  test('user can generate a ticket from a seeded document', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('shell.nav.documents').click();
    await page.getByTestId('document.row.seed-api-boundary-notes').click();

    await expect(page.getByTestId('document.editor.title')).toBeVisible();
    await expect(page.getByTestId('document.editor.body')).toBeVisible();

    await page.getByTestId('document.actions.generateTicket').click();
    await expect(page.getByTestId('ticket.review.row.seed-api-boundary-notes')).toBeVisible();
  });
});
