import { expect, test, type Page, type Route } from '@playwright/test';
import type { ProjectChannelChatSummary } from '../src/api/types';
import { createDeferred } from './helpers/deferred';

test('recent backend sessions form the Workshop rail and the latest conversation opens', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop');

  await expect(page.getByTestId('chat.sessions')).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Recent direct conversations' })).toContainText('Catalog sorting');
  await expect(page.getByRole('navigation', { name: 'Recent direct conversations' })).toContainText('Ticket cockpit');
  await expect(page.getByRole('navigation', { name: 'Project channels' })).toContainText('General');
  await expect(page.getByLabel('1 unread')).toBeVisible();
  await expect(page.getByTestId('chat.sessions.item.9008')).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('chat.message.assistant')).toContainText('The catalog sorts by title.');
});

test('selecting a session uses a canonical URL and reloads backend-owned messages', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.sessions.item.9007').click();

  await expect(page).toHaveURL('/projects/7/workshop/sessions/9007');
  await expect(page.getByTestId('chat.sessions.item.9007')).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket cockpit calmer.');

  await page.reload();
  await expect(page).toHaveURL('/projects/7/workshop/sessions/9007');
  await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket cockpit calmer.');
});

test('new conversation stays local until the first message creates a durable session', async ({ page }) => {
  const state = await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.sessions.new').click();
  await expect(page).toHaveURL('/projects/7/workshop');
  await expect(page.getByRole('heading', { name: 'What would you like to work on?' })).toBeVisible();
  expect(state.createdSessionCount).toBe(0);

  await page.getByTestId('chat.composer.input').fill('Plan a stable catalog sort.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.message.assistant')).toContainText('Catalog sorting is handled by CatalogService.');
  await expect(page).toHaveURL('/projects/7/workshop/sessions/9010');
  expect(state.createdSessionCount).toBe(1);
  await expect(page.getByTestId('chat.sessions.item.9010')).toHaveAttribute('aria-current', 'page');
});

test('an unknown direct-session URL returns an honest conversation outcome', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop/sessions/9999');

  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await expect(page.getByRole('heading', { name: 'Conversation not found' })).toBeVisible();
  await expect(page.getByTestId('chat.composer')).toHaveCount(0);

  await page.getByRole('button', { name: 'Open recent conversations' }).click();
  await expect(page).toHaveURL('/projects/7/workshop');
  await expect(page.getByTestId('chat.workspace')).toBeVisible();
});

test('shared-channel URLs open persisted human conversation and mark durable unread state', async ({ page }) => {
  const markRead = createDeferred();
  const state = await mockSessionWorkspace(page, { markReadGate: markRead.promise });
  await page.goto('/projects/7/workshop/channels/general');

  await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
  await expect(page.getByRole('heading', { name: '# General' })).toBeVisible();
  await expect(page.getByTestId('chat.channel.message.7101')).toContainText('Human planning stays visible here.');
  await expect(page.getByTestId('chat.channel.assistant-status')).toContainText('explicitly mentions @IronDev');
  await expect(page.getByTestId('chat.channel.collaborationState')).toContainText('All notifications');
  await expect(page.getByTestId('chat.channel.collaborationState')).toContainText('Presence unavailable');
  await expect.poll(() => state.markReadRequests).toBe(1);
  await expect(page.getByLabel('1 unread')).toBeVisible();
  markRead.resolve();
  await expect.poll(() => state.markReadResponses).toBe(1);
  await expect(page.getByLabel('1 unread')).toHaveCount(0);
  await expect(page.getByTestId('chat.workspace')).toHaveCount(0);
  if (process.env.IRONDEV_VISUAL_SMOKE === '1') {
    await page.screenshot({ path: '../reports/visual-smoke/collab-state-1.png', fullPage: true });
  }
});

test('a read-marker failure keeps the shared channel visible and reports unknown unread state', async ({ page }) => {
  await mockSessionWorkspace(page, { failMarkRead: true });
  await page.goto('/projects/7/workshop/channels/general');

  await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
  await expect(page.getByTestId('chat.channel.message.7101')).toContainText('Human planning stays visible here.');
  await expect(page.getByTestId('chat.channel.collaborationState')).toContainText('Unread state unavailable');
});

