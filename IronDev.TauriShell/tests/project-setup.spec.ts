import { expect, test, type Page, type Route } from '@playwright/test';

const PROFILE_ID = 'greenfield-winforms-net10-mstest-v1';
const DESCRIPTOR_SHA = 'a'.repeat(64);
const TEMPLATE_SHA = 'b'.repeat(64);
const UNDERSTANDING_SHA = 'c'.repeat(64);
const PLANNING_SHA = 'd'.repeat(64);
const PLAN_HASH = 'e'.repeat(64);
const BINDING_ID = '11111111-1111-4111-8111-111111111111';
const PROFILE_AUTHORITY_ID = '22222222-2222-4222-8222-222222222222';
const CONFIRMATION_ID = '33333333-3333-4333-8333-333333333333';

type Compatibility = 'Compatible' | 'Incompatible' | 'NeedsConfirmation' | 'NoPreference';
type PlanState = 'ReadyForConfirmation' | 'UnsupportedProfile' | 'EnvironmentUnavailable' | 'NeedsConfirmation';

interface MockState {
  planBodies: Array<Record<string, unknown>>;
  confirmationBodies: Array<Record<string, unknown>>;
  oldSetupRequests: string[];
}

interface MockOptions {
  context?: (projectId: number) => unknown;
  onPlan?: (
    route: Route,
    body: Record<string, unknown>,
    projectId: number,
    requestNumber: number
  ) => Promise<void> | void;
  onConfirmation?: (
    route: Route,
    body: Record<string, unknown>,
    projectId: number,
    requestNumber: number
  ) => Promise<void> | void;
}

function workbenchSession(projectId: number) {
  return {
    workbenchSessionId: projectId === 7 ? 7007 : 8008,
    leaseEpoch: projectId === 7 ? 1 : 2
  };
}

function profile(compatibility: Compatibility = 'NoPreference') {
  return {
    profileDefinitionId: PROFILE_ID,
    displayName: '.NET 10 Windows Forms with MSTest (planning preview)',
    compatibility,
    compatibilityReason: compatibility === 'Incompatible'
      ? 'The desired technology is not WinForms.'
      : compatibility === 'NeedsConfirmation'
        ? 'Confirm the desired technology in Workbench.'
        : compatibility === 'NoPreference'
          ? 'No technology preference is confirmed; selecting this profile is an explicit choice.'
          : 'The desired technology matches this profile.',
    planningReadiness: 'PreviewPlanningOnly',
    certificationState: 'NotCertificationReady',
    descriptorSha256: DESCRIPTOR_SHA,
    templateBundleSha256: TEMPLATE_SHA
  };
}

function repositoryContext(
  projectId = 7,
  compatibility: Compatibility = 'NoPreference',
  environmentState: 'Available' | 'Unavailable' | 'Unsafe' = 'Available'
) {
  return {
    projectId,
    tenantId: 3,
    projectName: projectId === 7 ? 'SecondRepo' : 'OtherRepo',
    projectLifecyclePhase: 'Shaping',
    executionReadiness: 'NotConfigured',
    readinessReasonCode: 'RepositoryNotConfigured',
    repositoryBinding: null,
    executionProfile: null,
    latestConfirmation: null,
    environmentCapability: {
      state: environmentState,
      reasonCode: environmentState === 'Available' ? 'RepositorySetupEnvironmentAvailable' : 'RepositorySetupWorkspaceRootUnavailable',
      message: environmentState === 'Available'
        ? 'The approved IronDev workspace root and planning artifacts are available.'
        : 'The required isolated workspace or pinned artifacts are not configured.',
      suggestedTarget: environmentState === 'Available' ? `C:\\IronDev\\workspace\\project-${projectId}` : ''
    },
    availableProfiles: [profile(compatibility)]
  };
}

