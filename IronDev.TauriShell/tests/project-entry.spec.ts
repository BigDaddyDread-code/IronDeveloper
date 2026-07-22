import { expect, test, type Page } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';
import { createDeferred } from './helpers/deferred';
import { workItemProjection } from './helpers/mockWorkItem';
import { workbenchProjectEntryContext } from './helpers/mockWorkbench';

const READY_READINESS = {
  projectId: 7,
  isReady: true,
  blockedCount: 0,
  blockedStates: [] as string[],
  checks: [] as unknown[],
  nextAction: { kind: 'OpenBoard', checkCode: null, allowed: true, reasonCode: null, label: 'Open Board', nextSafeAction: 'Open the project Board.' },
  proposedProfile: null,
  boundary: 'Readiness is computed from stored truth and scan evidence.'
};

const SETUP_READINESS = {
  ...READY_READINESS,
  projectId: 8,
  isReady: false,
  blockedCount: 2,
  blockedStates: ['BlockedMissingBuildCommand', 'BlockedMissingTestCommand'],
  checks: [
    setupCheck('BuildCommand', 'Build command', 'dotnet build ParcelTracker.slnx'),
    setupCheck('TestCommand', 'Test command', 'dotnet test ParcelTracker.slnx')
  ],
  nextAction: {
    kind: 'ConfirmBuildCommand',
    checkCode: 'BuildCommand',
    allowed: true,
    reasonCode: 'BlockedMissingBuildCommand',
    label: 'Confirm build command',
    nextSafeAction: 'Confirm or edit the detected build command.'
  }
};

function setupCheck(code: string, label: string, detectedValue: string) {
  return {
    code,
    name: label,
    label,
    state: 'NeedsConfirmation',
    summary: `A likely ${label.toLowerCase()} was detected.`,
    evidence: `Detected candidate: ${detectedValue}`,
    remedy: `Confirm or edit the ${label.toLowerCase()}.`,
    blocking: true,
    detectedValue,
    actionKind: code === 'BuildCommand' ? 'ConfirmBuildCommand' : 'ConfirmTestCommand'
  };
}

const DEFAULT_PROJECTS = [
  { id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller', lifecyclePhase: 'Shaping', executionReadiness: 'Ready' },
  { id: 8, tenantId: 3, name: 'ParcelTracker', localPath: 'C:\\repos\\ParcelTracker', lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured' },
  { id: 10, tenantId: 3, name: 'StatusDown', localPath: 'C:\\repos\\StatusDown', lifecyclePhase: 'Delivery', executionReadiness: 'ValidationRequired' }
];

test('explicit login lands on the project screen and ignores any prior selected project', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  const state = await mockProjectEntryApi(page);
  await page.goto('/');

  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Choose a project' })).toBeVisible();
  await expect(page.getByTestId('flow.shell')).toHaveCount(0);
  expect(state.legacySelectionRequests).toBe(0);
  expect(state.workbenchOpenRequests).toBe(0);
});

test('versioned preview identity remains visible after login', async ({ page }) => {
  await mockProjectEntryApi(page);
  await page.goto('/');

  await expect(page.getByTestId('auth.workbenchIdentity')).toContainText('V2 0.1.0-preview.5 / workbench-pr02a');
  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('flow.projectEntry.health')).toContainText('V2 0.1.0-preview.5 / workbench-pr02a');
  await expect(page.getByTestId('flow.projectEntry.workbenchIdentity')).toContainText('V2 0.1.0-preview.5 / workbench-pr02a');
});

test('configured fallback does not auto-open a project', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true, fallbackProjectId: 7 });
  await page.goto('/');

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toHaveCount(0);
});

