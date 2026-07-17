import { expect, test, type Page, type Route } from '@playwright/test';
import { projectBoardResponse } from './helpers/mockBoard';

type Readiness = Record<string, unknown>;

function setupCheck(
  code: string,
  label: string,
  state = 'NeedsConfirmation',
  detectedValue = '',
  remedy = `Resolve ${label.toLowerCase()}.`
) {
  const actionKind =
    code === 'BuildCommand'
      ? 'ConfirmBuildCommand'
      : code === 'TestCommand'
        ? 'ConfirmTestCommand'
        : code === 'ProjectProfile'
        ? 'ConfirmProjectProfile'
        : code === 'CodeIndex'
          ? 'IndexProject'
          : code === 'BuilderApplyPermission'
            ? 'EnableBuilderApply'
        : code === 'RepositoryAccess' || code === 'RootSafety'
            ? 'ChangeRepository'
            : 'ResolveAdditionalSetup';
  return {
    code,
    name: label,
    label,
    state,
    summary: `${label} needs attention.`,
    evidence: detectedValue ? `Detected candidate: ${detectedValue}` : `${label} was not confirmed.`,
    remedy,
    blocking: state !== 'Confirmed',
    detectedValue,
    actionKind
  };
}

function blockedReadiness(check: ReturnType<typeof setupCheck>, kind = check.actionKind): Readiness {
  return {
    projectId: 7,
    isReady: false,
    blockedCount: 1,
    blockedStates: ['BlockedSetupRequired'],
    checks: [check],
    nextAction: {
      kind,
      checkCode: check.code,
      allowed: true,
      reasonCode: 'BlockedSetupRequired',
      label: `Resolve ${check.label}`,
      nextSafeAction: check.remedy
    },
    proposedProfile: check.code === 'ProjectProfile'
      ? { projectId: 7, applicationType: 'ASP.NET Core', primaryLanguage: 'C#', framework: '.NET 10', solutionFile: 'Second.slnx' }
      : null,
    boundary: 'Readiness is computed from stored truth and scan evidence.'
  };
}

const READY_READINESS: Readiness = {
  projectId: 7,
  isReady: true,
  blockedCount: 0,
  blockedStates: [],
  checks: [setupCheck('BuildCommand', 'Build command', 'Confirmed')],
  nextAction: {
    kind: 'OpenBoard',
    checkCode: null,
    allowed: true,
    reasonCode: null,
    label: 'Open Board',
    nextSafeAction: 'Open the project Board.'
  },
  proposedProfile: null,
  boundary: 'Readiness is computed from stored truth and scan evidence.'
};

interface SetupMockOptions {
  readiness: () => Readiness;
  onCommand?: (body: { commandType: string; commandText: string }) => Promise<{ status: number; body?: unknown }> | { status: number; body?: unknown };
  onProfile?: (body: Record<string, unknown>) => void;
  onLocalPath?: (body: { localPath: string }) => void;
  onIndex?: (requestBody: string | null) => Promise<{ status: number; body?: unknown }> | { status: number; body?: unknown };
  onBuilderPermission?: (body: { enabled: boolean }) => Promise<{ status: number; body?: unknown }> | { status: number; body?: unknown };
  projectPath?: () => string;
  readinessRoute?: (route: Route, requestNumber: number) => Promise<void>;
}

