import { expect, test } from '@playwright/test';

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

async function openTickets(page: import('@playwright/test').Page) {
  await page.getByTestId('shell.nav.tickets').click();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
}

async function mockHealthyApi(page: import('@playwright/test').Page, options: { isTestEnvironment?: boolean } = {}) {
  const isTestEnvironment = options.isTestEnvironment ?? true;
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
        environment: isTestEnvironment ? 'LocalTest' : 'LocalDev',
        database: isTestEnvironment ? 'IronDeveloper_Test' : 'IronDeveloper_Dev',
        weaviatePrefix: isTestEnvironment ? 'irondev_test' : 'irondev_dev',
        isTestEnvironment,
        workspaceRoot: 'C:\\IronDevTestWorkspaces\\',
        logsRoot: 'C:\\IronDevTestLogs\\',
        dangerRealRepoWritesEnabled: false
      })
    });
  });
}

async function seedToken(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
  });
}

async function seedSelectedProject(page: import('@playwright/test').Page, projectId: number) {
  await page.addInitScript((id) => {
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', `${id}`);
  }, projectId);
}

function isTicketListRoute(url: string) {
  return new URL(url).pathname === '/irondev-api/api/projects/7/tickets';
}

test('tickets shell exposes cockpit regions and auth state', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('app.header')).toBeVisible();
  await expect(page.getByTestId('app.apiStatus')).toBeVisible();
  await expect(page.getByTestId('home.workspace')).toBeVisible();
  for (const route of ['home', 'chat', 'build', 'tickets', 'knowledge', 'runs', 'settings']) {
    await expect(page.getByTestId(`shell.nav.${route}`)).toBeVisible();
  }
  await expect(page.getByText('Chat to Build', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Run Reports', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Promotion Review', { exact: true })).toHaveCount(0);
  await expect(page.getByTestId('shell.nav.tickets')).toBeVisible();
  await openTickets(page);
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('tickets.header')).toBeVisible();
  await expect(page.getByTestId('ticket.list')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeVisible();
  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeVisible();
  await expect(page.getByTestId('environment.badge')).toContainText('LocalTest');
  await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeDisabled();
  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeDisabled();
  await expect(page.getByTestId('ticket.command.create')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Sign in or configure a valid token');
  await expect(page.getByTestId('app.authState')).toBeVisible();
  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.signIn')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toBeVisible();
  await expect(page.getByTestId('auth.password')).toBeVisible();
  await expect(page.getByTestId('auth.localtestCredentials')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('localtest@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('auth.submit')).toBeVisible();
  await expect(page.getByTestId('home.localtestSession')).toHaveCount(0);
  await expect(page.getByTestId('app.authState.configureToken')).toBeVisible();
  await expect(page.getByTestId('app.authState.retry')).toBeVisible();
  await expect(page.getByTestId('api.status.authRequired')).toBeVisible();
  await expect(page.getByTestId('api.status.connected')).toBeVisible();
  await expect(page.getByTestId('project.status.missing')).toBeVisible();
  expect(await page.getByTestId('project.status.selected').count()).toBe(0);
  expect(await page.getByTestId('project.status.fallback').count()).toBe(0);
  await expect(page.getByTestId('ticket.inspector.evidence')).toContainText('Project required');

  await page.getByTestId('app.authState.configureToken').click();
  await expect(page.getByTestId('auth.tokenInput')).toBeVisible();
  await expect(page.getByTestId('auth.saveToken')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('LocalTest login uses the normal auth form and continues to project selection', async ({ page }) => {
  await mockHealthyApi(page);
  let loginBody: unknown = null;

  await page.route('**/irondev-api/api/auth/login', async (route) => {
    loginBody = route.request().postDataJSON();
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'localtest-token' })
    });
  });
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'localtest@irondev.local', displayName: 'LocalTest User', selectedTenantId: 3 })
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
      body: JSON.stringify([
        {
          id: 8,
          tenantId: 3,
          name: 'Selectable Project',
          description: 'Project chosen from Home',
          localPath: 'C:\\IronDevTestWorkspaces\\SelectableProject'
        }
      ])
    });
  });

  await page.goto('/');

  await expect(page.getByTestId('home.authState')).toBeVisible();
  await expect(page.getByTestId('auth.localtestCredentials')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('localtest@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('home.localtestSession')).toHaveCount(0);

  await page.getByTestId('auth.submit').click();

  expect(loginBody).toEqual({
    email: 'localtest@irondev.local',
    password: 'change-me-local-only'
  });
  await expect(page.getByTestId('home.projectSelector')).toBeVisible();
  await expect(page.getByTestId('project.option')).toContainText('Selectable Project');
  await expectNoHorizontalOverflow(page);
});

test('failed LocalTest login gives seeded credential recovery guidance', async ({ page }) => {
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/login', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'Invalid credentials.' })
    });
  });

  await page.goto('/');

  await expect(page.getByTestId('auth.localtestCredentials')).toBeVisible();
  await page.getByTestId('auth.submit').click();

  await expect(page.getByText('LocalTest sign in failed')).toBeVisible();
  await expect(page.getByText('localtest@irondev.local / change-me-local-only')).toBeVisible();
  await expect(page.getByText('tools/localtest/reset-localtest-data.ps1')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('localtest@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expectNoHorizontalOverflow(page);
});

test('LocalTest credentials are not exposed outside LocalTest', async ({ page }) => {
  await mockHealthyApi(page, { isTestEnvironment: false });

  await page.goto('/');

  await expect(page.getByTestId('home.authState')).toBeVisible();
  await expect(page.getByTestId('auth.localtestCredentials')).toHaveCount(0);
  await expect(page.getByTestId('auth.email')).toHaveValue('');
  await expect(page.getByTestId('auth.password')).toHaveValue('');
  await expect(page.getByText('change-me-local-only')).toHaveCount(0);
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows offline state and does not overflow in a narrow desktop window', async ({ page }) => {
  await page.route('**/irondev-api/health', async (route) => {
    await route.abort('connectionrefused');
  });

  await page.setViewportSize({ width: 920, height: 760 });
  await page.goto('/');
  await openTickets(page);

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByText('IronDev.Api is offline', { exact: true })).toBeVisible();
  await expect(page.getByText('dotnet run --project IronDev.Api', { exact: true })).toBeVisible();
  await expect(page.getByTestId('api.status.disconnected')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('IronDev.Api is offline');
  expect(await page.getByTestId('app.authState.configureToken').count()).toBe(0);
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows tenant required state after token auth', async ({ page }) => {
  await seedToken(page);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: null })
    });
  });
  await page.route('**/irondev-api/api/tenants**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 3, name: 'IronDev Local', slug: 'irondev-local' },
        { id: 4, name: 'IronDev Review', slug: 'irondev-review' }
      ])
    });
  });

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByRole('heading', { name: 'Tenant required' })).toBeVisible();
  await expect(page.getByTestId('tenant.selector')).toBeVisible();
  await expect(page.getByTestId('tenant.option')).toHaveCount(2);
  await expect(page.getByTestId('api.status.connected')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Select a tenant');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows project required state when no projects are available', async ({ page }) => {
  await seedToken(page);
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
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByRole('heading', { name: 'Project required' })).toBeVisible();
  await expect(page.getByTestId('project.selector')).toBeVisible();
  await expect(page.getByTestId('project.selector.empty')).toContainText('No projects found');
  await expect(page.getByTestId('app.header').getByTestId('project.status.missing')).toBeVisible();
  expect(await page.getByTestId('project.status.selected').count()).toBe(0);
  expect(await page.getByTestId('project.status.fallback').count()).toBe(0);
  await expect(page.getByTestId('ticket.inspector.evidence')).toContainText('Project required');
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Select a project');
  await expectNoHorizontalOverflow(page);
});

