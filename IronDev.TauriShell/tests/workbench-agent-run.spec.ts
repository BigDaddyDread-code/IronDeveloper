import { expect, test, type Page, type Route } from '@playwright/test';

type AgentRunStatus = 'Pending' | 'Running' | 'NeedsInput' | 'Completed' | 'Failed' | 'Cancelled' | 'Superseded' | 'Stale';

interface AgentRunMockOptions {
  initialSession?: boolean;
  initialActiveRun?: boolean;
  unavailable?: boolean;
  holdRunning?: boolean;
  holdSubmit?: boolean;
  holdRecovery?: boolean;
  multipleSessions?: boolean;
  multipleProjects?: boolean;
  pollFailureStatus?: 401 | 403 | 404 | 409;
  transientPollFailureStatuses?: Array<408 | 429>;
  sessionTransportFailures?: number;
  submitTransportFailures?: number;
  terminalStatus?: 'Completed' | 'NeedsInput' | 'Failed' | 'Cancelled';
  terminalAppearsDuringRecovery?: boolean;
}

interface AgentRunMockState {
  submitRequests: number;
  submitOperationIds: string[];
  sessionWrites: number;
  sessionOperationIds: string[];
  directMessageWrites: number;
  legacyCompletions: number;
  cancelRequests: number;
  pollRequests: number;
  recoveryRequests: number;
  history: Array<Record<string, unknown>>;
  releaseRun: () => void;
  releaseSubmit: () => void;
  releaseRecovery: () => void;
}

test('V2 composer delegates the whole turn to AgentRun and renders the server-owned reply', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page);
  await page.goto('/projects/7/workshop');

  await expect(page.getByTestId('chat.agentRun.boundary')).toBeVisible();
  await expect(page.getByTestId('chat.documentSource.open')).toHaveCount(0);
  await page.getByTestId('chat.composer.input').fill('Help me shape a calm login flow.');
  await page.getByTestId('chat.command.send').click();

  await expect.poll(() => state.submitRequests).toBe(1);
  await expect(page.getByTestId('chat.agentRun.status')).toContainText(/Business Analyst (queued|working)/);
  await expect(page.getByTestId('chat.message.assistant')).toContainText('Start with the login outcome and the people who need it.');
  expect(state.directMessageWrites).toBe(0);
  expect(state.legacyCompletions).toBe(0);
  expect(state.submitOperationIds).toHaveLength(1);

  await expect(page.getByTestId('chat.sessions.boundReason')).toContainText('bound to this governed conversation');
  await expect(page.getByTestId('chat.sessions.new')).toBeDisabled();
});

test('reload recovers and polls the active AgentRun without submitting a duplicate turn', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { initialSession: true, initialActiveRun: true, holdRunning: true });
  await page.goto('/projects/7/workshop/sessions/9007');

  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst working');
  await page.reload();
  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst working');
  state.releaseRun();
  await expect(page.getByTestId('chat.message.assistant')).toContainText('Start with the login outcome and the people who need it.');
  expect(state.submitRequests).toBe(0);
  expect(state.directMessageWrites).toBe(0);
  expect(state.legacyCompletions).toBe(0);
});

test('a deep link restores the conversation bound by the Workbench session', async ({ page }) => {
  await mockAgentRunWorkspace(page, {
    initialSession: true,
    initialActiveRun: true,
    holdRunning: true,
    multipleSessions: true
  });
  await page.goto('/projects/7/workshop/sessions/9008');

  await expect(page).toHaveURL(/\/sessions\/9007$/);
  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst working');
});

test('terminal recovery refreshes history when materialization lands between the reads', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    initialSession: true,
    terminalAppearsDuringRecovery: true
  });
  await page.goto('/projects/7/workshop/sessions/9007');

  await expect(page.getByTestId('chat.message.assistant')).toContainText('Start with the login outcome and the people who need it.');
  expect(state.submitRequests).toBe(0);
});

