import { expect, test, type Page, type Route } from '@playwright/test';

test('recent backend sessions form the Chat rail and the latest conversation opens', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat');

  await expect(page.getByTestId('chat.sessions')).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Recent conversations' })).toContainText('Catalog sorting');
  await expect(page.getByRole('navigation', { name: 'Recent conversations' })).toContainText('Ticket cockpit');
  await expect(page.getByTestId('chat.sessions.item.9008')).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('chat.message.assistant')).toContainText('The catalog sorts by title.');
});

test('selecting a session uses a canonical URL and reloads backend-owned messages', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat');

  await page.getByTestId('chat.sessions.item.9007').click();

  await expect(page).toHaveURL('/projects/7/chat/sessions/9007');
  await expect(page.getByTestId('chat.sessions.item.9007')).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket cockpit calmer.');

  await page.reload();
  await expect(page).toHaveURL('/projects/7/chat/sessions/9007');
  await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket cockpit calmer.');
});

test('new conversation stays local until the first message creates a durable session', async ({ page }) => {
  const state = await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat/sessions/9007');

  await page.getByTestId('chat.sessions.new').click();
  await expect(page).toHaveURL('/projects/7/chat');
  await expect(page.getByRole('heading', { name: 'What would you like to work on?' })).toBeVisible();
  expect(state.createdSessionCount).toBe(0);

  await page.getByTestId('chat.composer.input').fill('Plan a stable catalog sort.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.message.assistant')).toContainText('Catalog sorting is handled by CatalogService.');
  await expect(page).toHaveURL('/projects/7/chat/sessions/9010');
  expect(state.createdSessionCount).toBe(1);
  await expect(page.getByTestId('chat.sessions.item.9010')).toHaveAttribute('aria-current', 'page');
});

test('an unknown direct-session URL returns an honest conversation outcome', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat/sessions/9999');

  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await expect(page.getByRole('heading', { name: 'Conversation not found' })).toBeVisible();
  await expect(page.getByTestId('chat.composer')).toHaveCount(0);

  await page.getByRole('button', { name: 'Open recent conversations' }).click();
  await expect(page).toHaveURL('/projects/7/chat');
  await expect(page.getByTestId('chat.workspace')).toBeVisible();
});

test('shared-channel URLs refuse with 501 instead of showing a direct session', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat/channels/general');

  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('501');
  await expect(page.getByRole('heading', { name: 'Project channels are not implemented' })).toBeVisible();
  await expect(page.getByTestId('chat.workspace')).toHaveCount(0);
});

test('session-list failure preserves the route and retries backend truth', async ({ page }) => {
  const state = await mockSessionWorkspace(page, { failSessionListOnce: true });
  await page.goto('/projects/7/chat/sessions/9008');

  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('503');
  await expect(page.getByRole('heading', { name: 'Conversations are unavailable' })).toBeVisible();
  await page.getByRole('button', { name: 'Retry' }).click();

  await expect(page).toHaveURL('/projects/7/chat/sessions/9008');
  await expect(page.getByTestId('chat.message.assistant')).toContainText('The catalog sorts by title.');
  expect(state.sessionListRequests).toBeGreaterThanOrEqual(2);
});

test.describe('narrow session navigation', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('opens as a drawer, selects a session, and leaves no horizontal overflow', async ({ page }) => {
    await mockSessionWorkspace(page);
    await page.goto('/projects/7/chat');

    const rail = page.getByTestId('chat.sessions');
    await expect(rail).toHaveClass('chat-session-rail');

    await page.getByTestId('chat.sessions.toggle').click();
    await expect(page.getByRole('button', { name: 'Close conversations', exact: true })).toBeVisible();
    await expect(rail).toHaveClass('chat-session-rail chat-session-rail--open');

    await page.getByTestId('chat.sessions.item.9007').click();
    await expect(page).toHaveURL('/projects/7/chat/sessions/9007');
    await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket cockpit calmer.');

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });
});

interface SessionMockOptions {
  failSessionListOnce?: boolean;
}

interface SessionMockState {
  createdSessionCount: number;
  sessionListRequests: number;
}