async function mockSetup(page: Page, options: SetupMockOptions) {
  let readinessRequests = 0;
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await page.route('**/irondev-api/health', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) })
  );
  await page.route('**/irondev-api/api/environment', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true }) })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ userId: 7, email: 'bob@irondev.local', displayName: 'Bob Developer', selectedTenantId: 3 }) })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]) })
  );
  await page.route('**/irondev-api/api/projects', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'SecondRepo', localPath: options.projectPath?.() ?? 'C:\\repos\\Second' }])
    });
  });
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) })
  );
  await page.route('**/irondev-api/api/projects/7/tickets', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) })
  );
  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', async (route) => {
    readinessRequests += 1;
    if (options.readinessRoute) {
      await options.readinessRoute(route, readinessRequests);
      return;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(options.readiness()) });
  });
  await page.route('**/irondev-api/api/projects/7/board', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(projectBoardResponse({ projectName: 'SecondRepo', readiness: options.readiness() }))
    })
  );
  await page.route('**/irondev-api/api/projects/7/profile/commands', async (route) => {
    const body = route.request().postDataJSON() as { commandType: string; commandText: string };
    const result = options.onCommand ? await options.onCommand(body) : { status: 200 };
    await route.fulfill({
      status: result.status,
      contentType: 'application/json',
      body: result.body === undefined ? '' : JSON.stringify(result.body)
    });
  });
  await page.route('**/irondev-api/api/projects/7/profile', async (route) => {
    options.onProfile?.(route.request().postDataJSON() as Record<string, unknown>);
    await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
  });
  await page.route('**/irondev-api/api/projects/7/local-path', async (route) => {
    options.onLocalPath?.(route.request().postDataJSON() as { localPath: string });
    await route.fulfill({ status: 204, body: '' });
  });
  await page.route('**/irondev-api/api/projects/7/provisioning/code-index', async (route) => {
    const result = options.onIndex
      ? await options.onIndex(route.request().postData())
      : { status: 200, body: { allowed: true, status: 'Succeeded', message: 'Indexed 24 files.', changed: true, correlationId: 'index-correlation', indexResult: { storedFileCount: 24 } } };
    await route.fulfill({
      status: result.status,
      contentType: 'application/json',
      body: result.body === undefined ? '' : JSON.stringify(result.body)
    });
  });
  await page.route('**/irondev-api/api/projects/7/provisioning/builder-workspace-permission', async (route) => {
    const body = route.request().postDataJSON() as { enabled: boolean };
    const result = options.onBuilderPermission
      ? await options.onBuilderPermission(body)
      : { status: 200, body: { allowed: true, status: 'Succeeded', message: 'Builder permission updated.', changed: true, correlationId: 'builder-correlation' } };
    await route.fulfill({
      status: result.status,
      contentType: 'application/json',
      body: result.body === undefined ? '' : JSON.stringify(result.body)
    });
  });
}

async function openSetup(page: Page) {
  await page.goto('/');
  await page.getByTestId('flow.nav.library').click();
  await page.getByTestId('flow.library.nav.provisioning').click();
  await expect(page.getByTestId('flow.projectSetup')).toBeVisible();
}

test('next task comes from backend nextAction and stable code, even when the display label changes', async ({ page }) => {
  const check = setupCheck('BuildCommand', 'Compilation recipe', 'NeedsConfirmation', 'dotnet build Second.slnx');
  await mockSetup(page, { readiness: () => blockedReadiness(check, 'ConfirmBuildCommand') });
  await openSetup(page);

  await expect(page.getByRole('heading', { name: 'Confirm the build command' })).toBeVisible();
  await expect(page.getByTestId('flow.projectSetup.input.BuildCommand')).toHaveValue('dotnet build Second.slnx');
  await expect(page.getByTestId('flow.projectSetup').locator('.fl-pri:visible')).toHaveCount(1);
});

test('build command confirmation re-evaluates and shows Ready before Board', async ({ page }) => {
  let ready = false;
  const saved: Array<{ commandType: string; commandText: string }> = [];
  const check = setupCheck('BuildCommand', 'Build command', 'NeedsConfirmation', 'dotnet build Second.slnx');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(check),
    onCommand: (body) => {
      saved.push(body);
      ready = true;
      return { status: 200 };
    }
  });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.confirm.BuildCommand').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toContainText('Ready for governed runs');
  await expect(page.getByTestId('flow.board.columns')).toHaveCount(0);
  expect(saved[0]).toEqual(expect.objectContaining({ commandType: 'Build', commandText: 'dotnet build Second.slnx' }));
});

test('test command save failure retains the entered value', async ({ page }) => {
  const check = setupCheck('TestCommand', 'Test command', 'Missing');
  await mockSetup(page, {
    readiness: () => blockedReadiness(check),
    onCommand: () => ({ status: 400, body: { error: 'Command rejected by policy.' } })
  });
  await openSetup(page);

  const input = page.getByTestId('flow.projectSetup.input.TestCommand');
  await input.fill('dotnet test Second.Tests.csproj');
  await page.getByTestId('flow.projectSetup.confirm.TestCommand').click();
  await expect(input).toHaveValue('dotnet test Second.Tests.csproj');
  await expect(page.getByRole('alert')).toBeVisible();
});

test('project structure confirmation posts the proposal and re-evaluates', async ({ page }) => {
  let ready = false;
  const savedProfiles: Array<Record<string, unknown>> = [];
  const check = setupCheck('ProjectProfile', 'Architecture proposal');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(check),
    onProfile: (body) => {
      savedProfiles.push(body);
      ready = true;
    }
  });
  await openSetup(page);

  await expect(page.getByText('ASP.NET Core', { exact: true })).toBeVisible();
  await page.getByTestId('flow.projectSetup.confirm.ProjectProfile').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();
  expect(savedProfiles[0].projectId).toBe(7);
});

