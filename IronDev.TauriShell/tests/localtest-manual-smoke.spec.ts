import { expect, test } from '@playwright/test';

test.describe('LocalTest manual cockpit smoke', () => {
  test.skip(process.env.IRONDEV_LOCALTEST_LIVE !== '1', 'Set IRONDEV_LOCALTEST_LIVE=1 to run against the live LocalTest API.');

  test('proves the seeded LocalTest ticket cockpit loop', async ({ page }) => {
    const notes: string[] = [];

    await page.goto('/');
    await page.evaluate(() => window.localStorage.clear());
    await page.reload();

    await expect(page.getByTestId('app.shell')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('environment.badge')).toContainText('LocalTest', { timeout: 15_000 });
    await expect(page.getByTestId('api.status.connected')).toBeVisible();
    await expect(page.getByTestId('auth.form')).toBeVisible();

    await page.getByTestId('auth.email').fill('localtest@irondev.local');
    await page.getByTestId('auth.password').fill('change-me-local-only');
    await page.getByTestId('auth.submit').click();

    if (await page.getByTestId('tenant.selector').isVisible({ timeout: 5_000 }).catch(() => false)) {
      await page.getByTestId('tenant.selector').selectOption('1');
      notes.push('Tenant selector shown and Local Test Tenant selected.');
    } else {
      notes.push('Tenant auto-selected through the tenant API.');
    }

    if (await page.getByTestId('project.selector').isVisible({ timeout: 5_000 }).catch(() => false)) {
      await page.getByTestId('project.selector').selectOption('1');
      notes.push('Project selector shown and IronDev Local Test Project selected.');
    } else {
      notes.push('Project selected from configured LocalTest project id 1.');
    }

    await expect(page.getByTestId('project.status.selected')).toContainText('IronDev Local Test Project', { timeout: 15_000 });
    await expect(page.getByTestId('ticket.row')).toHaveCount(3, { timeout: 15_000 });

    const ticketList = await page.getByTestId('ticket.list').innerText();
    for (const title of ['Add Governed Tool Architecture', 'Wire Start Disposable Run', 'Improve Ticket Workspace UI']) {
      expect(ticketList).toContain(title);
    }

    await page.getByRole('button', { name: /Add Governed Tool Architecture/ }).click();
    await expect(page.getByTestId('ticket.detail.header')).toContainText('Add Governed Tool Architecture');
    await expect(page.getByTestId('ticket.detail.executionEvidence')).toBeVisible();
    await expect(page.getByTestId('ticket.evidence.empty').first()).toContainText(/No linked execution evidence|No execution evidence/i);
    await expect(page.getByTestId('ticket.inspector.blockedActions')).toContainText('No execution run is linked to this ticket yet.');
    await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeDisabled();
    notes.push('Unlinked seeded ticket shows honest empty evidence and keeps Review Latest Run disabled.');

    await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeEnabled();
    await page.getByTestId('ticket.command.startDisposableRun').click();
    await expect(page.getByTestId('ticket.runReview')).toBeVisible({ timeout: 120_000 });
    await expect(page.getByTestId('ticket.runReview.disposable')).toContainText('Disposable run');
    await expect(page.getByTestId('ticket.runReview.events')).toContainText(/DisposableCommandCompleted|DisposableCommandFailed/, {
      timeout: 120_000
    });
    await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeEnabled();
    notes.push('Start Disposable Run created a real backend-owned run and opened the in-ticket review panel.');

    await page.getByRole('button', { name: /Wire Start Disposable Run/ }).click();
    await expect(page.getByTestId('ticket.detail.header')).toContainText('Wire Start Disposable Run');
    await expect(page.getByTestId('ticket.evidence.latestRun')).toContainText('localtest-run-ticket-3002', { timeout: 15_000 });
    await expect(page.getByTestId('ticket.inspector.latestRun')).toContainText('localtest-run-ticket-3002');
    await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeEnabled();
    notes.push('Linked seeded ticket shows real linked run evidence and enables Review Latest Run.');

    await page.getByTestId('ticket.command.reviewLatestRun').click();
    await expect(page.getByTestId('ticket.runReview')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('ticket.runReview.summary')).toContainText('localtest-run-ticket-3002');
    await expect(page.getByTestId('ticket.runReview.disposable')).toContainText('Disposable run');
    await expect(page.getByTestId('ticket.runReview.events')).toContainText('RunCompleted');
    notes.push('Run review panel opens in-ticket with disposable marker and persisted events.');

    await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeVisible();

    const bodyText = await page.locator('body').innerText();
    expect(bodyText).not.toMatch(/\bfake\b/i);

    test.info().annotations.push({
      type: 'localtest-notes',
      description: notes.join(' | ')
    });
  });
});
