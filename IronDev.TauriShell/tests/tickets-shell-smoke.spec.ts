import { expect, test } from '@playwright/test';

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

async function mockHealthyApi(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'healthy' })
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
    window.localStorage.setItem('irondev.selectedProjectId', `${id}`);
  }, projectId);
}

test('tickets shell exposes cockpit regions and auth state', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('app.shell')).toBeVisible();
  await expect(page.getByTestId('app.header')).toBeVisible();
  await expect(page.getByTestId('app.apiStatus')).toBeVisible();
  await expect(page.getByTestId('shell.nav.tickets')).toBeVisible();
  await expect(page.getByTestId('tickets.workspace')).toBeVisible();
  await expect(page.getByTestId('tickets.header')).toBeVisible();
  await expect(page.getByTestId('ticket.list')).toBeVisible();
  await expect(page.getByTestId('ticket.detail')).toBeVisible();
  await expect(page.getByTestId('ticket.inspector')).toBeVisible();
  await expect(page.getByTestId('ticket.command.refresh')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Sign in or configure a valid token');
  await expect(page.getByTestId('app.authState')).toBeVisible();
  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.signIn')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toBeVisible();
  await expect(page.getByTestId('auth.password')).toBeVisible();
  await expect(page.getByTestId('auth.submit')).toBeVisible();
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

test('tickets shell shows offline state and does not overflow in a narrow desktop window', async ({ page }) => {
  await page.route('**/irondev-api/health', async (route) => {
    await route.abort('connectionrefused');
  });

  await page.setViewportSize({ width: 920, height: 760 });
  await page.goto('/');

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
  await page.route('**/irondev-api/api/auth/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: null })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    });
  });

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Tenant required' })).toBeVisible();
  await expect(page.getByTestId('tenant.selector')).toBeVisible();
  await expect(page.getByTestId('tenant.option')).toHaveCount(1);
  await expect(page.getByTestId('api.status.connected')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Select a tenant');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell shows project required state when no projects are available', async ({ page }) => {
  await seedToken(page);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Project required' })).toBeVisible();
  await expect(page.getByTestId('project.selector')).toBeVisible();
  await expect(page.getByTestId('app.header').getByTestId('project.status.missing')).toBeVisible();
  expect(await page.getByTestId('project.status.selected').count()).toBe(0);
  expect(await page.getByTestId('project.status.fallback').count()).toBe(0);
  await expect(page.getByTestId('ticket.inspector.evidence')).toContainText('Project required');
  await expect(page.getByTestId('ticket.command.create')).toBeDisabled();
  await expect(page.getByTestId('ticket.create.blockedReason')).toContainText('Select a project');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell labels fallback project context without treating it as selected', async ({ page }) => {
  await seedToken(page);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
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

  await page.goto('/');

  await expect(page.getByTestId('project.status.selected')).toBeVisible();
  await expect(page.getByTestId('ticket.command.create')).toBeEnabled();
  await expect(page.getByTestId('ticket.row')).toHaveCount(2);
  await expect(page.getByTestId('ticket.detail.header')).toBeVisible();
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
  await expect(page.getByTestId('ticket.detail')).toContainText('Make tickets cockpit real');
  await expect(page.getByTestId('ticket.detail')).toContainText('Render ticket data through the Tauri API client.');

  await page.getByTestId('ticket.command.refreshReadiness').click();
  await expect(page.getByTestId('ticket.detail.readiness')).toContainText('Ready to build.');
  await expect(page.getByTestId('ticket.inspector.buildReadiness')).toContainText('Ready to build.');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell changes selected ticket detail through the API facade', async ({ page }) => {
  await mockTicketProject(page);

  await page.goto('/');

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

  await page.getByTestId('ticket.command.refreshReadiness').click();
  await expect(page.getByTestId('ticket.command.refreshReadiness')).toContainText('Checking readiness');
  await expect(page.getByTestId('ticket.detail.readiness')).toContainText('Build readiness is not available for this ticket yet.');
  await expect(page.getByTestId('ticket.inspector.buildReadiness')).toContainText('Unavailable');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell opens edit mode, validates title, and cancels dirty changes', async ({ page }) => {
  await mockTicketProjectForEdit(page, async (request) => request.postDataJSON() as Record<string, unknown>);

  await page.goto('/');

  await page.getByTestId('ticket.command.edit').click();
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
  await page.getByTestId('ticket.command.edit').click();
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
  await page.getByTestId('ticket.command.edit').click();
  await page.getByTestId('ticket.edit.title').fill('Save failure title');
  await page.getByTestId('ticket.command.save').click();

  await expect(page.getByTestId('ticket.edit.error')).toContainText('Ticket save failed with HTTP 500.');
  await expect(page.getByTestId('ticket.edit.form')).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('tickets shell blocks selection changes while edit form is dirty', async ({ page }) => {
  await mockTicketProjectForEdit(page, async (request) => request.postDataJSON() as Record<string, unknown>);

  await page.goto('/');
  await page.getByTestId('ticket.command.edit').click();
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
  await page.getByTestId('ticket.command.generatePlan').click();

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
  await page.getByTestId('ticket.command.generatePlan').click();

  await expect(page.getByTestId('ticket.detail.plan')).toContainText('Plan not available yet.');
  await expectNoHorizontalOverflow(page);
});

test('tickets shell opens create panel and validates required title', async ({ page }) => {
  await mockTicketProject(page);
  await page.goto('/');

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

async function mockTicketProject(page: import('@playwright/test').Page) {
  await seedToken(page);
  await seedSelectedProject(page, 7);
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
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
}

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

  await page.route('**/irondev-api/api/auth/me', async (route) => {
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

  await page.route('**/irondev-api/api/auth/me', async (route) => {
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
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
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