test('home project selector persists selection and unblocks project workspaces', async ({ page }) => {
  await seedToken(page);
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
      body: JSON.stringify([
        {
          id: 8,
          tenantId: 3,
          name: 'Selectable Project',
          description: 'Project chosen from Home',
          localPath: 'C:\\IronDevTestWorkspaces\\SelectableProject'
        }
      ])
    });
  });
  await page.route('**/irondev-api/api/projects/8/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 8 }) });
  });
  await page.route('**/irondev-api/api/projects/8/chat/complete', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: 'Project state: Selectable Project is ready.',
        contextSummary: 'Context used: selected project.',
        linkedFilePaths: '',
        linkedSymbols: '',
        traceId: null
      })
    });
  });
  await page.route('**/irondev-api/api/projects/8/tickets', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 801,
          projectId: 8,
          title: 'Selected project ticket',
          summary: 'Loaded after selecting the project.',
          status: 'Open',
          priority: 'Medium',
          type: 'Task',
          description: 'Project-scoped ticket.',
          acceptanceCriteria: '- Loads from project 8',
          createdAt: '2026-05-28T00:00:00Z',
          updatedAt: '2026-05-28T00:00:00Z'
        }
      ])
    });
  });
  await page.route('**/irondev-api/api/projects/8/tickets/801', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: 801,
        projectId: 8,
        title: 'Selected project ticket',
        summary: 'Loaded after selecting the project.',
        status: 'Open',
        priority: 'Medium',
        type: 'Task',
        description: 'Project-scoped ticket.',
        acceptanceCriteria: '- Loads from project 8',
        createdAt: '2026-05-28T00:00:00Z',
        updatedAt: '2026-05-28T00:00:00Z'
      })
    });
  });
  await page.route('**/irondev-api/api/projects/8/tickets/801/build-readiness', async (route) => {
    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'No readiness yet.' }) });
  });
  await page.route('**/irondev-api/api/projects/8/tickets/801/implementation-plan', async (route) => {
    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'No plan yet.' }) });
  });
  await page.route('**/irondev-api/api/projects/8/tickets/801/evidence-summary', async (route) => {
    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'No evidence yet.' }) });
  });

  await page.goto('/');
  await expect(page.getByTestId('home.workspace')).toBeVisible();
  await expect(page.getByTestId('project.status.missing')).toBeVisible();
  await expect(page.getByTestId('home.projectSelector')).toBeVisible();
  await expect(page.getByTestId('project.option')).toContainText('Selectable Project');

  await page.getByTestId('project.option.select.8').click();
  await expect(page.getByTestId('project.status.selected')).toContainText('Selectable Project');

  await page.reload();
  await expect(page.getByTestId('project.status.selected')).toContainText('Selectable Project');

  await page.getByTestId('shell.nav.chat').click();
  await expect(page.getByTestId('chat.command.reviewProjectState')).toBeEnabled();
  await page.getByTestId('chat.command.reviewProjectState').click();
  await expect(page.getByTestId('chat.thread')).toContainText('Project state: Selectable Project is ready.');

  await page.getByTestId('shell.nav.build').click();
  await expect(page.getByTestId('build.summary.project')).toContainText('Selectable Project');

  await page.getByTestId('shell.nav.tickets').click();
  await expect(page.getByTestId('ticket.list')).toContainText('Selected project ticket');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell labels fallback project context without treating it as selected', async ({ page }) => {
  await seedToken(page);
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
      body: JSON.stringify([{ id: 1, tenantId: 3, name: 'Fallback Project', description: 'Configured project' }])
    });
  });
  await page.route('**/irondev-api/api/projects/1/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 1 }) });
  });
  await page.route('**/irondev-api/api/projects/1/tickets', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByTestId('project.status.fallback')).toContainText('Fallback project 1');
  expect(await page.getByTestId('project.status.selected').count()).toBe(0);
  await expect(page.getByTestId('ticket.inspector.evidence')).toContainText('Fallback project 1');
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Fallback project context is read-only');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell loads mocked project ticket data', async ({ page }) => {
  await seedToken(page);
  await seedSelectedProject(page, 7);
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
  await page.route('**/irondev-api/api/projects/7/tickets/101/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketEvidenceSummaryNoLinkedRun) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...ticketEvidenceSummaryNoLinkedRun, ticketId: 102 }) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ticketDetail101)
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ticketDetail102)
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101/build-readiness', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 0,
        message: 'Ready to build.',
        warnings: [],
        blockingIssues: [],
        isReady: true
      })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (!isTicketListRoute(route.request().url())) {
      await route.fallback();
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 101,
          projectId: 7,
          title: 'Make tickets cockpit real',
          status: 'Ready',
          priority: 'High',
          summary: 'Render ticket data through the Tauri API client.'
        },
        {
          id: 102,
          projectId: 7,
          title: 'Add project selection',
          status: 'Draft',
          priority: 'Medium',
          summary: 'Pick active project before loading tickets.'
        }
      ])
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ticketDetail101)
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ticketDetail102)
    });
  });

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByTestId('project.status.selected')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeEnabled();
  await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeVisible();
  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeVisible();
  await expect(page.getByTestId('ticket.command.startDisposableRun')).toBeEnabled();
  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeDisabled();
  await expect(page.getByTestId('ticket.row')).toHaveCount(2);
  await expect(page.getByTestId('ticket.detail.header')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.executionEvidence')).toBeVisible();
  await expect(page.getByTestId('ticket.evidence.empty')).toBeVisible();
  await expect(page.getByTestId('ticket.evidence.empty')).toContainText('No build evidence yet');
  await expect(page.getByTestId('ticket.detail.brief')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.plan')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.context')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.tests')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.build')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.acceptanceCriteria')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.readiness')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.evidence')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.linkedDocuments')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.decisions')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.affectedFiles')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.affectedSymbols')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.buildReadiness')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.warnings')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.traceLinks')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.latestRun')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.latestPromotionPackage')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.blockedActions')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.blockedActions')).toContainText('No execution run is linked to this ticket yet.');
  await expect(page.getByTestId('ticket.inspector.nextSafeAction')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector.nextSafeAction')).toContainText('Refresh build readiness');
  await expect(page.getByTestId('ticket.detail')).toContainText('Make tickets cockpit real');
  await expect(page.getByTestId('ticket.detail')).toContainText('Render ticket data through the Tauri API client.');

  await page.getByTestId('ticket.detail.build').getByTestId('ticket.command.refreshReadiness').click();
  await expect(page.getByTestId('ticket.detail.readiness')).toContainText('Ready to build.');
  await expect(page.getByTestId('ticket.inspector.buildReadiness')).toContainText('Ready to build.');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell changes selected ticket detail through the API facade', async ({ page }) => {
  await mockTicketProject(page);

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByTestId('ticket.detail.header')).toContainText('Make tickets cockpit real');
  await page.getByText('Add project selection', { exact: true }).click();
  await expect(page.getByTestId('ticket.detail.header')).toContainText('Add project selection');
  await expect(page.getByTestId('ticket.detail.brief')).toContainText('Pick active project before loading tickets.');
  await expect(page.getByTestId('ticket.inspector.affectedFiles')).toContainText('src/App.tsx');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell handles readiness loading and unavailable state', async ({ page }) => {
  await mockTicketProject(page);
  await page.route('**/irondev-api/api/projects/7/tickets/101/build-readiness', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 150));
    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'Not found' }) });
  });

  await page.goto('/');
  await openTickets(page);

  await page.getByTestId('ticket.detail.build').getByTestId('ticket.command.refreshReadiness').click();
  await expect(page.getByTestId('ticket.detail.build').getByTestId('ticket.command.refreshReadiness')).toContainText('Checking readiness');
  await expect(page.getByTestId('ticket.detail.readiness')).toContainText('Build readiness is not available for this ticket yet.');
  await expect(page.getByTestId('ticket.inspector.buildReadiness')).toContainText('Unavailable');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell opens edit mode, validates title, and cancels dirty changes', async ({ page }) => {
  await mockTicketProjectForEdit(page, async (request) => request.postDataJSON() as Record<string, unknown>);

  await page.goto('/');
  await openTickets(page);

  await page.getByTestId('workspace.commands').getByTestId('ticket.command.edit').click();
  await expect(page.getByTestId('ticket.edit.form')).toBeVisible();
  await expect(page.getByTestId('ticket.edit.title')).toHaveValue('Make tickets cockpit real');

  await page.getByTestId('ticket.edit.title').fill('Updated workflow title');
  await expect(page.getByTestId('ticket.edit.dirtyState')).toContainText('Unsaved changes');

  await page.getByTestId('ticket.edit.title').fill('');
  await expect(page.getByTestId('ticket.edit.validation')).toContainText('Title is required');
  await expect(page.getByTestId('ticket.command.save')).toBeDisabled();

  await page.getByTestId('ticket.command.cancel').click();
  await expect(page.getByTestId('ticket.detail.header')).toContainText('Make tickets cockpit real');
  await expect(page.getByTestId('ticket.edit.form')).toHaveCount(0);
  await expectNoHorizontalOverflow(page);
});