test('an ambiguous submit reuses the same client operation id', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    initialSession: true,
    multipleSessions: true,
    submitTransportFailures: 1
  });
  await page.goto('/projects/7/workshop/sessions/9007');

  const prompt = 'Clarify the login recovery behavior.';
  await page.getByTestId('chat.composer.input').fill(prompt);
  await page.getByTestId('chat.command.send').click();
  await expect.poll(() => state.submitOperationIds.length).toBe(1);
  await expect(page.getByTestId('chat.composer.input')).toHaveValue(prompt);

  await expect(page.getByTestId('chat.sessions.new')).toBeDisabled();
  await expect(page.getByTestId('chat.sessions.item.9008')).toBeDisabled();
  await expect(page.getByTestId('chat.sessions.boundReason')).toContainText('Delivery is unresolved');

  // The server may have committed before the transport failed. Once recovery
  // shows that run as terminal, the unchanged replay is still required to
  // authoritatively resolve the retained receipt.
  await expect(page.getByTestId('chat.message.assistant')).toContainText('Start with the login outcome and the people who need it.');
  await expect(page.getByTestId('chat.sessions.new')).toBeDisabled();
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.message.assistant')).toContainText('Start with the login outcome and the people who need it.');
  expect(state.submitOperationIds).toHaveLength(2);
  expect(state.submitOperationIds[1]).toBe(state.submitOperationIds[0]);
  expect(state.directMessageWrites).toBe(0);
  await expect(page.getByTestId('chat.sessions.boundReason')).toContainText('bound to this governed conversation');
});

test('an ambiguous first-session create is fenced and replays its exact operation id', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { sessionTransportFailures: 1 });
  await page.goto('/projects/7/workshop');

  const prompt = 'Shape a durable first conversation.';
  await page.getByTestId('chat.composer.input').fill(prompt);
  await page.getByTestId('chat.command.send').click();
  await expect.poll(() => state.sessionOperationIds.length).toBe(1);
  await expect(page.getByTestId('chat.sessions.new')).toBeDisabled();
  await expect(page.getByTestId('chat.sessions.boundReason')).toContainText('Delivery is unresolved');
  await expect(page.getByTestId('chat.composer.input')).toHaveValue(prompt);

  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.message.assistant')).toContainText('Start with the login outcome and the people who need it.');
  expect(state.sessionOperationIds).toHaveLength(2);
  expect(state.sessionOperationIds[1]).toBe(state.sessionOperationIds[0]);
  expect(state.sessionWrites).toBe(2);
});

test('an active AgentRun can be cancelled with its own fenced operation', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { holdRunning: true });
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.composer.input').fill('Explore this idea until I stop the run.');
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.agentRun.cancel')).toBeVisible();
  await page.getByTestId('chat.agentRun.cancel').click();

  await expect.poll(() => state.cancelRequests).toBe(1);
  await expect(page.getByTestId('chat.sending')).toHaveCount(0);
  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst run cancelled');
  expect(state.history.filter((message) => message.role === 'assistant')).toHaveLength(0);

  await page.reload();
  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst run cancelled');
  await expect(page.getByTestId('chat.message.assistant')).toHaveCount(0);
});

test('a bounded terminal failure is visible and never invents an assistant reply', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { terminalStatus: 'Failed' });
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.composer.input').fill('Shape this without leaking provider diagnostics.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst run failed safely');
  await expect(page.getByTestId('chat.error')).toContainText('provider failure');
  await expect(page.getByTestId('chat.message.assistant')).toHaveCount(0);
  expect(state.history.filter((message) => message.role === 'assistant')).toHaveLength(0);

  await page.reload();
  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst run failed safely');
  await expect(page.getByTestId('chat.error')).toContainText('provider failure');
  await expect(page.getByTestId('chat.message.assistant')).toHaveCount(0);
});

test('provider or worker unavailability is reported before the composer can submit', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { unavailable: true });
  await page.goto('/projects/7/workshop');

  await expect(page.getByTestId('chat.command.send')).toBeDisabled();
  await expect(page.getByTestId('chat.command.send')).toHaveAttribute('title', /Business Analyst service is unavailable/);
  expect(state.submitRequests).toBe(0);
  expect(state.sessionWrites).toBe(0);
});

