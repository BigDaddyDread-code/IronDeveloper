import { expect, test, type Page } from '@playwright/test';

test('settings shows tenant AI connection metadata without credential material', async ({ page }) => {
  await mockWorkspace(page, {
    connections: [
      {
        id: 'deployment-default',
        tenantId: 3,
        displayName: 'Deployment default',
        providerKind: 'openai',
        controlledEndpointId: 'deployment-default-openai',
        controlledEndpoint: 'https://api.openai.com',
        credentialConfigured: true,
        credentialStatus: 'Configured',
        lastSuccessfulTestUtc: null,
        lastFailedTestUtc: null,
        availableModels: ['gpt-4o'],
        enabled: true,
        tenantAvailable: true,
        projectAvailable: true,
        credentialRotatedUtc: null,
        createdByUserId: 0,
        createdUtc: null,
        updatedByUserId: 7,
        updatedUtc: null,
        version: 'IronDev AI Connection Contract 2.5.0',
        boundary: 'AI connection metadata is non-secret. Credential values are write-only and never returned by this endpoint.'
      }
    ]
  });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.aiConnections')).toBeVisible();
  await expect(page.getByTestId('flow.settings.aiConnections.count')).toContainText('1 available');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.provider')).toHaveText('openai');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.endpoint')).toHaveText('https://api.openai.com');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.credential')).toHaveText('Configured');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.models')).toContainText('gpt-4o');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.boundary')).toContainText('never returned');
  await expect(page.locator('body')).not.toContainText('configured-secret-value');
  await expect(page.locator('body')).not.toContainText('api_key');
});

test('settings shows an honest empty state when no AI connections are returned', async ({ page }) => {
  await mockWorkspace(page, { connections: [] });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.aiConnections.count')).toContainText('None configured');
  await expect(page.getByTestId('flow.settings.aiConnections.empty')).toContainText('No AI connection metadata');
});

async function mockWorkspace(page: Page, options: { connections: unknown[] }) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) });
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
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]) });
  });
  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 7, tenantId: 3, name: 'BookSeller', description: 'Dogfood project' }]) });
  });
  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
  await page.route('**/irondev-api/api/v1/ai-connections', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(options.connections) });
  });
  await page.route('**/irondev-api/api/v1/agent-profiles', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
}
