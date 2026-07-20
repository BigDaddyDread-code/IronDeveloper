import { expect, test, type Page, type Route } from '@playwright/test';

test('help and typos stay deterministic while /ticket starts one governed BA run and loads durable proposals', async ({ page }) => {
  const state = await mockSlashCommandWorkspace(page);
  await page.goto('/projects/7/workshop/sessions/9007');

  const composer = page.getByTestId('chat.composer.input');
  await expect(page.getByTestId('chat.command.menu')).toBeVisible();
  await page.getByTestId('chat.command.option.help').click();
  await expect(composer).toHaveValue('/help');
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.command.help.result')).toContainText('Workbench commands');
  await expect(composer).toHaveValue('');

  await composer.fill('  /tickte do not persist this private tail');
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.error')).toContainText('/tickte is not available');
  await expect(composer).toHaveValue('  /tickte do not persist this private tail');

  await composer.fill('/TICKET split independent user outcomes');
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.ticketProposals.summary')).toContainText('Ready for review');
  await expect(page.getByText('Split independent user outcomes', { exact: true })).toBeVisible();
  await expect(composer).toHaveValue('');

  expect(state.inputBodies).toHaveLength(3);
  expect(state.inputBodies.map((body) => body.composerText)).toEqual([
    '/help',
    '  /tickte do not persist this private tail',
    '/TICKET split independent user outcomes'
  ]);
  expect(state.inputBodies.slice(0, 2).every((body) => body.chatSessionId === null)).toBe(true);
  expect(state.inputBodies[2].chatSessionId).toBe(9007);
  expect(new Set(state.inputBodies.map((body) => body.clientOperationId)).size).toBe(3);
  expect(state.agentRunSubmissions).toBe(0);
  expect(state.chatSessionWrites).toBe(0);
});

interface SlashCommandMockState {
  inputBodies: Array<Record<string, unknown>>;
  agentRunSubmissions: number;
  chatSessionWrites: number;
}

