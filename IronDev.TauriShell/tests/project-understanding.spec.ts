import { expect, test, type Page, type Route } from '@playwright/test';

const conflictId = '11111111-2222-4333-8444-555555555555';
const secondConflictId = '22222222-3333-4444-8555-666666666666';
const proposalId = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const agentRunId = '99999999-8888-4777-8666-555555555555';

test('V2 Project context presents durable facts, Unknown fields, conflicts, provenance, and read-only operational truth', async ({ page }) => {
  await mockProjectUnderstandingWorkspace(page);
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await expect(page.getByTestId('chat.projectUnderstanding.summary')).toContainText('BookSeller');
  await expect(page.getByTestId('chat.projectUnderstanding.group.confirmed')).toContainText('Product summary');
  await expect(page.getByTestId('chat.projectUnderstanding.fact.productsummary')).toContainText(
    'Captured by the Business Analyst from explicit user evidence'
  );
  await expect(page.getByTestId('chat.projectUnderstanding.group.inferred')).toContainText('Primary users');
  await expect(page.getByTestId('chat.projectUnderstanding.group.conflicted')).toContainText('Application type');
  const unknownGroup = page.getByTestId('chat.projectUnderstanding.group.unknown');
  await expect(unknownGroup.locator('.project-understanding-fact')).toHaveCount(8);
  await expect(unknownGroup.getByTestId('chat.projectUnderstanding.fact.goals.add')).toHaveText('Add confirmed value');

  const conflict = page.getByTestId(`chat.projectUnderstanding.conflict.${conflictId}`);
  await expect(conflict).toContainText('Web application');
  await expect(conflict).toContainText('Desktop application');
  await expect(conflict.getByRole('button', { name: 'Keep current value' })).toBeVisible();
  await expect(conflict.getByRole('button', { name: 'Use proposed value' })).toBeVisible();

  const operational = page.getByTestId('chat.projectUnderstanding.operational');
  await expect(operational.getByTestId('chat.projectUnderstanding.operational.lifecycle')).toContainText('Shaping');
  await expect(operational.getByTestId('chat.projectUnderstanding.operational.readiness')).toContainText('Not configured');
  await expect(operational.getByTestId('chat.projectUnderstanding.operational.repository')).toContainText('Not configured');
  await expect(operational.getByRole('button')).toHaveCount(0);

  const productFact = page.getByTestId('chat.projectUnderstanding.fact.productsummary');
  await productFact.getByRole('button', { name: 'Message #9101' }).click();
  await expect(page.locator('[data-message-id="user-9101"]')).toBeFocused();
});

test('Project context distinguishes a setup-confirmed repository authority', async ({ page }) => {
  await mockProjectUnderstandingWorkspace(page, { repositoryBindingState: 'SetupConfirmed' });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await expect(page.getByTestId('chat.projectUnderstanding.operational.repository')).toContainText('Setup confirmed');
});

test('Project context never presents a legacy-unverified binding as configured', async ({ page }) => {
  await mockProjectUnderstandingWorkspace(page, { repositoryBindingState: 'LegacyUnverified' });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await expect(page.getByTestId('chat.projectUnderstanding.operational.repository')).toContainText('Legacy unverified');
  await expect(page.getByTestId('chat.projectUnderstanding.operational.repository')).not.toContainText('Configured');
});

test('Project context rejects a malformed repository-binding projection', async ({ page }) => {
  await mockProjectUnderstandingWorkspace(page, { malformedRepositoryBinding: true });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await expect(page.getByTestId('chat.projectUnderstanding.error')).toContainText('Workshop conversation remains available');
  await expect(page.getByTestId('chat.projectUnderstanding.operational')).toHaveCount(0);
});

