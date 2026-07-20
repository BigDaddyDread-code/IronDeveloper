import { expect, test, type Page, type Route } from '@playwright/test';
import type { TicketProposalSetReadModel } from '../src/api/types';

const setId = '11111111-2222-4333-8444-555555555555';
const firstProposalId = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const secondProposalId = 'bbbbbbbb-cccc-4ddd-8eee-ffffffffffff';
const agentRunId = '99999999-8888-4777-8666-555555555555';
const issueId = '77777777-6666-4555-8444-333333333333';
const secondProjectSetId = '33333333-4444-4555-8666-777777777777';
const secondProjectProposalId = 'cccccccc-dddd-4eee-8fff-000000000001';

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

test('explicit confirmation atomically creates permanent tickets and locks the committed review', async ({ page }) => {
  const state = await mockProposalWorkspace(page, 'Ready');
  state.commitExecutionReadiness = 'Ready';
  await page.goto('/projects/7/workshop/sessions/9007');

  const commit = page.getByTestId('chat.ticketProposals.commit');
  await expect(commit).toContainText('does not configure a repository or authorize Builder execution');
  await page.getByTestId('chat.ticketProposals.commit.open').click();
  const confirmation = page.getByTestId('chat.ticketProposals.commit.confirmation');
  await expect(confirmation).toContainText('Confirm revision 1');
  await expect(confirmation).toContainText('Repository setup remains required');
  await expect(confirmation).toContainText('Builder is not authorized');
  expect(state.commitBodies).toHaveLength(0);

  await page.getByTestId('chat.ticketProposals.commit.submit').click();

  const committed = page.getByTestId('chat.ticketProposals.committed');
  await expect(committed).toContainText('Tickets created atomically');
  await expect(committed).toContainText('Ticket #4201');
  await expect(committed).toContainText('Ticket #4202');
  await expect(committed).toContainText('Blocked by #4201');
  await expect(committed).toContainText('Project phase is Delivery');
  await expect(committed).toContainText('execution readiness remains Ready');
  await expect(committed).toContainText('Repository setup remains required');
  await expect(committed).toContainText('Builder is not authorized');
  await expect(page.getByTestId(`chat.ticketProposal.${firstProposalId}.edit`)).toHaveCount(0);
  await expect(page.getByTestId('chat.ticketProposals.regenerate')).toHaveCount(0);

  expect(state.commitBodies).toHaveLength(1);
  expect(state.commitBodies[0]).toMatchObject({
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    expectedProposalSetRevision: 1
  });
  expect(Object.keys(state.commitBodies[0]).sort()).toEqual([
    'clientOperationId', 'expectedProposalSetRevision', 'leaseEpoch', 'workbenchSessionId'
  ]);
  expect(String(state.commitBodies[0].clientOperationId)).toMatch(/^[0-9a-f-]{36}$/i);
  expect(state.permanentTicketWrites).toBe(0);
});

test('ambiguous ticket creation survives panel close and reopens its exact replay control', async ({ page }) => {
  const state = await mockProposalWorkspace(page, 'Ready');
  state.failNextCommitAmbiguously = true;
  state.commitExecutionReadiness = 'Ready';
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.ticketProposals.commit.open').click();
  await page.getByTestId('chat.ticketProposals.commit.submit').click();
  await expect(page.getByTestId('chat.ticketProposals.deliveryUnresolved')).toContainText('Retry only the unchanged review action');
  await expect(page.getByTestId('chat.ticketProposals.commit.submit')).toHaveText('Retry exact ticket creation');
  const firstAttempt = { ...state.commitBodies[0] };

  await page.getByTestId('chat.ticketProposals.close').click();
  await expect(page.getByTestId('chat.ticketProposals')).toHaveCount(0);
  await page.getByTestId('chat.ticketProposals.show').click();
  await expect(page.getByTestId('chat.ticketProposals.commit.confirmation')).toBeVisible();
  await expect(page.getByTestId('chat.ticketProposals.commit.submit')).toHaveText('Retry exact ticket creation');

  await page.getByTestId('chat.ticketProposals.commit.submit').click();

  await expect(page.getByTestId('chat.ticketProposals.committed')).toContainText('recovered by exact idempotent replay');
  await expect(page.getByTestId('chat.ticketProposals.committed')).toContainText('execution readiness remains Ready');
  expect(state.commitBodies).toHaveLength(2);
  expect(state.commitBodies[1]).toEqual(firstAttempt);
  expect(state.permanentTicketWrites).toBe(0);
});

