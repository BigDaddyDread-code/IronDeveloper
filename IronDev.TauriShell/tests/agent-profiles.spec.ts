import { expect, test, type Page } from '@playwright/test';

// AG-5: the Settings → Agents panel. Edit a per-agent model and voice through
// the governed endpoints; a refused secret is shown honestly. Voice and model
// only — the panel never claims to grant authority.

test('agents panel edits a model and voice as a versioned draft', async ({ page }) => {
  await mockWorkspace(page);
  const state = await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.agents').click();

  await expect(page.getByTestId('flow.settings.agents')).toBeVisible();
  await expect(page.getByTestId('flow.settings.agent.analyst')).toContainText('Workshop guide');
  await expect(page.getByTestId('flow.settings.agent.analyst.defaultVersion')).toContainText('IronDev Agent Defaults 2.5.0');
  await expect(page.getByTestId('flow.settings.agent.analyst.boundary')).toContainText('cannot approve');
  await expect(page.getByTestId('flow.settings.agent.analyst.boundary')).toContainText('apply source');
  await expect(page.getByTestId('flow.settings.agent.builder.connection')).toHaveValue('deployment-default');
  await expect(page.getByTestId('flow.settings.agent.tester')).toBeVisible();
  await expect(page.getByTestId('flow.settings.agent.critic')).toBeVisible();

  await page.getByTestId('flow.settings.agent.tester.provider').selectOption('ollama');
  await page.getByTestId('flow.settings.agent.tester.model').fill('llama3');
  await page.getByTestId('flow.settings.agent.tester.personality').fill('Terse and exacting.');
  await page.getByTestId('flow.settings.agent.tester.save').click();

  await expect(page.getByText('Draft saved.')).toBeVisible();
  expect(state.lastUpdate.role.toLowerCase()).toBe('tester');
  expect(state.lastUpdate.body.provider).toBe('ollama');
  expect(state.lastUpdate.body.model).toBe('llama3');
  expect(state.lastUpdate.url).toContain('scope=project');
});

test('agents panel switches explicitly between project overrides and tenant defaults', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockWorkspace(page);
  await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.agents').click();

  await expect(page.getByTestId('flow.settings.agents.scope.project')).toHaveAttribute('aria-pressed', 'true');
  await page.getByTestId('flow.settings.agents.scope.tenant').click();
  await expect(page.getByTestId('flow.settings.agents.scope.tenant')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('flow.settings.agent.builder')).toBeVisible();
});

test('the orchestrator card is honest — deterministic, no model to configure', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.agents').click();

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
  await page.getByTestId('flow.settings.section.agents').click();

  await expect(page.getByTestId('flow.settings.agent.builder.effective.summary')).toContainText('openai / gpt-4o / 60s');
  await expect(page.getByTestId('flow.settings.agent.builder.effective.connectionSource')).toContainText('DeploymentDefault');
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
  await page.getByTestId('flow.settings.section.agents').click();

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
  await page.getByTestId('flow.settings.section.agents').click();

  await page.getByTestId('flow.settings.agent.builder.personality').fill('use sk-secret');
  await page.getByTestId('flow.settings.agent.builder.save').click();

  await expect(page.getByTestId('flow.settings.agents.error')).toContainText('secret');
});

test('agents panel resets fields and restores immutable published versions', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.agents').click();

  await expect(page.getByTestId('flow.settings.agent.builder.history')).toContainText('v1');
  await expect(page.getByTestId('flow.settings.agent.builder.history')).toContainText('Known working profile');
  await page.getByTestId('flow.settings.agent.builder.compare.1').check();
  await page.getByTestId('flow.settings.agent.builder.compare.2').check();
  await expect(page.getByTestId('flow.settings.agent.builder.comparison')).toContainText('Compare v1 to v2');
  await expect(page.getByTestId('flow.settings.agent.builder.comparison')).toContainText('changed');
  await page.getByTestId('flow.settings.agent.builder.history').getByText('v2 - Current profile', { exact: false }).click();
  await expect(page.getByTestId('flow.settings.agent.builder.usage.2')).toContainText('run-usage-2');
  await page.getByTestId('flow.settings.agent.builder.recoveryReason').fill('Return to the known baseline');
  await page.getByTestId('flow.settings.agent.builder.resetField').selectOption('skill');
  await page.getByTestId('flow.settings.agent.builder.resetFieldAction').click();
  await expect(page.getByTestId('flow.settings.agent.builder.notice')).toContainText('Reset published');

  await page.getByTestId('flow.settings.agent.builder.recoveryReason').fill('Restore the prior working version');
  await page.getByTestId('flow.settings.agent.builder.history').getByText('v1 - Known working profile', { exact: false }).click();
  await page.getByTestId('flow.settings.agent.builder.restore.1').click();
  await expect(page.getByTestId('flow.settings.agent.builder.notice')).toContainText('restored as new version');
});