test('an empty typed understanding exposes all eleven normative fields and accepts a new confirmed value', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page, { emptyFacts: true });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  const unknownGroup = page.getByTestId('chat.projectUnderstanding.group.unknown');
  await expect(unknownGroup.locator('.project-understanding-fact')).toHaveCount(11);
  await page.getByTestId('chat.projectUnderstanding.fact.productsummary.add').click();
  await page.getByTestId('chat.projectUnderstanding.fact.productsummary.input').fill('A calm catalogue for independent booksellers.');
  await page.getByTestId('chat.projectUnderstanding.fact.productsummary.save').click();

  await expect(page.getByTestId('chat.projectUnderstanding.group.confirmed')).toContainText('A calm catalogue for independent booksellers.');
  expect(state.factBodies).toHaveLength(1);
  expect(state.factBodies[0]).toMatchObject({
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    expectedUnderstandingRevision: 3,
    action: 'Edit',
    value: 'A calm catalogue for independent booksellers.',
  });
  expect(state.factBodies[0]).not.toHaveProperty('userLocked');
});

test('Project context load failure stays isolated from Workshop and retries independently', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page, { holdUnderstandingReadFailure: true });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await expect(page.getByTestId('chat.projectUnderstanding.error')).toContainText('Workshop conversation remains available');
  await expect(page.getByTestId('chat.composer.input')).toBeEnabled();
  state.allowProject7UnderstandingReads();
  await page.getByTestId('chat.projectUnderstanding.retryLoad').click();
  await expect(page.getByTestId('chat.projectUnderstanding.summary')).toContainText('BookSeller');
  expect(state.understandingReads[7]).toBeGreaterThanOrEqual(2);
});

test('an ambiguous fact mutation retains and exactly replays its fenced operation receipt', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page, { ambiguousFactWrite: true });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();
  await expect(page.getByTestId('chat.projectUnderstanding.fact.productsummary.lock')).toBeVisible();

  await page.getByTestId('chat.projectUnderstanding.fact.productsummary.lock').click();
  await expect(page.getByTestId('chat.projectUnderstanding.deliveryUnresolved')).toBeVisible();
  expect(state.factBodies).toHaveLength(1);
  expect(state.factBodies[0]).toMatchObject({
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    expectedUnderstandingRevision: 3,
    action: 'SetLock',
    userLocked: true
  });
  expect(state.factBodies[0]).not.toHaveProperty('value');
  expect(state.factBodies[0].clientOperationId).toMatch(/^[0-9a-f-]{36}$/i);

  await page.getByTestId('chat.projectUnderstanding.retryMutation').click();
  await expect(page.getByTestId('chat.projectUnderstanding.deliveryUnresolved')).toHaveCount(0);
  await expect(page.getByTestId('chat.projectUnderstanding.fact.productsummary')).toContainText('Locked by you');
  expect(state.factBodies).toHaveLength(2);
  expect(state.factBodies[1]).toEqual(state.factBodies[0]);
});

test('a malformed mutation success remains ambiguous and replays the exact retained operation', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page, { malformedFactSuccess: true });
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await page.getByTestId('chat.projectUnderstanding.fact.productsummary.lock').click();
  await expect(page.getByTestId('chat.projectUnderstanding.deliveryUnresolved')).toBeVisible();
  await expect(page.getByTestId('chat.projectUnderstanding.mutationError')).toContainText('could not be confirmed');
  expect(state.factBodies).toHaveLength(1);

  await page.getByTestId('chat.projectUnderstanding.retryMutation').click();
  await expect(page.getByTestId('chat.projectUnderstanding.deliveryUnresolved')).toHaveCount(0);
  await expect(page.getByTestId('chat.projectUnderstanding.fact.productsummary')).toContainText('Locked by you');
  expect(state.factBodies).toHaveLength(2);
  expect(state.factBodies[1]).toEqual(state.factBodies[0]);
});

