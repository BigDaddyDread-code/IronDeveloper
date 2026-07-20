import { expect, test, type Page, type Route } from '@playwright/test';
import type { TicketProposalSetReadModel } from '../src/api/types';

const setId = '11111111-2222-4333-8444-555555555555';
const firstProposalId = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const secondProposalId = 'bbbbbbbb-cccc-4ddd-8eee-ffffffffffff';
const agentRunId = '99999999-8888-4777-8666-555555555555';
const issueId = '77777777-6666-4555-8444-333333333333';

test('ticket proposals are durable review state with source links, edit, reorder, and remove', async ({ page }) => {
  const state = await mockProposalWorkspace(page, 'Ready');
  await page.goto('/projects/7/workshop/sessions/9007');

  await expect(page.getByTestId('chat.ticketProposals')).toBeVisible();
  await expect(page.getByTestId('chat.ticketProposals.summary')).toContainText('Ready for review');
  await expect(page.getByTestId(`chat.ticketProposal.${firstProposalId}`)).toContainText('Calm login entry');

  await page.getByTestId(`chat.ticketProposal.${firstProposalId}`).getByRole('button', { name: 'Message #9101' }).click();
  await expect(page.locator('[data-message-id="user-9101"]')).toBeFocused();
  await page.getByTestId(`chat.ticketProposal.${firstProposalId}`).getByRole('button', { name: 'Message #9001' }).click();
  await expect(page.getByTestId('chat.ticketProposals.source.9001')).toContainText('Earlier project intent remains inspectable.');

  await page.getByTestId(`chat.ticketProposal.${firstProposalId}.edit`).click();
  const editor = page.getByTestId(`chat.ticketProposal.${firstProposalId}.editor`);
  await editor.getByLabel('Title').fill('Calm and accessible login entry');
  await page.getByTestId(`chat.ticketProposal.${firstProposalId}.save`).click();
  await expect(page.getByTestId(`chat.ticketProposal.${firstProposalId}`)).toContainText('Calm and accessible login entry');

  await page.getByTestId('chat.ticketProposals.history').getByText(/Revision and actor history/).click();
  const firstRevision = page.getByTestId('chat.ticketProposals.history.revision.1');
  await firstRevision.locator('summary').click();
  await expect(firstRevision).toContainText('Calm login entry');

  await page.getByTestId(`chat.ticketProposal.${secondProposalId}`).getByRole('button', { name: 'Move up' }).click();
  await expect(page.getByTestId(`chat.ticketProposal.${secondProposalId}`).locator('.ticket-proposal-card__order')).toHaveText('1');

  await page.getByTestId(`chat.ticketProposal.${secondProposalId}.remove`).click();
  await expect(page.getByTestId(`chat.ticketProposal.${secondProposalId}`)).toHaveCount(0);

  expect(state.editBodies).toHaveLength(1);
  expect(state.editBodies[0]).toMatchObject({
    expectedProposalSetRevision: 1,
    title: 'Calm and accessible login entry'
  });
  expect(state.reorderBodies).toHaveLength(1);
  expect(state.reorderBodies[0].orderedProposalIds).toEqual([secondProposalId, firstProposalId]);
  expect(state.removeBodies).toHaveLength(1);
  expect(state.permanentTicketWrites).toBe(0);
});

test('a stale review revision reloads the authoritative durable set before another write', async ({ page }) => {
  const state = await mockProposalWorkspace(page, 'Ready');
  state.rejectNextEditWithRevisionConflict = true;
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId(`chat.ticketProposal.${firstProposalId}.edit`).click();
  const editor = page.getByTestId(`chat.ticketProposal.${firstProposalId}.editor`);
  await editor.getByLabel('Title').fill('Stale local title');
  await page.getByTestId(`chat.ticketProposal.${firstProposalId}.save`).click();

  await expect(page.getByTestId(`chat.ticketProposal.${firstProposalId}`)).toContainText('Authoritative remote login title');
  await expect(page.getByTestId('chat.ticketProposals.mutationError')).toContainText('changed elsewhere');
  expect(state.editBodies).toHaveLength(1);
});