test('a deferred governed submission fences direct navigation and retains its bound conversation', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    initialSession: true,
    holdSubmit: true,
    holdRunning: true,
    multipleSessions: true
  });
  await page.goto('/projects/7/workshop/sessions/9007');

  await page.getByTestId('chat.composer.input').fill('Keep this turn attached to its original conversation.');
  await page.getByTestId('chat.command.send').click();
  await expect.poll(() => state.submitRequests).toBe(1);

  const otherSession = page.getByTestId('chat.sessions.item.9008');
  await expect(otherSession).toBeDisabled();
  await expect(page.getByTestId('chat.sessions.boundReason')).toContainText('being submitted or processed');

  // Even a synthetic click cannot move the active route while submission owns it.
  await otherSession.evaluate((button) => {
    button.removeAttribute('disabled');
    (button as HTMLButtonElement).click();
  });
  await expect(page).toHaveURL(/\/sessions\/9007$/);

  state.releaseSubmit();
  await expect(page).toHaveURL(/\/sessions\/9007$/);
  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst working');
});

test('an authoritative polling rejection stops instead of retrying forever', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { pollFailureStatus: 404 });
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.composer.input').fill('Stop polling if my access disappears.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.error')).toContainText('no longer accessible');
  await expect(page.getByTestId('chat.sending')).toHaveCount(0);
  const requestsAfterStop = state.pollRequests;
  await page.waitForTimeout(2_000);
  expect(state.pollRequests).toBe(requestsAfterStop);
});

test('a lease-fence polling rejection stops with the authoritative recovery guidance', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, { pollFailureStatus: 409 });
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.composer.input').fill('Stop when this lease is no longer current.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.error')).toContainText('write lease changed');
  const requestsAfterStop = state.pollRequests;
  await page.waitForTimeout(2_000);
  expect(state.pollRequests).toBe(requestsAfterStop);
});

test('transient polling throttling retries and a cancelled terminal state clears the warning', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    transientPollFailureStatuses: [429],
    terminalStatus: 'Cancelled'
  });
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.composer.input').fill('Keep checking after a temporary status throttle.');
  await page.getByTestId('chat.command.send').click();

  await expect(page.getByTestId('chat.agentRun.status')).toContainText('Business Analyst run cancelled');
  await expect(page.getByTestId('chat.error')).toHaveCount(0);
  expect(state.pollRequests).toBeGreaterThan(1);
});

test('a late submission response cannot cross into a newly selected project', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    holdSubmit: true,
    holdRunning: true,
    multipleProjects: true
  });
  await page.goto('/projects/7/workshop');

  await page.getByTestId('chat.composer.input').fill('This belongs only to project seven.');
  await page.getByTestId('chat.command.send').click();
  await expect.poll(() => state.submitRequests).toBe(1);

  await page.getByTestId('flow.projectSwitcher').click();
  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page).toHaveURL('/projects/8/workshop');
  await expect(page.getByTestId('chat.composer.input')).toHaveValue('');
  await expect(page.getByTestId('chat.error')).toHaveCount(0);

  state.releaseSubmit();
  await page.waitForTimeout(1_000);
  await expect(page).toHaveURL('/projects/8/workshop');
  await expect(page.getByTestId('chat.agentRun.status')).toHaveCount(0);
  await expect(page.getByTestId('chat.message.user')).toHaveCount(0);
  await expect(page.getByTestId('chat.composer.input')).toHaveValue('');
  await expect(page.getByTestId('chat.error')).toHaveCount(0);
});

test('a held recovery response cannot restore project-A state after switching projects', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    initialSession: true,
    initialActiveRun: true,
    holdRecovery: true,
    holdRunning: true,
    multipleProjects: true
  });
  await page.goto('/projects/7/workshop/sessions/9007');
  await expect.poll(() => state.recoveryRequests).toBe(1);

  await page.getByTestId('flow.projectSwitcher').click();
  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page).toHaveURL('/projects/8/workshop');

  state.releaseRecovery();
  await page.waitForTimeout(500);
  await expect(page.getByTestId('chat.agentRun.status')).toHaveCount(0);
  await expect(page.getByTestId('chat.message.user')).toHaveCount(0);
  await expect(page.getByTestId('chat.composer.input')).toHaveValue('');
  await expect(page.getByTestId('chat.error')).toHaveCount(0);
});

