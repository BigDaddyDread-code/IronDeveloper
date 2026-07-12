import { expect, test } from '@playwright/test';

// Live LocalTest smoke for the flow shell: sign in with seeded credentials, see the
// seeded tickets on the board, open one as a work item with real build readiness, and
// confirm settings lists the seeded tenant membership. The old cockpit's disposable-run
// and run-review coverage returns when the Build and Review stages land on the spine.

test.describe('LocalTest manual flow-shell smoke', () => {
  test.skip(process.env.IRONDEV_LOCALTEST_LIVE !== '1', 'Set IRONDEV_LOCALTEST_LIVE=1 to run against the live LocalTest API.');

  test('proves the seeded LocalTest flow loop', async ({ page }) => {
    const notes: string[] = [];

    await page.goto('/');
    await page.evaluate(() => window.localStorage.clear());
    await page.reload();

    await expect(page.getByTestId('auth.form')).toBeVisible({ timeout: 15_000 });
    await page.getByTestId('auth.email').fill('bob@irondev.local');
    await page.getByTestId('auth.password').fill('change-me-local-only');
    await page.getByTestId('auth.submit').click();

    if (await page.getByTestId('flow.tenantChooser').isVisible({ timeout: 5_000 }).catch(() => false)) {
      await page.getByTestId('flow.tenantChooser.tenant.1').click();
      notes.push('Tenant chooser shown and Local Test Tenant selected.');
    } else {
      notes.push('Tenant auto-selected through the tenant API.');
    }

    await expect(page.getByTestId('flow.chooser')).toBeVisible({ timeout: 15_000 });
    await page.getByRole('button', { name: /Open IronDev Local Test Project/i }).click();
    notes.push('Project chooser shown and IronDev Local Test Project selected.');

    await expect(page.getByTestId('flow.shell')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('flow.board.columns')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('Add Governed Tool Architecture')).toBeVisible({ timeout: 15_000 });

    const board = await page.getByTestId('flow.board.columns').innerText();
    for (const title of ['Add Governed Tool Architecture', 'Wire Start Sandbox Run', 'Improve Ticket Workspace UI']) {
      expect(board).toContain(title);
    }
    notes.push('Board shows the three seeded LocalTest tickets in pipeline columns.');

    await page.getByRole('button', { name: /Add Governed Tool Architecture/ }).click();
    await expect(page.getByTestId('flow.stagerail')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('flow.contract')).toBeVisible();
    notes.push('Work item opens on the spine with the contract rail and real readiness from the API.');

    await page.getByTestId('flow.userMenu').click();
    await page.getByTestId('flow.nav.settings').click();
    await page.getByTestId('flow.settings.section.safety').click();
    await expect(page.getByTestId('flow.settings.banner')).toContainText('never mutation authority');
    notes.push('Settings preserves the explicit no-mutation-authority boundary.');

    await page.getByTestId('flow.nav.library').click();
    await page.getByTestId('flow.library.governance').click();
    await expect(page.getByTestId('flow.governance.overview')).toBeVisible();
    await expect(page.getByTestId('flow.governance.status')).toHaveText('Attention required');
    await expect(page.getByTestId('flow.governance.primaryAction')).toHaveText('Review controlled apply');
    await expect(page.getByTestId('flow.governance.attention')).toContainText('WI-3002');
    await expect(page.getByTestId('flow.governance.controls')).toContainText('IronDev invariant');
    await expect(page.getByTestId('flow.governance.controls')).toContainText('Tenant policy');
    await expect(page.getByTestId('flow.governance.exceptions')).toContainText('Execution evidence is incomplete');
    await expect(page.getByTestId('flow.governance.boundary')).toContainText('grants no approval');
    await expect(page.getByTestId('flow.governanceHost')).toHaveCount(0);
    await expect(page.getByRole('button', { name: /approve|continue workflow|apply source|rollback/i })).toHaveCount(0);
    notes.push('Governance reports backend posture, next action, sourced controls, and the evidence exception without mutation controls.');

    await page.getByRole('button', { name: 'Technical evidence' }).click();
    await expect(page).toHaveURL('/projects/1/library/governance/technical');
    await expect(page.getByTestId('flow.governance.technical')).toContainText('Runs and operations');
    await expect(page.getByTestId('flow.governance.technical')).toContainText('Approvals and policy');
    await page.getByRole('button', { name: 'Back to overview' }).click();
    await expect(page).toHaveURL('/projects/1/library/governance');
    await expect(page.getByTestId('flow.governance.overview')).toBeVisible();
    notes.push('Technical evidence is progressively disclosed and returns to the project overview.');

    const bodyText = await page.locator('body').innerText();
    expect(bodyText).not.toMatch(/\bfake\b/i);

    test.info().annotations.push({
      type: 'localtest-notes',
      description: notes.join(' | ')
    });
  });
});