test('tickets shell saves edited ticket through the API and clears dirty state', async ({ page }) => {
  let postedBody: unknown = null;
  await mockTicketProjectForEdit(page, async (request) => {
    postedBody = request.postDataJSON();

    return {
      ...(postedBody as Record<string, unknown>),
      title: 'Saved Tauri workflow title',
      summary: 'Saved through the ticket workflow parity form.'
    };
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('workspace.commands').getByTestId('ticket.command.edit').click();
  await page.getByTestId('ticket.edit.title').fill('Saved Tauri workflow title');
  await page.getByTestId('ticket.edit.summary').fill('Saved through the ticket workflow parity form.');
  await page.getByTestId('ticket.command.save').click();

  await expect(page.getByTestId('ticket.edit.success')).toContainText('Ticket saved through IronDev.Api.');
  await expect(page.getByTestId('ticket.detail.header')).toContainText('Saved Tauri workflow title');
  await expect(page.getByRole('button', { name: 'Saved Tauri workflow title' })).toBeVisible();
  expect(postedBody).toMatchObject({
    id: 101,
    projectId: 7,
    title: 'Saved Tauri workflow title',
    summary: 'Saved through the ticket workflow parity form.'
  });
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows product error when ticket save API fails', async ({ page }) => {
  await mockTicketProjectForEdit(page, async () => {
    throw new Error('save failed');
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('workspace.commands').getByTestId('ticket.command.edit').click();
  await page.getByTestId('ticket.edit.title').fill('Save failure title');
  await page.getByTestId('ticket.command.save').click();

  await expect(page.getByTestId('ticket.edit.error')).toContainText('Ticket save failed with HTTP 500.');
  await expect(page.getByTestId('ticket.edit.form')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('tickets shell blocks selection changes while edit form is dirty', async ({ page }) => {
  await mockTicketProjectForEdit(page, async (request) => request.postDataJSON() as Record<string, unknown>);

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('workspace.commands').getByTestId('ticket.command.edit').click();
  await page.getByTestId('ticket.edit.title').fill('Dirty title that should block selection');
  await page.getByText('Add project selection', { exact: true }).click();

  await expect(page.getByTestId('ticket.edit.form')).toBeVisible();
  await expect(page.getByTestId('ticket.edit.title')).toHaveValue('Dirty title that should block selection');
  await expect(page.getByTestId('ticket.edit.validation')).toContainText('Save or cancel');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell refreshes implementation plan through the API', async ({ page }) => {
  await mockTicketProjectForEdit(page, async (request) => request.postDataJSON() as Record<string, unknown>);
  await page.route('**/irondev-api/api/tickets/101/implementation-plan', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: 71,
        tenantId: 3,
        projectId: 7,
        ticketId: 101,
        title: 'Tauri ticket workflow plan',
        goal: 'Prove safe edit and review workflow parity.',
        scope: 'Tauri ticket surface only.',
        proposedSteps: 'Edit draft\nSave through API\nRefresh readiness',
        risksNotes: 'No apply/build mutation in this slice.',
        status: 'Draft',
        updatedDate: '2026-05-26T02:15:00Z'
      })
    });
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('workspace.commands').getByTestId('ticket.command.generatePlan').click();

  await expect(page.getByTestId('ticket.detail.plan')).toContainText('Prove safe edit and review workflow parity.');
  await expect(page.getByTestId('ticket.detail.plan')).toContainText('Edit draft');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows unavailable plan state when plan endpoint has no data', async ({ page }) => {
  await mockTicketProjectForEdit(page, async (request) => request.postDataJSON() as Record<string, unknown>);
  await page.route('**/irondev-api/api/tickets/101/implementation-plan', async (route) => {
    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'Not found' }) });
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('workspace.commands').getByTestId('ticket.command.generatePlan').click();

  await expect(page.getByTestId('ticket.detail.plan')).toContainText('Plan not available yet.');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell opens create panel and validates required title', async ({ page }) => {
  await mockTicketProject(page);
  await page.goto('/');
  await openTickets(page);

  await page.getByTestId('ticket.command.create').click();

  await expect(page.getByTestId('ticket.create.panel')).toBeVisible();
  await expect(page.getByTestId('ticket.create.title')).toBeVisible();
  await expect(page.getByTestId('ticket.create.summary')).toBeVisible();
  await expect(page.getByTestId('ticket.create.type')).toBeVisible();
  await expect(page.getByTestId('ticket.create.priority')).toBeVisible();
  await expect(page.getByTestId('ticket.create.acceptanceCriteria')).toBeVisible();

  await page.getByTestId('ticket.create.summary').fill('Summary without a title should not submit.');
  await page.getByTestId('ticket.create.submit').click();

  await expect(page.getByTestId('ticket.create.error')).toContainText('Title is required');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell creates a ticket through the API and selects the result', async ({ page }) => {
  let postedBody: unknown = null;
  await mockTicketProjectForCreate(page, async (request) => {
    postedBody = request.postDataJSON();

    return {
      id: 201,
      projectId: 7,
      title: 'Create safe Tauri ticket action',
      ticketType: 'UI / Workflow',
      status: 'Draft',
      priority: 'High',
      summary: 'Prove ticket creation through IronDev.Api.',
      acceptanceCriteria: 'Create through API\nReload ticket list',
      contextSummary: 'Created from deterministic Playwright test.',
      createdDate: '2026-05-26T01:15:00Z'
    };
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('ticket.command.create').click();
  await page.getByTestId('ticket.create.title').fill('Create safe Tauri ticket action');
  await page.getByTestId('ticket.create.summary').fill('Prove ticket creation through IronDev.Api.');
  await page.getByTestId('ticket.create.type').fill('UI / Workflow');
  await page.getByTestId('ticket.create.priority').selectOption('High');
  await page.getByTestId('ticket.create.acceptanceCriteria').fill('Create through API\nReload ticket list');
  await page.getByTestId('ticket.create.submit').click();

  await expect(page.getByTestId('ticket.create.success')).toContainText('IronDev ticket #201 was created and selected.');
  await expect(page.getByTestId('ticket.row')).toHaveCount(3);
  await page.getByTestId('ticket.create.cancel').click();
  await expect(page.getByTestId('ticket.detail.header')).toContainText('Create safe Tauri ticket action');
  expect(postedBody).toMatchObject({
    title: 'Create safe Tauri ticket action',
    summary: 'Prove ticket creation through IronDev.Api.',
    type: 'UI / Workflow',
    priority: 'High',
    acceptanceCriteria: ['Create through API', 'Reload ticket list']
  });
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows product error when ticket create API fails', async ({ page }) => {
  await mockTicketProjectForCreate(page, async () => {
    throw new Error('api failure');
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('ticket.command.create').click();
  await page.getByTestId('ticket.create.title').fill('Create should fail cleanly');
  await page.getByTestId('ticket.create.summary').fill('The API mock returns a server error.');
  await page.getByTestId('ticket.create.submit').click();

  await expect(page.getByTestId('ticket.create.error')).toContainText('Ticket creation failed with HTTP 500.');
  await expect(page.getByTestId('ticket.row')).toHaveCount(2);
  await page.getByTestId('ticket.create.cancel').click();
  await expect(page.getByTestId('ticket.detail')).not.toContainText('Create should fail cleanly');
  await expectNoHorizontalOverflow(page);
});

test('run reports cockpit shows summaries, timeline, and evidence', async ({ page }) => {
  await mockTicketProject(page);
  await mockRunReportWorkspace(page, {
    runs: [runReportSummaryWithFailures, runReportSummarySuccess],
    runDetails: {
      'run-900': runReportDetailFailure,
      'run-901': runReportDetailSuccess
    },
    evidence: {
      'run-900': runReportEvidenceFailure,
      'run-901': runReportEvidenceSuccess
    }
  });

  await page.goto('/');
  await openTickets(page);

  await page.getByTestId('shell.nav.runs').click();
  await expect(page.getByTestId('run-reports.workspace')).toBeVisible();
  await expect(page.getByTestId('run-reports.list')).toBeVisible();
  await expect(page.getByTestId('run-reports.summary')).toBeVisible();
  await expect(page.getByTestId('run-reports.inspector')).toBeVisible();
  await expect(page.getByTestId('run-reports.filters')).toBeVisible();
  await expect(page.getByTestId('run-reports.filter.latest')).toBeVisible();
  await expect(page.getByTestId('run-reports.filter.failed')).toBeVisible();
  await expect(page.getByTestId('run-reports.row')).toHaveCount(2);
  await expect(page.getByTestId('run-reports.summary')).toContainText('run-901');
  await expect(page.getByTestId('run-reports.timeline')).toContainText('Build');
  await expect(page.getByTestId('run-reports.evidence')).toContainText('evidence/run-901');
  await expect(page.getByTestId('run-reports.governance')).toBeVisible();
  await expect(page.getByTestId('run-reports.invariants')).toBeVisible();
  await expect(page.getByTestId('run-reports.blockedActions')).toBeVisible();
  await expect(page.getByTestId('run-reports.nextAction')).toContainText('No explicit blocks surfaced');
  await expect(page.getByTestId('run-reports.command.refresh')).toBeVisible();
  await expect(page.getByTestId('run-reports.command.refresh')).toBeEnabled();
  await expectNoHorizontalOverflow(page);
});

test('product navigation does not expose promotion review as a main workspace', async ({ page }) => {
  await mockTicketProject(page);
  await mockRunReportWorkspace(page, {
    runs: [runReportSummaryForPromotion],
    runDetails: {
      'run-901': runReportDetailForPromotion
    },
    evidence: {
      'run-901': runReportEvidenceSuccess
    }
  });

  await page.goto('/');
  await openTickets(page);
  await expect(page.getByTestId('shell.nav.promotion-review')).toHaveCount(0);
  await page.getByTestId('shell.nav.runs').click();
  await expect(page.getByTestId('run-reports.workspace')).toBeVisible();
  await expect(page.getByTestId('run-reports.summary')).toContainText('run-901');
  await expectNoHorizontalOverflow(page);
});

test('ticket evidence links open run report and promotion context', async ({ page }) => {
  await mockTicketProject(page);
  await page.route('**/irondev-api/api/projects/7/tickets/101/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketEvidenceSummaryWithLinkedRun) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101/build-runs/run-901/review', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketRunReviewForPromotion) });
  });
  await mockRunReportWorkspace(page, {
    runs: [runReportSummaryForPromotion],
    runDetails: {
      'run-901': runReportDetailForPromotion
    },
    evidence: {
      'run-901': runReportEvidenceSuccess
    }
  });

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByTestId('ticket.evidence.latestRun')).toBeVisible();
  await expect(page.getByTestId('ticket.evidence.latestPromotionPackage')).toBeVisible();
  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeEnabled();
  await page.getByTestId('ticket.command.reviewLatestRun').click();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('ticket.detail.header')).toContainText('Make tickets cockpit real');
  await expect(page.getByTestId('ticket.runReview')).toBeVisible();
  await expect(page.getByTestId('ticket.runReview.summary')).toContainText('run-901');
  await expect(page.getByTestId('ticket.runReview.disposable')).toBeVisible();
  await expect(page.getByTestId('ticket.runReview.evidence')).toContainText('Primary build timeline');
  await expect(page.getByTestId('ticket.runReview.evidence')).toContainText('evidence/run-901/build.log');
  await expect(page.getByTestId('ticket.runReview.events')).toContainText('ApprovalRequired');
  await expectNoHorizontalOverflow(page);
});