async function mockSlashCommandWorkspace(page: Page): Promise<SlashCommandMockState> {
  const state: SlashCommandMockState = {
    inputBodies: [],
    agentRunSubmissions: 0,
    chatSessionWrites: 0
  };
  const agentRunId = '99999999-aaaa-4bbb-8ccc-dddddddddddd';
  const proposalSetId = '11111111-2222-4333-8444-555555555555';
  const proposalId = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
  let ticketSubmitted = false;

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/localtest/preflight', (route) => json(route, {
    state: 'LocalTestReady',
    environment: 'LocalTest',
    database: 'IronDeveloper_Test',
    apiBuildCommit: 'test-commit',
    launcherRepositoryCommit: 'test-commit',
    apiBaseUrl: 'http://localhost:5000',
    sessionMode: 'SmokeSimulation',
    sandboxApplyRequested: false,
    sandboxApplyEnabled: false,
    sandboxApplyRoot: null,
    capabilities: ['WorkbenchAgentRuns', 'WorkbenchCommands']
  }));
  await page.route('**/irondev-api/api/environment', (route) => json(route, {
    environment: 'LocalTest',
    database: 'IronDeveloper_Test',
    isTestEnvironment: true,
    workbench: {
      version: '0.1.0-preview.10',
      mode: 'V2',
      v2Enabled: true,
      v1FallbackEnabled: true,
      conversationAuthorityEnabled: true,
      previewId: 'workbench-pr04a',
      apiBuildIdentity: 'test-build',
      apiCommit: 'test-commit',
      resetSupported: true
    }
  }));
  await page.route('**/irondev-api/api/auth/me**', (route) => json(route, {
    userId: 7,
    email: 'bob@irondev.local',
    displayName: 'Bob',
    selectedTenantId: 3
  }));
  await page.route('**/irondev-api/api/tenants', (route) => json(route, [
    { id: 3, name: 'IronDev Local', slug: 'irondev-local' }
  ]));
  await page.route('**/irondev-api/api/projects', (route) => json(route, [
    { id: 7, tenantId: 3, name: 'Command Studio', localPath: null, lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured' }
  ]));
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) => {
    const body = route.request().postDataJSON() as { clientOperationId: string };
    return json(route, {
      projectId: 7,
      tenantId: 3,
      name: 'Command Studio',
      projectLifecyclePhase: 'Shaping',
      executionReadiness: 'NotConfigured',
      repositoryBinding: null,
      workbenchSessionId: 7007,
      leaseEpoch: 1,
      wasResumed: true,
      wasTakenOver: false,
      clientOperationId: body.clientOperationId
    });
  });
  await page.route('**/irondev-api/api/projects/7/channels', (route) => json(route, {
    projectId: 7,
    canCreateChannels: true,
    channels: []
  }));
  await page.route('**/irondev-api/api/projects/7/notifications**', (route) => json(route, {
    projectId: 7,
    unreadCount: 0,
    notifications: []
  }));
  await page.route('**/irondev-api/api/projects/7/tickets**', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => {
    if (route.request().method() === 'POST') {
      state.chatSessionWrites += 1;
      return json(route, 9007);
    }
    return json(route, [{ id: 9007, tenantId: 3, projectId: 7, title: 'Shaping', summary: 'Command smoke' }]);
  });
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007', (route) => json(route, {
    id: 9007,
    tenantId: 3,
    projectId: 7,
    title: 'Shaping',
    summary: 'Command smoke'
  }));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages', (route) => json(route,
    ticketSubmitted ? [
      { id: 9199, projectId: 7, chatSessionId: 9007, role: 'user', message: '/TICKET split independent user outcomes', createdDate: '2026-07-20T01:02:00Z' },
      { id: 9200, projectId: 7, chatSessionId: 9007, role: 'assistant', message: 'I prepared one proposal. No permanent ticket was created.', createdDate: '2026-07-20T01:02:01Z' }
    ] : []));
  await page.route('**/irondev-api/api/workbench/projects/7/agent-runs/current**', (route) => json(route, {
    submissionAvailable: true,
    unavailableCategory: null,
    boundChatSessionId: 9007,
    activeRun: null,
    latestRun: null
  }));
  await page.route(`**/irondev-api/api/workbench/projects/7/agent-runs/${agentRunId}`, (route) => json(route, {
    agentRunId,
    tenantId: 3,
    projectId: 7,
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    actorUserId: 7,
    chatSessionId: 9007,
    sourceUserMessageId: 9199,
    status: 'Completed',
    attemptCount: 1,
    assistantMessageId: 9200,
    createdAtUtc: '2026-07-20T01:02:00Z',
    startedAtUtc: '2026-07-20T01:02:00Z',
    completedAtUtc: '2026-07-20T01:02:01Z',
    cancellationRequestedAtUtc: null,
    failureCategory: null,
    retryable: false,
    invocationKind: 'TicketProposalGeneration',
    ticketProposalSetId: proposalSetId,
    ticketProposalRevision: 1
  }));
  await page.route('**/irondev-api/api/workbench/projects/7/agent-runs', (route) => {
    if (route.request().method() === 'POST') {
      state.agentRunSubmissions += 1;
    }
    return json(route, { error: 'unexpected_agent_run' }, 500);
  });
  await page.route('**/irondev-api/api/workbench/projects/7/understanding', (route) =>
    json(route, { error: 'not_available_in_command_test' }, 503));
  await page.route('**/irondev-api/api/workbench/projects/7/inputs', (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown> & {
      clientOperationId: string;
      composerText: string;
      workbenchSessionId: number;
      leaseEpoch: number;
    };
    state.inputBodies.push({ ...body });
    const token = body.composerText.trimStart().split(/\s/, 1)[0];
    if (token.toLowerCase() !== '/help' && token.toLowerCase() !== '/ticket') {
      return json(route, {
        error: 'workbench_command_unknown',
        message: 'Unknown Workbench command. Use /help to see the available commands.',
        rawCommandToken: token
      }, 400);
    }
    const isHelp = token.toLowerCase() === '/help';
    const instruction = body.composerText.trimStart().slice(token.length).trim() || null;
    if (!isHelp) {
      ticketSubmitted = true;
      return json(route, {
        kind: 'AgentRun',
        projectId: 7,
        workbenchSessionId: body.workbenchSessionId,
        leaseEpoch: body.leaseEpoch,
        clientOperationId: body.clientOperationId,
        normalizedCommand: '/ticket',
        instruction,
        title: 'Ticket proposals',
        message: 'Ticket proposal generation has started from the current governed Workbench context.',
        isReplay: false,
        agentRun: {
          agentRunId,
          projectId: 7,
          workbenchSessionId: body.workbenchSessionId,
          leaseEpoch: body.leaseEpoch,
          chatSessionId: body.chatSessionId,
          userMessageId: 9199,
          status: 'Pending',
          clientOperationId: body.clientOperationId,
          createdAtUtc: '2026-07-20T01:02:00Z',
          isReplay: false,
          invocationKind: 'TicketProposalGeneration',
          ticketProposalSetId: proposalSetId,
          ticketProposalRevision: null
        },
        rawCommandToken: null,
        reasonCode: null
      }, 202);
    }
    return json(route, {
      kind: 'Help',
      projectId: 7,
      workbenchSessionId: body.workbenchSessionId,
      leaseEpoch: body.leaseEpoch,
      clientOperationId: body.clientOperationId,
      normalizedCommand: '/help',
      instruction,
      title: 'Workbench commands',
      message: 'Available commands: /help and /ticket.',
      isReplay: false,
      agentRun: null,
      rawCommandToken: null,
      reasonCode: null
    });
  });
  await page.route('**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/current**', (route) => {
    if (!ticketSubmitted) {
      return route.fulfill({ status: 204 });
    }
    return json(route, proposalSet());
  });
  await page.route(`**/irondev-api/api/workbench/projects/7/ticket-proposal-sets/${proposalSetId}/history**`, (route) =>
    json(route, [{
      revision: 1,
      changeKind: 'Generated',
      actorUserId: 7,
      agentRunId,
      createdAtUtc: '2026-07-20T01:02:01Z',
      proposalSet: proposalSet()
    }]));

  function proposalSet() {
    return {
      ticketProposalSetId: proposalSetId,
      projectId: 7,
      workbenchSessionId: 7007,
      leaseEpoch: 1,
      revision: 1,
      basedOnUnderstandingRevision: 2,
      status: 'Ready',
      splitReason: 'One independently testable user-visible outcome.',
      proposals: [{
        ticketProposalId: proposalId,
        title: 'Split independent user outcomes',
        problem: 'The current outcome is too broad.',
        proposedChange: 'Deliver one bounded outcome.',
        acceptanceCriteria: ['The user can observe the bounded outcome.'],
        dependencyProposalIds: [],
        suggestedOrder: 1,
        sourceMessageIds: [9199]
      }],
      openQuestions: [],
      potentialConflicts: [],
      sourceMessageIds: [9199],
      createdByAgentRunId: agentRunId,
      createdAtUtc: '2026-07-20T01:02:01Z',
      updatedAtUtc: '2026-07-20T01:02:01Z'
    };
  }

  return state;
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