test('projects render as whole clickable tiles with the start tile last', async ({ page }) => {
  const projects = createDeferred();
  const state = await mockProjectEntryApi(page, { signedIn: true, projectListGate: projects.promise });
  await page.goto('/');

  await expect.poll(() => state.projectListRequests).toBeGreaterThanOrEqual(1);
  projects.resolve();
  const grid = page.getByTestId('flow.chooser.list');
  await expect(grid).toBeVisible();
  await expect(page.getByRole('button', { name: /^Open BookSeller in Workbench/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Open ParcelTracker in Workbench/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Start a new project' })).toBeVisible();

  const tiles = grid.locator('.fl-project-tile');
  await expect(tiles).toHaveCount(4);
  await expect(tiles.nth(3)).toHaveAttribute('data-testid', 'flow.projectEntry.start');

  const projectTileHeight = (await tiles.first().boundingBox())?.height;
  const connectTileHeight = (await tiles.nth(3).boundingBox())?.height;
  expect(projectTileHeight).toBeGreaterThan(120);
  expect(Math.abs((projectTileHeight ?? 0) - (connectTileHeight ?? 0))).toBeLessThanOrEqual(2);
});

test('existing project opens Workbench without a readiness gate', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.7').click();

  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');
});

test('repository-free project opens Workbench instead of Project Setup', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.8').click();

  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('flow.projectSetup')).toHaveCount(0);
});

test('project selection failure stays on the project screen', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true, selectionFailures: [8] });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.8').click();

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.projectEntry.error')).toContainText('ParcelTracker could not be opened');
  await expect(page.getByTestId('flow.projectSetup')).toHaveCount(0);
});

test('project open reuses its operation id after an ambiguous transport failure', async ({ page }) => {
  const state = await mockProjectEntryApi(page, { signedIn: true, selectionTransportFailures: [8] });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page.getByTestId('flow.projectEntry.error')).toContainText('ParcelTracker could not be opened');

  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');

  expect(state.workbenchOpenOperationIds).toHaveLength(2);
  expect(state.workbenchOpenOperationIds[1]).toBe(state.workbenchOpenOperationIds[0]);
});

test('start tile opens repository-independent project entry', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.projectEntry.start').click();

  await expect(page.getByTestId('flow.startProject')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Start a new project' })).toBeFocused();
  await expect(page.getByText('Repository setup happens later.')).toBeVisible();
  await expect(page.getByTestId('flow.startProject.name')).toBeVisible();
  await expect(page.getByText(/LocalTest sandbox/i)).toHaveCount(0);
  await expect(page.getByText(/Starter/i)).toHaveCount(0);
  await expect(page.getByPlaceholder(/path/i)).toHaveCount(0);

  await page.getByTestId('flow.startProject.back').click();
  await expect(page.getByTestId('flow.projectEntry.start')).toBeFocused();
});

test('project start preserves its idempotency key on retry and opens Workbench', async ({ page }) => {
  const state = await mockProjectEntryApi(page, { signedIn: true, createFailure: true });
  await page.goto('/');

  await page.getByTestId('flow.projectEntry.start').click();
  await page.getByTestId('flow.startProject.name').fill('Fresh idea / no tech selected');
  await page.getByTestId('flow.startProject.submit').click();

  await expect(page.getByTestId('flow.startProject.error')).toContainText('Project could not be started');
  await expect(page.getByTestId('flow.startProject.name')).toHaveValue('Fresh idea / no tech selected');

  await mockCreateSuccess(page, state);
  await page.getByTestId('flow.startProject.submit').click();
  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('flow.projectSetup')).toHaveCount(0);
  expect(state.startOperationIds).toHaveLength(2);
  expect(state.startOperationIds[1]).toBe(state.startOperationIds[0]);
});

test('project start uses a new operation id when the failed payload changes', async ({ page }) => {
  const state = await mockProjectEntryApi(page, { signedIn: true, createFailure: true });
  await page.goto('/');

  await page.getByTestId('flow.projectEntry.start').click();
  await page.getByTestId('flow.startProject.name').fill('First idea');
  await page.getByTestId('flow.startProject.submit').click();
  await expect(page.getByTestId('flow.startProject.error')).toBeVisible();

  await mockCreateSuccess(page, state);
  await page.getByTestId('flow.startProject.name').fill('Changed idea');
  await page.getByTestId('flow.startProject.submit').click();
  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');

  expect(state.startOperationIds).toHaveLength(2);
  expect(state.startOperationIds[1]).not.toBe(state.startOperationIds[0]);
});