test('settings advanced shows honest product and runtime identity', async ({ page }) => {
  await mockWorkspace(page);
  await mockAgentProfiles(page);

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.advanced').click();

  await expect(page.getByTestId('flow.settings.about')).toContainText('IronDev');
  await expect(page.getByTestId('flow.settings.about')).toContainText('0.5.0');
  await expect(page.getByTestId('flow.settings.about')).toContainText('Reported: IronDeveloper_Test');
  await expect(page.getByTestId('flow.settings.about')).toContainText('Worker');
  await expect(page.getByTestId('flow.settings.about')).toContainText('Not reported');
  await page.getByTestId('flow.settings.about.diagnostics').click();
  await expect(page.getByText('IronDev 0.5.0 (development)')).toBeVisible();
});

test('configuration pack import previews differences and creates drafts without publishing', async ({ page }) => {
  await mockWorkspace(page);
  let importBody: Record<string, unknown> | null = null;
  let publishCalled = false;
  const pack = {
    format: 'irondev-agent-configuration-pack',
    formatVersion: 1,
    packId: 'portable-pack-1',
    exportedAtUtc: '2026-07-12T03:00:00Z',
    sourceScope: 'Tenant',
    sourceTenantId: 9,
    sourceProjectId: null,
    profiles: [{
      role: 1,
      values: { aiConnectionId: 'source-openai', provider: 'openai', model: 'gpt-5', timeoutSeconds: 45, skill: 'Build carefully.', personality: 'Direct.' },
      logicalConnectionName: 'OpenAI shared',
      builtInDefaultVersion: 'IronDev Agent Defaults 2.5.0',
      sourcePublishedVersion: 3
    }],
    boundary: 'non-secret draft only'
  };
  const preview = {
    succeeded: true,
    code: '',
    failureReason: '',
    targetScope: 'Project',
    targetProjectId: 1,
    differences: [{ role: 1, field: 'model', currentValue: 'gpt-4o', importedValue: 'gpt-5', changed: true }],
    expectedRevisions: { Builder: 7 },
    draftOnly: true,
    sourceProvenance: 'Pack portable-pack-1 exported from Tenant scope.',
    boundary: 'non-secret draft only'
  };

  await page.route('**/api/v1/agent-profiles/**', async (route) => {
    const url = route.request().url();
    if (url.includes('/draft/publish')) publishCalled = true;
    if (url.includes('/configuration-pack/preview')) return route.fulfill({ status: 200, json: preview });
    if (url.includes('/configuration-pack/import')) {
      importBody = route.request().postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 200, json: {
        succeeded: true,
        code: '',
        failureReason: '',
        createdDrafts: [{ role: 1, revision: 8, basePublishedVersion: 3, values: pack.profiles[0].values, isValid: true, validationIssues: [], updatedAtUtc: '2026-07-12T03:01:00Z' }],
        published: false,
        preview,
        boundary: 'non-secret draft only'
      } });
    }
    return route.fulfill({ status: 404, json: { error: 'not mocked' } });
  });

  await page.goto('/');
  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await page.getByTestId('flow.settings.section.advanced').click();
  await page.getByTestId('flow.settings.configurationPack.file').setInputFiles({
    name: 'portable-pack.json',
    mimeType: 'application/json',
    buffer: Buffer.from(JSON.stringify(pack))
  });

  await expect(page.getByTestId('flow.settings.configurationPack.preview')).toContainText('gpt-4o');
  await expect(page.getByTestId('flow.settings.configurationPack.preview')).toContainText('gpt-5');
  await page.getByTestId('flow.settings.configurationPack.createDrafts').click();
  await expect(page.getByTestId('flow.settings.configurationPack.success')).toContainText('Nothing was published');
  expect(importBody).toMatchObject({ expectedRevisions: { Builder: 7 } });
  expect(publishCalled).toBe(false);
});

interface AgentState {
  lastUpdate: { role: string; body: Record<string, unknown>; url: string };
  revision: number;
  publishedVersion: number;
}