test('NeedsInput stays proposal-free, resolves a blocking question, and regenerates through a governed AgentRun', async ({ page }) => {
  const state = await mockProposalWorkspace(page, 'NeedsInput');
  await page.goto('/projects/7/workshop/sessions/9007');

  await expect(page.getByTestId('chat.ticketProposals.needsInput')).toBeVisible();
  await expect(page.locator('.ticket-proposal-card')).toHaveCount(0);
  const issue = page.getByTestId(`chat.ticketProposal.issue.${issueId}`);
  await issue.getByLabel(/Resolution for/).fill('Start with email and password for project members.');
  await issue.getByRole('button', { name: 'Resolve' }).click();
  await expect(issue).toContainText('Resolved');

  const regenerate = page.getByTestId('chat.ticketProposals.regenerate');
  await regenerate.getByRole('textbox').fill('Create one bounded login proposal from that decision.');
  await page.getByTestId('chat.ticketProposals.regenerate.submit').click();
  await expect(page.getByTestId(`chat.ticketProposal.${firstProposalId}`)).toContainText('Calm login entry');

  expect(state.resolveBodies).toHaveLength(1);
  expect(state.regenerationBodies).toHaveLength(1);
  expect(state.regenerationBodies[0]).toMatchObject({
    chatSessionId: 9007,
    instruction: 'Create one bounded login proposal from that decision.'
  });
  expect(state.permanentTicketWrites).toBe(0);
});

interface ProposalMockState {
  editBodies: Array<Record<string, unknown>>;
  reorderBodies: Array<Record<string, unknown>>;
  removeBodies: Array<Record<string, unknown>>;
  resolveBodies: Array<Record<string, unknown>>;
  regenerationBodies: Array<Record<string, unknown>>;
  permanentTicketWrites: number;
  rejectNextEditWithRevisionConflict: boolean;
}