test('a direct-session list failure does not hide a healthy shared channel', async ({ page }) => {
  await mockSessionWorkspace(page, { failSessionListOnce: true });
  await page.goto('/projects/7/workshop/channels/general');

  await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
  await expect(page.getByTestId('chat.channel.message.7101')).toContainText('Human planning stays visible here.');
});

test('a human channel message stays human while explicit IronDev invocation persists an attributed answer', async ({ page }) => {
  const state = await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop/channels/general');

  await page.getByTestId('chat.channel.composer').fill('Approved as discussion only.');
  await page.getByTestId('chat.channel.send').click();
  await expect(page.getByText('Approved as discussion only.')).toBeVisible();
  expect(state.channelMessageCount).toBe(2);

  await page.reload();
  await expect(page.getByText('Approved as discussion only.')).toBeVisible();

  await page.getByTestId('chat.channel.composer').fill('@IronDev summarize the project boundary');
  await page.getByTestId('chat.channel.send').click();
  await expect(page.getByText('The project boundary keeps conversation separate from workflow authority.')).toBeVisible();
  await expect(page.getByTestId('chat.channel.assistant.sources.7201')).toContainText('requested by Bob');
  await expect(page.getByTestId('chat.channel.assistant.sources.7201')).toContainText('src/ProjectBoundary.cs');
  expect(state.channelMessageCount).toBe(4);
  if (process.env.IRONDEV_VISUAL_SMOKE === '1') {
    await page.screenshot({ path: '../reports/visual-smoke/chat-assistant-1.png', fullPage: true });
  }
});

test('assistant completion failure preserves the saved request and supports an explicit retry', async ({ page }) => {
  const state = await mockSessionWorkspace(page, { failAssistantCompletionOnce: true });
  await page.goto('/projects/7/workshop/channels/general');

  await page.getByTestId('chat.channel.composer').fill('@IronDev inspect the project boundary');
  await page.getByTestId('chat.channel.send').click();

  await expect(page.getByText('@IronDev inspect the project boundary')).toBeVisible();
  await expect(page.getByTestId('chat.channel.error')).toContainText('message is saved');
  await expect(page.getByTestId('chat.channel.assistant.turn.7201')).toContainText('Requested');
  expect(state.assistantCompletionRequests).toBe(1);

  await page.reload();
  await expect(page.getByText('@IronDev inspect the project boundary')).toBeVisible();
  await expect(page.getByTestId('chat.channel.assistant.turn.7201')).toContainText('Requested');
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByText('The project boundary keeps conversation separate from workflow authority.')).toBeVisible();
  expect(state.assistantCompletionRequests).toBe(2);
});

test('channel member suggestions insert a durable person mention token', async ({ page }) => {
  const state = await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop/channels/general');

  await page.getByTestId('chat.channel.composer').fill('Please review @chan');
  await expect(page.getByTestId('chat.channel.mentions')).toContainText('Channel Reader');
  await page.getByRole('option', { name: /Channel Reader/ }).click();
  await expect(page.getByTestId('chat.channel.composer')).toHaveValue('Please review @channel-reader ');
  await page.getByTestId('chat.channel.send').click();
  await expect(page.getByText('Please review @channel-reader')).toBeVisible();
  expect(state.channelMessageCount).toBe(2);
});

test('project notification inbox acknowledges a mention and opens its channel', async ({ page }) => {
  const state = await mockSessionWorkspace(page, { withNotification: true });
  await page.goto('/projects/7/workshop');

  await expect(page.getByTestId('flow.notifications')).toContainText('1');
  await page.getByTestId('flow.notifications').click();
  await expect(page.getByTestId('flow.notification.8101')).toContainText('Alice mentioned you');
  if (process.env.IRONDEV_VISUAL_SMOKE === '1') {
    await page.screenshot({ path: '../reports/visual-smoke/chat-mentions-1.png', fullPage: true });
  }
  await page.getByTestId('flow.notification.8101').click();

  await expect(page).toHaveURL('/projects/7/workshop/channels/general');
  expect(state.markNotificationReadRequests).toBe(1);
});

