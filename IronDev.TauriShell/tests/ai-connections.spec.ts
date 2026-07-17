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
        supportedPurposes: ['ProjectFeatureWork'],
        purposeDescription: 'Executable provider for project feature work',
        lastSuccessfulTestUtc: null,
        lastFailedTestUtc: null,
        availableModels: ['gpt-4o'],
        enabled: true,
        tenantAvailable: true,
        projectAvailable: true,
        credentialRotatedUtc: null,
        credentialRevokedUtc: null,
        createdByUserId: 0,
        createdUtc: null,
        updatedByUserId: 7,
        updatedUtc: null,
        version: 'IronDev AI Connection Contract 2.6.0',
        boundary: 'AI connection metadata is non-secret. Credential values are write-only and never returned by this endpoint.'
      }
    ]
  });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.aiConnections').click();

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

test('settings separates the LocalTest smoke fixture from project-work connections', async ({ page }) => {
  await mockWorkspace(page, {
    connections: [
      {
        id: 'deployment-default', tenantId: 3, displayName: 'Deployment default', providerKind: 'openai',
        controlledEndpointId: 'deployment-default-openai', controlledEndpoint: 'https://api.openai.com',
        credentialConfigured: true, credentialStatus: 'Configured', supportedPurposes: ['ProjectFeatureWork'],
        purposeDescription: 'Executable provider for project feature work', availableModels: ['gpt-4o'], enabled: true,
        tenantAvailable: true, projectAvailable: true, createdByUserId: 0, updatedByUserId: 7,
        version: 'IronDev AI Connection Contract 2.6.0', boundary: 'Non-secret metadata.'
      },
      {
        id: 'localtest-deterministic', tenantId: 3, displayName: 'LocalTest deterministic smoke',
        providerKind: 'alpha-smoke-deterministic', controlledEndpointId: 'localtest-deterministic',
        controlledEndpoint: 'localtest:deterministic-model-words', credentialConfigured: true,
        credentialStatus: 'Not required', supportedPurposes: ['SmokeSimulation'],
        purposeDescription: 'Fixed fixture · does not implement project work', availableModels: ['localtest-deterministic'],
        enabled: true, tenantAvailable: true, projectAvailable: true, createdByUserId: 0, updatedByUserId: 0,
        version: 'IronDev AI Connection Contract 2.6.0', boundary: 'Non-secret metadata.'
      }
    ]
  });

  await page.goto('/projects/7/library/settings/ai-connections');

  await expect(page.getByTestId('flow.settings.aiConnections.count')).toContainText('1 project-work · 1 smoke fixture');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.1')).toContainText('LocalTest deterministic smoke');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.1.purposeDescription')).toHaveText('Fixed fixture · does not implement project work');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.1.availability')).toHaveText('Smoke only');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.1.credentialInput')).toHaveCount(0);
  await expect(page.getByTestId('flow.settings.aiConnections.connection.1.test')).toHaveText('Test smoke fixture');
});

test('settings stores and revokes AI credentials without rendering the secret', async ({ page }) => {
  const secret = 'local-provider-credential-value';
  await mockWorkspace(page, {
    connections: [
      {
        id: 'deployment-default',
        tenantId: 3,
        displayName: 'Deployment default',
        providerKind: 'openai',
        controlledEndpointId: 'deployment-default-openai',
        controlledEndpoint: 'https://api.openai.com',
        credentialConfigured: false,
        credentialStatus: 'Missing',
        supportedPurposes: ['ProjectFeatureWork'],
        purposeDescription: 'Executable provider for project feature work',
        lastSuccessfulTestUtc: null,
        lastFailedTestUtc: null,
        availableModels: ['gpt-4o'],
        enabled: true,
        tenantAvailable: true,
        projectAvailable: true,
        credentialRotatedUtc: null,
        credentialRevokedUtc: null,
        createdByUserId: 0,
        createdUtc: null,
        updatedByUserId: 7,
        updatedUtc: null,
        version: 'IronDev AI Connection Contract 2.6.0',
        boundary: 'Credential values are accepted only on write, stored protected, and never returned by API responses.'
      }
    ]
  });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.aiConnections').click();

  await page.getByTestId('flow.settings.aiConnections.connection.0.credentialInput').fill(secret);
  await page.getByTestId('flow.settings.aiConnections.connection.0.reason').fill('manual local test');
  await page.getByTestId('flow.settings.aiConnections.connection.0.saveCredential').click();

  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.credential')).toHaveText('Configured');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.rotated')).not.toHaveText('Never');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.credentialInput')).toHaveValue('');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.message')).toContainText('Credential stored');
  await expect(page.locator('body')).not.toContainText(secret);

  await page.getByTestId('flow.settings.aiConnections.connection.0.revokeCredential').click();

  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.credential')).toHaveText('Revoked');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.revoked')).not.toHaveText('Never');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.message')).toContainText('Credential revoked');
  await expect(page.locator('body')).not.toContainText(secret);
});