test.describe('late ticket-commit responses stay fenced to their original Workbench authority', () => {
  test('a deferred success from project A cannot clear project B delivery uncertainty', async ({ page }) => {
    const harness = await mockAuthoritySwitchDuringCommit(page, 'success');
    await page.goto('/projects/7/workshop/sessions/9007');

    await page.getByTestId('chat.ticketProposals.commit.open').click();
    await page.getByTestId('chat.ticketProposals.commit.submit').click();
    await expect.poll(() => harness.projectACommitBodies.length).toBe(1);

    await navigateInApp(page, '/projects/8/workshop/sessions/9008');
    await expect(page.getByTestId(`chat.ticketProposal.${secondProjectProposalId}`)).toContainText('Project B guarded proposal');
    await page.getByTestId('chat.ticketProposals.commit.open').click();
    await page.getByTestId('chat.ticketProposals.commit.submit').click();
    await expect(page.getByTestId('chat.ticketProposals.deliveryUnresolved')).toBeVisible();
    await expect(page.getByTestId('chat.ticketProposals.commit.submit')).toHaveText('Retry exact ticket creation');

    const lateResponse = page.waitForResponse((response) =>
      response.url().includes(`/ticket-proposal-sets/${setId}/commits`));
    harness.releaseProjectACommit();
    await lateResponse;
    await settleReactUpdates(page);

    await expect(page.getByTestId(`chat.ticketProposal.${secondProjectProposalId}`)).toContainText('Project B guarded proposal');
    await expect(page.getByTestId('chat.ticketProposals.deliveryUnresolved')).toBeVisible();
    await expect(page.getByTestId('chat.ticketProposals.commit.submit')).toHaveText('Retry exact ticket creation');
    expect(harness.projectBCommitBodies).toHaveLength(1);
  });

  test('a deferred failure from project A cannot create a delivery fence in project B', async ({ page }) => {
    const harness = await mockAuthoritySwitchDuringCommit(page, 'failure');
    await page.goto('/projects/7/workshop/sessions/9007');

    await page.getByTestId('chat.ticketProposals.commit.open').click();
    await page.getByTestId('chat.ticketProposals.commit.submit').click();
    await expect.poll(() => harness.projectACommitBodies.length).toBe(1);

    await navigateInApp(page, '/projects/8/workshop/sessions/9008');
    await expect(page.getByTestId(`chat.ticketProposal.${secondProjectProposalId}`)).toContainText('Project B guarded proposal');
    await expect(page.getByTestId('chat.ticketProposals.deliveryUnresolved')).toHaveCount(0);
    await expect(page.getByTestId('chat.ticketProposals.commit.open')).toBeEnabled();

    const lateFailure = page.waitForEvent('requestfailed', (request) =>
      request.url().includes(`/ticket-proposal-sets/${setId}/commits`));
    harness.releaseProjectACommit();
    await lateFailure;
    await settleReactUpdates(page);

    await expect(page.getByTestId(`chat.ticketProposal.${secondProjectProposalId}`)).toContainText('Project B guarded proposal');
    await expect(page.getByTestId('chat.ticketProposals.deliveryUnresolved')).toHaveCount(0);
    await expect(page.getByTestId('chat.ticketProposals.mutationError')).toHaveCount(0);
    await expect(page.getByTestId('chat.ticketProposals.commit.open')).toBeEnabled();
  });
});

interface ProposalMockState {
  editBodies: Array<Record<string, unknown>>;
  reorderBodies: Array<Record<string, unknown>>;
  removeBodies: Array<Record<string, unknown>>;
  resolveBodies: Array<Record<string, unknown>>;
  regenerationBodies: Array<Record<string, unknown>>;
  commitBodies: Array<Record<string, unknown>>;
  permanentTicketWrites: number;
  rejectNextEditWithRevisionConflict: boolean;
  failNextCommitAmbiguously: boolean;
  commitExecutionReadiness: 'NotConfigured' | 'ValidationRequired' | 'Ready';
}

