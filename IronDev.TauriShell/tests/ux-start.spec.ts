import { expect, test, type Page } from '@playwright/test';

// UX-START-0/1 — the session front door and the project cockpit. The project is
// the authority boundary: no project, no work item; no readiness, no run. These
// tests mock the backend and assert the UI's side of the contract: named
// preflight instead of a mute error chip, a chooser whose badges are backend
// readiness (never inference), create-project landing on readiness, and a
// cockpit whose single primary action follows backend facts.

test('an unreachable API gets a named preflight with a retry, not a dead chip', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
  });
  await page.route('**/irondev-api/health', (route) => route.abort());
  await page.goto('/');

  await expect(page.getByTestId('flow.preflight')).toBeVisible();
  await expect(page.getByTestId('flow.preflight.detail')).toContainText('No response from');
  await expect(page.getByTestId('flow.preflight.retry')).toBeEnabled();
  await expect(page.getByText('start-localtest.ps1')).toBeVisible();
});

test('signed in without a project lands on the chooser; badges are backend readiness truth', async ({ page }) => {
  await mockStart(page, { selectedProjectId: null });
  await page.goto('/');

  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.chooser.readiness.7')).toContainText('Ready');
  await expect(page.getByTestId('flow.chooser.readiness.8')).toContainText('Setup required, 2 items');

  // Selecting a project changes context — the cockpit renders with the same truth.
  await page.getByTestId('flow.chooser.project.7').click();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await expect(page.getByTestId('flow.cockpit.badge')).toContainText('Ready to run');
});

test('creating a project lands on the readiness screen, never straight into work', async ({ page }) => {
  await mockStart(page, { selectedProjectId: null });
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ id: 9, tenantId: 3, name: 'FreshRepo', localPath: 'C:\\repos\\FreshRepo' })
      });
      return;
    }
    await route.fallback();
  });
  await page.goto('/');

  await page.getByTestId('flow.projectEntry.connect').click();
  await page.getByTestId('flow.chooser.create.name').fill('FreshRepo');
  await page.getByTestId('flow.chooser.create.path').fill('C:\\repos\\FreshRepo');
  await page.getByTestId('flow.chooser.create.submit').click();

  await expect(page.getByTestId('flow.projectSetup')).toBeVisible();
});

test('cockpit: a gate-waiting item outranks new work, with the reason named', async ({ page }) => {
  await mockStart(page, {
    selectedProjectId: 7,
    tickets: [
      reviewTicket(41, 'search-by-author', 'PausedForApproval'),
      { id: 42, tenantId: 3, projectId: 7, title: 'validate-book', status: 'Applied', acceptanceCriteria: 'x' }
    ]
  });
  await page.goto('/');

  await expect(page.getByTestId('flow.cockpit.primary.review')).toContainText('Review waiting item');
  await expect(page.getByTestId('flow.cockpit.attention')).toContainText('search-by-author');
  await expect(page.getByTestId('flow.cockpit.attention')).toContainText('waiting at the human gate');
});