test('code index is a semantic action and advances to Builder permission', async ({ page }) => {
  let stage: 'index' | 'builder' = 'index';
  let requestBody: string | null = 'not-called';
  const indexCheck = setupCheck('CodeIndex', 'Code index', 'Missing', '', 'Use Index project.');
  const builderCheck = setupCheck('BuilderApplyPermission', 'Builder apply permission');
  await mockSetup(page, {
    readiness: () => stage === 'index' ? blockedReadiness(indexCheck) : blockedReadiness(builderCheck),
    onIndex: (body) => {
      requestBody = body;
      stage = 'builder';
      return {
        status: 200,
        body: {
          allowed: true,
          status: 'Succeeded',
          message: 'Indexed 24 files.',
          changed: true,
          correlationId: 'index-24',
          indexResult: { storedFileCount: 24 }
        }
      };
    }
  });
  await openSetup(page);

  await expect(page.getByRole('heading', { name: 'Code index required' })).toBeVisible();
  await expect(page.getByTestId('flow.projectSetup.next')).not.toContainText('POST /api/');
  await page.getByTestId('flow.projectSetup.indexProject').click();
  await expect(page.getByTestId('flow.projectSetup.completion')).toContainText('Indexed 24 files');
  await expect(page.getByRole('button', { name: 'Continue setup' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Enable governed Builder workspace writes' })).toBeVisible();
  expect(requestBody).toBeNull();
});

test('Builder permission requires confirmation, explains the boundary, and advances to Ready', async ({ page }) => {
  let ready = false;
  const requests: Array<{ enabled: boolean }> = [];
  const check = setupCheck('BuilderApplyPermission', 'Builder apply permission');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(check),
    onBuilderPermission: (body) => {
      requests.push(body);
      ready = true;
      return { status: 200, body: { allowed: true, status: 'Succeeded', message: 'Enabled.', changed: true, correlationId: 'builder-enable' } };
    }
  });
  await openSetup(page);

  const next = page.getByTestId('flow.projectSetup.next');
  await expect(next).toContainText('only inside IronDev-controlled disposable workspaces');
  await expect(next).toContainText('It does not approve changes.');
  await expect(next).toContainText('It does not apply to the source repository.');
  await expect(next).toContainText('It does not commit, push, merge, release or deploy.');
  await expect(page.getByTestId('flow.projectSetup.enableBuilderApply')).toBeDisabled();
  await page.getByTestId('flow.projectSetup.confirmBuilderBoundary').check();
  await page.getByTestId('flow.projectSetup.enableBuilderApply').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toContainText('Ready for governed runs');
  expect(requests).toEqual([{ enabled: true }]);
});

test('semantic action failure shows the backend reason and a visible retry', async ({ page }) => {
  let attempts = 0;
  let stage: 'index' | 'builder' = 'index';
  const indexCheck = setupCheck('CodeIndex', 'Code index', 'Missing');
  const builderCheck = setupCheck('BuilderApplyPermission', 'Builder apply permission');
  await mockSetup(page, {
    readiness: () => stage === 'index' ? blockedReadiness(indexCheck) : blockedReadiness(builderCheck),
    onIndex: () => {
      attempts += 1;
      if (attempts === 1) {
        return {
          status: 409,
          body: {
            allowed: false,
            reasonCode: 'project_setup_repository_path_unsafe',
            message: 'The configured repository path is unsafe.',
            nextSafeActions: ['Select a safe repository root, then retry.'],
            correlationId: 'index-refusal'
          }
        };
      }
      stage = 'builder';
      return { status: 200, body: { allowed: true, status: 'Succeeded', message: 'Indexed 1 files.', changed: true, correlationId: 'index-retry', indexResult: { storedFileCount: 1 } } };
    }
  });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.indexProject').click();
  await expect(page.getByRole('alert')).toContainText('The configured repository path is unsafe.');
  await expect(page.getByTestId('flow.projectSetup.indexProject')).toBeVisible();
  await page.getByTestId('flow.projectSetup.indexProject').click();
  await expect(page.getByRole('heading', { name: 'Enable governed Builder workspace writes' })).toBeVisible();
  expect(attempts).toBe(2);
});

test('Ready setup can disable Builder workspace permission through the narrow action', async ({ page }) => {
  let ready = true;
  const bodies: Array<{ enabled: boolean }> = [];
  const builderCheck = setupCheck('BuilderApplyPermission', 'Builder apply permission');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(builderCheck),
    onBuilderPermission: (body) => {
      bodies.push(body);
      ready = false;
      return { status: 200, body: { allowed: true, status: 'Succeeded', message: 'Disabled.', changed: true, correlationId: 'builder-disable' } };
    }
  });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.disableBuilderApply').click();
  await expect(page.getByRole('heading', { name: 'Enable governed Builder workspace writes' })).toBeVisible();
  expect(bodies).toEqual([{ enabled: false }]);
});

