import { expect, test, type Page, type Route } from '@playwright/test';
import type { ProjectGovernanceOverview } from '../src/api/types';
import { mockProjectBoard } from './helpers/mockBoard';

test('attention overview renders backend truth and primary action opens the Work Item', async ({ page }) => {
  await prepareSelectedProject(page);
  await mockOverview(page, overviewFixture('AttentionRequired'));
  await page.goto('/projects/7/library/governance');

  await expect(page.getByTestId('flow.governance.overview')).toBeVisible();
  await expect(page.getByTestId('flow.governance.status')).toHaveText('Attention required');
  await expect(page.getByTestId('flow.governance.attention')).toContainText('WI-104');
  await expect(page.getByTestId('flow.governance.controls')).toContainText('Controlled apply only');
  await expect(page.getByTestId('flow.governance.exceptions')).toContainText('Partial source mutation');
  await expect(page.getByTestId('flow.governance.decisions')).toContainText('Alice Reviewer');
  await expect(page.getByTestId('flow.governance.boundary')).toContainText('grants no approval');
  await expect(page.getByRole('button', { name: 'Review interrupted apply' })).toHaveCount(1);
  await expect(page.getByRole('button', { name: /approve|continue workflow|apply source|rollback/i })).toHaveCount(0);

  await page.getByTestId('flow.governance.primaryAction').click();
  await expect(page).toHaveURL('/projects/7/work-items/104');
});

test('controls-active overview renders honest empty states without a primary command', async ({ page }) => {
  await prepareSelectedProject(page);
  const model = overviewFixture('ControlsActive');
  model.primaryAction = { kind: 'None', label: 'No action required', summary: 'No governance action is waiting.', workItemId: null, targetRoute: null };
  model.attentionItems = [];
  model.exceptions = [];
  model.recentDecisions = [];
  model.statusSummary = 'Required controls are active and no governance action is waiting.';
  await mockOverview(page, model);
  await page.goto('/projects/7/library/governance');

  await expect(page.getByTestId('flow.governance.status')).toHaveText('Controls active');
  await expect(page.getByTestId('flow.governance.attention')).toContainText('No governance action is waiting.');
  await expect(page.getByTestId('flow.governance.exceptions')).toContainText('No active governance exceptions');
  await expect(page.getByTestId('flow.governance.primaryAction')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText('Safe');
  await expect(page.locator('body')).not.toContainText('Fully compliant');
});

test('cross-project and malformed backend routes remain inert', async ({ page }) => {
  await prepareSelectedProject(page);
  const model = overviewFixture('AttentionRequired');
  model.primaryAction.targetRoute = '/projects/8/work-items/104';
  model.attentionItems![0].targetRoute = '/projects/8/work-items/104';
  model.exceptions![0].targetRoute = '//outside.example/project';
  model.recentDecisions![0].targetRoute = '/not-a-product-route';
  model.navigation.audit = '/projects/8/library/audit';
  model.navigation.settings = 'https://outside.example/settings';
  await mockOverview(page, model);
  await page.goto('/projects/7/library/governance');

  await expect(page.getByTestId('flow.governance.primaryAction')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'View audit' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Open settings' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Open WI-104|Inspect|Open Work Item/ })).toHaveCount(0);
});

test('degraded overview exposes partial section failure without hiding available controls', async ({ page }) => {
  await prepareSelectedProject(page);
  const model = overviewFixture('Degraded');
  model.sectionIssues = [{ section: 'WorkItem:104', status: 'Unavailable', summary: 'Apply evidence could not be evaluated.', retryable: true }];
  model.statusSummary = 'Governance is degraded because one required section could not be evaluated.';
  await mockOverview(page, model);
  await page.goto('/projects/7/library/governance');

  await expect(page.getByTestId('flow.governance.status')).toHaveText('Degraded');
  await expect(page.getByRole('heading', { name: 'Some governance evidence is unavailable' })).toBeVisible();
  await expect(page.getByText('Apply evidence could not be evaluated.')).toBeVisible();
  await expect(page.getByTestId('flow.governance.controls')).toContainText('Human approval');
});

test('unavailable overview retries the same backend-owned read', async ({ page }) => {
  await prepareSelectedProject(page);
  let attempts = 0;
  const overviewPattern = '**/irondev-api/api/projects/7/governance/overview';
  await page.route(overviewPattern, async (route) => {
    attempts += 1;
    await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: 'Unavailable' }) });
  });
  await page.goto('/projects/7/library/governance');

  await expect(page.getByRole('heading', { name: 'Governance is unavailable' })).toBeVisible();
  await page.unroute(overviewPattern);
  await mockOverview(page, overviewFixture('ControlsActive'));
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByTestId('flow.governance.overview')).toBeVisible();
  expect(attempts).toBeGreaterThanOrEqual(1);
});

