import { expect, test, type Page } from '@playwright/test';

// AG-5: the Settings → Agents panel. Edit a per-agent model and voice through
// the governed endpoints; a refused secret is shown honestly. Voice and model
// only — the panel never claims to grant authority.

test('agents panel edits a model and voice and saves through the governed endpoint', async ({ page }) => {
  await mockWorkspace(page);
  const state = await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.agents')).toBeVisible();
  await expect(page.getByTestId('flow.settings.agent.analyst')).toContainText('Workshop guide');
  await expect(page.getByTestId('flow.settings.agent.analyst.defaultVersion')).toContainText('IronDev Agent Defaults 2.5.0');
  await expect(page.getByTestId('flow.settings.agent.analyst.boundary')).toContainText('cannot approve');
  await expect(page.getByTestId('flow.settings.agent.analyst.boundary')).toContainText('apply source');
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

test('the orchestrator card is honest — deterministic, no model to configure', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.agent.orchestrator')).toBeVisible();
  // It states what it is...
  await expect(page.getByTestId('flow.settings.agent.orchestrator.deterministic')).toContainText('runs no model');
  // ...and offers nothing to configure (no dead provider dropdown).
  await expect(page.getByTestId('flow.settings.agent.orchestrator.provider')).toHaveCount(0);
  await expect(page.getByTestId('flow.settings.agent.orchestrator.save')).toHaveCount(0);
  // A real model-running agent still has its editor.
  await expect(page.getByTestId('flow.settings.agent.builder.provider')).toBeVisible();
});

test('agents panel shows effective profile provenance', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.agent.builder.effective.summary')).toContainText('openai / gpt-4o / 60s');
  await expect(page.getByTestId('flow.settings.agent.builder.effective.providerSource')).toContainText('DeploymentDefault');
  await expect(page.getByTestId('flow.settings.agent.builder.effective.skillSource')).toContainText('BuiltInDefault');
  await expect(page.getByTestId('flow.settings.agent.builder.effective.hash')).toContainText('sha256:');
  await expect(page.getByTestId('flow.settings.agent.orchestrator.effective.summary')).toContainText('Deterministic / No model / 0s');
  await expect(page.getByTestId('flow.settings.agent.orchestrator.effective.providerSource')).toContainText('DeterministicRole');
});

test('agents panel accepts numeric backend role values from LocalTest', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page, { numericRoles: true });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await expect(page.getByTestId('flow.settings.agent.analyst')).toContainText('Workshop guide');
  await expect(page.getByTestId('flow.settings.agent.orchestrator')).toBeVisible();
  await expect(page.getByTestId('flow.settings.agent.orchestrator.deterministic')).toContainText('runs no model');
  await expect(page.getByTestId('flow.settings.agent.tester.provider')).toBeVisible();
});

test('a secret in a profile is refused and shown honestly', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page, { refuseSecret: true });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();

  await page.getByTestId('flow.settings.agent.builder.personality').fill('use sk-secret');
  await page.getByTestId('flow.settings.agent.builder.save').click();

  await expect(page.getByTestId('flow.settings.agents.error')).toContainText('secret');
});

interface AgentState {
  lastUpdate: { role: string; body: Record<string, unknown> };
}

async function mockAgentProfiles(page: Page, options: { numericRoles?: boolean; refuseSecret?: boolean } = {}): Promise<AgentState> {
  const state: AgentState = { lastUpdate: { role: '', body: {} } };
  const roleName = (role: string | number) => {
    if (role === 4 || role === 'Analyst') return 'Workshop guide';
    if (role === 1) return 'Builder';
    if (role === 2) return 'Tester';
    if (role === 3) return 'Critic';
    if (role === 0) return 'Orchestrator';
    return String(role);
  };
  const profile = (role: string | number) => ({
    role,
    displayName: roleName(role),
    builtInDefaultName: role === 'Orchestrator' || role === 0 ? '' : 'IronDev Agent Defaults',
    builtInDefaultVersion: role === 'Orchestrator' || role === 0 ? '' : 'IronDev Agent Defaults 2.5.0',
    provider: options.numericRoles ? 'OpenAI' : 'openai',
    model: 'gpt-4o',
    baseUrl: '',
    timeoutSeconds: 60,
    skill: '',
    personality: '',
    boundary: role === 'Analyst' || role === 4
      ? 'The Analyst is the Workshop guide. It cannot approve, start a governed build, continue workflow, disposition findings, or apply source.'
      : 'An agent profile configures voice and model, never authority.'
  });
  const effectiveProfile = (role: string | number) => {
    const isOrchestrator = role === 'Orchestrator' || role === 0;
    const sourceLayer = isOrchestrator ? 'DeterministicRole' : 'DeploymentDefault';
    const sourceLabel = isOrchestrator ? 'Orchestrator' : 'Ai';

    return {
      role,
      displayName: roleName(role),
      aiConnectionId: isOrchestrator ? '' : 'deployment-default',
      provider: isOrchestrator ? '' : (options.numericRoles ? 'OpenAI' : 'openai'),
      model: isOrchestrator ? '' : 'gpt-4o',
      timeoutSeconds: isOrchestrator ? 0 : 60,
      effectiveSkill: '',
      effectivePersonality: '',
      fieldSources: [
        { field: 'provider', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : `${sourceLabel}:Provider`, inherited: true, detail: '' },
        { field: 'model', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : `${sourceLabel}:Model`, inherited: true, detail: '' },
        { field: 'timeoutSeconds', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : `${sourceLabel}:TimeoutSeconds`, inherited: true, detail: '' },
        { field: 'effectiveSkill', sourceLayer: isOrchestrator ? sourceLayer : 'BuiltInDefault', sourceLabel: isOrchestrator ? sourceLabel : 'IronDev Agent Defaults', inherited: true, version: isOrchestrator ? '' : 'IronDev Agent Defaults 2.5.0', detail: '' },
        { field: 'effectivePersonality', sourceLayer: isOrchestrator ? sourceLayer : 'BuiltInDefault', sourceLabel: isOrchestrator ? sourceLabel : 'IronDev Agent Defaults', inherited: true, version: isOrchestrator ? '' : 'IronDev Agent Defaults 2.5.0', detail: '' }
      ],
      builtInDefaultVersion: isOrchestrator ? '' : 'IronDev Agent Defaults 2.5.0',
      tenantProfileVersion: null,
      projectProfileVersion: null,
      effectiveHash: `sha256:test-effective-${String(role).toLowerCase()}`,
      boundary: 'An agent profile configures voice and model, never authority.'
    };
  };

  await page.route('**/irondev-api/api/v1/agent-profiles/effective**', async (route) => {
    const roles = options.numericRoles ? [4, 1, 2, 3, 0] : ['Analyst', 'Builder', 'Tester', 'Critic', 'Orchestrator'];
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(roles.map(effectiveProfile))
    });
  });

  await page.route('**/irondev-api/api/v1/agent-profiles', async (route) => {
    const roles = options.numericRoles ? [4, 1, 2, 3, 0] : ['Analyst', 'Builder', 'Tester', 'Critic', 'Orchestrator'];
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(roles.map(profile))
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
  await page.route('**/irondev-api/api/v1/ai-connections', async (route) => {
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