test('ticket start disposable run links a real run review without enabling review early', async ({ page }) => {
  let runStarted = false;
  await mockTicketProject(page);
  await page.route('**/irondev-api/api/projects/7/tickets/101/evidence-summary', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(runStarted ? ticketEvidenceSummaryWithLinkedRun : ticketEvidenceSummaryNoLinkedRun)
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101/build-runs/disposable', async (route) => {
    runStarted = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: 'run-901',
        projectId: 7,
        ticketId: 101,
        status: 'Running',
        currentNode: 'LoadTicket',
        requiresHumanApproval: false,
        message: 'Disposable run started.'
      })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101/build-runs/run-901/review', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketRunReviewForPromotion) });
  });

  await page.goto('/');
  await openTickets(page);

  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeDisabled();
  await page.getByTestId('ticket.command.startDisposableRun').click();
  await expect(page.getByTestId('ticket.runReview')).toBeVisible();
  await expect(page.getByTestId('ticket.runReview.summary')).toContainText('run-901');
  await expect(page.getByTestId('ticket.command.reviewLatestRun')).toBeEnabled();
  await expect(page.getByTestId('ticket.runReview.status')).toContainText('AwaitingCodeApproval');
  await expect(page.getByTestId('ticket.runReview.disposable')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('chat workspace sends project-scoped messages and reviews project state', async ({ page }) => {
  let lastPrompt = '';
  let lastMode = '';
  await mockTicketProject(page);
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    const body = route.request().postDataJSON() as { prompt?: string; mode?: string };
    lastPrompt = body.prompt ?? '';
    lastMode = body.mode ?? '';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: lastPrompt.includes('recent tickets')
          ? 'Project state: tickets are ready for review. Recommended next action: inspect build readiness.'
          : 'Use the Tickets workspace to review build readiness.',
        contextSummary: 'Context used: tickets, recent runs, and decisions.',
        linkedFilePaths: 'IronDev.TauriShell/src/features/tickets/TicketsWorkspace.tsx',
        linkedSymbols: 'TicketsWorkspace',
        traceId: 909
      })
    });
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('shell.nav.chat').click();

  await expect(page.getByTestId('chat.workspace')).toBeVisible();
  await expect(page.getByTestId('chat-build.stageRail')).toHaveCount(0);
  await expect(page.getByTestId('chat.command.send')).toBeDisabled();
  await expect(page.getByTestId('chat.command.continueInBuild')).toBeDisabled();
  await expect(page.getByTestId('chat.composer.disabledReason')).toContainText('Enter a message before sending.');

  await page.getByTestId('chat.composer.input').fill('Where should I start?');
  await expect(page.getByTestId('chat.command.send')).toBeEnabled();
  await expect(page.getByTestId('chat.command.continueInBuild')).toBeEnabled();
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.thread')).toContainText('Where should I start?');
  await expect(page.getByTestId('chat.thread')).toContainText('Use the Tickets workspace to review build readiness.');
  await expect(page.getByTestId('chat.contextPanel')).toContainText('Context used: tickets, recent runs, and decisions.');
  await expect(page.getByTestId('chat.sources')).toContainText('TicketsWorkspace.tsx');
  await expect(page.getByTestId('chat.sources')).toContainText('TicketsWorkspace');

  await page.getByTestId('chat.command.reviewProjectState').click();
  await expect(page.getByTestId('chat.thread')).toContainText('Review Project State');
  await expect(page.getByTestId('chat.thread')).toContainText('Project state: tickets are ready for review.');
  expect(lastPrompt).toContain('recent tickets');
  expect(lastPrompt).toContain('recent decisions');
  expect(lastPrompt).toContain('recent runs');
  expect(lastMode).toBe('projectStateReview');
  await expectNoHorizontalOverflow(page);
});