async function mockProposalWorkspace(page: Page, initialStatus: 'Ready' | 'NeedsInput'): Promise<ProposalMockState> {
  const state: ProposalMockState = {
    editBodies: [],
    reorderBodies: [],
    removeBodies: [],
    resolveBodies: [],
    regenerationBodies: [],
    permanentTicketWrites: 0,
    rejectNextEditWithRevisionConflict: false
  };
  let model = proposalSetFixture(initialStatus);
  const nextRevision = (changeKind: string, actor = true) => {
    model = { ...model, revision: model.revision + 1, updatedAtUtc: new Date().toISOString() };
    history.push(historyEntry(model.revision, changeKind, actor, model));
  };
  const history = [historyEntry(1, 'Generated', false, model)];

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/localtest/preflight', (route) => json(route, {
    state: 'LocalTestReady', environment: 'LocalTest', database: 'IronDeveloper_Test',
    apiBuildCommit: 'test-commit', launcherRepositoryCommit: 'test-commit',
    apiBaseUrl: 'http://localhost:5000', sessionMode: 'SmokeSimulation',
    sandboxApplyRequested: false, sandboxApplyEnabled: false, sandboxApplyRoot: null,
    capabilities: ['WorkbenchAgentRuns', 'TicketProposals']
  }));
  await page.route('**/irondev-api/api/environment', (route) => json(route, {
    environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true,
    workbench: {
      version: '0.1.0-preview.10', mode: 'V2', v2Enabled: true, v1FallbackEnabled: true,
      conversationAuthorityEnabled: true, previewId: 'workbench-pr04a', apiBuildIdentity: 'test-build',
      apiCommit: 'test-commit', resetSupported: true
    }
  }));
  await page.route('**/irondev-api/api/auth/me**', (route) => json(route, {
    userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3
  }));
  await page.route('**/irondev-api/api/tenants', (route) => json(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]));
  await page.route('**/irondev-api/api/projects', (route) => json(route, [{
    id: 7, tenantId: 3, name: 'Login Studio', localPath: null,
    lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured'
  }]));
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) => {
    const body = route.request().postDataJSON() as { clientOperationId: string };
    return json(route, {
      projectId: 7, tenantId: 3, name: 'Login Studio', projectLifecyclePhase: 'Shaping',
      executionReadiness: 'NotConfigured', repositoryBinding: null, workbenchSessionId: 7007,
      leaseEpoch: 1, wasResumed: true, wasTakenOver: false, clientOperationId: body.clientOperationId
    });
  });
  await page.route('**/irondev-api/api/projects/7/channels', (route) => json(route, { projectId: 7, canCreateChannels: true, channels: [] }));
  await page.route('**/irondev-api/api/projects/7/notifications**', (route) => json(route, { projectId: 7, unreadCount: 0, notifications: [] }));
  await page.route('**/irondev-api/api/projects/7/tickets**', (route) => {
    if (route.request().method() !== 'GET') state.permanentTicketWrites += 1;
    return json(route, []);
  });
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => json(route, [
    { id: 9007, tenantId: 3, projectId: 7, title: 'Login shaping', summary: 'Proposal sources' }
  ]));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007', (route) => json(route, {
    id: 9007, tenantId: 3, projectId: 7, title: 'Login shaping', summary: 'Proposal sources'
  }));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages', (route) => json(route, [
    { id: 9101, tenantId: 3, projectId: 7, chatSessionId: 9007, role: 'user', message: 'Members need a calm login flow.', createdDate: '2026-07-20T01:00:00Z' },
    { id: 9102, tenantId: 3, projectId: 7, chatSessionId: 9007, role: 'user', message: 'Recovery can be delivered independently.', createdDate: '2026-07-20T01:01:00Z' }
  ]));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages/*/audit', (route) => json(route, { error: 'no_audit' }, 404));
  await page.route('**/irondev-api/api/projects/7/chat/messages/9001', (route) => json(route, {
    id: 9001, tenantId: 3, projectId: 7, chatSessionId: 8999, role: 'user',
    message: 'Earlier project intent remains inspectable.', createdDate: '2026-07-19T23:00:00Z'
  }));
  await page.route('**/irondev-api/api/workbench/projects/7/agent-runs/current**', (route) => json(route, {
    submissionAvailable: true, unavailableCategory: null, boundChatSessionId: 9007, activeRun: null, latestRun: null
  }));
  await page.route(`**/irondev-api/api/workbench/projects/7/agent-runs/${agentRunId}`, (route) => json(route, {
    agentRunId, tenantId: 3, projectId: 7, workbenchSessionId: 7007, leaseEpoch: 1,
    actorUserId: 7, chatSessionId: 9007, sourceUserMessageId: 9199, status: 'Completed',
    attemptCount: 1, assistantMessageId: 9200, createdAtUtc: '2026-07-20T01:02:00Z',
    startedAtUtc: '2026-07-20T01:02:01Z', completedAtUtc: '2026-07-20T01:02:02Z',
    cancellationRequestedAtUtc: null, failureCategory: null, retryable: false
  }));
  await page.route('**/irondev-api/api/workbench/projects/7/understanding', (route) => json(route, { error: 'not_needed' }, 503));

  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/history**`, (route) => {
    const url = new URL(route.request().url());
    expect(url.searchParams.get('workbenchSessionId')).toBe('7007');
    expect(url.searchParams.get('leaseEpoch')).toBe('1');
    return json(route, history);
  });
  await page.route('**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/current**', (route) => json(route, model));
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/proposals/*/remove`, (route) => {
    const path = new URL(route.request().url()).pathname;
    const proposalId = path.split('/proposals/')[1].split('/')[0];
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.removeBodies.push({ ...body });
    model = { ...model, proposals: model.proposals.filter((proposal) => proposal.ticketProposalId !== proposalId) };
    model.proposals = model.proposals.map((proposal, index) => ({ ...proposal, suggestedOrder: index + 1 }));
    nextRevision('ProposalRemoved');
    return mutation(route, model, String(body.clientOperationId));
  });
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/proposals/*`, (route) => {
    const path = new URL(route.request().url()).pathname;
    const proposalId = path.split('/proposals/')[1].split('/')[0];
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.editBodies.push({ ...body });
    if (state.rejectNextEditWithRevisionConflict) {
      state.rejectNextEditWithRevisionConflict = false;
      model = {
        ...model,
        revision: model.revision + 1,
        proposals: model.proposals.map((proposal) => proposal.ticketProposalId === proposalId
          ? { ...proposal, title: 'Authoritative remote login title' }
          : proposal)
      };
      history.push(historyEntry(model.revision, 'ProposalEdited', true, model));
      return json(route, { error: 'ticket_proposal_revision_conflict', currentRevision: model.revision }, 409);
    }
    model = {
      ...model,
      proposals: model.proposals.map((proposal) => proposal.ticketProposalId === proposalId
        ? { ...proposal, title: String(body.title), problem: String(body.problem), proposedChange: String(body.proposedChange), acceptanceCriteria: body.acceptanceCriteria as string[] }
        : proposal)
    };
    nextRevision('ProposalEdited');
    return mutation(route, model, String(body.clientOperationId));
  });
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/reorder`, (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.reorderBodies.push({ ...body });
    const ordered = body.orderedProposalIds as string[];
    model = { ...model, proposals: ordered.map((id, index) => ({ ...model.proposals.find((proposal) => proposal.ticketProposalId === id)!, suggestedOrder: index + 1 })) };
    nextRevision('ProposalsReordered');
    return mutation(route, model, String(body.clientOperationId));
  });
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/issues/${issueId}/resolve`, (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.resolveBodies.push({ ...body });
    model = {
      ...model,
      openQuestions: model.openQuestions.map((issue) => issue.issueId === issueId
        ? { ...issue, status: 'Resolved' as const, resolution: String(body.resolution) }
        : issue)
    };
    nextRevision('IssueResolved');
    return mutation(route, model, String(body.clientOperationId));
  });
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/regenerations`, (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.regenerationBodies.push({ ...body });
    model = proposalSetFixture('Ready', model.revision + 1);
    history.push(historyEntry(model.revision, 'Regenerated', false, model));
    return json(route, {
      agentRunId, projectId: 7, workbenchSessionId: 7007, leaseEpoch: 1, chatSessionId: 9007,
      userMessageId: 9199, status: 'Pending', clientOperationId: body.clientOperationId,
      createdAtUtc: '2026-07-20T01:02:00Z', isReplay: false,
      invocationKind: 'TicketProposalRegeneration', ticketProposalSetId: setId,
      ticketProposalRevision: Number(body.expectedProposalSetRevision)
    }, 202);
  });
  return state;
}

function proposalSetFixture(status: 'Ready' | 'NeedsInput', revision = 1): TicketProposalSetReadModel {
  return {
    ticketProposalSetId: setId, projectId: 7, workbenchSessionId: 7007, leaseEpoch: 1,
    revision, basedOnUnderstandingRevision: 3, status,
    splitReason: status === 'Ready' ? 'Login and recovery have independent user-visible outcomes.' : null,
    proposals: status === 'Ready' ? [
      {
        ticketProposalId: firstProposalId, title: 'Calm login entry', problem: 'Members cannot enter reliably.',
        proposedChange: 'Add a bounded email and password login flow.',
        acceptanceCriteria: ['A valid member can sign in.', 'An invalid attempt is explained safely.'],
        dependencyProposalIds: [], suggestedOrder: 1, sourceMessageIds: [9101, 9001]
      },
      {
        ticketProposalId: secondProposalId, title: 'Account recovery', problem: 'Members can become locked out.',
        proposedChange: 'Add a separate recovery journey.', acceptanceCriteria: ['A member can request recovery.'],
        dependencyProposalIds: [], suggestedOrder: 2, sourceMessageIds: [9102]
      }
    ] : [],
    openQuestions: status === 'NeedsInput' ? [{
      issueId, kind: 'BlockingQuestion', text: 'Which member identity should v0.1 support?', status: 'Open', resolution: null,
      sourceMessageIds: [9101]
    }] : [],
    potentialConflicts: [], sourceMessageIds: [9101, 9102], createdByAgentRunId: agentRunId,
    createdAtUtc: '2026-07-20T01:02:00Z', updatedAtUtc: '2026-07-20T01:02:00Z'
  };
}

function historyEntry(
  revision: number,
  changeKind: string,
  actor: boolean,
  proposalSet: TicketProposalSetReadModel
) {
  return {
    revision,
    changeKind,
    actorUserId: 7,
    agentRunId: actor ? null : agentRunId,
    createdAtUtc: '2026-07-20T01:02:00Z',
    proposalSet
  };
}

function mutation(route: Route, proposalSet: unknown, clientOperationId: string) {
  return json(route, { proposalSet, clientOperationId, isReplay: false });
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
