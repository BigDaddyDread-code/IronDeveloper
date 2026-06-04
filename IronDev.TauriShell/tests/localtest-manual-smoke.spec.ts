import { expect, test } from '@playwright/test';

test.describe('LocalTest manual cockpit smoke', () => {
  test.skip(process.env.IRONDEV_LOCALTEST_LIVE !== '1', 'Set IRONDEV_LOCALTEST_LIVE=1 to run against the live LocalTest API.');

  test('proves the seeded LocalTest ticket cockpit loop', async ({ page }) => {
    const notes: string[] = [];

    await page.goto('/');
    await page.evaluate(() => window.localStorage.clear());
    await page.reload();

    await expect(page.getByTestId('app.shell')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('home.workspace')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('environment.badge')).toContainText('LocalTest', { timeout: 15_000 });
    await expect(page.getByTestId('api.status.connected')).toBeVisible();

    await page.getByTestId('shell.nav.tickets').click();
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
      await page.getByTestId('project.option.select.1').click();
      notes.push('Project selector shown and IronDev Local Test Project selected.');
    } else {
      notes.push('Project selected from configured LocalTest project id 1.');
    }

    await expect(page.getByTestId('project.status.selected')).toContainText('IronDev Local Test Project', { timeout: 15_000 });

    await page.getByTestId('shell.nav.chat').click();
    await expect(page.getByTestId('chat.workspace')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('chat.command.send')).toBeVisible();
    await expect(page.getByTestId('chat.command.send')).toBeDisabled();
    await page.getByTestId('chat.composer.input').fill('Where are we on this project?');
    await expect(page.getByTestId('chat.command.send')).toBeEnabled();
    await page.getByTestId('chat.command.send').click();
    await expect(page.getByTestId('chat.thread')).toContainText('Where are we on this project?');
    await expect(page.getByTestId('chat.thread')).toContainText(/Project state|Recent tickets/i, { timeout: 30_000 });
    await expect(page.getByTestId('chat.contextPanel')).toContainText(/Context summary|grounded project context/i);
    await expect(page.getByTestId('chat.command.reviewProjectState')).toBeVisible();
    await page.getByTestId('chat.command.reviewProjectState').click();
    await expect(page.getByTestId('chat.thread')).toContainText('Review Project State');
    await expect(page.getByTestId('chat.thread')).toContainText(/Recommended next actions/i, { timeout: 30_000 });
    notes.push('Chat workspace sends project-scoped messages and renders grounded project review output.');

    await page.getByTestId('shell.nav.tickets').click();
    await expect(page.getByTestId('ticket.row')).toHaveCount(3, { timeout: 15_000 });

    const ticketList = await page.getByTestId('ticket.list').innerText();
    for (const title of ['Add Governed Tool Architecture', 'Wire Start Sandbox Run', 'Improve Ticket Workspace UI']) {
      expect(ticketList).toContain(title);
    }

    await page.getByRole('button', { name: /Add Governed Tool Architecture/ }).click();
    await expect(page.getByTestId('ticket.detail.header')).toContainText('Add Governed Tool Architecture');
    await expect(page.getByTestId('ticket.detail.executionEvidence')).toBeVisible();
    await expect(page.getByTestId('ticket.evidence.empty').first()).toContainText(/No linked build evidence|No build evidence/i);
    await expect(page.getByTestId('ticket.inspector.blockedActions')).toContainText('No execution run is linked to this ticket yet.');
    await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeDisabled();
    notes.push('Unlinked seeded ticket shows honest empty evidence and keeps Review Run disabled.');

    await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeEnabled();
    await page.getByTestId('ticket.command.startDisposableRun').click();
    await expect(page.getByTestId('ticket.runReview')).toBeVisible({ timeout: 120_000 });
    await expect(page.getByTestId('ticket.runReview.disposable')).toContainText('Sandbox run');
    await expect(page.getByTestId('ticket.runReview.events')).toContainText(/DisposableCommandCompleted|DisposableCommandFailed/, {
      timeout: 120_000
    });
    await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeEnabled();
    notes.push('Start Sandbox Run created a real backend-owned run and opened the in-ticket review panel.');

    await page.getByRole('button', { name: /Wire Start Sandbox Run/ }).click();
    await expect(page.getByTestId('ticket.detail.header')).toContainText('Wire Start Sandbox Run');
    await expect(page.getByTestId('ticket.evidence.latestRun')).toContainText('localtest-run-ticket-3002', { timeout: 15_000 });
    await expect(page.getByTestId('ticket.inspector.latestRun')).toContainText('localtest-run-ticket-3002');
    await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeEnabled();
    notes.push('Linked seeded ticket shows real linked run evidence and enables Review Run.');

    await page.getByTestId('ticket.command.reviewLatestRun').click();
    await expect(page.getByTestId('ticket.runReview')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('ticket.runReview.summary')).toContainText('localtest-run-ticket-3002');
    await expect(page.getByTestId('ticket.runReview.disposable')).toContainText('Sandbox run');
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