test('chat workspace keeps typed text visible when send fails', async ({ page }) => {
  await mockTicketProject(page);
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'Chat unavailable' }) });
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('shell.nav.chat').click();

  await page.getByTestId('chat.composer.input').fill('Please summarize the current risks.');
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.error')).toContainText('Send failed. Chat unavailable');
  await expect(page.getByTestId('chat.composer.input')).toHaveValue('Please summarize the current risks.');
  await expectNoHorizontalOverflow(page);
});

test('build route runs the reusable discussion-to-code spine', async ({ page }) => {
  await mockTicketProject(page);
  await page.route('**/irondev-api/api/projects/7/discussions', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ documentId: 222, documentVersionId: 333 })
    });
  });
  await page.route('**/irondev-api/api/projects/7/documents/333/tickets', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ticketId: 444, sourceDocumentVersionId: 333 })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/444/review', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        reviewId: 'review-444',
        result: {
          reviewId: 'review-444',
          projectId: 7,
          ticketId: 444,
          scenarioId: 'console.generic',
          createdUtc: '2026-05-28T01:02:03Z',
          contributions: [
            {
              role: 'Planner',
              summary: 'Plan the smallest disposable code proposal.',
              concerns: [],
              recommendations: ['Generate only inside the disposable workspace.']
            },
            {
              role: 'Tester',
              summary: 'Verify output through persisted command evidence.',
              concerns: [],
              recommendations: ['Run build and runtime verification.']
            }
          ],
          decision: {
            proceed: true,
            recommendedNextStep: 'Start disposable code run.',
            guardrails: ['Do not apply generated code to the real repository.']
          }
        }
      })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/444/disposable-code-runs', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ runId: 'run-444', state: 'PausedForApproval', isDisposable: true })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/444/build-runs/run-444/review-package', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: 'run-444',
        projectId: 7,
        ticketId: 444,
        state: 'PausedForApproval',
        generatedFiles: [
          {
            relativePath: 'GeneratedApp/Program.cs',
            content: 'Console.WriteLine("Hello from a reusable spine");',
            sha256: 'abcdef1234567890'
          }
        ],
        commandEvidence: [
          {
            command: 'dotnet build',
            exitCode: '0',
            stdoutPath: 'evidence/build.stdout.log',
            stderrPath: 'evidence/build.stderr.log',
            durationMs: '1234'
          },
          {
            command: 'dotnet run',
            exitCode: '0',
            stdoutPath: 'evidence/run.stdout.log',
            stderrPath: 'evidence/run.stderr.log',
            durationMs: '234'
          }
        ],
        outputVerification: {
          expected: 'Hello from a reusable spine',
          actual: 'Hello from a reusable spine',
          verified: true,
          evidencePath: 'evidence/output.json'
        },
        outputVerifications: [
          {
            expected: 'Hello from a reusable spine',
            actual: 'Hello from a reusable spine',
            verified: true,
            evidencePath: 'evidence/output.json'
          }
        ],
        codeStandards: {
          status: 'Passed',
          summary: 'Read-only standards check passed.',
          evidencePath: 'evidence/code-standards.json'
        },
        fileSetHash: 'fileset-444',
        risks: ['Review generated code before approval.'],
        humanReviewChecklist: ['Confirm the generated files match the ticket intent.'],
        events: [
          {
            eventType: 'RunCreated',
            message: 'Run was created.',
            timestampUtc: '2026-05-28T01:02:04Z'
          },
          {
            eventType: 'RunPausedForApproval',
            message: 'Run paused for human review.',
            timestampUtc: '2026-05-28T01:02:08Z'
          }
        ]
      })
    });
  });

  await page.goto('/');
  await openTickets(page);
  await page.getByTestId('shell.nav.chat').click();
  await page.getByTestId('chat.composer.input').fill('Create a small console app that proves the reusable spine.');
  await page.getByTestId('chat.command.continueInBuild').click();
  await expect(page.getByTestId('build.workspace')).toBeVisible();
  await expect(page.getByTestId('chat-build.stageRail')).toBeVisible();
  await expect(page.getByTestId('chat-build.discussion.content')).toHaveValue('Create a small console app that proves the reusable spine.');
  await page.getByTestId('chat-build.command.saveDiscussion').click();
  await expect(page.getByTestId('chat-build.discussionDocument')).toContainText('222');
  await page.getByTestId('chat-build.command.createTicket').click();
  await expect(page.getByTestId('chat-build.generatedTicket')).toContainText('444');
  await page.getByTestId('chat-build.command.reviewTicket').click();
  await expect(page.getByTestId('chat-build.ticketReview')).toContainText('review-444');
  await page.getByTestId('chat-build.command.startDisposableRun').click();
  await expect(page.getByTestId('chat-build.disposableRun')).toContainText('PausedForApproval');
  await expect(page.getByTestId('chat-build.reviewPackage')).toContainText('fileset-444');
  await expect(page.getByTestId('chat-build.generatedFiles')).toContainText('GeneratedApp/Program.cs');
  await expect(page.getByTestId('chat-build.commandEvidence')).toContainText('dotnet build');
  await expect(page.getByTestId('chat-build.commandEvidence')).toContainText('dotnet run');
  await expect(page.getByTestId('chat-build.outputVerification')).toContainText('Verified');
  await expect(page.getByTestId('chat-build.humanReviewChecklist')).toContainText('Confirm the generated files');
  await expect(page.getByTestId('chat-build.runEventTimeline')).toContainText('RunPausedForApproval');
  await expectNoHorizontalOverflow(page);
});