test('Workshop mutations carry the current Workbench session and lease epoch', async ({ page }) => {
  const state = await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.7').click();
  await page.getByTestId('chat.composer.input').fill('Shape this project');
  await page.getByTestId('chat.command.send').click();
  await expect.poll(() => state.chatMutationBodies.length).toBe(4);

  for (const body of state.chatMutationBodies) {
    expect(body.workbenchSessionId).toBe(7007);
    expect(body.leaseEpoch).toBe(1);
    expect(body.clientOperationId).toMatch(/^[0-9a-f-]{36}$/i);
  }
});

test('keyboard activates a project tile', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.7').focus();
  await page.keyboard.press('Enter');

  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');
});

test('changing projects clears the active work item', async ({ page }) => {
  await mockProjectEntryApi(page, {
    signedIn: true,
    selectedProjectId: 7,
    readinessByProject: {
      8: { ...READY_READINESS, projectId: 8 }
    },
    ticketsByProject: {
      7: [{ id: 41, tenantId: 3, projectId: 7, title: 'BookSeller only ticket', status: 'Draft', acceptanceCriteria: 'Ship it' }],
      8: []
    }
  });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.7').click();
  await page.getByTestId('flow.nav.board').click();
  await page.getByRole('button', { name: /BookSeller only ticket/ }).click();
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.locator('body')).toContainText('BookSeller only ticket');

  await page.getByTestId('flow.projectSwitcher').click();
  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page.getByTestId('flow.nav.workshop')).toHaveAttribute('aria-current', 'page');

  await expect(page.getByTestId('flow.nav.workitem')).toBeDisabled();
  await expect(page.locator('body')).not.toContainText('BookSeller only ticket');
});

async function mockProjectEntryApi(page: Page, options: MockOptions = {}) {
  const state: MockState = {
    projectListRequests: 0,
    legacySelectionRequests: 0,
    workbenchOpenRequests: 0,
    workbenchOpenOperationIds: [],
    startOperationIds: [],
    chatMutationBodies: [],
    startedProject: null
  };
  if (options.signedIn) {
    await page.addInitScript(({ selectedProjectId, fallbackProjectId }) => {
      window.localStorage.setItem('irondev.token', 'tenant-token');
      window.localStorage.setItem('irondev.tenantId', '3');
      if (selectedProjectId === null || selectedProjectId === undefined) {
        window.localStorage.removeItem('irondev.selectedProjectId');
      } else {
        window.localStorage.setItem('irondev.selectedProjectId', `${selectedProjectId}`);
      }
      if (fallbackProjectId !== undefined) {
        window.localStorage.setItem('irondev.projectId', `${fallbackProjectId}`);
      }
    }, { selectedProjectId: options.selectedProjectId, fallbackProjectId: options.fallbackProjectId });
  }

  await mockCommonApi(page, options, state);
  await mockCreateRoute(page, options, state);
  return state;
}