function setupPlan(
  projectId = 7,
  state: PlanState = 'ReadyForConfirmation',
  compatibility: Compatibility = 'NoPreference'
) {
  const authority = workbenchSession(projectId);
  const projectName = projectId === 7 ? 'SecondRepo' : 'OtherRepo';
  const safeName = projectName.replace(/[^A-Za-z0-9]/g, '');
  const solutionPath = `${safeName}.sln`;
  const appProjectPath = `src/${safeName}/${safeName}.csproj`;
  const testProjectPath = `tests/${safeName}.Tests/${safeName}.Tests.csproj`;
  return {
    schemaVersion: 1,
    source: 'ProjectUnderstanding',
    projectId,
    canonicalProjectName: projectName,
    ...authority,
    basedOnUnderstandingRevision: 4,
    basedOnUnderstandingHash: UNDERSTANDING_SHA,
    profileDescriptorRevision: 1,
    profileDescriptorSha256: DESCRIPTOR_SHA,
    state,
    reasonCode: state === 'ReadyForConfirmation' ? 'RepositorySetupReadyForConfirmation' : `RepositorySetup${state}`,
    message: state === 'EnvironmentUnavailable'
      ? 'The approved workspace root is unavailable.'
      : state === 'UnsupportedProfile'
        ? 'The desired technology is unsupported in v0.1.'
        : state === 'NeedsConfirmation'
          ? 'Confirm the desired technology in Workbench.'
          : 'Review this deterministic, server-owned setup plan.',
    profile: profile(compatibility),
    targetPath: `C:\\IronDev\\workspace\\${projectName.toLowerCase()}`,
    solutionName: safeName,
    appProjectName: safeName,
    testProjectName: `${safeName}.Tests`,
    solutionPath,
    appProjectPath,
    testProjectPath,
    templateBundleSha256: TEMPLATE_SHA,
    planningBundleSha256: PLANNING_SHA,
    targetFramework: 'net10.0-windows',
    language: 'C#',
    applicationKind: 'WinForms',
    testFramework: 'MSTest',
    sdkVersion: '10.0.302',
    runtimeVersion: '10.0.10',
    restoreCommand: `dotnet restore "${solutionPath}" --configfile C:\\IronDev\\NuGet.Config --locked-mode`,
    buildCommand: `dotnet build "${solutionPath}" --configuration Release --no-restore`,
    testCommand: `dotnet test "${testProjectPath}" --configuration Release --no-restore --no-build`,
    toolchainManifestId: 'dotnet-sdk-10.0.302-runtime-10.0.10-planning-v1',
    executionImageReference: 'mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025',
    defaultBranch: 'main',
    initializeGit: true,
    indexAfterProvisioning: true,
    sandboxValidation: 'Validate the created tree inside the approved isolated workspace.',
    resourcePolicy: 'Use the pinned LocalTest resource policy.',
    planHash: PLAN_HASH
  };
}

function confirmationResult(
  body: Record<string, unknown>,
  projectId = 7,
  isReplay = false
) {
  const plan = setupPlan(projectId);
  return {
    projectId,
    confirmationId: CONFIRMATION_ID,
    clientOperationId: body.clientOperationId,
    isReplay,
    projectLifecyclePhase: 'Shaping',
    executionReadiness: 'NotConfigured',
    readinessReasonCode: 'RepositoryProvisioningPending',
    repositoryBinding: {
      id: BINDING_ID,
      projectId,
      revision: 1,
      repositoryKind: 'Greenfield',
      canonicalPath: plan.targetPath,
      bindingState: 'SetupConfirmed',
      defaultBranch: null,
      baselineCommit: null,
      createdByActorUserId: 7,
      confirmedAtUtc: '2026-07-21T01:02:03Z'
    },
    executionProfile: {
      id: PROFILE_AUTHORITY_ID,
      projectId,
      revision: 1,
      repositoryBindingId: BINDING_ID,
      profileDefinitionId: PROFILE_ID,
      profileDescriptorRevision: plan.profileDescriptorRevision,
      descriptorSha256: DESCRIPTOR_SHA,
      templateBundleSha256: TEMPLATE_SHA,
      planningBundleSha256: PLANNING_SHA,
      targetFramework: plan.targetFramework,
      language: plan.language,
      applicationKind: plan.applicationKind,
      testFramework: plan.testFramework,
      sdkVersion: plan.sdkVersion,
      runtimeVersion: plan.runtimeVersion,
      solutionPath: plan.solutionPath,
      appProjectPath: plan.appProjectPath,
      testProjectPath: plan.testProjectPath,
      restoreCommand: plan.restoreCommand,
      buildCommand: plan.buildCommand,
      testCommand: plan.testCommand,
      toolchainManifestId: plan.toolchainManifestId,
      executionImageReference: plan.executionImageReference,
      planningReadiness: 'PreviewPlanningOnly',
      certificationState: 'NotCertificationReady'
    },
    setupPlan: plan
  };
}

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

