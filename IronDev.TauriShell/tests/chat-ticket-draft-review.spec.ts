import { expect, test, type Page, type Route } from '@playwright/test';
import { mockProjectWorkItem } from './helpers/mockWorkItem';

test('an incomplete backend draft opens review with honest blockers and no mutation', async ({ page }) => {
  const state = await mockTicketDraftWorkspace(page, {
    readyForConfirmation: false,
    openQuestions: ['Which sort order becomes the default?'],
    conflicts: ['The current API exposes no stable sort key.']
  });
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.baDraft.review').click();

  await expect(page.getByRole('dialog', { name: 'Review ticket draft' })).toBeVisible();
  await expect(page.getByTestId('chat.ticketDraft.blockers')).toContainText('not ready for confirmation');
  await expect(page.getByTestId('chat.ticketDraft.blockers')).toContainText('Resolve every potential conflict');
  await expect(page.getByTestId('chat.ticketDraft.questions')).toContainText('Which sort order becomes the default?');
  await expect(page.getByTestId('chat.ticketDraft.provenance')).toContainText('Session 9007');
  await expect(page.getByTestId('chat.ticketDraft.provenance')).toContainText('5001');
  await expect(page.getByTestId('chat.ticketDraft.create')).toBeDisabled();
  expect(state.confirmRequests).toHaveLength(0);
});

test('a ready reviewed draft creates one backend ticket and opens the real Work Item', async ({ page }) => {
  const state = await mockTicketDraftWorkspace(page, { readyForConfirmation: true });
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.baDraft.review').click();
  await expect(page.getByTestId('chat.ticketDraft.create')).toBeEnabled();
  await page.getByTestId('chat.ticketDraft.create').click();

  await expect(page.getByTestId('chat.ticketDraft.success')).toContainText('Ticket #44');
  await expect(page.getByTestId('chat.ticketDraft.success')).toContainText('does not imply readiness');
  expect(state.confirmRequests).toHaveLength(1);
  expect(state.confirmRequests[0]).toMatchObject({
    sourceChatSessionId: 9007,
    draft: { candidateTitle: 'Add stable catalog sorting', sourceMessageIds: ['5001'] }
  });

  await page.getByTestId('chat.ticketDraft.openWorkItem').click();
  await expect(page).toHaveURL('/projects/7/work-items/44');
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.locator('body')).toContainText('Add stable catalog sorting');
});

test('backend refusal remains in review and never renders fake ticket success', async ({ page }) => {
  const state = await mockTicketDraftWorkspace(page, {
    readyForConfirmation: true,
    confirmStatus: 400,
    confirmError: 'BA draft source chat message 5001 was not found in the source session.'
  });
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.baDraft.review').click();
  await page.getByTestId('chat.ticketDraft.create').click();

  await expect(page.getByTestId('chat.ticketDraft.error')).toContainText('source chat message 5001 was not found');
  await expect(page.getByTestId('chat.ticketDraft.review')).toBeVisible();
  await expect(page.getByTestId('chat.ticketDraft.success')).toHaveCount(0);
  expect(state.confirmRequests).toHaveLength(1);
});

test('a success response without a real ticket ID is treated as failure', async ({ page }) => {
  await mockTicketDraftWorkspace(page, { readyForConfirmation: true, omitTicketId: true });
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.baDraft.review').click();
  await page.getByTestId('chat.ticketDraft.create').click();

  await expect(page.getByTestId('chat.ticketDraft.error')).toContainText('did not include a ticket identifier');
  await expect(page.getByTestId('chat.ticketDraft.success')).toHaveCount(0);
  await expect(page).toHaveURL('/projects/7/workshop/sessions/9007');
});

test.describe('narrow ticket draft review', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('uses the full viewport without overflow and closes back to the conversation', async ({ page }) => {
    await mockTicketDraftWorkspace(page, { readyForConfirmation: true });
    await page.goto('/projects/7/workshop/sessions/9007');

    await page.getByTestId('chat.baDraft.review').click();
    await expect(page.getByRole('dialog', { name: 'Review ticket draft' })).toBeVisible();
    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
      dialogWidth: document.querySelector('.chat-ticket-review')?.getBoundingClientRect().width ?? 0
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
    expect(dimensions.dialogWidth).toBe(dimensions.clientWidth);

    await page.getByTestId('chat.ticketDraft.close').click();
    await expect(page.getByTestId('chat.ticketDraft.review')).toHaveCount(0);
    await expect(page.getByTestId('chat.composer')).toBeVisible();
  });
});