test('locking preserves conflicts and resolving one named conflict rebases the remaining comparison', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page);
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();

  await page.getByTestId('chat.projectUnderstanding.fact.primaryusers.confirm').click();
  expect(state.factBodies[0]).toMatchObject({ action: 'Confirm', expectedUnderstandingRevision: 3 });
  expect(state.factBodies[0]).not.toHaveProperty('value');
  expect(state.factBodies[0]).not.toHaveProperty('userLocked');
  expect(state.factBodies[0]).not.toHaveProperty('conflictId');

  await page.getByTestId('chat.projectUnderstanding.fact.applicationtype.lock').click();
  await expect(page.getByTestId(`chat.projectUnderstanding.conflict.${conflictId}`)).toBeVisible();
  await expect(page.getByTestId(`chat.projectUnderstanding.conflict.${secondConflictId}`)).toBeVisible();
  expect(state.factBodies[1]).toMatchObject({ action: 'SetLock', userLocked: true, expectedUnderstandingRevision: 4 });
  expect(state.factBodies[1]).not.toHaveProperty('conflictId');
  expect(state.factBodies[1]).not.toHaveProperty('value');

  await page.getByTestId('chat.projectUnderstanding.fact.applicationtype.unlock').click();
  await expect(page.getByTestId(`chat.projectUnderstanding.conflict.${conflictId}`)).toBeVisible();
  await expect(page.getByTestId(`chat.projectUnderstanding.conflict.${secondConflictId}`)).toBeVisible();
  expect(state.factBodies[2]).toMatchObject({ action: 'SetLock', userLocked: false, expectedUnderstandingRevision: 5 });

  await page.getByTestId(`chat.projectUnderstanding.conflict.${conflictId}.useProposed`).click();
  expect(state.factBodies[3]).toMatchObject({
    action: 'ResolveConflict',
    conflictId,
    value: 'Desktop application',
    expectedUnderstandingRevision: 6
  });
  expect(state.factBodies[3]).not.toHaveProperty('userLocked');
  await expect(page.getByTestId(`chat.projectUnderstanding.conflict.${conflictId}`)).toHaveCount(0);
  const remaining = page.getByTestId(`chat.projectUnderstanding.conflict.${secondConflictId}`);
  await expect(remaining).toContainText('Desktop application');
  await expect(remaining).toContainText('Mobile application');
});

test('accepting a rename updates canonical shell identity without reopening the Workbench session', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page);
  await page.goto('/projects/7/workshop/sessions/9007');
  await page.getByTestId('chat.contextPanel.show').click();
  await expect(page.getByTestId('chat.projectUnderstanding.renameProposal')).toContainText('Catalog Studio');
  const openCountBeforeRename = state.openRequests[7];

  await page.getByTestId('chat.projectUnderstanding.acceptRename').click();

  await expect(page.getByTestId('flow.projectSwitcher')).toHaveText('Catalog Studio');
  await expect(page.getByText('Catalog Studio / Direct with Business Analyst')).toBeVisible();
  await expect(page).toHaveTitle('IronDev — Catalog Studio');
  await expect(page).toHaveURL(/\/projects\/7\/workshop\/sessions\/9007$/);
  await expect(page.getByTestId('chat.composer.input')).toBeEnabled();
  expect(state.renameBodies).toHaveLength(1);
  expect(state.renameBodies[0]).toMatchObject({ workbenchSessionId: 7007, leaseEpoch: 1 });
  expect(state.renameBodies[0]).not.toHaveProperty('expectedUnderstandingRevision');
  expect(state.openRequests[7]).toBe(openCountBeforeRename);
});

test('a delayed understanding response cannot leak across project authority', async ({ page }) => {
  const state = await mockProjectUnderstandingWorkspace(page, { multipleProjects: true, holdProject7Understanding: true });
  await page.goto('/projects/7/workshop/sessions/9007');
  await expect.poll(() => state.understandingReads[7]).toBeGreaterThan(0);

  await page.getByTestId('flow.projectSwitcher').click();
  await page.getByTestId('flow.chooser.project.8').click();
  await expect(page).toHaveURL(/\/projects\/8\/workshop/);
  await page.getByTestId('chat.contextPanel.show').click();
  await expect(page.getByTestId('chat.projectUnderstanding.summary')).toContainText('Warehouse Planner');

  state.releaseProject7Understanding();
  await page.waitForTimeout(150);
  await expect(page.getByTestId('chat.projectUnderstanding.summary')).toContainText('Warehouse Planner');
  await expect(page.getByTestId('chat.projectContext')).not.toContainText('BookSeller');
  await expect(page.getByTestId('flow.projectSwitcher')).toHaveText('Warehouse Planner');
});