function projectIdFrom(route: Route): number {
  const match = new URL(route.request().url()).pathname.match(/\/projects\/(\d+)\//);
  if (!match) throw new Error(`Project id missing from ${route.request().url()}`);
  return Number(match[1]);
}

async function mockRepositorySetup(page: Page, options: MockOptions = {}): Promise<MockState> {
  const state: MockState = { planBodies: [], confirmationBodies: [], oldSetupRequests: [] };
  let planRequests = 0;
  let confirmationRequests = 0;

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
  });

  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;
    if (/\/api\/projects\/\d+\/(?:provisioning|profile|local-path)/.test(path)) {
      state.oldSetupRequests.push(path);
    }
  });

  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/localtest/preflight', (route) => json(route, {
    state: 'LocalTestReady', environment: 'LocalTest', database: 'IronDeveloper_Test',
    apiBuildCommit: 'test-commit', launcherRepositoryCommit: 'test-commit',
    apiBaseUrl: 'http://localhost:5000', sessionMode: 'SmokeSimulation',
    sandboxApplyRequested: false, sandboxApplyEnabled: false, sandboxApplyRoot: null,
    capabilities: ['WorkflowSmokeSimulation']
  }));
  await page.route('**/irondev-api/api/environment', (route) => json(route, {
    environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true
  }));
  await page.route('**/irondev-api/api/auth/me**', (route) => json(route, {
    userId: 7, email: 'bob@irondev.local', displayName: 'Bob Developer', selectedTenantId: 3
  }));
  await page.route('**/irondev-api/api/tenants', (route) => json(route, [
    { id: 3, name: 'IronDev Local', slug: 'irondev-local' }
  ]));
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() !== 'GET') return route.fallback();
    await json(route, [
      { id: 7, tenantId: 3, name: 'SecondRepo', localPath: null },
      { id: 8, tenantId: 3, name: 'OtherRepo', localPath: null }
    ]);
  });
  await page.route('**/irondev-api/api/workbench/projects/*/open', async (route) => {
    const projectId = projectIdFrom(route);
    const request = route.request().postDataJSON() as { clientOperationId: string };
    await json(route, {
      projectId,
      tenantId: 3,
      name: projectId === 7 ? 'SecondRepo' : 'OtherRepo',
      projectLifecyclePhase: 'Shaping',
      executionReadiness: 'NotConfigured',
      repositoryBinding: null,
      ...workbenchSession(projectId),
      wasResumed: true,
      wasTakenOver: false,
      clientOperationId: request.clientOperationId
    });
  });
  await page.route('**/irondev-api/api/projects/*/tickets', (route) => json(route, []));
  await page.route('**/irondev-api/api/workbench/projects/*/repository', (route) => {
    const projectId = projectIdFrom(route);
    return json(route, options.context?.(projectId) ?? repositoryContext(projectId));
  });
  await page.route('**/irondev-api/api/workbench/projects/*/repository/setup-plans', async (route) => {
    planRequests += 1;
    const projectId = projectIdFrom(route);
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.planBodies.push(body);
    if (options.onPlan) return options.onPlan(route, body, projectId, planRequests);
    await json(route, setupPlan(projectId));
  });
  await page.route('**/irondev-api/api/workbench/projects/*/repository/setup-confirmations', async (route) => {
    confirmationRequests += 1;
    const projectId = projectIdFrom(route);
    const body = route.request().postDataJSON() as Record<string, unknown>;
    state.confirmationBodies.push(body);
    if (options.onConfirmation) return options.onConfirmation(route, body, projectId, confirmationRequests);
    await json(route, confirmationResult(body, projectId));
  });

  return state;
}

