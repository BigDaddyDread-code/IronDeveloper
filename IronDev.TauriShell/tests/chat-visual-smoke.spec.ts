import { expect, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualReportDir = path.join(process.cwd(), 'reports', 'visual-smoke');

test('captures login hierarchy for LocalTest review', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.flowHint')).toHaveText('Sign in, then select a project to continue.');
  await expect(page.getByTestId('auth.localtestCredentials')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('localtest@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('auth.submit')).toBeVisible();
  await expect(page.getByTestId('app.versionStrip')).toBeVisible();

  await capture(page, 'login-localtest.png');
});

test('captures Chat hierarchy with visible composer and collapsible context', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: [
          '## Project state',
          '',
          '- Tickets are ready for review.',
          '- Build readiness should be checked before sandbox work.',
          '',
          '```ts',
          'const nextAction = "Review build readiness";',
          '```'
        ].join('\n'),
        contextSummary: 'Context used: project summary, recent tickets, recent runs.',
        linkedFilePaths: 'IronDev.TauriShell/src/features/chatToBuild/ChatWorkspace.tsx',
        linkedSymbols: 'ChatWorkspace',
        traceId: 42
      })
    });
  });

  await page.goto('/');
  await page.getByTestId('shell.nav.chat').click();

  await expect(page.getByTestId('chat.workspace')).toBeVisible();
  await expect(page.getByTestId('chat.sessions')).toHaveCount(0);
  await expect(page.getByTestId('chat.composer')).toBeVisible();
  await expect(page.getByTestId('chat.contextPanel')).toBeVisible();
  await capture(page, 'chat-empty-with-context.png');

  await page.getByTestId('chat.composer.input').fill('Where should I start?');
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.thread').getByRole('heading', { name: 'Project state' })).toBeVisible();
  await expect(page.getByTestId('chat.composer')).toBeVisible();
  await capture(page, 'chat-response-with-context.png');

  await page.getByTestId('chat.contextPanel.toggle').click();
  await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);
  await expect(page.getByTestId('chat.contextPanel.show')).toBeVisible();
  await expect(page.getByTestId('chat.composer')).toBeVisible();
  await capture(page, 'chat-response-context-hidden.png');
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
}