test('a late project-A success cannot erase project-B ambiguous-submit identity', async ({ page }) => {
  const state = await mockAgentRunWorkspace(page, {
    holdSubmit: true,
    holdRunning: true,
    multipleProjects: true
  });
  const projectBRunId = '88888888-2222-3333-4444-555555555555';
  const projectBOperationIds: string[] = [];
  const projectBHistory: Array<Record<string, unknown>> = [];
  let projectBSessionExists = false;
  let projectBSubmitFailures = 1;

  await page.route('**/irondev-api/api/projects/8/chat/sessions', (route) => {
    if (route.request().method() === 'GET') {
      return json(route, projectBSessionExists
        ? [{ id: 9808, tenantId: 3, projectId: 8, title: 'Project B conversation', summary: 'Governed conversation' }]
        : []);
    }
    projectBSessionExists = true;
    return json(route, 9808);
  });
  await page.route('**/irondev-api/api/projects/8/chat/sessions/9808', (route) =>
    json(route, { id: 9808, tenantId: 3, projectId: 8, title: 'Project B conversation', summary: 'Governed conversation' }));
  await page.route('**/irondev-api/api/projects/8/chat/sessions/9808/messages', (route) =>
    json(route, projectBHistory));
  await page.route('**/irondev-api/api/projects/8/chat/sessions/9808/messages/*/audit', (route) =>
    json(route, { error: 'no_audit' }, 404));
  await page.route('**/irondev-api/api/workbench/projects/8/agent-runs**', (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'GET' && url.pathname.endsWith('/current')) {
      return json(route, {
        submissionAvailable: true,
        unavailableCategory: null,
        boundChatSessionId: null,
        activeRun: null,
        latestRun: null
      });
    }
    if (request.method() === 'POST' && url.pathname.endsWith('/agent-runs')) {
      const body = request.postDataJSON() as { clientOperationId: string; message: string };
      projectBOperationIds.push(body.clientOperationId);
      if (projectBSubmitFailures > 0) {
        projectBSubmitFailures -= 1;
        return route.abort('failed');
      }
      projectBHistory.push({
        id: 9811,
        tenantId: 3,
        projectId: 8,
        chatSessionId: 9808,
        role: 'user',
        message: body.message,
        createdDate: '2026-07-20T02:00:00Z'
      });
      return json(route, {
        agentRunId: projectBRunId,
        projectId: 8,
        workbenchSessionId: 8008,
        leaseEpoch: 1,
        chatSessionId: 9808,
        userMessageId: 9811,
        status: 'Pending',
        clientOperationId: body.clientOperationId,
        createdAtUtc: '2026-07-20T02:00:00Z',
        isReplay: false
      }, 202);
    }
    if (request.method() === 'GET' && url.pathname.endsWith(`/${projectBRunId}`)) {
      if (!projectBHistory.some((message) => message.role === 'assistant')) {
        projectBHistory.push({
          id: 9812,
          tenantId: 3,
          projectId: 8,
          chatSessionId: 9808,
          role: 'assistant',
          message: 'Project B stayed fenced to its own operation.',
          createdDate: '2026-07-20T02:00:02Z'
        });
      }
      return json(route, {
        agentRunId: projectBRunId,
        tenantId: 3,
        projectId: 8,
        workbenchSessionId: 8008,
        leaseEpoch: 1,
        actorUserId: 7,
        chatSessionId: 9808,
        sourceUserMessageId: 9811,
        status: 'Completed',
        attemptCount: 1,
        assistantMessageId: 9812,
        createdAtUtc: '2026-07-20T02:00:00Z',
        startedAtUtc: '2026-07-20T02:00:01Z',
        completedAtUtc: '2026-07-20T02:00:02Z',
        cancellationRequestedAtUtc: null,
        failureCategory: null,
        retryable: false
      });
    }
    return json(route, { error: 'unexpected_project_b_agent_request' }, 500);
  });

  await page.goto('/projects/7/workshop');
  await page.getByTestId('chat.composer.input').fill('Delayed project A turn.');
  await page.getByTestId('chat.command.send').click();
  await expect.poll(() => state.submitRequests).toBe(1);

  await page.getByTestId('flow.projectSwitcher').click();
  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page).toHaveURL('/projects/8/workshop');

  const projectBPrompt = 'Ambiguous project B turn.';
  await page.getByTestId('chat.composer.input').fill(projectBPrompt);
  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.error')).toContainText('Delivery could not be confirmed');
  await expect(page.getByTestId('chat.composer.input')).toHaveValue(projectBPrompt);
  expect(projectBOperationIds).toHaveLength(1);

  state.releaseSubmit();
  await page.waitForTimeout(500);
  await expect(page).toHaveURL(/\/projects\/8\/workshop\/sessions\/9808$/);
  await expect(page.getByTestId('chat.composer.input')).toHaveValue(projectBPrompt);

  await page.getByTestId('chat.command.send').click();
  await expect(page.getByTestId('chat.message.assistant')).toContainText('Project B stayed fenced to its own operation.');
  expect(projectBOperationIds).toHaveLength(2);
  expect(projectBOperationIds[1]).toBe(projectBOperationIds[0]);
});

