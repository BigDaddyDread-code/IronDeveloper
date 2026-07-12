import { expect, test, type Page } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';
import { createDeferred } from './helpers/deferred';
import { workItemProjection } from './helpers/mockWorkItem';

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
  { id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' },
  { id: 8, tenantId: 3, name: 'ParcelTracker', localPath: 'C:\\repos\\ParcelTracker' },
  { id: 10, tenantId: 3, name: 'StatusDown', localPath: 'C:\\repos\\StatusDown' }
];

test('explicit login lands on the project screen and ignores any prior selected project', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await mockProjectEntryApi(page);
  await page.goto('/');

  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Choose a project' })).toBeVisible();
  await expect(page.getByTestId('flow.shell')).toHaveCount(0);
});

test('configured fallback does not auto-open a project', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true, fallbackProjectId: 7 });
  await page.goto('/');

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toHaveCount(0);
});

test('projects render as whole clickable tiles with the connect tile last', async ({ page }) => {
  const projects = createDeferred();
  const state = await mockProjectEntryApi(page, { signedIn: true, projectListGate: projects.promise });
  await page.goto('/');

  await expect.poll(() => state.projectListRequests).toBeGreaterThanOrEqual(1);
  projects.resolve();
  const grid = page.getByTestId('flow.chooser.list');
  await expect(grid).toBeVisible();
  await expect(page.getByRole('button', { name: /^Open BookSeller\. Ready/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Open ParcelTracker\. Setup required/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Connect another project. Add a local repository' })).toBeVisible();

  const tiles = grid.locator('.fl-project-tile');
  await expect(tiles).toHaveCount(4);
  await expect(tiles.nth(3)).toHaveAttribute('data-testid', 'flow.projectEntry.connect');

  const projectTileHeight = (await tiles.first().boundingBox())?.height;
  const connectTileHeight = (await tiles.nth(3).boundingBox())?.height;
  expect(projectTileHeight).toBeGreaterThan(120);
  expect(Math.abs((projectTileHeight ?? 0) - (connectTileHeight ?? 0))).toBeLessThanOrEqual(2);
});

test('ready project opens Board', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.7').click();

  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await expect(page.getByTestId('flow.cockpit.badge')).toContainText('Ready to run');
});

test('unready project opens Project Setup', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.8').click();

  await expect(page.getByTestId('flow.projectSetup')).toBeVisible();
  await expect(page.getByTestId('flow.projectSetup.next')).toContainText('Confirm the build command');
});

test('readiness failure still opens Project Setup', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true, readinessFailures: [10] });
  await page.goto('/');

  await expect(page.getByTestId('flow.chooser.readiness.10')).toContainText('Status unavailable');
  await page.getByTestId('flow.chooser.project.10').click();

  await expect(page.getByTestId('flow.projectSetup.unavailable')).toBeVisible();
});

test('project selection failure stays on the project screen', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true, selectionFailures: [8] });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.8').click();

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.projectEntry.error')).toContainText('ParcelTracker could not be opened');
  await expect(page.getByTestId('flow.projectSetup')).toHaveCount(0);
});

test('connect tile opens a dedicated screen and suggests the project name from path', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.projectEntry.connect').click();

  await expect(page.getByTestId('flow.connectProject')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Connect a project' })).toBeFocused();

  await page.getByTestId('flow.chooser.create.path').fill('C:\\repos\\FreshRepo');
  await expect(page.getByTestId('flow.chooser.create.name')).toHaveValue('FreshRepo');

  await page.getByTestId('flow.connectProject.back').click();
  await expect(page.getByTestId('flow.projectEntry.connect')).toBeFocused();
});

test('project creation opens Project Setup and failed creation preserves values', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true, createFailure: true });
  await page.goto('/');

  await page.getByTestId('flow.projectEntry.connect').click();
  await page.getByTestId('flow.chooser.create.name').fill('FreshRepo');
  await page.getByTestId('flow.chooser.create.path').fill('C:\\repos\\FreshRepo');
  await page.getByTestId('flow.chooser.create.submit').click();

  await expect(page.getByTestId('flow.chooser.create.error')).toContainText('Repository path was rejected');
  await expect(page.getByTestId('flow.chooser.create.name')).toHaveValue('FreshRepo');
  await expect(page.getByTestId('flow.chooser.create.path')).toHaveValue('C:\\repos\\FreshRepo');

  await mockCreateSuccess(page);
  await page.getByTestId('flow.chooser.create.submit').click();
  await expect(page.getByTestId('flow.projectSetup')).toBeVisible();
});

test('keyboard activates a project tile', async ({ page }) => {
  await mockProjectEntryApi(page, { signedIn: true });
  await page.goto('/');

  await page.getByTestId('flow.chooser.project.7').focus();
  await page.keyboard.press('Enter');

  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
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

  await page.getByRole('button', { name: /BookSeller only ticket/ }).click();
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.locator('body')).toContainText('BookSeller only ticket');

  await page.getByTestId('flow.projectSwitcher').click();
  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();

  await expect(page.getByTestId('flow.nav.workitem')).toBeDisabled();
  await expect(page.locator('body')).not.toContainText('BookSeller only ticket');
});

async function mockProjectEntryApi(page: Page, options: MockOptions = {}) {
  const state: MockState = { projectListRequests: 0 };
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
  await mockCreateRoute(page, options);
  return state;
}

async function mockCommonApi(page: Page, options: MockOptions, state: MockState) {
  const projects = options.projects ?? DEFAULT_PROJECTS;
  const ticketsByProject = options.ticketsByProject ?? {};

  await page.route('**/irondev-api/health', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) })
  );
  await page.route('**/irondev-api/api/environment', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true })
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
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(projects) });
  });

  for (const candidate of [...projects, { id: 9, tenantId: 3, name: 'FreshRepo', localPath: 'C:\\repos\\FreshRepo' }]) {
    const projectId = candidate.id;
    await page.route(`**/irondev-api/api/projects/${projectId}/select`, (route) => {
      if (options.selectionFailures?.includes(projectId)) {
        return route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'Project unavailable' }) });
      }
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId }) });
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

async function mockCreateRoute(page: Page, options: MockOptions) {
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }

    if (options.createFailure) {
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Repository path was rejected' })
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ id: 9, tenantId: 3, name: 'FreshRepo', localPath: 'C:\\repos\\FreshRepo' })
    });
  });
}

async function mockCreateSuccess(page: Page) {
  await page.unroute('**/irondev-api/api/projects');
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ id: 9, tenantId: 3, name: 'FreshRepo', localPath: 'C:\\repos\\FreshRepo' })
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DEFAULT_PROJECTS)
    });
  });
}

interface MockOptions {
  signedIn?: boolean;
  selectedProjectId?: number | null;
  fallbackProjectId?: number;
  createFailure?: boolean;
  projects?: Array<{ id: number; tenantId: number; name: string; localPath: string }>;
  readinessByProject?: Record<number, Record<string, unknown>>;
  readinessFailures?: number[];
  selectionFailures?: number[];
  ticketsByProject?: Record<number, Array<Record<string, unknown>>>;
  projectListGate?: Promise<void>;
}

interface MockState {
  projectListRequests: number;
}