async function mockCommonApi(page: Page, options: MockOptions, state: MockState) {
  const projects = options.projects ?? DEFAULT_PROJECTS;
  const ticketsByProject = options.ticketsByProject ?? {};

  await page.route('**/irondev-api/health', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) })
  );
  await page.route('**/irondev-api/api/localtest/preflight', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        state: 'LocalTestReady',
        environment: 'LocalTest',
        database: 'IronDeveloper_Test',
        apiBuildIdentity: '1.0.0+test-commit',
        apiBuildCommit: 'test-commit',
        launcherRepositoryCommit: 'test-commit',
        sessionId: 'playwright-session',
        apiBaseUrl: 'http://localhost:5000',
        apiPid: 1234,
        seedContractVersion: 1,
        seededLoginCheckResult: 'Passed',
        nextSafeAction: 'Sign in with the documented LocalTest credentials.',
        resetCommand: null,
        detail: 'LocalTest front door is ready.',
        workbenchVersion: '0.1.0-preview.5',
        workbenchMode: 'V2',
        previewId: 'workbench-pr02a',
        sessionMode: 'SmokeSimulation',
        sandboxApplyRequested: false,
        sandboxApplyEnabled: false,
        sandboxApplyRoot: null,
        capabilities: ['SmokeSimulation']
      })
    })
  );
  await page.route('**/irondev-api/api/environment', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        environment: 'LocalTest',
        database: 'IronDeveloper_Test_workbench_pr01',
        isTestEnvironment: true,
        workbench: {
          version: '0.1.0-preview.5',
          mode: 'V2',
          v2Enabled: true,
          v1FallbackEnabled: true,
          previewId: 'workbench-pr02a',
          apiBuildIdentity: '1.0.0+test-commit',
          apiCommit: 'test-commit',
          resetSupported: true
        }
      })
    })
  );
  await page.route('**/irondev-api/api/auth/login', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'base-token', userId: 7, displayName: 'Local Test User' })
    })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) => {
    const authorization = route.request().headers().authorization ?? '';
    const selectedTenantId = authorization.includes('tenant-token') ? 3 : null;
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        userId: 7,
        email: 'bob@irondev.local',
        displayName: 'Local Test User',
        selectedTenantId
      })
    });
  });
  await page.route('**/irondev-api/api/tenants', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'Local Test Tenant', slug: 'local-test' }])
    })
  );
  await page.route('**/irondev-api/api/tenants/select', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'tenant-token', userId: 7, displayName: 'Local Test User' })
    })
  );
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }

    state.projectListRequests += 1;
    await options.projectListGate;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state.startedProject ? [...projects, state.startedProject] : projects)
    });
  });

  for (const candidate of [...projects, { id: 9, tenantId: 3, name: 'Fresh idea / no tech selected', localPath: null, lifecyclePhase: 'Shaping' }]) {
    const projectId = candidate.id;
    await page.route(`**/irondev-api/api/projects/${projectId}/select`, (route) => {
      state.legacySelectionRequests += 1;
      return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'Legacy selection must not be called.' }) });
    });
    await page.route(`**/irondev-api/api/workbench/projects/${projectId}/open`, (route) => {
      state.workbenchOpenRequests += 1;
      const request = route.request().postDataJSON() as { clientOperationId?: string };
      state.workbenchOpenOperationIds.push(request.clientOperationId ?? '');
      if (options.selectionTransportFailures?.includes(projectId) &&
          state.workbenchOpenOperationIds.filter((operationId) => operationId === request.clientOperationId).length === 1) {
        return route.abort('connectionfailed');
      }
      if (options.selectionFailures?.includes(projectId)) {
        return route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'Project unavailable' }) });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(workbenchProjectEntryContext(route, projectId, {
          name: candidate.name,
          projectLifecyclePhase: candidate.lifecyclePhase === 'Delivery'
            ? 'Delivery'
            : candidate.lifecyclePhase === 'Archived'
              ? 'Archived'
              : 'Shaping',
          executionReadiness: 'NotConfigured',
          workbenchSessionId: 7000 + projectId,
          wasResumed: false
        }))
      });
    });
    await page.route(`**/irondev-api/api/projects/${projectId}/tickets`, async (route) => {
      if (route.request().method() !== 'GET') {
        await route.fallback();
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(ticketsByProject[projectId] ?? [])
      });
    });
    for (const ticket of ticketsByProject[projectId] ?? []) {
      await page.route(`**/irondev-api/api/projects/${projectId}/tickets/${ticket.id}`, (route) =>
        route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticket) })
      );
      await page.route(`**/irondev-api/api/projects/${projectId}/work-items/${ticket.id}`, (route) =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(workItemProjection({
            projectId,
            workItemId: Number(ticket.id),
            title: String(ticket.title),
            ticket
          }))
        })
      );
    }
    await page.route(`**/irondev-api/api/projects/${projectId}/provisioning/readiness`, (route) => {
      if (options.readinessFailures?.includes(projectId)) {
        return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'readiness failed' }) });
      }

      const readiness = options.readinessByProject?.[projectId] ?? (projectId === 8 || projectId === 9 ? { ...SETUP_READINESS, projectId } : { ...READY_READINESS, projectId });
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(readiness) });
    });
    const boardReadiness = options.readinessByProject?.[projectId] ?? (projectId === 8 || projectId === 9 ? { ...SETUP_READINESS, projectId } : { ...READY_READINESS, projectId });
    await mockProjectBoard(page, {
      projectId,
      projectName: candidate.name,
      tickets: ticketsByProject[projectId] ?? [],
      readiness: boardReadiness
    });
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/sessions`, (route) => {
      if (route.request().method() === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      }
      state.chatMutationBodies.push(route.request().postDataJSON() as Record<string, unknown>);
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(9007) });
    });
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/sessions/9007/messages`, (route) => {
      if (route.request().method() === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      }
      state.chatMutationBodies.push(route.request().postDataJSON() as Record<string, unknown>);
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state.chatMutationBodies.length) });
    });
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/complete`, (route) => {
      state.chatMutationBodies.push(route.request().postDataJSON() as Record<string, unknown>);
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ response: 'Let us shape the idea.', mode: 'Exploration', reasoningTrace: [] })
      });
    });
  }

  await page.route('**/irondev-api/api/projects/**/tickets/**/build-readiness', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isReady: true, message: 'Ready', warnings: [], blockingIssues: [] })
    })
  );
  await page.route('**/irondev-api/api/projects/**/tickets/**/evidence-summary', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ticketId: 41,
        status: 'empty',
        message: 'No evidence',
        latestRun: null,
        latestPromotionPackage: null,
        linkedTraceCount: 0,
        linkedDocumentCount: 0,
        linkedDecisionCount: 0,
        linkedRunCount: 0,
        hasBlockingWarnings: false,
        blockedActions: []
      })
    })
  );
}

async function mockCreateRoute(page: Page, options: MockOptions, state: MockState) {
  await page.route('**/irondev-api/api/projects/start', async (route) => {
    const request = route.request().postDataJSON() as { clientOperationId: string };
    state.startOperationIds.push(request.clientOperationId);
    if (options.createFailure) {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'project_start_failed', message: 'Project could not be started' })
      });
      return;
    }

    state.startedProject = { id: 9, tenantId: 3, name: 'Fresh idea / no tech selected', localPath: null, lifecyclePhase: 'Shaping' };
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify(startProjectResponse())
    });
  });
}

async function mockCreateSuccess(page: Page, state: MockState) {
  state.startedProject = { id: 9, tenantId: 3, name: 'Fresh idea / no tech selected', localPath: null, lifecyclePhase: 'Shaping' };
  await page.unroute('**/irondev-api/api/projects/start');
  await page.route('**/irondev-api/api/projects/start', (route) => {
    const request = route.request().postDataJSON() as { clientOperationId: string };
    state.startOperationIds.push(request.clientOperationId);
    return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(startProjectResponse()) });
  });
}

function startProjectResponse() {
  return {
    projectId: 9,
    tenantId: 3,
    name: 'Fresh idea / no tech selected',
    projectLifecyclePhase: 'Shaping',
    executionReadiness: 'NotConfigured',
    repositoryBinding: null,
    workbenchSessionId: 9009,
    leaseEpoch: 1,
    clientOperationId: '22222222-2222-2222-2222-222222222222',
    createdAtUtc: '2026-07-18T00:00:00Z',
    isReplay: false
  };
}

interface MockOptions {
  signedIn?: boolean;
  selectedProjectId?: number | null;
  fallbackProjectId?: number;
  createFailure?: boolean;
  projects?: Array<{
    id: number;
    tenantId: number;
    name: string;
    localPath: string | null;
    lifecyclePhase?: string;
    executionReadiness?: string;
  }>;
  readinessByProject?: Record<number, Record<string, unknown>>;
  readinessFailures?: number[];
  selectionFailures?: number[];
  selectionTransportFailures?: number[];
  ticketsByProject?: Record<number, Array<Record<string, unknown>>>;
  projectListGate?: Promise<void>;
}

interface MockState {
  projectListRequests: number;
  legacySelectionRequests: number;
  workbenchOpenRequests: number;
  workbenchOpenOperationIds: string[];
  startOperationIds: string[];
  chatMutationBodies: Array<Record<string, unknown>>;
  startedProject: { id: number; tenantId: number; name: string; localPath: null; lifecyclePhase: string } | null;
}