async function mockTicketProject(page: import('@playwright/test').Page) {
  await seedToken(page);
  await seedSelectedProject(page, 7);
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
  await page.route('**/irondev-api/api/projects/7/tickets/101/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketEvidenceSummaryNoLinkedRun) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...ticketEvidenceSummaryNoLinkedRun, ticketId: 102 }) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101/build-runs/disposable', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: 'run-901',
        projectId: 7,
        ticketId: 101,
        status: 'Running',
        currentNode: 'LoadTicket',
        requiresHumanApproval: false,
        message: 'Disposable run started.'
      })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketDetail101) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketDetail102) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (!isTicketListRoute(route.request().url())) {
      await route.fallback();
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 101,
          projectId: 7,
          title: 'Make tickets cockpit real',
          status: 'Ready',
          priority: 'High',
          summary: 'Render ticket data through the Tauri API client.'
        },
        {
          id: 102,
          projectId: 7,
          title: 'Add project selection',
          status: 'Draft',
          priority: 'Medium',
          summary: 'Pick active project before loading tickets.'
        }
      ])
    });
  });
  await page.route('**/irondev-api/api/run-reports', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([])
    });
  });
}

async function mockRunReportWorkspace(
  page: import('@playwright/test').Page,
  payload: {
    runs: Array<typeof runReportSummarySuccess>;
    runDetails: Record<string, RunReportDetailPayload>;
    evidence: Record<string, RunReportEvidencePayload[]>;
  }
) {
  await page.route('**/irondev-api/api/run-reports', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(payload.runs)
    });
  });

  await page.route('**/irondev-api/api/run-reports/*/evidence', async (route) => {
    const url = new URL(route.request().url());
    const parts = url.pathname.split('/');
    const runId = decodeURIComponent(parts.at(-2) ?? '');
    const items = payload.evidence[runId] ?? [];

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(items)
    });
  });

  await page.route('**/irondev-api/api/run-reports/*', async (route) => {
    const url = new URL(route.request().url());
    const parts = url.pathname.split('/');
    const runId = decodeURIComponent(parts.at(-1) ?? '');

    if (payload.runDetails[runId]) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(payload.runDetails[runId])
      });
      return;
    }

    await route.fulfill({
      status: 404,
      contentType: 'application/json',
      body: JSON.stringify({ message: `Run ${runId} not found` })
    });
  });
}