async function mockSessionWorkspace(page: Page, options: SessionMockOptions = {}): Promise<SessionMockState> {
  const state: SessionMockState = { createdSessionCount: 0, sessionListRequests: 0 };
  let nextSessionId = 9010;
  let nextMessageId = 9200;
  const sessions = [
    {
      id: 9008,
      tenantId: 3,
      projectId: 7,
      title: 'Catalog sorting',
      summary: 'Direct with IronDev',
      createdDate: '2026-07-10T08:00:00Z',
      updatedDate: '2026-07-10T08:10:00Z',
      dateGroup: 'Today'
    },
    {
      id: 9007,
      tenantId: 3,
      projectId: 7,
      title: 'Ticket cockpit',
      summary: 'Direct with IronDev',
      createdDate: '2026-07-09T07:00:00Z',
      updatedDate: '2026-07-09T07:30:00Z',
      dateGroup: 'This Week'
    }
  ];
  const messages = new Map<number, Array<Record<string, unknown>>>([
    [
      9008,
      [
        {
          id: 9108,
          tenantId: 3,
          projectId: 7,
          chatSessionId: 9008,
          role: 'assistant',
          message: 'The catalog sorts by title.',
          createdDate: '2026-07-10T08:10:00Z'
        }
      ]
    ],
    [
      9007,
      [
        {
          id: 9107,
          tenantId: 3,
          projectId: 7,
          chatSessionId: 9007,
          role: 'user',
          message: 'Make the ticket cockpit calmer.',
          createdDate: '2026-07-09T07:30:00Z'
        }
      ]
    ]
  ]);

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/environment', (route) =>
    json(route, { environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    json(route, { userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    json(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    json(route, [{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
  );
  await page.route('**/irondev-api/api/projects/7/select', (route) => json(route, { projectId: 7 }));

  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions$/, async (route) => {
    if (route.request().method() === 'GET') {
      state.sessionListRequests += 1;
      if (options.failSessionListOnce && state.sessionListRequests === 1) {
        return json(route, { error: 'Session store unavailable.' }, 500);
      }
      return json(route, sessions);
    }

    const request = route.request().postDataJSON() as { title?: string };
    const id = nextSessionId++;
    state.createdSessionCount += 1;
    sessions.unshift({
      id,
      tenantId: 3,
      projectId: 7,
      title: request.title ?? 'Untitled conversation',
      summary: 'Project conversation',
      createdDate: '2026-07-10T09:00:00Z',
      updatedDate: '2026-07-10T09:00:00Z',
      dateGroup: 'Today'
    });
    messages.set(id, []);
    return json(route, id);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions\/(\d+)$/, (route) => {
    const id = Number(route.request().url().match(/\/sessions\/(\d+)$/)?.[1]);
    const found = sessions.find((item) => item.id === id);
    return found ? json(route, found) : route.fulfill({ status: 204 });
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions\/(\d+)\/messages$/, (route) => {
    const id = Number(route.request().url().match(/\/sessions\/(\d+)\/messages$/)?.[1]);
    if (route.request().method() === 'GET') {
      return json(route, messages.get(id) ?? []);
    }

    const request = route.request().postDataJSON() as { role: string; message: string; tags?: string };
    nextMessageId += 1;
    const history = messages.get(id) ?? [];
    history.push({
      id: nextMessageId,
      tenantId: 3,
      projectId: 7,
      chatSessionId: id,
      role: request.role,
      message: request.message,
      tags: request.tags,
      createdDate: '2026-07-10T09:00:00Z'
    });
    messages.set(id, history);
    return json(route, nextMessageId);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions\/\d+\/messages\/\d+\/audit$/, (route) =>
    json(route, { error: 'No durable audit row.' }, 404)
  );

  await page.route('**/irondev-api/api/projects/7/chat/complete', (route) =>
    json(route, {
      response: 'Catalog sorting is handled by CatalogService.',
      contextSummary: 'Inspected catalog sorting.',
      linkedFilePaths: 'src/Catalog/CatalogService.cs',
      mode: 'Exploration',
      gate: {
        mode: 'Exploration',
        canSaveDiscussion: true,
        canCreateTicket: false,
        canViewSources: true,
        canCopyMarkdown: true,
        reason: 'Project exploration response.',
        confidence: 0.9,
        governanceActions: ['saveDiscussion', 'viewSources', 'copyMarkdown']
      }
    })
  );

  return state;
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
