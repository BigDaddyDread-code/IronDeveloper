import { expect, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { mockProjectBoard } from './helpers/mockBoard';

const visualReportDir = path.join(process.cwd(), 'reports', 'visual-smoke');

test('captures login hierarchy for LocalTest review', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.flowHint')).toHaveText('Sign in, then select a project to continue.');
  await expect(page.getByTestId('auth.localtestCredentials')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('bob@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('auth.submit')).toBeVisible();
  await expect(page.getByTestId('auth.apiStatusChip')).toContainText('LocalTest');
  await expect(page.getByTestId('app.authState.configureToken')).toHaveCount(0);

  await capture(page, 'login-localtest.png');
});

test('captures Board and Shape hierarchy for LocalTest review', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: [
          '## Suggested criteria',
          '',
          '- Catalog sorts by title, author, and price.',
          '- Default sort is title, ascending.'
        ].join('\n'),
        contextSummary: 'Shaping lane using project context.',
        linkedFilePaths: 'src/Catalog/CatalogService.cs',
        traceId: 42
      })
    });
  });

  await page.goto('/');
  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await capture(page, 'board-pipeline.png');

  await page.getByTestId('flow.board.new').click();
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.getByTestId('flow.contract')).toBeVisible();
  await capture(page, 'shape-empty-contract.png');

  await page.getByTestId('flow.shape.prompt').fill('Users need to sort the catalog.');
  await page.getByTestId('flow.shape.prompt').press('Enter');
  await expect(page.getByRole('heading', { name: 'Suggested criteria' })).toBeVisible();
  await expect(page.getByTestId('flow.shape.gate')).toContainText('blocked');
  await capture(page, 'shape-discussion-response.png');
});

async function capture(page: import('@playwright/test').Page, name: string) {
  await mkdir(visualReportDir, { recursive: true });
  await page.screenshot({
    path: path.join(visualReportDir, name),
    fullPage: true
  });
}

async function mockHealthyApi(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'healthy' })
    });
  });
  await page.route('**/irondev-api/api/environment', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        environment: 'LocalTest',
        database: 'IronDeveloper_Test',
        weaviatePrefix: 'irondev_test',
        isTestEnvironment: true,
        workspaceRoot: 'C:\\IronDevTestWorkspaces\\',
        logsRoot: 'C:\\IronDevTestLogs\\',
        dangerRealRepoWritesEnabled: false
      })
    });
  });
}

async function mockSelectedProject(page: import('@playwright/test').Page) {
  const boardTickets = [
    { id: 42, tenantId: 3, projectId: 7, title: 'Add book sorting to catalog', status: 'Draft', acceptanceCriteria: null }
  ];
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 })
    });
  });
  await page.route('**/irondev-api/api/tenants**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    });
  });
  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(boardTickets)
    });
  });
  await mockProjectBoard(page, { projectName: 'IronDeveloper', tickets: boardTickets });
  await page.route('**/irondev-api/api/projects/7/chat/sessions', async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(9007) });
  });
  await page.route('**/irondev-api/api/projects/7/chat/sessions/*/messages', async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(9107) });
  });
}