const runReportSummarySuccess = {
  runId: 'run-901',
  traceId: 'trace-901',
  project: 'IronDeveloper',
  title: 'Promotion review for disposable feature',
  status: 'Succeeded',
  recommendation: 'Needs human review',
  startedUtc: '2026-05-26T01:15:00Z',
  completedUtc: '2026-05-26T01:18:00Z',
  realRepoMutationCount: 1,
  disposableFilesChanged: 3
};

const runReportSummaryWithFailures = {
  runId: 'run-900',
  traceId: 'trace-900',
  project: 'IronDeveloper',
  title: 'Earlier failed run',
  status: 'Failed',
  recommendation: 'Needs rerun',
  startedUtc: '2026-05-25T23:15:00Z',
  completedUtc: '2026-05-25T23:18:00Z',
  realRepoMutationCount: 0,
  disposableFilesChanged: 2
};

const runReportSummaryForPromotion = runReportSummarySuccess;

const runReportDetailSuccess = {
  ...runReportSummarySuccess,
  stages: [
    {
      stageName: 'Build',
      status: 'success',
      summary: 'Build and checks completed.'
    }
  ],
  summary: 'Run completed with expected output for evidence collection.',
  workspacePath: '/tmp/irondev-run-901',
  boundary: 'workspace'
};

const runReportDetailFailure = {
  ...runReportSummaryWithFailures,
  attempts: [
    {
      type: 'Build',
      attemptNumber: 1,
      status: 'failed',
      summary: 'Compilation failed due to temporary tooling issue.'
    }
  ],
  summary: 'Build failure prevented promotion packaging.',
  workspacePath: '/tmp/irondev-run-900',
  boundary: 'workspace'
};

const runReportDetailForPromotion = {
  ...runReportSummarySuccess,
  promotionReview: {
    packageId: 'pkg-run-901',
    proposedChangeId: 'chg-run-901',
    approvalState: 'NeedsHumanReview',
    recommendation: 'Proceed to human review.',
    runtimeProfileId: 'dotnet-csharp',
    targetLanguage: 'C#',
    targetStack: 'ASP.NET Core',
    promotableFileCount: 1,
    blockedFileCount: 1,
    promotableFiles: [{ relativePath: 'src/App.Feature.cs' }],
    blockedFiles: [{ relativePath: 'src/App.Blocked.cs' }],
    risks: [
      {
        severity: 'Medium',
        category: 'Governance',
        message: 'Manual review required before apply.',
        mitigation: 'Use policy approval.'
      }
    ],
    requiredChecks: ['policy-compliance'],
    explicitApprovalsNeeded: ['approval-ticket-901'],
    blockedActions: ['Apply operation is blocked until human approval is recorded.']
  },
  policy: {
    policyId: 'policy-run-901',
    configurableSettings: ['require-human-review'],
    hardInvariants: ['No production writes without approval.', 'No schema changes without migration plan.']
  },
  summary: 'Promotion package is ready for review.',
  stages: [
    {
      stageName: 'Generate Promotion Package',
      status: 'success',
      summary: 'Packaging completed with policy snapshot included.'
    }
  ],
  realRepoMutationCount: 1,
  disposableFilesChanged: 3,
  workspacePath: '/tmp/irondev-run-901',
  boundary: 'governed'
};

const runReportEvidenceSuccess = [
  {
    type: 'file',
    path: 'evidence/run-901/build.log',
    summary: 'Primary build timeline'
  },
  {
    type: 'policy',
    path: 'evidence/run-901/policy.json',
    summary: 'Policy snapshot'
  }
];

const runReportEvidenceFailure = [
  {
    type: 'file',
    path: 'evidence/run-900/build.log',
    summary: 'Failure trace'
  }
];

const ticketEvidenceSummaryNoLinkedRun = {
  ticketId: 101,
  status: 'loaded',
  message: 'No linked execution evidence is available yet.',
  latestRun: null,
  latestPromotionPackage: null,
  linkedTraceCount: 2,
  linkedDocumentCount: 1,
  linkedDecisionCount: 0,
  linkedRunCount: 0,
  hasBlockingWarnings: true,
  blockedActions: ['No execution run is linked to this ticket yet.'],
  nextSafeAction: 'Refresh build readiness'
};

const ticketEvidenceSummaryWithLinkedRun = {
  ticketId: 101,
  status: 'loaded',
  message: 'Execution evidence is available for this ticket.',
  latestRun: {
    runId: 'run-901',
    traceId: 'trace-901',
    title: 'Promotion review for disposable feature',
    status: 'needsHumanReview',
    recommendation: 'Needs human review',
    startedUtc: '2026-05-26T01:15:00Z',
    completedUtc: '2026-05-26T01:18:00Z'
  },
  latestPromotionPackage: {
    packageId: 'pkg-run-901',
    proposedChangeId: 'chg-run-901',
    approvalState: 'NeedsHumanReview',
    recommendation: 'Proceed to human review.',
    runtimeProfile: 'dotnet-csharp',
    targetLanguage: 'C#',
    filesToPromoteCount: 1,
    filesBlockedCount: 1,
    activeRepoMutationCount: 1,
    sourceRunId: 'run-901'
  },
  linkedTraceCount: 2,
  linkedDocumentCount: 1,
  linkedDecisionCount: 0,
  linkedRunCount: 1,
  hasBlockingWarnings: false,
  blockedActions: [],
  nextSafeAction: 'Review run'
};