async function openSetup(page: Page, projectId = 7) {
  await page.goto(`/projects/${projectId}/setup`);
  await expect(page.getByTestId('flow.projectSetup')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('heading', { name: 'Configure repository' })).toBeVisible();
}

async function openReadyPlan(page: Page) {
  await page.getByTestId('repositorySetup.reviewPlan').click();
  await expect(page.getByTestId('repositorySetup.review')).toBeVisible();
}

test('supported or explicitly selected profile produces a complete read-only setup review', async ({ page }) => {
  const state = await mockRepositorySetup(page);
  await openSetup(page);

  await expect(page.getByTestId('repositorySetup.compatibility')).toContainText('NoPreference');
  await expect(page.getByTestId('repositorySetup.capability')).toContainText('Available');
  await expect(page.getByTestId('repositorySetup.capability')).toContainText('approved IronDev workspace root');
  await expect(page.locator('input[type="text"], textarea')).toHaveCount(0);
  await openReadyPlan(page);

  expect(state.planBodies).toEqual([{
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    profileDefinitionId: PROFILE_ID
  }]);
  await expect(page.getByTestId('repositorySetup.review')).toContainText('ProjectUnderstanding');
  await expect(page.getByTestId('repositorySetup.review')).toContainText('10.0.302 / 10.0.10');
  await expect(page.getByTestId('repositorySetup.review')).toContainText('mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025');
  await expect(page.getByTestId('repositorySetup.review')).toContainText('dotnet restore');
  await expect(page.getByTestId('repositorySetup.review')).toContainText('Initialize Git');
  await expect(page.getByTestId('repositorySetup.review')).toContainText('Index after provisioning');
  await expect(page.getByTestId('repositorySetup.review')).toContainText('NotCertificationReady');
  await expect(page.getByTestId('repositorySetup.confirm')).toBeDisabled();
  await expect(page.getByRole('button', { name: /Save repository|Index project|Enable governed Builder|Disable governed Builder/i })).toHaveCount(0);
  expect(state.oldSetupRequests).toEqual([]);
});

test('cancel and Back leave without sending a setup confirmation', async ({ page }) => {
  const state = await mockRepositorySetup(page);
  await openSetup(page);
  await openReadyPlan(page);

  await page.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByTestId('repositorySetup.configure')).toBeVisible();
  expect(state.confirmationBodies).toHaveLength(0);
  await page.getByRole('button', { name: 'Back to projects' }).click();
  await expect(page).toHaveURL(/\/projects$/);
  expect(state.confirmationBodies).toHaveLength(0);
});

test('unsupported desired technology is not silently replaced by WinForms', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    context: (projectId) => repositoryContext(projectId, 'Incompatible')
  });
  await openSetup(page);

  const unsupported = page.getByTestId('repositorySetup.unsupported');
  await expect(unsupported).toContainText('No v0.1 execution profile is available');
  await expect(unsupported).toContainText('will not substitute WinForms');
  await expect(unsupported).toContainText('continue shaping the project and creating tickets');
  await expect(page.getByTestId('repositorySetup.reviewPlan')).toHaveCount(0);
  expect(state.planBodies).toHaveLength(0);
  expect(state.confirmationBodies).toHaveLength(0);
});

test('unconfirmed desired technology stays distinct from unsupported technology', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    context: (projectId) => repositoryContext(projectId, 'NeedsConfirmation')
  });
  await openSetup(page);

  const needsTechnology = page.getByTestId('repositorySetup.needsTechnology');
  await expect(needsTechnology).toContainText('Confirm the desired technology first');
  await expect(needsTechnology).toContainText('Return to Workbench');
  await expect(needsTechnology).toContainText('No profile will be substituted');
  await expect(needsTechnology).toContainText('continue shaping the project and creating tickets');
  await expect(page.getByTestId('repositorySetup.unsupported')).toHaveCount(0);
  await expect(page.getByTestId('repositorySetup.reviewPlan')).toHaveCount(0);
  expect(state.planBodies).toHaveLength(0);
  expect(state.confirmationBodies).toHaveLength(0);
});

