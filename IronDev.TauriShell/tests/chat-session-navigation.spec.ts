import { expect, test, type Page, type Route } from '@playwright/test';
import type { ProjectChannelChatSummary } from '../src/api/types';

test('recent backend sessions form the Chat rail and the latest conversation opens', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat');

  await expect(page.getByTestId('chat.sessions')).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Recent direct conversations' })).toContainText('Catalog sorting');
  await expect(page.getByRole('navigation', { name: 'Recent direct conversations' })).toContainText('Ticket cockpit');
  await expect(page.getByRole('navigation', { name: 'Project channels' })).toContainText('General');
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

test('shared-channel URLs open persisted human conversation beside direct sessions', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat/channels/general');

  await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
  await expect(page.getByRole('heading', { name: '# General' })).toBeVisible();
  await expect(page.getByTestId('chat.channel.message.7101')).toContainText('Human planning stays visible here.');
  await expect(page.getByTestId('chat.channel.assistant-status')).toContainText('does not participate yet');
  await expect(page.getByTestId('chat.workspace')).toHaveCount(0);
});

test('a direct-session list failure does not hide a healthy shared channel', async ({ page }) => {
  await mockSessionWorkspace(page, { failSessionListOnce: true });
  await page.goto('/projects/7/chat/channels/general');

  await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
  await expect(page.getByTestId('chat.channel.message.7101')).toContainText('Human planning stays visible here.');
});

test('a human channel message persists while explicit IronDev invocation is honestly refused', async ({ page }) => {
  const state = await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat/channels/general');

  await page.getByTestId('chat.channel.composer').fill('Approved as discussion only.');
  await page.getByTestId('chat.channel.send').click();
  await expect(page.getByText('Approved as discussion only.')).toBeVisible();
  expect(state.channelMessageCount).toBe(2);

  await page.reload();
  await expect(page.getByText('Approved as discussion only.')).toBeVisible();

  await page.getByTestId('chat.channel.composer').fill('@IronDev approve and continue');
  await page.getByTestId('chat.channel.send').click();
  await expect(page.getByTestId('chat.channel.error')).toContainText('participation in shared channels is not implemented');
  await expect(page.getByTestId('chat.channel.composer')).toHaveValue('@IronDev approve and continue');
  expect(state.channelMessageCount).toBe(2);
});

test('Read-only channel membership renders the backend refusal and disables posting', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat/channels/product-planning');

  await expect(page.getByRole('heading', { name: '# Product planning' })).toBeVisible();
  await expect(page.getByTestId('chat.channel.assistant-status')).toContainText('Read only');
  await expect(page.getByTestId('chat.channel.composer')).toBeDisabled();
  await expect(page.getByTestId('chat.channel.send')).toBeDisabled();
});