async function mockProposalWorkspace(page: Page, initialStatus: 'Ready' | 'NeedsInput'): Promise<ProposalMockState> {
  const state: ProposalMockState = {
    editBodies: [],
    reorderBodies: [],
    removeBodies: [],
    resolveBodies: [],
    regenerationBodies: [],
    commitBodies: [],
    permanentTicketWrites: 0,
    rejectNextEditWithRevisionConflict: false,
    failNextCommitAmbiguously: false,
    commitExecutionReadiness: 'NotConfigured'
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
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/commits`, (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.commitBodies.push({ ...body });
    if (state.failNextCommitAmbiguously) {
      state.failNextCommitAmbiguously = false;
      return route.abort('connectionreset');
    }
    const reviewedRevision = Number(body.expectedProposalSetRevision);
    const firstTicketId = 4201;
    const secondTicketId = 4202;
    model = { ...model, status: 'Committed' as const };
    nextRevision('Committed');
    return json(route, {
      proposalSet: model,
      commitment: {
        commitmentId: '22222222-3333-4444-8555-666666666666',
        ticketProposalSetId: setId,
        reviewedRevision,
        committedRevision: model.revision,
        reviewedSnapshotHash: 'f'.repeat(64),
        actorUserId: 7,
        committedAtUtc: '2026-07-20T01:03:00Z',
        tickets: [
          {
            ticketProposalId: firstProposalId,
            projectTicketId: firstTicketId,
            title: 'Calm login entry',
            suggestedOrder: 1,
            blockedByTicketIds: []
          },
          {
            ticketProposalId: secondProposalId,
            projectTicketId: secondTicketId,
            title: 'Account recovery',
            suggestedOrder: 2,
            blockedByTicketIds: [firstTicketId]
          }
        ]
      },
      projectLifecyclePhase: 'Delivery',
      executionReadiness: state.commitExecutionReadiness,
      clientOperationId: body.clientOperationId,
      isReplay: state.commitBodies.length > 1
    });
  });
  return state;
}

interface AuthoritySwitchCommitHarness {
  projectACommitBodies: Array<Record<string, unknown>>;
  projectBCommitBodies: Array<Record<string, unknown>>;
  releaseProjectACommit: () => void;
}

async function mockAuthoritySwitchDuringCommit(
  page: Page,
  lateOutcome: 'success' | 'failure'
): Promise<AuthoritySwitchCommitHarness> {
  await mockProposalWorkspace(page, 'Ready');
  const projectACommitBodies: Array<Record<string, unknown>> = [];
  const projectBCommitBodies: Array<Record<string, unknown>> = [];
  let releaseProjectACommit = () => {};
  const projectACommitGate = new Promise<void>((resolve) => { releaseProjectACommit = resolve; });
  const projectAModel = proposalSetFixture('Ready');
  const projectBModel = secondProjectProposalSetFixture();

  await page.route('**/irondev-api/api/projects', (route) => json(route, [
    {
      id: 7, tenantId: 3, name: 'Login Studio', localPath: null,
      lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured'
    },
    {
      id: 8, tenantId: 3, name: 'Project B', localPath: null,
      lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured'
    }
  ]));

  // The route changes before ProjectContext finishes opening project B. Keep
  // that short project-A/session-B transition deterministic and off the proxy.
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9008', (route) => json(route, {
    id: 9008, tenantId: 3, projectId: 7, title: 'Authority switch in progress', summary: null
  }));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9008/messages', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9008/messages/*/audit', (route) =>
    json(route, { error: 'no_audit' }, 404));

  await page.route('**/irondev-api/api/workbench/projects/8/open', (route) => {
    const body = route.request().postDataJSON() as { clientOperationId: string };
    return json(route, {
      projectId: 8, tenantId: 3, name: 'Project B', projectLifecyclePhase: 'Shaping',
      executionReadiness: 'NotConfigured', repositoryBinding: null, workbenchSessionId: 8008,
      leaseEpoch: 2, wasResumed: true, wasTakenOver: false, clientOperationId: body.clientOperationId
    });
  });
  await page.route('**/irondev-api/api/projects/8/channels', (route) =>
    json(route, { projectId: 8, canCreateChannels: true, channels: [] }));
  await page.route('**/irondev-api/api/projects/8/notifications**', (route) =>
    json(route, { projectId: 8, unreadCount: 0, notifications: [] }));
  await page.route('**/irondev-api/api/projects/8/tickets**', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/8/chat/sessions', (route) => json(route, [
    { id: 9008, tenantId: 3, projectId: 8, title: 'Project B shaping', summary: 'Independent authority' }
  ]));
  await page.route('**/irondev-api/api/projects/8/chat/sessions/9008', (route) => json(route, {
    id: 9008, tenantId: 3, projectId: 8, title: 'Project B shaping', summary: 'Independent authority'
  }));
  await page.route('**/irondev-api/api/projects/8/chat/sessions/9008/messages', (route) => json(route, [
    {
      id: 8101, tenantId: 3, projectId: 8, chatSessionId: 9008, role: 'user',
      message: 'Project B must retain its own delivery fence.', createdDate: '2026-07-20T02:00:00Z'
    }
  ]));
  await page.route('**/irondev-api/api/projects/8/chat/sessions/9008/messages/*/audit', (route) =>
    json(route, { error: 'no_audit' }, 404));
  await page.route('**/irondev-api/api/workbench/projects/8/agent-runs/current**', (route) => json(route, {
    submissionAvailable: true, unavailableCategory: null, boundChatSessionId: 9008, activeRun: null, latestRun: null
  }));
  await page.route('**/irondev-api/api/workbench/projects/8/understanding', (route) =>
    json(route, { error: 'not_needed' }, 503));
  await page.route(`**/irondev-api/api/workbench/projects/8/ticket-proposal-sets/${secondProjectSetId}/history**`, (route) =>
    json(route, [historyEntry(1, 'Generated', false, projectBModel)]));
  await page.route('**/irondev-api/api/workbench/projects/8/ticket-proposal-sets/current**', (route) =>
    json(route, projectBModel));
  await page.route(`**/irondev-api/api/workbench/projects/8/ticket-proposal-sets/${secondProjectSetId}/commits`, (route) => {
    projectBCommitBodies.push({ ...(route.request().postDataJSON() as Record<string, unknown>) });
    return route.abort('connectionreset');
  });

  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${setId}/commits`, async (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown>;
    projectACommitBodies.push({ ...body });
    await projectACommitGate;
    if (lateOutcome === 'failure') {
      return route.abort('connectionreset');
    }
    return json(route, commitResultFixture(projectAModel, body, 4201));
  });

  return { projectACommitBodies, projectBCommitBodies, releaseProjectACommit };
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
        dependencyProposalIds: [firstProposalId], suggestedOrder: 2, sourceMessageIds: [9102]
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

function secondProjectProposalSetFixture(): TicketProposalSetReadModel {
  return {
    ticketProposalSetId: secondProjectSetId,
    projectId: 8,
    workbenchSessionId: 8008,
    leaseEpoch: 2,
    revision: 1,
    basedOnUnderstandingRevision: 1,
    status: 'Ready',
    splitReason: 'Project B has one independent acceptance boundary.',
    proposals: [{
      ticketProposalId: secondProjectProposalId,
      title: 'Project B guarded proposal',
      problem: 'A response from another authority could corrupt this project state.',
      proposedChange: 'Keep every delivery fence scoped to its originating Workbench authority.',
      acceptanceCriteria: ['Late responses from project A cannot change project B delivery state.'],
      dependencyProposalIds: [],
      suggestedOrder: 1,
      sourceMessageIds: [8101]
    }],
    openQuestions: [],
    potentialConflicts: [],
    sourceMessageIds: [8101],
    createdByAgentRunId: '88888888-7777-4666-8555-444444444444',
    createdAtUtc: '2026-07-20T02:01:00Z',
    updatedAtUtc: '2026-07-20T02:01:00Z'
  };
}

function commitResultFixture(
  proposalSet: TicketProposalSetReadModel,
  request: Record<string, unknown>,
  firstTicketId: number
) {
  const committedSet = {
    ...proposalSet,
    status: 'Committed' as const,
    revision: proposalSet.revision + 1,
    updatedAtUtc: '2026-07-20T02:02:00Z'
  };
  const ticketIds = new Map(
    proposalSet.proposals.map((proposal, index) => [proposal.ticketProposalId, firstTicketId + index])
  );
  return {
    proposalSet: committedSet,
    commitment: {
      commitmentId: '22222222-3333-4444-8555-666666666666',
      ticketProposalSetId: proposalSet.ticketProposalSetId,
      reviewedRevision: Number(request.expectedProposalSetRevision),
      committedRevision: committedSet.revision,
      reviewedSnapshotHash: 'f'.repeat(64),
      actorUserId: 7,
      committedAtUtc: '2026-07-20T02:02:00Z',
      tickets: proposalSet.proposals.map((proposal) => ({
        ticketProposalId: proposal.ticketProposalId,
        projectTicketId: ticketIds.get(proposal.ticketProposalId),
        title: proposal.title,
        suggestedOrder: proposal.suggestedOrder,
        blockedByTicketIds: proposal.dependencyProposalIds.map((dependencyId) => ticketIds.get(dependencyId))
      }))
    },
    projectLifecyclePhase: 'Delivery',
    executionReadiness: 'NotConfigured',
    clientOperationId: request.clientOperationId,
    isReplay: false
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

async function navigateInApp(page: Page, pathname: string) {
  await page.evaluate((nextPath) => {
    window.history.pushState(null, '', nextPath);
    window.dispatchEvent(new Event('irondev:navigation'));
  }, pathname);
}

async function settleReactUpdates(page: Page) {
  await page.evaluate(() => new Promise<void>((resolve) => {
    window.requestAnimationFrame(() => window.requestAnimationFrame(() => resolve()));
  }));
}