test('settings shows an honest empty state when no AI connections are returned', async ({ page }) => {
  await mockWorkspace(page, { connections: [] });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.aiConnections').click();

  await expect(page.getByTestId('flow.settings.aiConnections.count')).toContainText('None configured');
  await expect(page.getByTestId('flow.settings.aiConnections.empty')).toContainText('No AI connection metadata');
});

test('settings tests a controlled connection and renders durable health truth', async ({ page }) => {
  await mockWorkspace(page, {
    connections: [{
      id: 'deployment-default', tenantId: 3, displayName: 'Deployment default', providerKind: 'openai',
      controlledEndpointId: 'deployment-default-openai', controlledEndpoint: 'https://api.openai.com',
      credentialConfigured: true, credentialStatus: 'Configured', lastSuccessfulTestUtc: null, lastFailedTestUtc: null,
      supportedPurposes: ['ProjectFeatureWork'], purposeDescription: 'Executable provider for project feature work',
      availableModels: ['gpt-4o'], enabled: true, tenantAvailable: true, projectAvailable: true,
      credentialRotatedUtc: null, credentialRevokedUtc: null, createdByUserId: 0, createdUtc: null,
      updatedByUserId: 7, updatedUtc: null, version: 'IronDev AI Connection Contract 2.6.0',
      boundary: 'Credential values are never returned.'
    }]
  });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.aiConnections').click();
  await page.getByTestId('flow.settings.aiConnections.connection.0.test').click();

  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.message')).toContainText('passed');
  await expect(page.getByTestId('flow.settings.aiConnections.connection.0.lastSuccess')).not.toHaveText('Never');
  await expect(page.locator('body')).not.toContainText('configured-secret-value');
});

async function mockWorkspace(page: Page, options: { connections: unknown[] }) {
  let connections = options.connections as Array<Record<string, unknown>>;

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
  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
  await page.route('**/irondev-api/api/v1/ai-connections/**', async (route) => {
    const url = route.request().url();
    const current = connections[0] ?? {
      id: 'deployment-default',
      tenantId: 3,
      displayName: 'Deployment default'
    };

    if (route.request().method() === 'PUT' && url.endsWith('/credential')) {
      const body = route.request().postDataJSON() as { credential?: string };
      if (!body.credential?.trim()) {
        await route.fulfill({
          status: 400,
          contentType: 'application/json',
          body: JSON.stringify({ succeeded: false, failureReason: 'Credential is required.', boundary: 'write-only' })
        });
        return;
      }

      const next = {
        ...current,
        credentialConfigured: true,
        credentialStatus: 'Configured',
        credentialRotatedUtc: '2026-07-12T00:00:00Z',
        credentialRevokedUtc: null,
        updatedUtc: '2026-07-12T00:00:00Z'
      };
      connections = [next];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ succeeded: true, connection: next, boundary: 'write-only' })
      });
      return;
    }

    if (route.request().method() === 'POST' && url.endsWith('/credential/revoke')) {
      const next = {
        ...current,
        credentialConfigured: false,
        credentialStatus: 'Revoked',
        credentialRotatedUtc: null,
        credentialRevokedUtc: '2026-07-12T00:01:00Z',
        updatedUtc: '2026-07-12T00:01:00Z'
      };
      connections = [next];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ succeeded: true, connection: next, boundary: 'write-only' })
      });
      return;
    }

    if (route.request().method() === 'POST' && url.endsWith('/test')) {
      const testedAtUtc = '2026-07-12T00:02:00Z';
      const next = { ...current, lastSuccessfulTestUtc: testedAtUtc };
      connections = [next];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ succeeded: true, status: 'Passed', testedAtUtc, connection: next, boundary: 'Non-secret test outcome.' })
      });
      return;
    }

    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'Not found' }) });
  });
  await page.route('**/irondev-api/api/v1/ai-connections', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(connections) });
  });
  await page.route('**/irondev-api/api/v1/agent-profiles', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
}