test('repository path change saves then re-evaluates backend truth', async ({ page }) => {
  let ready = false;
  let savedPath = '';
  const check = setupCheck('RepositoryAccess', 'Repository', 'Missing', '', 'Choose a repository that exists.');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(check, 'ChangeRepository'),
    onLocalPath: (body) => {
      savedPath = body.localPath;
      ready = true;
    }
  });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.repository.path').fill('C:\\repos\\MovedSecond');
  await page.getByTestId('flow.projectSetup.repository.save').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();
  expect(savedPath).toBe('C:\\repos\\MovedSecond');
});

test('unknown blocking check remains visible with the backend remedy', async ({ page }) => {
  const check = setupCheck('FutureRepositoryPolicy', 'Future repository policy', 'NeedsConfirmation', '', 'Ask a project owner to satisfy policy F-12.');
  await mockSetup(page, { readiness: () => blockedReadiness(check, 'ResolveAdditionalSetup') });
  await openSetup(page);

  await expect(page.getByTestId('flow.projectSetup.next')).toContainText('Additional setup required');
  await expect(page.getByTestId('flow.projectSetup.next')).toContainText('policy F-12');
  await expect(page.getByTestId('flow.projectSetup.row.FutureRepositoryPolicy')).toContainText('Needs attention');
});

test('readiness failure never renders Ready and offers retry', async ({ page }) => {
  await mockSetup(page, {
    readiness: () => READY_READINESS,
    readinessRoute: async (route) => {
      await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'boom' }) });
    }
  });
  await openSetup(page);

  await expect(page.getByTestId('flow.projectSetup.unavailable')).toContainText('Nothing has been marked ready');
  await expect(page.getByTestId('flow.projectSetup.ready')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry setup check' })).toBeVisible();
});

test('Open Board for shaping works while governed runs remain blocked', async ({ page }) => {
  const check = setupCheck('TestCommand', 'Test command', 'Missing');
  await mockSetup(page, { readiness: () => blockedReadiness(check) });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.openBoardForShaping').click();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await expect(page.getByTestId('flow.cockpit.setup')).toContainText('Governed runs are blocked until project setup is complete');
});

test('Open Board from Ready state uses the normal project Board', async ({ page }) => {
  await mockSetup(page, { readiness: () => READY_READINESS });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.openBoard').click();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
});

test('refresh restores backend-confirmed Ready state', async ({ page }) => {
  let ready = false;
  const check = setupCheck('BuildCommand', 'Build command', 'NeedsConfirmation', 'dotnet build Second.slnx');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(check),
    onCommand: () => {
      ready = true;
      return { status: 200 };
    }
  });
  await openSetup(page);
  await page.getByTestId('flow.projectSetup.confirm.BuildCommand').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();

  await page.reload();
  await page.getByTestId('flow.nav.library').click();
  await page.getByTestId('flow.library.nav.provisioning').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();
});

test('a late stale readiness response cannot overwrite the current result', async ({ page }) => {
  const stale = blockedReadiness(setupCheck('RepositoryAccess', 'Repository', 'Missing'), 'ChangeRepository');
  let pathSaved = false;
  let postSaveRequests = 0;
  await mockSetup(page, {
    readiness: () => READY_READINESS,
    readinessRoute: async (route) => {
      if (!pathSaved) {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(stale) });
        return;
      }
      postSaveRequests += 1;
      if (postSaveRequests === 1) {
        await new Promise((resolve) => setTimeout(resolve, 250));
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(stale) });
        return;
      }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(READY_READINESS) });
    },
    onLocalPath: () => {
      pathSaved = true;
    },
    projectPath: () => pathSaved ? 'C:\\repos\\Current' : 'C:\\repos\\Second'
  });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.repository.path').fill('C:\\repos\\Current');
  await page.getByTestId('flow.projectSetup.repository.save').click();
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();
  await page.waitForTimeout(350);
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();
});

test('keyboard users can complete the current command task', async ({ page }) => {
  let ready = false;
  const check = setupCheck('BuildCommand', 'Build command', 'NeedsConfirmation', 'dotnet build Second.slnx');
  await mockSetup(page, {
    readiness: () => ready ? READY_READINESS : blockedReadiness(check),
    onCommand: () => {
      ready = true;
      return { status: 200 };
    }
  });
  await openSetup(page);

  await page.getByTestId('flow.projectSetup.input.BuildCommand').focus();
  await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('flow.projectSetup.ready')).toBeVisible();
});