async function mockAgentProfiles(page: Page, options: { numericRoles?: boolean; refuseSecret?: boolean } = {}): Promise<AgentState> {
  const state: AgentState = { lastUpdate: { role: '', body: {}, url: '' }, revision: 0, publishedVersion: 0 };
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
    aiConnectionId: role === 'Orchestrator' || role === 0 ? '' : 'deployment-default',
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
        { field: 'aiConnectionId', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : 'deployment-default', inherited: true, detail: '' },
        { field: 'provider', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : `${sourceLabel}:Provider`, inherited: true, detail: '' },
        { field: 'model', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : `${sourceLabel}:Model`, inherited: true, detail: '' },
        { field: 'timeoutSeconds', sourceLayer, sourceLabel: isOrchestrator ? sourceLabel : `${sourceLabel}:TimeoutSeconds`, inherited: true, detail: '' },
        { field: 'effectiveSkill', sourceLayer: isOrchestrator ? sourceLayer : 'BuiltInDefault', sourceLabel: isOrchestrator ? sourceLabel : 'IronDev Agent Defaults', inherited: true, version: isOrchestrator ? '' : 'IronDev Agent Defaults 2.5.0', detail: '' },
        { field: 'effectivePersonality', sourceLayer: isOrchestrator ? sourceLayer : 'BuiltInDefault', sourceLabel: isOrchestrator ? sourceLabel : 'IronDev Agent Defaults', inherited: true, version: isOrchestrator ? '' : 'IronDev Agent Defaults 2.5.0', detail: '' }
      ],
      builtInDefaultVersion: isOrchestrator ? '' : 'IronDev Agent Defaults 2.5.0',
      tenantProfileVersion: null,
      projectProfileVersion: null,
      publishedVersion: isOrchestrator ? null : 2,
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

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+\/draft(?:\?.*)?$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1].split('/')[0];
    if (route.request().method() === 'GET') {
      const current = profile(role);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          role,
          revision: state.revision,
          basePublishedVersion: state.publishedVersion,
          values: {
            aiConnectionId: current.aiConnectionId,
            provider: current.provider,
            model: current.model,
            timeoutSeconds: current.timeoutSeconds,
            skill: current.skill,
            personality: current.personality
          },
          isValid: true,
          validationIssues: [],
          updatedAtUtc: '2026-07-12T00:00:00Z'
        })
      });
      return;
    }

    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.lastUpdate = { role, body, url: route.request().url() };
    if (options.refuseSecret) {
      await route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({ succeeded: false, code: 'ValidationFailed', failureReason: 'This update looks like it contains a secret.', currentRevision: state.revision })
      });
      return;
    }
    state.revision += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        succeeded: true,
        code: '',
        failureReason: '',
        currentRevision: state.revision,
        draft: {
          role,
          revision: state.revision,
          basePublishedVersion: state.publishedVersion,
          values: body,
          isValid: true,
          validationIssues: [],
          updatedAtUtc: '2026-07-12T00:01:00Z'
        }
      })
    });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+\/draft\/test(?:\?.*)?$/i, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ succeeded: true, status: 'Passed', failureReason: '', validationIssues: [], executedAtUtc: '2026-07-12T00:02:00Z', summary: 'Draft configuration passed. No provider request was sent.', boundary: 'Voice and model, never authority.' }) });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+\/draft\/publish(?:\?.*)?$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1].split('/')[0];
    state.revision += 1;
    state.publishedVersion += 1;
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ succeeded: true, code: '', failureReason: '', currentRevision: state.revision, publishedVersion: { version: state.publishedVersion, role, values: {}, reason: 'test', actorUserId: 7, publishedAtUtc: '2026-07-12T00:03:00Z' }, profile: profile(role) }) });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+\/history(?:\?.*)?$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1].split('/')[0];
    const boundary = 'Usage is reconstructed from durable configuration snapshots in recent project runs.';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          version: { version: 2, role, values: { aiConnectionId: 'deployment-default', provider: 'openai', model: 'gpt-4o', timeoutSeconds: 60, skill: 'Current skill', personality: 'Current voice' }, reason: 'Current profile', actorUserId: 8, publishedAtUtc: '2026-07-12T00:04:00Z' },
          runUsage: [{ runId: 'run-usage-2', projectId: 7, workItemId: 42, capturedAtUtc: '2026-07-12T00:05:00Z' }],
          usageBoundary: boundary
        },
        {
          version: { version: 1, role, values: { aiConnectionId: 'deployment-default', provider: 'ollama', model: 'llama3', timeoutSeconds: 30, skill: 'Known skill', personality: 'Known voice' }, reason: 'Known working profile', actorUserId: 7, publishedAtUtc: '2026-07-12T00:03:00Z' },
          runUsage: [],
          usageBoundary: boundary
        }
      ])
    });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+\/reset(?:\?.*)?$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1].split('/')[0];
    state.revision += 1;
    state.publishedVersion += 1;
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ succeeded: true, code: '', failureReason: '', currentRevision: state.revision, publishedVersion: { version: state.publishedVersion, role, values: {}, reason: 'reset', actorUserId: 7, publishedAtUtc: '2026-07-12T00:04:00Z' }, profile: profile(role) }) });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+\/history\/\d+\/restore(?:\?.*)?$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1].split('/')[0];
    state.revision += 1;
    state.publishedVersion += 1;
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ succeeded: true, code: '', failureReason: '', currentRevision: state.revision, publishedVersion: { version: state.publishedVersion, role, values: {}, reason: 'restore', actorUserId: 7, publishedAtUtc: '2026-07-12T00:05:00Z' }, profile: profile(role) }) });
  });

  await page.route(/\/api\/v1\/agent-profiles\/[a-z]+$/i, async (route) => {
    const role = route.request().url().split('/agent-profiles/')[1];
    state.lastUpdate = { role, body: route.request().postDataJSON() as Record<string, unknown>, url: route.request().url() };
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
  await page.route('**/irondev-api/api/v1/ai-connections', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{
        id: 'deployment-default', tenantId: 3, displayName: 'Deployment default', providerKind: 'openai',
        controlledEndpointId: 'deployment-default-openai', controlledEndpoint: 'https://api.openai.com',
        credentialConfigured: true, credentialStatus: 'Configured', availableModels: ['gpt-4o'], enabled: true,
        tenantAvailable: true, projectAvailable: true, createdByUserId: 0, updatedByUserId: 7,
        version: 'IronDev AI Connection Contract 2.5.0', boundary: 'Non-secret metadata.'
      }])
    });
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
