import { expect, test, type Page, type Route } from '@playwright/test';
import { workbenchProjectEntryContext } from './helpers/mockWorkbench';

test('Tools shows backend registration truth without configuration or invocation controls', async ({ page }) => {
  await mockToolsWorkspace(page);
  await page.goto('/projects/7/library/tools');

  const tool = page.getByTestId('flow.tools.open.code_standards.analyse_patch');
  await expect(page.getByTestId('flow.tools.catalogue')).toBeVisible();
  await expect(tool).toContainText('Code standards analysis');
  await expect(tool).toContainText('Registered');
  await expect(tool).toContainText('Not required');
  await expect(tool).toContainText('Governed workflows only');
  await expect(tool).toContainText('Not implemented');
  await expect(tool).toContainText('Not checked');
  await expect(tool).toContainText('Read-only');
  await expect(page.getByRole('button', { name: /^(Add|Enable|Test|Run)$/ })).toHaveCount(0);
});

test('tool detail discloses the exact definition and survives refresh', async ({ page }) => {
  await mockToolsWorkspace(page);
  await page.goto('/projects/7/library/tools');

  await page.getByTestId('flow.tools.open.code_standards.analyse_patch').click();
  await expect(page).toHaveURL('/projects/7/library/tools/code_standards.analyse_patch');

  const detail = page.getByTestId('flow.tools.detail');
  await expect(detail).toContainText('Code standards analysis');
  await expect(detail).toContainText('BuilderAgent');
  await expect(detail).toContainText('TestingAgent');
  await expect(detail).toContainText('TesterAgent');
  await expect(detail).toContainText('CodeStandardsFinding');
  await expect(detail.getByText('Not declared', { exact: true })).toHaveCount(6);

  await detail.getByText('Definition details', { exact: true }).click();
  await expect(detail).toContainText('code_standards.analyse_patch');
  await expect(detail).toContainText('CodeStandardsAnalysisInput');
  await expect(detail).toContainText('CodeStandardsAnalysisResult');

  await page.reload();
  await expect(page).toHaveURL('/projects/7/library/tools/code_standards.analyse_patch');
  await expect(page.getByTestId('flow.tools.detail')).toContainText('Governed workflows only');
  await expect(page.getByRole('button', { name: /^(Add|Enable|Test|Run)$/ })).toHaveCount(0);
});

test('an empty backend registry has a distinct honest state', async ({ page }) => {
  await mockToolsWorkspace(page, { tools: [] });
  await page.goto('/projects/7/library/tools');

  await expect(page.getByTestId('flow.tools.empty')).toContainText('No governed tools are registered');
  await expect(page.getByTestId('flow.tools.empty')).toContainText('No Add or Request action is available');
});

test('catalogue failure preserves the route and retries backend truth', async ({ page }) => {
  const state = await mockToolsWorkspace(page, { catalogueInitiallyUnavailable: true });
  await page.goto('/projects/7/library/tools');

  await expect(page.getByRole('heading', { name: 'Project tools are unavailable', exact: true })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('flow.routeOutcome')).toContainText('Tool registry unavailable.');
  await expect(page).toHaveURL('/projects/7/library/tools');
  state.makeCatalogueAvailable();
  await page.getByTestId('flow.routeOutcome.primary').click();

  await expect(page.getByTestId('flow.tools.open.code_standards.analyse_patch')).toBeVisible();
  expect(state.catalogueRequests).toBeGreaterThanOrEqual(2);
});

test('an unknown tool returns an honest 404 and a working route back to Tools', async ({ page }) => {
  await mockToolsWorkspace(page);
  await page.goto('/projects/7/library/tools/not.registered');

  await expect(page.getByRole('heading', { name: 'Governed tool not found', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await page.getByTestId('flow.routeOutcome.primary').click();
  await expect(page).toHaveURL('/projects/7/library/tools');
  await expect(page.getByTestId('flow.tools.catalogue')).toBeVisible();
});

test.describe('narrow Tools', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('keeps catalogue and definition truth inside the viewport', async ({ page }) => {
    await mockToolsWorkspace(page);
    await page.goto('/projects/7/library/tools');
    await expect(page.getByTestId('flow.tools.catalogue')).toBeVisible();
    await page.getByTestId('flow.tools.open.code_standards.analyse_patch').click();
    await expect(page.getByTestId('flow.tools.detail')).toBeVisible();

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });
});

interface ToolsMockOptions {
  tools?: Array<Record<string, unknown>>;
  catalogueInitiallyUnavailable?: boolean;
}

interface ToolsMockState {
  catalogueRequests: number;
  makeCatalogueAvailable: () => void;
}

const toolSummary = {
  toolId: 'code_standards.analyse_patch',
  displayName: 'Code standards analysis',
  category: 'Testing and validation',
  description: 'Analyse a proposed patch or changed-file packet for IronDev code-standard risks.',
  registrationStatus: 'Registered',
  connectionStatus: 'Not required',
  projectUseStatus: 'Governed workflows only',
  directInvocationStatus: 'Not implemented',
  healthStatus: 'Not checked',
  effectiveScopeSummary: 'Read-only. No state, file, process, network, or workspace mutation.',
  boundary: 'Code standards analysis is deterministic and read-only.'
};

const toolDetail = {
  projectId: 7,
  projectName: 'BookSeller',
  ...toolSummary,
  definitionVersion: '1',
  capabilities: {
    mutatesState: false,
    allowsNestedCalls: false,
    allowsFileWrites: false,
    allowsProcessExecution: false,
    allowsNetworkAccess: false,
    allowsWorkspaceMutation: false
  },
  inputContract: 'CodeStandardsAnalysisInput',
  outputContract: 'CodeStandardsAnalysisResult',
  allowedCallers: ['BuilderAgent', 'TestingAgent', 'TesterAgent'],
  evidenceKinds: ['CodeStandardsFinding']
};

async function mockToolsWorkspace(page: Page, options: ToolsMockOptions = {}): Promise<ToolsMockState> {
  let catalogueAvailable = !options.catalogueInitiallyUnavailable;
  const state = {
    catalogueRequests: 0,
    makeCatalogueAvailable: () => { catalogueAvailable = true; }
  };
  const tools = options.tools ?? [toolSummary];

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
    capabilities: ['WorkflowSmokeSimulation']
  }));
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
  await page.route('**/irondev-api/api/workbench/projects/7/open', (route) =>
    json(route, workbenchProjectEntryContext(route, 7, { name: 'BookSeller' }))
  );
  await page.route('**/irondev-api/api/projects/7/tickets', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => json(route, []));

  await page.route(/\/irondev-api\/api\/projects\/7\/tools\/([^/?#]+)$/, (route) => {
    const toolId = decodeURIComponent(route.request().url().match(/tools\/([^/?#]+)$/)?.[1] ?? '');
    return toolId === toolSummary.toolId
      ? json(route, toolDetail)
      : json(route, { error: 'Governed tool not found.' }, 404);
  });
  await page.route(/\/irondev-api\/api\/projects\/7\/tools(?:\?[^#]*)?$/, (route) => {
    state.catalogueRequests += 1;
    if (!catalogueAvailable) {
      return json(route, { error: 'Tool registry unavailable.' }, 503);
    }
    return json(route, {
      projectId: 7,
      projectName: 'BookSeller',
      tools,
      boundary: 'Registration is not project enablement, invocation authority, approval, or permission to mutate state.'
    });
  });

  return state;
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