test('Read-only channel membership renders the backend refusal and disables posting', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop/channels/product-planning');

  await expect(page.getByRole('heading', { name: '# Product planning' })).toBeVisible();
  await expect(page.getByTestId('chat.channel.assistant-status')).toContainText('Read only');
  await expect(page.getByTestId('chat.channel.composer')).toBeDisabled();
  await expect(page.getByTestId('chat.channel.send')).toBeDisabled();
});

test('authorized channel creation lands on its canonical shared route', async ({ page }) => {
  await mockSessionWorkspace(page);
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.channels.new.toggle').click();
  await page.getByTestId('chat.channels.new.name').fill('Release planning');
  await page.getByTestId('chat.channels.new.visibility').selectOption('MembersOnly');
  await page.getByTestId('chat.channels.new.submit').click();

  await expect(page).toHaveURL('/projects/7/workshop/channels/release-planning');
  await expect(page.getByRole('heading', { name: '# Release planning' })).toBeVisible();
});

test('session-list failure preserves the route and retries backend truth', async ({ page }) => {
  const state = await mockSessionWorkspace(page, { failSessionListOnce: true });
  await page.goto('/projects/7/workshop/sessions/9008');

  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('503');
  await expect(page.getByRole('heading', { name: 'Conversations are unavailable' })).toBeVisible();
  await page.getByRole('button', { name: 'Retry' }).click();

  await expect(page).toHaveURL('/projects/7/workshop/sessions/9008');
  await expect(page.getByTestId('chat.message.assistant')).toContainText('The catalog sorts by title.');
  expect(state.sessionListRequests).toBeGreaterThanOrEqual(2);
});

test.describe('narrow session navigation', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('opens as a drawer, selects a session, and leaves no horizontal overflow', async ({ page }) => {
    await mockSessionWorkspace(page);
    await page.goto('/projects/7/workshop');

    const rail = page.getByTestId('chat.sessions');
    await expect(rail).toHaveClass('chat-session-rail');

    await page.getByTestId('chat.sessions.toggle').click();
    await expect(page.getByRole('button', { name: 'Close conversations', exact: true })).toBeVisible();
    await expect(rail).toHaveClass('chat-session-rail chat-session-rail--open');

    await page.getByTestId('chat.sessions.item.9007').click();
    await expect(page).toHaveURL('/projects/7/workshop/sessions/9007');
    await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket cockpit calmer.');

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });

  test('keeps a shared channel thread and composer inside the narrow viewport', async ({ page }) => {
    await mockSessionWorkspace(page);
    await page.goto('/projects/7/workshop/channels/general');

    await expect(page.getByTestId('chat.channel.workspace')).toBeVisible();
    await expect(page.getByTestId('chat.channel.composer')).toBeVisible();
    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });

  test('keeps an attributed IronDev answer and source path inside the narrow viewport', async ({ page }) => {
    await mockSessionWorkspace(page);
    await page.goto('/projects/7/workshop/channels/general');

    await page.getByTestId('chat.channel.composer').fill('@IronDev summarize the project boundary');
    await page.getByTestId('chat.channel.send').click();
    await expect(page.getByText('The project boundary keeps conversation separate from workflow authority.')).toBeVisible();
    await expect(page.getByTestId('chat.channel.assistant.sources.7201')).toContainText('src/ProjectBoundary.cs');

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
    if (process.env.IRONDEV_VISUAL_SMOKE === '1') {
      await page.screenshot({ path: '../reports/visual-smoke/chat-assistant-1-mobile.png', fullPage: true });
    }
  });

  test('keeps the notification inbox inside the narrow header', async ({ page }) => {
    await mockSessionWorkspace(page, { withNotification: true });
    await page.goto('/projects/7/workshop');

    await page.getByTestId('flow.notifications').click();
    await expect(page.getByTestId('flow.notification.8101')).toBeVisible();
    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
    if (process.env.IRONDEV_VISUAL_SMOKE === '1') {
      await page.screenshot({ path: '../reports/visual-smoke/chat-mentions-1-mobile.png', fullPage: true });
    }
  });
});

interface SessionMockOptions {
  failSessionListOnce?: boolean;
  failMarkRead?: boolean;
  failAssistantCompletionOnce?: boolean;
  withNotification?: boolean;
  markReadGate?: Promise<void>;
}

