import { expect, test, type Page } from '@playwright/test';

// AG-5: the Settings → Agents panel. Edit a per-agent model and voice through
// the governed endpoints; a refused secret is shown honestly. Voice and model
// only — the panel never claims to grant authority.

test('agents panel edits a model and voice and saves through the governed endpoint', async ({ page }) => {
  await mockWorkspace(page);
  const state = await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.agents')).toBeVisible();
  await expect(page.getByTestId('flow.settings.agent.tester')).toBeVisible();
  await expect(page.getByTestId('flow.settings.agent.critic')).toBeVisible();

  await page.getByTestId('flow.settings.agent.tester.provider').selectOption('ollama');
  await page.getByTestId('flow.settings.agent.tester.model').fill('llama3');
  await page.getByTestId('flow.settings.agent.tester.personality').fill('Terse and exacting.');
  await page.getByTestId('flow.settings.agent.tester.save').click();

  await expect(page.getByText('Saved.')).toBeVisible();
  expect(state.lastUpdate.role.toLowerCase()).toBe('tester');
  expect(state.lastUpdate.body.provider).toBe('ollama');
  expect(state.lastUpdate.body.model).toBe('llama3');
});

test('a secret in a profile is refused and shown honestly', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page, { refuseSecret: true });

  await page.goto('/');
  await page.getByTestId('flow.nav.settings').click();

  await page.getByTestId('flow.settings.agent.builder.personality').fill('use sk-secret');
  await page.getByTestId('flow.settings.agent.builder.save').click();

  await expect(page.getByTestId('flow.settings.agents.error')).toContainText('secret');
});

interface AgentState {
  lastUpdate: { role: string; body: Record<string, unknown> };
}

async function mockAgentProfiles(page: Page, options: { refuseSecret?: boolean } = {}): Promise<AgentState> {
  const state: AgentState = { lastUpdate: { role: '', body: {} } };
  const profile = (role: string) => ({
    role,
    provider: 'openai',
    model: 'gpt-4o',
    baseUrl: '',
    timeoutSeconds: 60,
    skill: '',
    personality: '',
    boundary: 'An agent profile configures voice and model, never authority.'
  });

  await page.route('**/irondev-api/api/v1/agent-profiles', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(['Orchestrator', 'Builder', 'Tester', 'Critic'].map(profile))
    });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1];
    state.lastUpdate = { role, body: route.request().postDataJSON() as Record<string, unknown> };
    if (options.refuseSecret) {
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ succeeded: false, failureReason: 'This update looks like it contains a secret.' })
      });
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ succeeded: true, failureReason: '', profile: profile(role) })
    });
  });

  return state;
}

async function mockWorkspace(page: Page) {
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
  await page.route('**/irondev-api/api/tenants/3/users', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
}