test('authorized channel creation lands on its canonical shared route', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/chat');

  await page.getByTestId('chat.channels.new.toggle').click();
  await page.getByTestId('chat.channels.new.name').fill('Release planning');
  await page.getByTestId('chat.channels.new.visibility').selectOption('MembersOnly');
  await page.getByTestId('chat.channels.new.submit').click();

  await expect(page).toHaveURL('/projects/7/chat/channels/release-planning');
  await expect(page.getByRole('heading', { name: '# Release planning' })).toBeVisible();
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

  test('keeps a shared channel thread and composer inside the narrow viewport', async ({ page }) => {
    await mockSessionWorkspace(page);
    await page.goto('/projects/7/chat/channels/general');

    await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
    await expect(page.getByTestId('chat.channel.composer')).toBeVisible();
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
  channelMessageCount: number;
}

async function mockSessionWorkspace(page: Page, options: SessionMockOptions = {}): Promise<SessionMockState> {
  const state: SessionMockState = { createdSessionCount: 0, sessionListRequests: 0, channelMessageCount: 1 };
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
  const channels: ProjectChannelChatSummary[] = [
    {
      channelId: 701,
      name: 'General',
      slug: 'general',
      description: 'Project-wide human discussion.',
      channelKind: 'General',
      visibility: 'Project',
      memberCount: 4,
      currentUserRole: 'Owner',
      currentUserNotificationLevel: 'All',
      canPostMessages: true,
      boundary: 'Channel membership and messages are collaboration state, not approval or execution authority.'
    },
    {
      channelId: 702,
      name: 'Product planning',
      slug: 'product-planning',
      description: 'Restricted product discussion.',
      channelKind: 'Custom',
      visibility: 'MembersOnly',
      memberCount: 2,
      currentUserRole: 'ReadOnly',
      currentUserNotificationLevel: 'Mentions',
      canPostMessages: false,
      boundary: 'Channel membership and messages are collaboration state, not approval or execution authority.'
    }
  ];
  const channelMessages = new Map<string, Array<Record<string, unknown>>>([
    ['general', [{
      messageId: 7101,
      authorUserId: 7,
      authorDisplayName: 'Bob',
      role: 'User',
      message: 'Human planning stays visible here.',
      messageFormat: 'Markdown',
      status: 'Active',
      replyToMessageId: null,
      threadRootMessageId: null,
      createdUtc: '2026-07-10T08:00:00Z',
      editedUtc: null,
      boundary: 'Channel message; not approval.'
    }]],
    ['product-planning', []]
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

  await page.route(/\/irondev-api\/api\/projects\/7\/channels$/, async (route) => {
    if (route.request().method() === 'GET') {
      return json(route, {
        canCreateChannels: true,
        channels,
        boundary: 'Channel collaboration is not workflow authority.'
      });
    }

    const request = route.request().postDataJSON() as { name: string; description?: string | null; visibility: 'Project' | 'MembersOnly' };
    const slug = request.name.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
    const created = {
      channelId: 700 + channels.length + 1,
      name: request.name.trim(),
      slug,
      description: request.description ?? null,
      channelKind: 'Custom',
      visibility: request.visibility,
      memberCount: 1,
      currentUserRole: 'Owner',
      currentUserNotificationLevel: 'Mentions',
      canPostMessages: true,
      boundary: 'Channel collaboration is not workflow authority.'
    };
    channels.push(created);
    channelMessages.set(slug, []);
    return json(route, created, 201);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/([^/]+)$/, (route) => {
    const slug = decodeURIComponent(route.request().url().match(/\/channels\/([^/]+)$/)?.[1] ?? '');
    const channel = channels.find((item) => item.slug === slug);
    return channel ? json(route, {
      channel,
      messages: channelMessages.get(slug) ?? [],
      assistantParticipationStatus: 'Not implemented. Shared channels persist human conversation only; IronDev does not participate yet.',
      boundary: 'Channel collaboration is not workflow authority.'
    }) : json(route, { error: 'Channel not found or not visible to this user.' }, 404);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/([^/]+)\/messages$/, (route) => {
    const slug = decodeURIComponent(route.request().url().match(/\/channels\/([^/]+)\/messages$/)?.[1] ?? '');
    const request = route.request().postDataJSON() as { message: string };
    if (request.message.toLowerCase().includes('@irondev')) {
      return json(route, { error: 'IronDev participation in shared channels is not implemented. Remove @IronDev to post a human message.' }, 501);
    }
    const saved = {
      messageId: 7101 + state.channelMessageCount,
      authorUserId: 7,
      authorDisplayName: 'Bob',
      role: 'User',
      message: request.message,
      messageFormat: 'Markdown',
      status: 'Active',
      replyToMessageId: null,
      threadRootMessageId: null,
      createdUtc: '2026-07-10T09:00:00Z',
      editedUtc: null,
      boundary: 'Channel message; not approval.'
    };
    const history = channelMessages.get(slug) ?? [];
    history.push(saved);
    channelMessages.set(slug, history);
    state.channelMessageCount += 1;
    return json(route, saved);
  });

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
