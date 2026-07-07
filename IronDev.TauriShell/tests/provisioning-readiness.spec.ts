import { expect, test, type Page } from '@playwright/test';

// PROJECT-1..3: the provisioning screen renders backend-computed readiness and asks
// only the pointed questions detection cannot prove. These tests mock the backend and
// pin the boundary: a detected command still blocks until confirmed, confirming POSTs
// to the governed profile endpoint and re-evaluates, and the UI never marks anything
// ready itself — it renders the verdict the backend computed.

interface ProvisioningMockState {
  confirmedBuild: boolean;
  savedCommands: Array<{ commandType: string; commandText: string }>;
  savedProfiles: Array<Record<string, unknown>>;
}

async function mockProvisioning(page: Page): Promise<ProvisioningMockState> {
  const state: ProvisioningMockState = { confirmedBuild: false, savedCommands: [], savedProfiles: [] };

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
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'SecondRepo', description: 'Provisioning target' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });

  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', async (route) => {
    const buildCheck = state.confirmedBuild
      ? {
          name: 'Build command',
          state: 'Confirmed',
          evidence: 'Stored default: dotnet build Second.sln',
          remedy: '',
          blocking: false,
          detectedValue: ''
        }
      : {
          name: 'Build command',
          state: 'NeedsConfirmation',
          evidence: 'Detected candidate: dotnet build Second.sln. A detected command is a proposal — it runs nothing until confirmed.',
          remedy: 'Confirm or edit it: POST /api/projects/{projectId}/profile/commands with CommandType=Build.',
          blocking: true,
          detectedValue: 'dotnet build Second.sln'
        };
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: 7,
        isReady: state.confirmedBuild,
        blockedStates: state.confirmedBuild ? [] : ['BlockedMissingBuildCommand'],
        checks: [
          {
            name: 'Repo path',
            state: 'Confirmed',
            evidence: 'C:\\repos\\Second exists and passed the root-safety check.',
            remedy: '',
            blocking: false,
            detectedValue: ''
          },
          buildCheck,
          {
            name: 'Test command',
            state: 'Confirmed',
            evidence: 'Stored default: dotnet test Second.sln',
            remedy: '',
            blocking: false,
            detectedValue: ''
          },
          {
            name: 'Architecture profile',
            state: 'Confirmed',
            evidence: 'Stored profile: WebApi · C#',
            remedy: '',
            blocking: false,
            detectedValue: ''
          }
        ],
        proposedProfile: null,
        boundary: 'Readiness is computed from stored truth and scan evidence.'
      })
    });
  });

  await page.route('**/irondev-api/api/projects/7/profile/commands', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    const body = route.request().postDataJSON() as { commandType: string; commandText: string };
    state.savedCommands.push(body);
    if (body.commandType === 'Build') {
      state.confirmedBuild = true;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({}) });
  });

  return state;
}

async function openProvisioning(page: Page) {
  await page.goto('/');
  await page.getByTestId('flow.nav.library').click();
  await page.getByTestId('flow.library.nav.provisioning').click();
}

test('a detected-but-unconfirmed build command blocks readiness with the candidate prefilled', async ({ page }) => {
  await mockProvisioning(page);
  await openProvisioning(page);

  await expect(page.getByTestId('flow.provisioning.verdict')).toContainText('Blocked: BlockedMissingBuildCommand');
  await expect(page.getByTestId('flow.provisioning.verdict')).toContainText('Unknowns remain');
  await expect(page.getByTestId('flow.provisioning.checks')).toContainText('runs nothing until confirmed');
  await expect(page.getByTestId('flow.provisioning.input.build')).toHaveValue('dotnet build Second.sln');
});

test('confirming the pointed question posts to the governed endpoint and readiness recomputes', async ({ page }) => {
  const state = await mockProvisioning(page);
  await openProvisioning(page);

  await page.getByTestId('flow.provisioning.confirm.build').click();

  await expect(page.getByTestId('flow.provisioning.verdict')).toContainText('Readiness satisfied');
  await expect(page.getByTestId('flow.provisioning.verdict')).toContainText('approves nothing');
  expect(state.savedCommands[0].commandType).toBe('Build');
  expect(state.savedCommands[0].commandText).toBe('dotnet build Second.sln');
});

test('an unreachable readiness backend renders honestly instead of inventing state', async ({ page }) => {
  await mockProvisioning(page);
  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', async (route) => {
    await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'boom' }) });
  });
  await openProvisioning(page);

  await expect(page.getByTestId('flow.provisioning.unavailable')).toContainText('nothing is shown rather than inventing state');
});