test('governance overview is keyboard reachable and does not overflow at 390px', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await prepareSelectedProject(page);
  await mockOverview(page, overviewFixture('AttentionRequired'));
  await page.goto('/projects/7/library/governance');

  await page.getByTestId('flow.governance.primaryAction').focus();
  await expect(page.getByTestId('flow.governance.primaryAction')).toBeFocused();
  const dimensions = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
});

test('canonical Governance drilldowns reuse backend truth and return to overview', async ({ page }) => {
  await prepareSelectedProject(page);
  await mockOverview(page, overviewFixture('AttentionRequired'));
  await page.goto('/projects/7/library/governance/controls');

  await expect(page.getByRole('button', { name: 'Controls', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByTestId('flow.governance.controls')).toContainText('Controlled apply only');
  await expect(page.getByTestId('flow.governance.attention')).toHaveCount(0);
  await page.getByRole('button', { name: 'Back to overview' }).click();
  await expect(page).toHaveURL('/projects/7/library/governance');
  await expect(page.getByTestId('flow.governance.attention')).toBeVisible();
});

test('technical evidence groups compatibility viewers and preserves their deep links', async ({ page }) => {
  await prepareSelectedProject(page);
  await mockOverview(page, overviewFixture('AttentionRequired'));
  await page.route('**/irondev-api/api/v1/governance/traces**', async (route) => fulfillJson(route, {
    status: 'governance_traces_found', data: { items: [], totalCount: 0 }, boundary: {}, warnings: [], errors: []
  }));
  await page.goto('/projects/7/library/governance/technical');

  await expect(page.getByTestId('flow.governance.technical')).toContainText('Runs and operations');
  await expect(page.getByTestId('flow.governance.technical')).toContainText('Approvals and policy');
  await expect(page.getByTestId('flow.governance.technical')).toContainText('Tools and memory');
  await expect(page.getByTestId('flow.governanceHost')).toHaveCount(0);
  await page.getByRole('button', { name: 'Governance timeline Audit technical traces' }).click();
  await expect(page).toHaveURL('/governance/timeline');
  await expect(page.getByTestId('flow.governanceHost')).toBeVisible();
  await expect(page.getByTestId('flow.governance.compatibilityNotice')).toContainText('Legacy evidence view');
  await expect(page.locator('.fl-chips')).toHaveCount(0);
  await page.getByRole('link', { name: 'Back to Governance' }).click();
  await expect(page).toHaveURL('/projects/7/library/governance');
  await expect(page.getByTestId('flow.governance.overview')).toBeVisible();
});

async function prepareSelectedProject(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await page.route('**/irondev-api/health', async (route) => fulfillJson(route, { status: 'ok' }));
  await page.route('**/irondev-api/api/environment**', async (route) => fulfillJson(route, {
    environment: 'LocalTest', database: 'IronDeveloper_Test', weaviatePrefix: 'irondev_test', isTestEnvironment: true,
    workspaceRoot: 'C:\\IronDevTestWorkspaces\\', logsRoot: 'C:\\IronDevTestLogs\\', dangerRealRepoWritesEnabled: false
  }));
  await page.route('**/irondev-api/api/auth/me**', async (route) => fulfillJson(route, {
    userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3
  }));
  await page.route('**/irondev-api/api/tenants', async (route) => fulfillJson(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }]));
  await page.route('**/irondev-api/api/projects', async (route) => fulfillJson(route, [{ id: 7, tenantId: 3, name: 'IronDeveloper' }]));
  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => fulfillJson(route, {
    projectId: 7,
    tenantId: 3,
    name: 'IronDeveloper',
    projectLifecyclePhase: 'Shaping',
    executionReadiness: 'NotConfigured',
    repositoryBinding: null,
    workbenchSessionId: 7007,
    leaseEpoch: 1,
    wasResumed: true,
    wasTakenOver: false,
    clientOperationId: '00000000-0000-0000-0000-000000000007'
  }));
  await page.route('**/irondev-api/api/projects/7/**', async (route) => fulfillJson(route, {}));
  await page.route('**/irondev-api/api/projects/7/notifications**', async (route) => fulfillJson(route, {
    projectId: 7, unreadCount: 0, notifications: [], boundary: 'Notification visibility grants no authority.'
  }));
  await page.route('**/irondev-api/api/projects/7/work-items/104', async (route) => {
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
  });
  await mockProjectBoard(page, { projectName: 'IronDeveloper', tickets: [] });
}