const ticketRunReviewForPromotion = {
  runId: 'run-901',
  projectId: 7,
  ticketId: 101,
  ticketTitle: 'Make tickets cockpit real',
  status: 'AwaitingCodeApproval',
  startedUtc: '2026-05-26T01:15:00Z',
  completedUtc: '2026-05-26T01:18:00Z',
  isDisposableRun: true,
  traceId: 'trace-901',
  evidenceSummary: '2 evidence item(s) are attached to this run.',
  outputSummary: 'Code proposal is ready for human approval.',
  failureReason: null,
  reportPath: 'runs/run-901/report.json',
  tracePath: 'trace:trace-901',
  logPath: 'runs/run-901/report.json',
  evidence: runReportEvidenceSuccess,
  events: [
    {
      eventId: 'evt-1',
      timestampUtc: '2026-05-26T01:15:00Z',
      runId: 'run-901',
      eventType: 'RunStarted',
      message: 'Ticket build run started for ticket 101.',
      payload: { projectId: '7', ticketId: '101', disposableRun: 'true', status: 'Running' }
    },
    {
      eventId: 'evt-2',
      timestampUtc: '2026-05-26T01:18:00Z',
      runId: 'run-901',
      eventType: 'ApprovalRequired',
      message: 'Code proposal is ready for human approval.',
      payload: { projectId: '7', ticketId: '101', disposableRun: 'true', status: 'AwaitingCodeApproval' }
    }
  ]
};

type RunReportDetailPayload =
  | typeof runReportDetailSuccess
  | typeof runReportDetailFailure
  | typeof runReportDetailForPromotion;
type RunReportEvidencePayload = (typeof runReportEvidenceSuccess)[number];

async function mockTicketProjectForCreate(
  page: import('@playwright/test').Page,
  createTicket: (request: import('@playwright/test').Request) => Promise<Record<string, unknown>>
) {
  await seedToken(page);
  await seedSelectedProject(page, 7);
  await mockHealthyApi(page);

  const baseTickets = [
    {
      id: 101,
      projectId: 7,
      title: 'Make tickets cockpit real',
      status: 'Ready',
      priority: 'High',
      summary: 'Render ticket data through the Tauri API client.'
    },
    {
      id: 102,
      projectId: 7,
      title: 'Add project selection',
      status: 'Draft',
      priority: 'Medium',
      summary: 'Pick active project before loading tickets.'
    }
  ];
  let createdTicket: Record<string, unknown> | null = null;

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
  await page.route('**/irondev-api/api/projects/7/tickets/101', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketDetail101) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketDetail102) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/201', async (route) => {
    await route.fulfill({
      status: createdTicket ? 200 : 404,
      contentType: 'application/json',
      body: JSON.stringify(createdTicket ?? { message: 'Not found' })
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (!isTicketListRoute(route.request().url())) {
      await route.fallback();
      return;
    }

    if (route.request().method() === 'POST') {
      try {
        createdTicket = await createTicket(route.request());
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(createdTicket) });
      } catch {
        await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: 'Server error' }) });
      }

      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(createdTicket ? [createdTicket, ...baseTickets] : baseTickets)
    });
  });
  await page.route('**/irondev-api/api/run-reports', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([])
    });
  });
}

async function mockTicketProjectForEdit(
  page: import('@playwright/test').Page,
  saveTicket: (request: import('@playwright/test').Request) => Promise<Record<string, unknown>>
) {
  await seedToken(page);
  await seedSelectedProject(page, 7);
  await mockHealthyApi(page);

  let detail101: Record<string, unknown> = { ...ticketDetail101 };
  const detail102: Record<string, unknown> = { ...ticketDetail102 };

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
  await page.route('**/irondev-api/api/projects/7/tickets/101/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ticketEvidenceSummaryNoLinkedRun) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102/evidence-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...ticketEvidenceSummaryNoLinkedRun, ticketId: 102 }) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/101', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail101) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/102', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail102) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/legacy', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fulfill({ status: 405, contentType: 'application/json', body: JSON.stringify({ message: 'Method not allowed' }) });
      return;
    }

    try {
      detail101 = await saveTicket(route.request());
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail101) });
    } catch {
      await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: 'Server error' }) });
    }
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (!isTicketListRoute(route.request().url())) {
      await route.fallback();
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: detail101.id,
          projectId: detail101.projectId,
          title: detail101.title,
          status: detail101.status,
          priority: detail101.priority,
          summary: detail101.summary
        },
        {
          id: detail102.id,
          projectId: detail102.projectId,
          title: detail102.title,
          status: detail102.status,
          priority: detail102.priority,
          summary: detail102.summary
        }
      ])
    });
  });
  await page.route('**/irondev-api/api/run-reports', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([])
    });
  });
}

const ticketDetail101 = {
  id: 101,
  projectId: 7,
  title: 'Make tickets cockpit real',
  ticketType: 'UI / Workflow',
  status: 'Ready',
  priority: 'High',
  summary: 'Render ticket data through the Tauri API client.',
  problem: 'The shell needs selected-ticket workflow parity, not only queue loading.',
  content: 'Use API-backed detail and a safe readiness refresh action.',
  acceptanceCriteria: 'Brief section renders\nPlan section renders\nInspector shows affected files',
  technicalNotes: 'Keep endpoint strings inside the API facade.',
  linkedFilePaths: 'src/App.tsx\nsrc/components/TicketDetail.tsx',
  linkedSymbols: 'TicketsWorkspace\nTicketDetail',
  unitTests: 'Playwright mocked API journey',
  integrationTests: 'Typed facade request coverage through deterministic route mocks',
  manualTests: 'Inspect cockpit at narrow desktop width',
  regressionTests: 'No horizontal overflow',
  buildValidation: 'Readiness endpoint is safe GET.',
  contextSummary: 'Project ticket loaded from IronDev.Api.',
  isGenerated: true,
  generationNote: 'Created from Tauri ticket detail parity slice.',
  sourceChatSessionId: 44,
  sourceChatMessageId: 45,
  sourceDocumentVersionId: 12,
  createdDate: '2026-05-25T02:32:00Z'
};

const ticketDetail102 = {
  id: 102,
  projectId: 7,
  title: 'Add project selection',
  ticketType: 'UI / Context',
  status: 'Draft',
  priority: 'Medium',
  summary: 'Pick active project before loading tickets.',
  problem: 'Ticket loading without project context is ambiguous.',
  content: 'Require selected project context before ticket detail loading.',
  acceptanceCriteria: 'Project selector renders\nSelected project badge renders',
  linkedFilePaths: 'src/App.tsx',
  linkedSymbols: 'ProjectContextState',
  contextSummary: 'Project context is selected before ticket data loads.',
  createdDate: '2026-05-25T03:15:00Z'
};