test('environment and artifact limitation is separate and nonblocking', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    context: (projectId) => repositoryContext(projectId, 'NoPreference', 'Unavailable'),
    onPlan: (route, _body, projectId) => json(route, setupPlan(projectId, 'EnvironmentUnavailable'))
  });
  await openSetup(page);

  await expect(page.getByTestId('repositorySetup.capability')).toContainText('Unavailable');
  await page.getByTestId('repositorySetup.reviewPlan').click();
  const unavailable = page.getByTestId('repositorySetup.environmentUnavailable');
  await expect(unavailable).toContainText('Greenfield provisioning is unavailable in this environment');
  await expect(unavailable).toContainText('continue shaping the project and creating tickets');
  await expect(page.getByTestId('repositorySetup.confirm')).toHaveCount(0);
  expect(state.confirmationBodies).toHaveLength(0);
});

test('unsafe target refusal stays authoritative and creates no confirmation operation', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    onPlan: (route) => json(route, {
      reasonCode: 'RepositorySetupTargetUnsafe',
      message: 'The configured workspace target is unsafe.',
      nextSafeActions: ['Configure an approved IronDev workspace root.']
    }, 422)
  });
  await openSetup(page);

  await page.getByTestId('repositorySetup.reviewPlan').click();
  await expect(page.getByRole('alert')).toContainText('The configured workspace target is unsafe.');
  await expect(page.getByRole('alert')).toContainText('No repository directory was created.');
  expect(state.planBodies[0]).toEqual({ workbenchSessionId: 7007, leaseEpoch: 1, profileDefinitionId: PROFILE_ID });
  expect(state.confirmationBodies).toHaveLength(0);
});

test('explicit confirmation sends the exact fence and records SetupConfirmed without provisioning', async ({ page }) => {
  const state = await mockRepositorySetup(page);
  await openSetup(page);
  await openReadyPlan(page);

  await page.getByTestId('repositorySetup.confirmCheck').check();
  await page.getByTestId('repositorySetup.confirm').click();
  const confirmed = page.getByTestId('repositorySetup.confirmed');
  await expect(confirmed).toContainText('Repository setup confirmed');
  await expect(confirmed).toContainText('SetupConfirmed');
  await expect(confirmed).toContainText('NotConfigured');
  await expect(confirmed).toContainText('repository directory was not created');
  await expect(confirmed).toContainText('Builder is not authorized');

  expect(Object.keys(state.confirmationBodies[0]).sort()).toEqual([
    'clientOperationId', 'expectedPlanHash', 'leaseEpoch', 'workbenchSessionId'
  ]);
  expect(state.confirmationBodies[0]).toMatchObject({
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    expectedPlanHash: PLAN_HASH
  });
  expect(String(state.confirmationBodies[0].clientOperationId)).toMatch(/^[0-9a-f-]{36}$/i);
  expect(state.oldSetupRequests).toEqual([]);
});