interface TicketDraftMockOptions {
  readyForConfirmation?: boolean;
  openQuestions?: string[];
  conflicts?: string[];
  confirmStatus?: number;
  confirmError?: string;
  omitTicketId?: boolean;
}

interface TicketDraftMockState {
  confirmRequests: Array<Record<string, unknown>>;
}

async function mockTicketDraftWorkspace(
  page: Page,
  options: TicketDraftMockOptions = {}
): Promise<TicketDraftMockState> {
  const state: TicketDraftMockState = { confirmRequests: [] };
  const draft = {
    candidateTitle: 'Add stable catalog sorting',
    problem: 'Customers cannot choose a predictable catalog order.',
    proposedChange: 'Add explicit stable catalog sorting.',
    businessRules: ['Sorting must remain stable for equal values.'],
    acceptanceCriteria: ['Title sorting produces a stable result.'],
    assumptions: ['The existing catalog endpoint remains compatible.'],
    openQuestions: options.openQuestions ?? [],
    sourceMessageIds: ['5001'],
    confidence: options.readyForConfirmation === false ? 0.62 : 0.92,
    readyForConfirmation: options.readyForConfirmation ?? true,
    potentialConflicts: options.conflicts ?? [],
    suggestedArtifact: 'Ticket',
    boundary: 'A BA draft is shaped evidence, not approval or execution authority.'
  };

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

  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions$/, (route) =>
    json(route, [{ id: 9007, tenantId: 3, projectId: 7, title: 'Catalog sorting', updatedDate: '2026-07-10T08:00:00Z' }])
  );
  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions\/9007\/messages$/, (route) =>
    json(route, [
      {
        id: 5001,
        tenantId: 3,
        projectId: 7,
        chatSessionId: 9007,
        role: 'user',
        message: 'Create a ticket for stable catalog sorting.',
        createdDate: '2026-07-10T08:00:00Z'
      },
      {
        id: 5002,
        tenantId: 3,
        projectId: 7,
        chatSessionId: 9007,
        role: 'assistant',
        message: 'I shaped a candidate ticket for review.',
        createdDate: '2026-07-10T08:01:00Z'
      }
    ])
  );
  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions\/9007\/messages\/5002\/audit$/, (route) =>
    json(route, {
      chatMessageId: 5002,
      source: 'DurableAudit',
      mode: 'Formalization',
      modeConfidence: 0.93,
      modeReason: 'The user explicitly requested a ticket.',
      clarification: { required: false, kind: 'None', questions: [] },
      gate: {
        mode: 'Formalization',
        canSaveDiscussion: true,
        canCreateTicket: true,
        canViewSources: true,
        canCopyMarkdown: true,
        reason: 'Ticket draft may be reviewed.',
        confidence: 0.93,
        governanceActions: ['createTicket', 'viewSources']
      },
      routeSource: 'ProjectChatContextPipeline',
      isFallbackEvidence: false,
      baDraft: draft
    })
  );
  await page.route(/\/irondev-api\/api\/projects\/7\/chat\/sessions\/9007\/messages\/5001\/audit$/, (route) =>
    json(route, { error: 'User messages have no assistant audit.' }, 404)
  );

  await page.route('**/irondev-api/api/projects/7/tickets/ba-draft/confirm', (route) => {
    state.confirmRequests.push(route.request().postDataJSON() as Record<string, unknown>);
    if (options.confirmStatus && options.confirmStatus >= 400) {
      return json(route, { error: options.confirmError ?? 'Ticket creation was refused.' }, options.confirmStatus);
    }
    return json(route, {
      id: options.omitTicketId ? undefined : 44,
      tenantId: 3,
      projectId: 7,
      title: 'Add stable catalog sorting',
      status: 'Draft',
      problem: draft.problem,
      acceptanceCriteria: draft.acceptanceCriteria.join('\n'),
      sourceChatSessionId: 9007,
      sourceChatMessageId: 5001
    });
  });
  await page.route('**/irondev-api/api/projects/7/tickets/44', (route) =>
    json(route, { id: 44, projectId: 7, title: 'Add stable catalog sorting', status: 'Draft' })
  );
  await mockProjectWorkItem(page, {
    workItemId: 44,
    title: 'Add stable catalog sorting',
    linkedChatSessionId: 9007,
    ticket: {
      id: 44,
      projectId: 7,
      title: 'Add stable catalog sorting',
      status: 'Draft',
      acceptanceCriteria: draft.acceptanceCriteria.join('\n'),
      sourceChatSessionId: 9007,
      sourceChatMessageId: 5001
    }
  });
  await page.route('**/irondev-api/api/projects/7/tickets', (route) => json(route, []));

  return state;
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
