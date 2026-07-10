import { expect, test, type Page } from '@playwright/test';

test('empty Chat is conversation-first with useful starters and no backstage diagnostics', async ({ page }) => {
  await mockChatWorkspace(page);
  await page.goto('/projects/7/chat');

  await expect(page.getByRole('heading', { name: 'Chat', exact: true })).toBeVisible();
  await expect(page.getByText('BookSeller / Direct with IronDev')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'What would you like to work on?' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Review the current project' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Shape a new feature' })).toBeVisible();
  await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);

  const route = page.getByTestId('chat.route');
  await expect(route).not.toContainText('Unknown');
  await expect(route).not.toContainText('Pending');
  await expect(route).not.toContainText('No trace id yet');
  await expect(route).not.toContainText('Review Project State');
  await expect(route).not.toContainText('Ready to send');
  await expect(route.locator('.surface-panel')).toHaveCount(0);
  await expect(route.locator('.command-button--primary')).toHaveCount(1);
  await expect(page.getByPlaceholder('Ask about this project or describe work...')).toBeVisible();
});

test('starter actions use the existing composer and project-review request', async ({ page }) => {
  const state = await mockChatWorkspace(page);
  await page.goto('/projects/7/chat');

  await page.getByRole('button', { name: 'Shape a new feature' }).click();
  await expect(page.getByTestId('chat.composer.input')).toHaveValue('I want to shape a new feature: ');
  await expect(page.getByTestId('chat.composer.input')).toBeFocused();

  await page.reload();
  await page.getByRole('button', { name: 'Review the current project' }).click();
  await expect.poll(() => state.lastCompletionMode).toBe('projectStateReview');
  await expect(page.getByText('Review Project State')).toBeVisible();
});

test('a historical user message reads as conversation, not a framed inspection screen', async ({ page }) => {
  await mockChatWorkspace(page, {
    history: [
      {
        id: 1,
        tenantId: 3,
        projectId: 7,
        chatSessionId: 9007,
        role: 'user',
        message: 'Make the ticket workspace calm and useful.',
        createdDate: '2026-07-10T07:34:00Z'
      }
    ]
  });
  await page.goto('/projects/7/chat');

  await expect(page.getByTestId('chat.message.user')).toContainText('Make the ticket workspace calm and useful.');
  await expect(page.getByTestId('chat.emptyState')).toHaveCount(0);
  await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);
  await expect(page.getByTestId('chat.composer')).toBeVisible();
});

test('sending keeps the compact composer attached to the active conversation', async ({ page }) => {
  await mockChatWorkspace(page, { completionDelayMs: 900 });
  await page.goto('/projects/7/chat');

  await page.getByTestId('chat.composer.input').fill('Review the ticket flow.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.sending')).toContainText('Sending');
  await expect(page.getByTestId('chat.command.send')).toContainText('Sending');
  await expect(page.getByTestId('chat.command.send')).toBeDisabled();
  await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);
});

test('an answered conversation reveals sources only when the user asks', async ({ page }) => {
  await mockChatWorkspace(page);
  await page.goto('/projects/7/chat');

  await page.getByTestId('chat.composer.input').fill('Where is catalog sorting implemented?');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.message.assistant')).toContainText('Catalog sorting is handled by CatalogService.');
  await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);
  await page.getByTestId('chat.message.viewSources').click();
  await expect(page.getByTestId('chat.contextPanel')).toBeVisible();
  await expect(page.getByTestId('chat.contextPanel')).toContainText('src/Catalog/CatalogService.cs');
});

test('a backend-returned ticket draft opens its decision material without changing its gate', async ({ page }) => {
  await mockChatWorkspace(page, { includeBaDraft: true });
  await page.goto('/projects/7/chat');

  await page.getByTestId('chat.composer.input').fill('Turn this idea into a ticket.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.contextPanel')).toBeVisible();
  await expect(page.getByTestId('chat.baDraft.panel')).toContainText('Improve catalog sorting');
  await expect(page.getByTestId('chat.baDraft.confirm')).toBeDisabled();
});

test('a failed response stays in the conversation and leaves the composer usable', async ({ page }) => {
  await mockChatWorkspace(page, { completionStatus: 500 });
  await page.goto('/projects/7/chat');

  await page.getByTestId('chat.composer.input').fill('Investigate the failing build.');
  await page.getByTestId('chat.command.send').click();

  await expect(page).toHaveURL('/projects/7/chat/sessions/9007');
  await expect(page.getByTestId('chat.message.user')).toContainText('Investigate the failing build.');
  await expect(page.getByTestId('chat.error')).toContainText('Chat service unavailable.');
  await expect(page.getByTestId('chat.composer')).toBeVisible();
  await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);
});