interface SessionMockState {
  createdSessionCount: number;
  sessionListRequests: number;
  channelMessageCount: number;
  markReadRequests: number;
  markReadResponses: number;
  assistantCompletionRequests: number;
  markNotificationReadRequests: number;
}

async function mockSessionWorkspace(page: Page, options: SessionMockOptions = {}): Promise<SessionMockState> {
  const state: SessionMockState = {
    createdSessionCount: 0,
    sessionListRequests: 0,
    channelMessageCount: 1,
    markReadRequests: 0,
    markReadResponses: 0,
    assistantCompletionRequests: 0,
    markNotificationReadRequests: 0
  };
  let nextSessionId = 9010;
  let nextMessageId = 9200;
  const sessions = [
    {
      id: 9008,
      tenantId: 3,
      projectId: 7,
      title: 'Catalog sorting',
      summary: 'Direct with Workshop guide',
      createdDate: '2026-07-10T08:00:00Z',
      updatedDate: '2026-07-10T08:10:00Z',
      dateGroup: 'Today'
    },
    {
      id: 9007,
      tenantId: 3,
      projectId: 7,
      title: 'Ticket cockpit',
      summary: 'Direct with Workshop guide',
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
      unreadCount: 1,
      lastReadMessageId: null,
      lastReadUtc: null,
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
      unreadCount: 0,
      lastReadMessageId: null,
      lastReadUtc: null,
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
  const channelTurns = new Map<string, Array<Record<string, unknown>>>([
    ['general', []],
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
      unreadCount: 0,
      lastReadMessageId: null,
      lastReadUtc: null,
      boundary: 'Channel collaboration is not workflow authority.'
    };
    channels.push(created);
    channelMessages.set(slug, []);
    channelTurns.set(slug, []);
    return json(route, created, 201);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/([^/]+)$/, (route) => {
    const slug = decodeURIComponent(route.request().url().match(/\/channels\/([^/]+)$/)?.[1] ?? '');
    const channel = channels.find((item) => item.slug === slug);
    return channel ? json(route, {
      channel,
      messages: channelMessages.get(slug) ?? [],
      assistantTurns: channelTurns.get(slug) ?? [],
      mentionCandidates: [{ userId: 8, displayName: 'Channel Reader', handle: 'channel-reader' }],
      readState: {
        unreadCount: channel.unreadCount,
        lastReadMessageId: channel.lastReadMessageId,
        lastReadUtc: channel.lastReadUtc,
        notificationLevel: channel.currentUserNotificationLevel,
        boundary: 'Read markers and notification preferences are collaboration state, not workflow authority.'
      },
      presence: {
        status: 'Unavailable',
        activeViewerCount: null,
        boundary: 'Presence is unavailable until the backend supplies durable or realtime presence truth.'
      },
      assistantParticipationStatus: 'IronDev responds in shared channels only when a message explicitly mentions @IronDev.',
      boundary: 'Channel collaboration is not workflow authority.'
    }) : json(route, { error: 'Channel not found or not visible to this user.' }, 404);
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/([^/]+)\/read$/, async (route) => {
    state.markReadRequests += 1;
    if (options.failMarkRead) {
      return json(route, { error: 'Read marker store unavailable.' }, 503);
    }

    await options.markReadGate;

    const slug = decodeURIComponent(route.request().url().match(/\/channels\/([^/]+)\/read$/)?.[1] ?? '');
    const channel = channels.find((item) => item.slug === slug);
    if (!channel) return json(route, { error: 'Channel not found.' }, 404);
    channel.unreadCount = 0;
    channel.lastReadMessageId = 7101;
    channel.lastReadUtc = '2026-07-10T08:01:00Z';
    state.markReadResponses += 1;
    return json(route, {
      unreadCount: 0,
      lastReadMessageId: 7101,
      lastReadUtc: '2026-07-10T08:01:00Z',
      notificationLevel: channel.currentUserNotificationLevel,
      boundary: 'Read markers and notification preferences are collaboration state, not workflow authority.'
    });
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/([^/]+)\/messages$/, (route) => {
    const slug = decodeURIComponent(route.request().url().match(/\/channels\/([^/]+)\/messages$/)?.[1] ?? '');
    const request = route.request().postDataJSON() as { message: string };
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
    const isAssistantRequest = /@irondev\b/i.test(request.message);
    const assistantTurn = isAssistantRequest ? {
      turnId: 7201,
      channelId: channels.find((item) => item.slug === slug)?.channelId ?? 701,
      requestMessageId: saved.messageId,
      responseMessageId: null,
      requestedByUserId: 7,
      requestedByDisplayName: 'Bob',
      prompt: request.message.replace(/@irondev\b/i, '').trim(),
      answer: null,
      mode: null,
      modeConfidence: null,
      modeReason: null,
      contextSummary: null,
      linkedFilePaths: null,
      linkedSymbols: null,
      linkedDocumentIds: null,
      dogfoodTraceId: null,
      traceId: null,
      status: 'Requested',
      failureReason: null,
      createdUtc: '2026-07-10T09:00:01Z',
      completedUtc: null,
      boundary: 'Assistant answer; not approval.'
    } : null;
    if (assistantTurn) channelTurns.set(slug, [assistantTurn]);
    return json(route, { message: saved, assistantTurn });
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/([^/]+)\/assistant-turns\/(\d+)\/complete$/, (route) => {
    state.assistantCompletionRequests += 1;
    if (options.failAssistantCompletionOnce && state.assistantCompletionRequests === 1) {
      return json(route, { error: 'Assistant completion unavailable.' }, 503);
    }

    const match = route.request().url().match(/\/channels\/([^/]+)\/assistant-turns\/(\d+)\/complete$/);
    const slug = decodeURIComponent(match?.[1] ?? '');
    const turnId = Number(match?.[2]);
    const turn = channelTurns.get(slug)?.find((item) => item.turnId === turnId);
    if (!turn) return json(route, { error: 'Assistant turn not found.' }, 404);
    const response = {
      messageId: 7101 + state.channelMessageCount,
      authorUserId: null,
      authorDisplayName: 'IronDev',
      role: 'Assistant',
      message: 'The project boundary keeps conversation separate from workflow authority.',
      messageFormat: 'Markdown',
      status: 'Active',
      replyToMessageId: turn.requestMessageId,
      threadRootMessageId: turn.requestMessageId,
      createdUtc: '2026-07-10T09:00:02Z',
      editedUtc: null,
      boundary: 'Assistant answer; not approval.'
    };
    const answered = {
      ...turn,
      responseMessageId: response.messageId,
      answer: response.message,
      mode: 'Exploration',
      modeConfidence: 0.92,
      modeReason: 'Project question.',
      contextSummary: 'Inspected the project boundary.',
      linkedFilePaths: 'src/ProjectBoundary.cs',
      status: 'Answered',
      completedUtc: '2026-07-10T09:00:02Z'
    };
    channelTurns.set(slug, [answered]);
    const history = channelMessages.get(slug) ?? [];
    if (!history.some((message) => message.messageId === response.messageId)) {
      history.push(response);
      state.channelMessageCount += 1;
    }
    channelMessages.set(slug, history);
    return json(route, { assistantTurn: answered, responseMessage: response });
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/notifications$/, (route) => {
    const notifications = options.withNotification ? [{
      notificationId: 8101,
      kind: 'Mention',
      channelId: 701,
      channelName: 'General',
      channelSlug: 'general',
      messageId: 7101,
      actorUserId: 8,
      actorDisplayName: 'Alice',
      title: 'Alice mentioned you in #general',
      body: '@bob please review this.',
      isRead: false,
      createdUtc: '2026-07-10T09:05:00Z',
      readUtc: null,
      boundary: 'Notification attention state; not approval.'
    }] : [];
    return json(route, {
      unreadCount: notifications.filter((notification) => !notification.isRead).length,
      notifications,
      boundary: 'Notification attention state; not approval.'
    });
  });

  await page.route(/\/irondev-api\/api\/projects\/7\/notifications\/(\d+)\/read$/, (route) => {
    state.markNotificationReadRequests += 1;
    return route.fulfill({ status: 204 });
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