interface UnderstandingMockOptions {
  ambiguousFactWrite?: boolean;
  emptyFacts?: boolean;
  holdProject7Understanding?: boolean;
  holdUnderstandingReadFailure?: boolean;
  malformedFactSuccess?: boolean;
  malformedRepositoryBinding?: boolean;
  multipleProjects?: boolean;
  repositoryBindingState?: 'SetupConfirmed' | 'LegacyUnverified';
}

interface UnderstandingMockState {
  factBodies: Array<Record<string, unknown>>;
  renameBodies: Array<Record<string, unknown>>;
  understandingReads: Record<number, number>;
  openRequests: Record<number, number>;
  allowProject7UnderstandingReads: () => void;
  releaseProject7Understanding: () => void;
}

async function mockProjectUnderstandingWorkspace(
  page: Page,
  options: UnderstandingMockOptions = {}
): Promise<UnderstandingMockState> {
  let releaseProject7Understanding = () => {};
  const project7UnderstandingGate = options.holdProject7Understanding
    ? new Promise<void>((resolve) => { releaseProject7Understanding = resolve; })
    : Promise.resolve();
  let allowProject7UnderstandingReads = !options.holdUnderstandingReadFailure;
  let ambiguousFactWrite = Boolean(options.ambiguousFactWrite);
  let malformedFactSuccess = Boolean(options.malformedFactSuccess);
  const factReceipts = new Map<string, { body: Record<string, unknown>; snapshot: UnderstandingFixture }>();
  const state: UnderstandingMockState = {
    factBodies: [],
    renameBodies: [],
    understandingReads: { 7: 0, 8: 0 },
    openRequests: { 7: 0, 8: 0 },
    allowProject7UnderstandingReads: () => { allowProject7UnderstandingReads = true; },
    releaseProject7Understanding: () => releaseProject7Understanding()
  };
  const models: Record<number, UnderstandingFixture> = {
    7: understandingFixture(7, 'BookSeller', Boolean(options.emptyFacts)),
    8: understandingFixture(8, 'Warehouse Planner', false)
  };
  const project7RepositoryBinding = options.repositoryBindingState
    ? repositoryBindingFixture(7, options.repositoryBindingState)
    : null;
  models[7].operationalProjections.repositoryBinding = project7RepositoryBinding;
  if (options.malformedRepositoryBinding) {
    models[7].operationalProjections.repositoryBinding = {};
  }
  models[8].facts[0] = {
    ...models[8].facts[0],
    value: 'A warehouse planning tool scoped only to project eight.',
    evidenceSummary: 'Captured in the project eight conversation.'
  };

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
    capabilities: ['WorkbenchAgentRuns', 'ProjectUnderstanding']
  }));
  await page.route('**/irondev-api/api/environment', (route) => json(route, {
    environment: 'LocalTest',
    database: 'IronDeveloper_Test',
    isTestEnvironment: true,
    workbench: {
      version: '0.1.0-preview.8',
      mode: 'V2',
      v2Enabled: true,
      v1FallbackEnabled: true,
      conversationAuthorityEnabled: true,
      previewId: 'workbench-pr02c-b',
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
    { id: 7, tenantId: 3, name: 'BookSeller', localPath: null, lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured' },
    ...(options.multipleProjects
      ? [{ id: 8, tenantId: 3, name: 'Second project', localPath: null, lifecyclePhase: 'Shaping', executionReadiness: 'NotConfigured' }]
      : [])
  ]));

  await page.route('**/irondev-api/api/workbench/projects/*/open', (route) => {
    const projectId = projectIdFrom(route.request().url());
    const request = route.request().postDataJSON() as { clientOperationId: string };
    state.openRequests[projectId] += 1;
    return json(route, {
      projectId,
      tenantId: 3,
      name: projectId === 7 ? 'BookSeller' : 'Second project',
      projectLifecyclePhase: 'Shaping',
      executionReadiness: 'NotConfigured',
      repositoryBinding: projectId === 7 ? project7RepositoryBinding : null,
      workbenchSessionId: projectId === 7 ? 7007 : 8008,
      leaseEpoch: 1,
      wasResumed: true,
      wasTakenOver: false,
      clientOperationId: request.clientOperationId
    });
  });

  for (const projectId of [7, 8]) {
    const sessionId = projectId === 7 ? 9007 : 9808;
    const hasSession = projectId === 7;
    await page.route(`**/irondev-api/api/projects/${projectId}/channels`, (route) => json(route, {
      projectId,
      canCreateChannels: true,
      channels: []
    }));
    await page.route(`**/irondev-api/api/projects/${projectId}/notifications**`, (route) => json(route, {
      projectId,
      unreadCount: 0,
      notifications: []
    }));
    await page.route(`**/irondev-api/api/projects/${projectId}/tickets**`, (route) => json(route, []));
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/sessions`, (route) => json(route, hasSession
      ? [{ id: sessionId, tenantId: 3, projectId, title: 'Governed conversation', summary: 'Project shaping' }]
      : []));
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/sessions/${sessionId}`, (route) => json(route, {
      id: sessionId,
      tenantId: 3,
      projectId,
      title: 'Governed conversation',
      summary: 'Project shaping'
    }));
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/sessions/${sessionId}/messages`, (route) => json(route, projectId === 7
      ? [{
          id: 9101,
          tenantId: 3,
          projectId: 7,
          chatSessionId: 9007,
          role: 'user',
          message: 'Independent booksellers need a calmer catalogue.',
          createdDate: '2026-07-20T01:00:00Z'
        }]
      : []));
    await page.route(`**/irondev-api/api/projects/${projectId}/chat/sessions/${sessionId}/messages/*/audit`, (route) =>
      json(route, { error: 'no_audit' }, 404));
    await page.route(`**/irondev-api/api/workbench/projects/${projectId}/agent-runs/current**`, (route) => json(route, {
      submissionAvailable: true,
      unavailableCategory: null,
      boundChatSessionId: hasSession ? sessionId : null,
      activeRun: null,
      latestRun: null
    }));
  }

  await page.route('**/irondev-api/api/workbench/projects/*/understanding', async (route) => {
    const projectId = projectIdFrom(route.request().url());
    state.understandingReads[projectId] += 1;
    if (projectId === 7 && options.holdProject7Understanding) {
      await project7UnderstandingGate;
    }
    if (projectId === 7 && !allowProject7UnderstandingReads) {
      return route.fulfill({ status: 503, contentType: 'text/plain', body: 'Temporary read failure.' });
    }
    return json(route, models[projectId]);
  });

  await page.route('**/irondev-api/api/workbench/projects/*/understanding/facts/*', (route) => {
    const projectId = projectIdFrom(route.request().url());
    const factKey = decodeURIComponent(new URL(route.request().url()).pathname.split('/').at(-1) ?? '');
    const body = route.request().postDataJSON() as Record<string, unknown> & { clientOperationId: string };
    state.factBodies.push({ ...body });
    const existing = factReceipts.get(body.clientOperationId);
    if (existing) {
      if (JSON.stringify(existing.body) !== JSON.stringify(body)) {
        return json(route, { error: 'operation_id_payload_mismatch' }, 409);
      }
      return json(route, { snapshot: existing.snapshot, clientOperationId: body.clientOperationId, isReplay: true });
    }

    const snapshot = clone(models[projectId]);
    snapshot.revision += 1;
    const fact = snapshot.facts.find((candidate) => candidate.key === factKey);
    if (!fact && body.action !== 'Edit') {
      return json(route, { error: 'project_understanding_invalid' }, 400);
    }

    let nextFact: UnderstandingFixture['facts'][number];
    if (body.action === 'Edit') {
      nextFact = {
        ...(fact ?? { key: factKey, sourceMessageIds: [] }),
        value: String(body.value),
        state: 'Confirmed',
        userLocked: Boolean(fact?.userLocked),
        authorKind: 'Actor',
        authorActorUserId: 7,
        authorAgentRunId: null,
        evidenceSummary: 'Confirmed in the project-understanding panel.',
        revision: snapshot.revision
      };
    } else if (body.action === 'Confirm') {
      nextFact = {
        ...fact!,
        state: 'Confirmed',
        authorKind: 'Actor',
        authorActorUserId: 7,
        authorAgentRunId: null,
        evidenceSummary: 'Confirmed in the project-understanding panel.',
        revision: snapshot.revision
      };
    } else if (body.action === 'SetLock') {
      nextFact = { ...fact!, userLocked: Boolean(body.userLocked), revision: snapshot.revision };
    } else if (body.action === 'ResolveConflict') {
      const selectedConflictId = String(body.conflictId);
      snapshot.conflicts = snapshot.conflicts.map((conflict) => conflict.conflictId === selectedConflictId && conflict.status === 'Open'
        ? { ...conflict, status: 'Resolved', resolvedAtRevision: snapshot.revision, resolvedByActorUserId: 7 }
        : conflict);
      const hasRemainingConflict = snapshot.conflicts.some((conflict) =>
        conflict.factKey === factKey && conflict.status === 'Open');
      nextFact = {
        ...fact!,
        value: String(body.value),
        state: hasRemainingConflict ? 'Conflicted' : 'Confirmed',
        authorKind: 'Actor',
        authorActorUserId: 7,
        authorAgentRunId: null,
        evidenceSummary: 'Resolved in the project-understanding panel.',
        revision: snapshot.revision
      };
    } else {
      return json(route, { error: 'project_understanding_invalid' }, 400);
    }
    snapshot.facts = fact
      ? snapshot.facts.map((candidate) => candidate.key === factKey ? nextFact : candidate)
      : [...snapshot.facts, nextFact];
    models[projectId] = snapshot;
    factReceipts.set(body.clientOperationId, { body: { ...body }, snapshot: clone(snapshot) });
    if (malformedFactSuccess) {
      malformedFactSuccess = false;
      const malformedSnapshot = clone(snapshot);
      malformedSnapshot.facts[0] = { ...malformedSnapshot.facts[0], state: 'InventedByProxy' };
      return json(route, { snapshot: malformedSnapshot, clientOperationId: body.clientOperationId, isReplay: false });
    }
    if (ambiguousFactWrite) {
      ambiguousFactWrite = false;
      return route.fulfill({ status: 504, contentType: 'text/plain', body: 'Gateway failed after commit.' });
    }
    return json(route, { snapshot, clientOperationId: body.clientOperationId, isReplay: false });
  });

  await page.route('**/irondev-api/api/workbench/projects/*/rename-proposals/*/accept', (route) => {
    const projectId = projectIdFrom(route.request().url());
    const body = route.request().postDataJSON() as Record<string, unknown> & { clientOperationId: string };
    state.renameBodies.push({ ...body });
    const snapshot = clone(models[projectId]);
    snapshot.projectName = 'Catalog Studio';
    snapshot.pendingRenameProposal = null;
    models[projectId] = snapshot;
    return json(route, { snapshot, clientOperationId: body.clientOperationId, isReplay: false });
  });

  return state;
}

function understandingFixture(projectId: number, projectName: string, emptyFacts: boolean): UnderstandingFixture {
  return {
    projectId,
    tenantId: 3,
    projectName,
    revision: 3,
    facts: emptyFacts ? [] : [
      {
        key: 'ProductSummary',
        value: 'A marketplace for independent booksellers.',
        state: 'Confirmed',
        userLocked: false,
        authorKind: 'Agent',
        authorActorUserId: null,
        authorAgentRunId: agentRunId,
        sourceMessageIds: [9101],
        evidenceSummary: 'The project owner described the product outcome.',
        revision: 2
      },
      {
        key: 'PrimaryUsers',
        value: 'Independent booksellers',
        state: 'Inferred',
        userLocked: false,
        authorKind: 'Agent',
        authorActorUserId: null,
        authorAgentRunId: agentRunId,
        sourceMessageIds: [9101],
        evidenceSummary: 'Inferred from the opening Workshop message.',
        revision: 3
      },
      {
        key: 'ApplicationType',
        value: 'Web application',
        state: 'Conflicted',
        userLocked: false,
        authorKind: 'Agent',
        authorActorUserId: null,
        authorAgentRunId: agentRunId,
        sourceMessageIds: [9101],
        evidenceSummary: 'Earlier discussion selected a web application.',
        revision: 3
      }
    ],
    conflicts: emptyFacts ? [] : [{
      conflictId,
      factKey: 'ApplicationType',
      currentValue: 'Web application',
      proposedValue: 'Desktop application',
      sourceMessageIds: [9101],
      evidenceSummary: 'The latest statement conflicts with the current application type.',
      createdByAgentRunId: agentRunId,
      createdAtRevision: 3,
      status: 'Open',
      resolvedAtRevision: null,
      resolvedByActorUserId: null
    }, {
      conflictId: secondConflictId,
      factKey: 'ApplicationType',
      currentValue: 'Web application',
      proposedValue: 'Mobile application',
      sourceMessageIds: [9101],
      evidenceSummary: 'A separate statement proposed a mobile application.',
      createdByAgentRunId: agentRunId,
      createdAtRevision: 3,
      status: 'Open',
      resolvedAtRevision: null,
      resolvedByActorUserId: null
    }],
    openQuestions: emptyFacts ? [] : ['Should catalogue access require an account?'],
    pendingRenameProposal: emptyFacts ? null : {
      proposalId,
      proposedName: 'Catalog Studio',
      status: 'Pending',
      basedOnProjectName: projectName,
      basedOnUnderstandingRevision: 3,
      proposedByAgentRunId: agentRunId,
      initiatingActorUserId: 7,
      sourceMessageIds: [9101],
      evidenceSummary: 'The conversation consistently describes a catalogue studio.',
      createdAtUtc: '2026-07-20T01:05:00Z'
    },
    operationalProjections: {
      projectLifecyclePhase: 'Shaping',
      projectLifecycleAuthority: 'ProjectLifecyclePhase',
      executionReadiness: 'NotConfigured',
      executionReadinessAuthority: 'ProjectReadinessAssessment',
      repositoryBinding: null
    }
  };
}

type UnderstandingFixture = {
  projectId: number;
  tenantId: number;
  projectName: string;
  revision: number;
  facts: Array<Record<string, unknown> & { key: string; sourceMessageIds: number[] }>;
  conflicts: Array<Record<string, unknown> & { factKey: string; status: string }>;
  openQuestions: string[];
  pendingRenameProposal: Record<string, unknown> | null;
  operationalProjections: Record<string, unknown>;
};

function projectIdFrom(url: string) {
  const match = new URL(url).pathname.match(/\/projects\/(\d+)\//);
  if (!match) throw new Error(`Project ID missing from ${url}`);
  return Number(match[1]);
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function repositoryBindingFixture(projectId: number, bindingState: 'SetupConfirmed' | 'LegacyUnverified') {
  const setupConfirmed = bindingState === 'SetupConfirmed';
  return {
    id: '55555555-6666-4777-8888-999999999999',
    projectId,
    revision: 1,
    repositoryKind: setupConfirmed ? 'Greenfield' : 'Existing',
    canonicalPath: `C:\\IronDevTestWorkspaces\\repositories\\project-${projectId}`,
    bindingState,
    defaultBranch: setupConfirmed ? 'main' : null,
    baselineCommit: null,
    createdByActorUserId: setupConfirmed ? 7 : null,
    confirmedAtUtc: setupConfirmed ? '2026-07-21T01:00:00Z' : null
  };
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