test('desktop Chat keeps one readable conversation column at 1366 and 1920 widths', async ({ page }) => {
  await mockChatWorkspace(page, {
    history: [
      {
        id: 1,
        tenantId: 3,
        projectId: 7,
        chatSessionId: 9007,
        role: 'user',
        message: 'Keep the conversation readable on wide displays.',
        createdDate: '2026-07-10T07:34:00Z'
      }
    ]
  });

  for (const viewport of [{ width: 1366, height: 768 }, { width: 1920, height: 1080 }]) {
    await page.setViewportSize(viewport);
    await page.goto('/projects/7/chat');
    await expect(page.getByTestId('chat.message.user')).toContainText('Keep the conversation readable on wide displays.');

    const layout = await page.evaluate(() => {
      const thread = document.querySelector('.chat-workspace-layout__thread')?.getBoundingClientRect();
      const composer = document.querySelector('.chat-composer')?.getBoundingClientRect();
      const message = document.querySelector('.chat-message--user')?.getBoundingClientRect();
      return {
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
        thread: thread ? { x: thread.x, width: thread.width, right: thread.right } : null,
        composer: composer ? { x: composer.x, width: composer.width, right: composer.right } : null,
        message: message ? { x: message.x, width: message.width, right: message.right } : null
      };
    });

    expect(layout.scrollWidth).toBeLessThanOrEqual(layout.clientWidth);
    expect(layout.thread?.width ?? 0).toBeLessThanOrEqual(900);
    expect(layout.thread?.width).toBe(layout.composer?.width);
    expect(layout.message?.width ?? 0).toBeLessThanOrEqual(640);
    expect(layout.message?.right).toBeLessThanOrEqual(layout.thread?.right ?? 0);
  }
});

test.describe('narrow Chat', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('keeps header, starters, and composer coherent without horizontal overflow', async ({ page }) => {
    await mockChatWorkspace(page);
    await page.goto('/projects/7/chat');

    await expect(page.getByRole('heading', { name: 'Chat', exact: true })).toBeVisible();
    await expect(page.getByTestId('chat.composer')).toBeVisible();
    await expect(page.getByTestId('chat.contextPanel')).toHaveCount(0);

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);

    const starterButtons = page.getByLabel('Conversation starters').getByRole('button');
    await expect(starterButtons).toHaveCount(4);
    for (const button of await starterButtons.all()) {
      const box = await button.boundingBox();
      expect(box?.width ?? 0).toBeGreaterThan(250);
    }
  });
});

interface ChatMockOptions {
  history?: Array<Record<string, unknown>>;
  completionDelayMs?: number;
  completionStatus?: number;
  includeBaDraft?: boolean;
}

interface ChatMockState {
  lastCompletionMode: string | null;
}

async function mockChatWorkspace(page: Page, options: ChatMockOptions = {}): Promise<ChatMockState> {
  const state: ChatMockState = { lastCompletionMode: null };
  const history = options.history ?? [];
  let messageId = 9100;
  let sessionExists = history.length > 0;
  let sessionTitle = 'Current conversation';

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

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
      body: JSON.stringify({ userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
    })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    })
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
    })
  );
  await page.route('**/irondev-api/api/projects/7/select', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) })
  );
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => {
    if (route.request().method() === 'GET') {
      const sessions = sessionExists
        ? [{ id: 9007, tenantId: 3, projectId: 7, title: sessionTitle, summary: 'Current conversation' }]
        : [];
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sessions) });
    }
    const request = route.request().postDataJSON() as { title?: string };
    sessionExists = true;
    sessionTitle = request.title ?? sessionTitle;
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(9007) });
  });
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007', (route) =>
    sessionExists
      ? route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ id: 9007, tenantId: 3, projectId: 7, title: sessionTitle, summary: 'Current conversation' })
        })
      : route.fulfill({ status: 204 })
  );
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages/*/audit', (route) =>
    route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'No audit row.' }) })
  );
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages', (route) => {
    if (route.request().method() === 'GET') {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(history) });
    }
    messageId += 1;
    const request = route.request().postDataJSON() as {
      role: string;
      message: string;
      tags?: string | null;
      contextSummary?: string | null;
      linkedFilePaths?: string | null;
      linkedSymbols?: string | null;
    };
    history.push({
      id: messageId,
      tenantId: 3,
      projectId: 7,
      chatSessionId: 9007,
      role: request.role,
      message: request.message,
      tags: request.tags,
      contextSummary: request.contextSummary,
      linkedFilePaths: request.linkedFilePaths,
      linkedSymbols: request.linkedSymbols,
      createdDate: '2026-07-10T08:00:00Z'
    });
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(messageId) });
  });
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    const body = route.request().postDataJSON() as { mode?: string };
    state.lastCompletionMode = body.mode ?? null;
    if (options.completionDelayMs) {
      await new Promise((resolve) => setTimeout(resolve, options.completionDelayMs));
    }
    if (options.completionStatus && options.completionStatus >= 400) {
      return route.fulfill({
        status: options.completionStatus,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Chat service unavailable.' })
      });
    }
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: 'Catalog sorting is handled by CatalogService.',
        contextSummary: 'Inspected the catalog service and its sorting tests.',
        linkedFilePaths: 'src/Catalog/CatalogService.cs',
        linkedSymbols: 'CatalogService.Sort',
        traceId: 42,
        mode: 'Exploration',
        gate: {
          mode: 'Exploration',
          canSaveDiscussion: true,
          canCreateTicket: false,
          canViewSources: true,
          canCopyMarkdown: true,
          reason: 'Project exploration response.',
          confidence: 0.92,
          governanceActions: ['saveDiscussion', 'viewSources', 'copyMarkdown']
        },
        baDraft: options.includeBaDraft
          ? {
              candidateTitle: 'Improve catalog sorting',
              problem: 'Customers cannot choose a stable sort order.',
              proposedChange: 'Add explicit catalog sort controls.',
              acceptanceCriteria: ['Sort by title is stable.'],
              openQuestions: ['Which sort becomes the default?'],
              confidence: 0.72,
              readyForConfirmation: false,
              potentialConflicts: [],
              suggestedArtifact: 'Ticket draft',
              boundary: 'Draft only. Confirmation remains a separate human action.'
            }
          : null
      })
    });
  });

  return state;
}