async function mockOverview(page: Page, model: ProjectGovernanceOverview) {
  await page.route('**/irondev-api/api/projects/7/governance/overview', async (route) => fulfillJson(route, model));
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}

function overviewFixture(overallStatus: 'ControlsActive' | 'AttentionRequired' | 'Degraded'): ProjectGovernanceOverview {
  return {
    projectId: 7,
    projectName: 'IronDeveloper',
    overallStatus,
    statusSummary: 'One item requires a human recovery response.',
    generatedUtc: '2026-07-12T03:00:00Z',
    version: '1',
    primaryAction: {
      kind: 'InterruptedApply', label: 'Review interrupted apply', summary: 'Source mutation may have occurred.',
      workItemId: 104, targetRoute: '/projects/7/work-items/104'
    },
    attentionItems: [{
      workItemId: 104, workItemReference: 'WI-104', title: 'Recover interrupted apply', kind: 'InterruptedApply',
      severity: 'Critical', summary: 'Source mutation may have occurred before failure.', waitingOn: 'Project Owner',
      recordedUtc: '2026-07-12T02:46:00Z', nextSafeAction: 'Inspect receipts and choose a recovery action.',
      targetRoute: '/projects/7/work-items/104'
    }],
    controls: [{
      id: 'human-approval', group: 'Human authority', label: 'Human approval', effectiveValue: 'Required',
      explanation: 'Governed continuation requires accepted human approval evidence.', source: 'IronDev invariant',
      configurable: false, detailRoute: '/projects/7/library/governance/controls'
    }, {
      id: 'controlled-apply', group: 'Source mutation', label: 'Source mutation', effectiveValue: 'Controlled apply only',
      explanation: 'Apply remains a separate governed operation.', source: 'IronDev invariant', configurable: false,
      detailRoute: '/projects/7/library/governance/controls'
    }],
    exceptions: [{
      id: 'apply-recovery-104', category: 'PartialMutationRisk', severity: 'Critical',
      title: 'Partial source mutation requires review', summary: 'Source mutation may have occurred before failure.',
      recordedUtc: '2026-07-12T02:46:00Z', workItemId: 104, targetRoute: '/projects/7/work-items/104'
    }],
    recentDecisions: [{
      id: '104:SkeletonFindingDispositionRecorded:1', kind: 'SkeletonFindingDispositionRecorded',
      summary: 'Finding F-1 was dispositioned.', actorDisplayName: 'Alice Reviewer', workItemId: 104,
      recordedUtc: '2026-07-12T02:40:00Z', targetRoute: '/projects/7/work-items/104'
    }],
    navigation: {
      overview: '/projects/7/library/governance', controls: '/projects/7/library/governance/controls',
      exceptions: '/projects/7/library/governance/exceptions', decisions: '/projects/7/library/governance/decisions',
      technical: '/projects/7/library/governance/technical', audit: '/projects/7/library/audit',
      settings: '/projects/7/library/settings'
    },
    sectionIssues: [],
    boundary: 'Governance reports effective controls, pending decisions, exceptions, and evidence. It grants no approval, continues no workflow, and applies no source.'
  };
}