test('cockpit: blocked readiness switches the primary action to setup and names every blocker with its remedy', async ({ page }) => {
  await mockStart(page, {
    selectedProjectId: 7,
    readiness: {
      projectId: 7,
      isReady: false,
      blockedCount: 2,
      blockedStates: ['BlockedMissingTestCommand', 'BlockedProjectNotIndexed'],
      checks: [
        {
          code: 'TestCommand',
          name: 'Test command',
          label: 'Test command',
          state: 'Missing',
          summary: 'No test command was detected.',
          evidence: 'No stored default and detection found no candidate.',
          remedy: 'Supply it: POST /api/projects/{projectId}/profile/commands with CommandType=Test.',
          blocking: true,
          detectedValue: '',
          actionKind: 'ConfirmTestCommand'
        },
        {
          code: 'CodeIndex',
          name: 'Code index',
          label: 'Code index',
          state: 'Missing',
          summary: 'The project has never been indexed.',
          evidence: 'The project has never been indexed.',
          remedy: 'Index it: POST /api/projects/{projectId}/code-index.',
          blocking: true,
          detectedValue: '',
          actionKind: 'ResolveAdditionalSetup'
        }
      ],
      nextAction: {
        kind: 'ConfirmTestCommand',
        checkCode: 'TestCommand',
        allowed: true,
        reasonCode: 'BlockedMissingTestCommand',
        label: 'Confirm test command',
        nextSafeAction: 'Supply the test command.'
      },
      proposedProfile: null,
      boundary: 'Readiness is computed from stored truth and scan evidence.'
    }
  });
  await page.goto('/');

  await expect(page.getByTestId('flow.cockpit.badge')).toContainText('Setup incomplete · 2 blocker(s)');
  await expect(page.getByTestId('flow.cockpit.primary.setup')).toContainText('Complete project setup');
  await expect(page.getByTestId('flow.cockpit.setup.test-command')).toContainText('Supply it');
  await expect(page.getByTestId('flow.cockpit.setup.code-index')).toContainText('Index it');
  await expect(page.getByTestId('flow.cockpit.setup')).toContainText('governed runs unlock when backend readiness is satisfied');

  await page.getByTestId('flow.cockpit.primary.setup').click();
  await expect(page.getByTestId('flow.projectSetup')).toBeVisible();
});

// ── Mocks ────────────────────────────────────────────────────────────────────

function reviewTicket(id: number, title: string, status: string) {
  return { id, tenantId: 3, projectId: 7, title, status, acceptanceCriteria: 'criteria' };
}

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

async function mockStart(
  page: Page,
  options: {
    selectedProjectId: number | null;
    tickets?: Array<Record<string, unknown>>;
    readiness?: Record<string, unknown>;
  }
) {
  const selectedProjectId = options.selectedProjectId;
  await page.addInitScript((projectId) => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    if (projectId !== null) {
      window.localStorage.setItem('irondev.selectedProjectId', `${projectId}`);
    } else {
      window.localStorage.removeItem('irondev.selectedProjectId');
    }
  }, selectedProjectId);

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
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 })
    })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    })
  );
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' },
        { id: 8, tenantId: 3, name: 'ParcelTracker', localPath: 'C:\\repos\\ParcelTracker' }
      ])
    });
  });
  for (const projectId of [7, 8, 9]) {
    await page.route(`**/irondev-api/api/projects/${projectId}/select`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId }) })
    );
    await page.route(`**/irondev-api/api/projects/${projectId}/tickets`, async (route) => {
      if (route.request().method() !== 'GET') {
        await route.fallback();
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(options.tickets ?? [])
      });
    });
  }
  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(options.readiness ?? READY_READINESS)
    })
  );
  await page.route('**/irondev-api/api/projects/8/provisioning/readiness', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ...READY_READINESS,
        projectId: 8,
        isReady: false,
        blockedCount: 2,
        blockedStates: ['BlockedMissingBuildCommand', 'BlockedMissingTestCommand'],
        checks: [setupCheck('BuildCommand', 'Build command', 'dotnet build ParcelTracker.slnx')],
        nextAction: {
          kind: 'ConfirmBuildCommand',
          checkCode: 'BuildCommand',
          allowed: true,
          reasonCode: 'BlockedMissingBuildCommand',
          label: 'Confirm build command',
          nextSafeAction: 'Confirm the build command.'
        }
      })
    })
  );
  await page.route('**/irondev-api/api/projects/9/provisioning/readiness', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ...READY_READINESS,
        projectId: 9,
        isReady: false,
        blockedCount: 1,
        blockedStates: ['BlockedUnknownArchitecture'],
        checks: [setupCheck('ProjectProfile', 'Architecture profile', 'ASP.NET Core')],
        nextAction: {
          kind: 'ConfirmProjectProfile',
          checkCode: 'ProjectProfile',
          allowed: true,
          reasonCode: 'BlockedUnknownArchitecture',
          label: 'Confirm project structure',
          nextSafeAction: 'Confirm the detected project structure.'
        }
      })
    })
  );
}

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
    actionKind: code === 'BuildCommand' ? 'ConfirmBuildCommand' : 'ConfirmProjectProfile'
  };
}