async function mockAgentRunWorkspace(page: Page, options: AgentRunMockOptions = {}): Promise<AgentRunMockState> {
  const agentRunId = '11111111-2222-3333-4444-555555555555';
  let holdRunning = Boolean(options.holdRunning);
  let releaseSubmit = () => {};
  const submitGate = options.holdSubmit
    ? new Promise<void>((resolve) => {
        releaseSubmit = resolve;
      })
    : Promise.resolve();
  let releaseRecovery = () => {};
  const recoveryGate = options.holdRecovery
    ? new Promise<void>((resolve) => {
        releaseRecovery = resolve;
      })
    : Promise.resolve();
  const state: AgentRunMockState = {
    submitRequests: 0,
    submitOperationIds: [],
    sessionWrites: 0,
    sessionOperationIds: [],
    directMessageWrites: 0,
    legacyCompletions: 0,
    cancelRequests: 0,
    pollRequests: 0,
    recoveryRequests: 0,
    history: [],
    releaseRun: () => {
      holdRunning = false;
      pollCount = 0;
    },
    releaseSubmit: () => releaseSubmit(),
    releaseRecovery: () => releaseRecovery()
  };
  let sessionExists = Boolean(options.initialSession || options.initialActiveRun || options.terminalAppearsDuringRecovery);
  let sessionTitle = 'Governed conversation';
  let boundChatSessionId: number | null = options.initialActiveRun || options.terminalAppearsDuringRecovery ? 9007 : null;
  let activeRun = Boolean(options.initialActiveRun);
  let currentStatus: AgentRunStatus = options.initialActiveRun
    ? 'Running'
    : options.terminalAppearsDuringRecovery
      ? 'Completed'
      : 'Pending';
  let pollCount = 0;
  let sessionTransportFailures = options.sessionTransportFailures ?? 0;
  let submitTransportFailures = options.submitTransportFailures ?? 0;
  let committedSubmitOperationId: string | null = null;
  const transientPollFailureStatuses = [...(options.transientPollFailureStatuses ?? [])];
  let assistantPersisted = false;

  if (options.initialActiveRun || options.terminalAppearsDuringRecovery) {
    state.history.push(chatMessage(9101, 'user', 'Help me shape a calm login flow.'));
  }

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
    capabilities: ['WorkbenchAgentRuns']
  }));
  await page.route('**/irondev-api/api/environment', (route) => json(route, {
    environment: 'LocalTest',
    database: 'IronDeveloper_Test',
    isTestEnvironment: true,
    workbench: {
      version: '0.1.0-preview.7',
      mode: 'V2',
      v2Enabled: true,
      v1FallbackEnabled: true,
      conversationAuthorityEnabled: true,
      previewId: 'workbench-pr02c-a',
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
    { id: 7, tenantId: 3, name: 'BookSeller', localPath: null },
    ...(options.multipleProjects
      ? [{ id: 8, tenantId: 3, name: 'Second project', localPath: null, lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured' }]
      : [])
  ]));
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) => json(route, {
    projectId: 7,
    tenantId: 3,
    name: 'BookSeller',
    projectLifecyclePhase: 'Shaping',
    executionReadiness: 'NotConfigured',
    repositoryBinding: null,
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    wasResumed: true,
    wasTakenOver: false,
    clientOperationId: '00000000-0000-0000-0000-000000000007'
  }));
  await page.route('**/irondev-api/api/workbench/projects/8/open', (route) => json(route, {
    projectId: 8,
    tenantId: 3,
    name: 'Second project',
    projectLifecyclePhase: 'Shaping',
    executionReadiness: 'NotConfigured',
    repositoryBinding: null,
    workbenchSessionId: 8008,
    leaseEpoch: 1,
    wasResumed: true,
    wasTakenOver: false,
    clientOperationId: '00000000-0000-0000-0000-000000000008'
  }));
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
  await page.route('**/irondev-api/api/projects/8/channels', (route) => json(route, {
    projectId: 8,
    canCreateChannels: true,
    channels: []
  }));
  await page.route('**/irondev-api/api/projects/8/notifications**', (route) => json(route, {
    projectId: 8,
    unreadCount: 0,
    notifications: []
  }));
  await page.route('**/irondev-api/api/projects/8/chat/sessions', (route) => json(route, []));
  await page.route('**/irondev-api/api/workbench/projects/8/agent-runs/current**', (route) => json(route, {
    submissionAvailable: true,
    unavailableCategory: null,
    boundChatSessionId: null,
    activeRun: null,
    latestRun: null
  }));

  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => {
    if (route.request().method() === 'GET') {
      const savedSessions = sessionExists
        ? [{ id: 9007, tenantId: 3, projectId: 7, title: sessionTitle, summary: 'Governed conversation' }]
        : [];
      if (options.multipleSessions) {
        savedSessions.push({ id: 9008, tenantId: 3, projectId: 7, title: 'Other conversation', summary: 'Previous conversation' });
      }
      return json(route, savedSessions);
    }
    const request = route.request().postDataJSON() as { title?: string; clientOperationId: string };
    state.sessionWrites += 1;
    state.sessionOperationIds.push(request.clientOperationId);
    sessionExists = true;
    sessionTitle = request.title ?? sessionTitle;
    if (sessionTransportFailures > 0) {
      sessionTransportFailures -= 1;
      return route.abort('failed');
    }
    return json(route, 9007);
  });
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007', (route) =>
    sessionExists
      ? json(route, { id: 9007, tenantId: 3, projectId: 7, title: sessionTitle, summary: 'Governed conversation' })
      : json(route, { error: 'not_found' }, 404));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages', (route) => {
    if (route.request().method() === 'POST') {
      state.directMessageWrites += 1;
      return json(route, { error: 'workbench_conversation_authority_required' }, 409);
    }
    return json(route, state.history);
  });
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9007/messages/*/audit', (route) =>
    json(route, { error: 'no_audit' }, 404));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9008', (route) =>
    json(route, { id: 9008, tenantId: 3, projectId: 7, title: 'Other conversation', summary: 'Previous conversation' }));
  await page.route('**/irondev-api/api/projects/7/chat/sessions/9008/messages', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/chat/complete', (route) => {
    state.legacyCompletions += 1;
    return json(route, { error: 'workbench_conversation_authority_required' }, 409);
  });

  await page.route('**/irondev-api/api/workbench/projects/7/agent-runs**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (request.method() === 'GET' && url.pathname.endsWith('/current')) {
      state.recoveryRequests += 1;
      await recoveryGate;
      const requestedChatSessionId = url.searchParams.get('chatSessionId');
      if (boundChatSessionId && requestedChatSessionId && Number(requestedChatSessionId) !== boundChatSessionId) {
        return json(route, { error: 'workbench_chat_session_mismatch' }, 409);
      }
      if (options.terminalAppearsDuringRecovery && !assistantPersisted) {
        assistantPersisted = true;
        state.history.push(chatMessage(9102, 'assistant', 'Start with the login outcome and the people who need it.'));
      }
      return json(route, {
        submissionAvailable: !options.unavailable,
        unavailableCategory: options.unavailable ? 'service_unavailable' : null,
        boundChatSessionId,
        activeRun: activeRun ? snapshot(currentStatus) : null,
        latestRun: boundChatSessionId ? snapshot(currentStatus) : null
      });
    }

    if (request.method() === 'POST' && url.pathname.endsWith('/cancel')) {
      state.cancelRequests += 1;
      currentStatus = 'Cancelled';
      activeRun = false;
      return json(route, {
        agentRunId,
        status: currentStatus,
        cancellationRequested: true,
        clientOperationId: (request.postDataJSON() as { clientOperationId: string }).clientOperationId,
        isReplay: false
      });
    }

    if (request.method() === 'GET' && url.pathname.endsWith(`/${agentRunId}`)) {
      state.pollRequests += 1;
      const transientStatus = transientPollFailureStatuses.shift();
      if (transientStatus) {
        return json(route, { error: 'temporarily_unavailable' }, transientStatus);
      }
      if (options.pollFailureStatus) {
        return json(route, {
          error: options.pollFailureStatus === 409
            ? 'workbench_lease_fence_rejected'
            : 'agent_run_not_found'
        }, options.pollFailureStatus);
      }
      if (currentStatus !== 'Cancelled') {
        if (holdRunning) {
          currentStatus = 'Running';
        } else {
          const terminalStatus = options.terminalStatus ?? 'Completed';
          const sequence: AgentRunStatus[] = options.initialActiveRun
            ? ['Running', terminalStatus]
            : ['Pending', 'Running', terminalStatus];
          currentStatus = sequence[Math.min(pollCount, sequence.length - 1)];
          pollCount += 1;
        }
      }
      if (isTerminal(currentStatus)) {
        activeRun = false;
        if ((currentStatus === 'Completed' || currentStatus === 'NeedsInput') && !assistantPersisted) {
          assistantPersisted = true;
          state.history.push(chatMessage(9102, 'assistant', 'Start with the login outcome and the people who need it.'));
        }
      }
      return json(route, snapshot(currentStatus));
    }

    if (request.method() === 'POST' && url.pathname.endsWith('/agent-runs')) {
      state.submitRequests += 1;
      const body = request.postDataJSON() as {
        clientOperationId: string;
        message: string;
        chatSessionId: number;
      };
      state.submitOperationIds.push(body.clientOperationId);
      await submitGate;
      if (submitTransportFailures > 0) {
        submitTransportFailures -= 1;
        committedSubmitOperationId = body.clientOperationId;
        boundChatSessionId = body.chatSessionId;
        activeRun = true;
        currentStatus = 'Pending';
        pollCount = 0;
        if (!state.history.some((message) => message.id === 9101)) {
          state.history.push(chatMessage(9101, 'user', body.message));
        }
        return route.abort('failed');
      }
      if (options.unavailable) {
        return json(route, {
          error: 'workbench_agent_run_unavailable',
          failureCategory: 'service_unavailable',
          retryable: false
        }, 503);
      }
      if (activeRun && body.clientOperationId !== committedSubmitOperationId) {
        return json(route, { error: 'workbench_agent_run_active', agentRunId }, 409);
      }

      if (body.clientOperationId !== committedSubmitOperationId) {
        boundChatSessionId = body.chatSessionId;
        activeRun = true;
        currentStatus = 'Pending';
        pollCount = 0;
        if (!state.history.some((message) => message.id === 9101)) {
          state.history.push(chatMessage(9101, 'user', body.message));
        }
      }
      return json(route, {
        agentRunId,
        projectId: 7,
        workbenchSessionId: 7007,
        leaseEpoch: 1,
        chatSessionId: 9007,
        userMessageId: 9101,
        status: 'Pending',
        clientOperationId: body.clientOperationId,
        createdAtUtc: '2026-07-20T02:00:00Z',
        isReplay: false
      }, 202);
    }

    return json(route, { error: 'unexpected_agent_run_request' }, 500);
  });

  return state;

  function snapshot(status: AgentRunStatus) {
    return {
      agentRunId,
      tenantId: 3,
      projectId: 7,
      workbenchSessionId: 7007,
      leaseEpoch: 1,
      actorUserId: 7,
      chatSessionId: 9007,
      sourceUserMessageId: 9101,
      status,
      attemptCount: status === 'Pending' ? 0 : 1,
      assistantMessageId: status === 'Completed' || status === 'NeedsInput' ? 9102 : null,
      createdAtUtc: '2026-07-20T02:00:00Z',
      startedAtUtc: status === 'Pending' ? null : '2026-07-20T02:00:01Z',
      completedAtUtc: isTerminal(status) ? '2026-07-20T02:00:02Z' : null,
      cancellationRequestedAtUtc: status === 'Cancelled' ? '2026-07-20T02:00:02Z' : null,
      failureCategory: status === 'Failed' ? 'provider_failure' : null,
      retryable: false
    };
  }
}

function chatMessage(id: number, role: 'user' | 'assistant', message: string) {
  return {
    id,
    tenantId: 3,
    projectId: 7,
    chatSessionId: 9007,
    role,
    message,
    createdDate: `2026-07-20T02:00:0${role === 'user' ? '0' : '2'}Z`
  };
}

function isTerminal(status: AgentRunStatus) {
  return status !== 'Pending' && status !== 'Running';
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