test('invalid success and generic 5xx retain one operation across reopen until exact replay succeeds', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    onConfirmation: async (route, body, projectId, requestNumber) => {
      if (requestNumber === 1) {
        const malformed = confirmationResult(body, projectId);
        await json(route, {
          ...malformed,
          setupPlan: {
            ...malformed.setupPlan,
            state: 'UnsupportedProfile'
          }
        });
        return;
      }
      if (requestNumber === 2) {
        await json(route, { error: 'Gateway response was incomplete.' }, 500);
        return;
      }
      await json(route, confirmationResult(body, projectId, true));
    }
  });
  await openSetup(page);
  await openReadyPlan(page);
  await page.getByTestId('repositorySetup.confirmCheck').check();
  await page.getByTestId('repositorySetup.confirm').click();

  await expect(page.getByTestId('repositorySetup.ambiguousLock')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancel' })).toHaveCount(0);
  const operationId = String(state.confirmationBodies[0].clientOperationId);
  await page.reload();
  await expect(page.getByTestId('repositorySetup.recoverConfirmation')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('repositorySetup.recovery.operation')).toContainText(operationId);

  await page.getByTestId('repositorySetup.retryExact').click();
  await expect(page.getByTestId('repositorySetup.recoverConfirmation')).toBeVisible();
  await page.getByTestId('repositorySetup.retryExact').click();
  await expect(page.getByTestId('repositorySetup.confirmed')).toBeVisible();

  expect(state.confirmationBodies).toHaveLength(3);
  expect(state.confirmationBodies[1]).toEqual(state.confirmationBodies[0]);
  expect(state.confirmationBodies[2]).toEqual(state.confirmationBodies[0]);
  const pendingKeys = await page.evaluate(() => Object.keys(sessionStorage).filter((key) => key.startsWith('irondev.repository-setup-confirmation')));
  expect(pendingKeys).toEqual([]);
});

test('structured definitive rejection clears the receipt so the next confirmation uses a new operation id', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    onConfirmation: (route, body, projectId, requestNumber) => requestNumber === 1
      ? json(route, { error: 'workbench_lease_fence_rejected', message: 'The write lease is stale.' }, 409)
      : json(route, confirmationResult(body, projectId))
  });
  await openSetup(page);
  await openReadyPlan(page);
  await page.getByTestId('repositorySetup.confirmCheck').check();
  await page.getByTestId('repositorySetup.confirm').click();

  await expect(page.getByRole('alert')).toContainText('The write lease is stale.');
  await expect(page.getByRole('button', { name: 'Cancel' })).toBeVisible();
  await expect(page.getByTestId('repositorySetup.confirm')).toHaveText('Confirm repository setup');
  await page.getByTestId('repositorySetup.confirm').click();
  await expect(page.getByTestId('repositorySetup.confirmed')).toBeVisible();
  expect(state.confirmationBodies).toHaveLength(2);
  expect(state.confirmationBodies[1].clientOperationId).not.toBe(state.confirmationBodies[0].clientOperationId);
});

test('late project-A confirmation cannot install state into project B', async ({ page }) => {
  const state = await mockRepositorySetup(page, {
    onConfirmation: async (route, body, projectId) => {
      if (projectId === 7) {
        await new Promise((resolve) => setTimeout(resolve, 450));
      }
      await json(route, confirmationResult(body, projectId));
    }
  });
  await openSetup(page, 7);
  await openReadyPlan(page);
  await page.getByTestId('repositorySetup.confirmCheck').check();
  await page.getByTestId('repositorySetup.confirm').click();
  await expect.poll(() => state.confirmationBodies.length).toBe(1);

  await page.evaluate(() => {
    window.history.pushState(null, '', '/projects/8/setup');
    window.dispatchEvent(new Event('irondev:navigation'));
  });
  await expect(page.getByText('OtherRepo', { exact: true })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('repositorySetup.configure')).toBeVisible();
  await page.waitForTimeout(550);
  await expect(page.getByTestId('repositorySetup.confirmed')).toHaveCount(0);
  await expect(page.getByText('OtherRepo', { exact: true })).toBeVisible();
});

test('strict response validation refuses a malformed repository context', async ({ page }) => {
  await mockRepositorySetup(page, {
    context: (projectId) => {
      const malformed = repositoryContext(projectId) as Record<string, unknown>;
      delete malformed.environmentCapability;
      return malformed;
    }
  });
  await openSetup(page);

  const unavailable = page.getByTestId('repositorySetup.unavailable');
  await expect(unavailable).toContainText('Repository setup is unavailable');
  await expect(unavailable).toContainText('invalid success response');
  await expect(page.getByTestId('repositorySetup.reviewPlan')).toHaveCount(0);
});
