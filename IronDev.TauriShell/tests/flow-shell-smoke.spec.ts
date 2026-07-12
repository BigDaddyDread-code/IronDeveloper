import { expect, test } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';

// Flow-shell smoke: sign-in gate, board, shape stage, settings users, and a
// governance deep link rendering inside the Library. Replaces the old
// tickets-shell-smoke, which asserted the eight-workspace shell.

test('normal sign-in appears before any product surface', async ({ page }) => {
  await mockHealthyApi(page);
  await page.goto('/');

  await expect(page.getByTestId('auth.route')).toBeVisible();
  await expect(page.getByTestId('flow.shell')).toHaveCount(0);
  await expect(page.getByTestId('auth.form')).toBeVisible();
  await expect(page.getByTestId('auth.email')).toHaveValue('bob@irondev.local');
  await expect(page.getByTestId('auth.password')).toHaveValue('change-me-local-only');
  await expect(page.getByTestId('auth.flowHint')).toHaveText('Sign in, then select a project to continue.');
  await expect(page.getByTestId('auth.submit')).toBeVisible();
  await expect(page.getByTestId('auth.apiStatusChip')).toContainText('LocalTest');
  await expect(page.getByTestId('app.authState.configureToken')).toHaveCount(0);
  await expect(page.getByTestId('tenant.selector')).toHaveCount(0);
  await expect(page.getByTestId('project.selector')).toHaveCount(0);
});

test('invalid credentials render one inline sign-in error', async ({ page }) => {
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/login', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'Invalid email or password.' })
    });
  });
  await page.goto('/');

  await page.getByTestId('auth.password').fill('wrong-password');
  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('auth.error')).toHaveText('LocalTest sign in failed. Reset the LocalTest data and retry.');
  await expect(page.getByTestId('auth.error')).toHaveCount(1);
  await expect(page.locator('body')).not.toContainText('TOKEN REJECTED');
  await expect(page.locator('body')).not.toContainText('Authentication failed');
});

test('valid LocalTest login auto-selects one tenant and lands on project chooser', async ({ page }) => {
  await mockHealthyApi(page);
  await mockLoginToSingleTenantChooser(page);
  await page.goto('/');

  await page.getByTestId('auth.submit').click();

  await expect(page.getByTestId('flow.tenantChooser')).toHaveCount(0);
  await expect(page.getByTestId('flow.chooser')).toBeVisible();
  await expect(page.getByTestId('flow.chooser.project.7')).toContainText('BookSeller');
});

test('multiple tenants render a separate tenant chooser', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
  });
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Robert', selectedTenantId: null })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 3, name: 'Local Test Tenant', slug: 'local-test' },
        { id: 4, name: 'Client Tenant', slug: 'client' }
      ])
    });
  });
  await page.goto('/');

  await expect(page.getByTestId('flow.tenantChooser')).toBeVisible();
  await expect(page.getByText('Welcome, Robert')).toBeVisible();
  await expect(page.getByTestId('auth.form')).toHaveCount(0);
  await expect(page.getByTestId('flow.chooser')).toHaveCount(0);
});

test('board renders pipeline columns with project tickets', async ({ page }) => {
  await mockSelectedProject(page);
  await page.goto('/');

  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toBeVisible();
  await expect(page.getByTestId('flow.board.columns')).toContainText('Shape');
  await expect(page.getByTestId('flow.board.columns')).toContainText('Done');
  await expect(page.getByTestId('flow.board.columns')).toContainText('Add book sorting to catalog');
});

test('shape stage earns promotion through the readiness gate', async ({ page }) => {
  await mockSelectedProject(page);
  await page.goto('/');

  await page.getByTestId('flow.board.new').click();
  await expect(page.getByTestId('flow.stagerail')).toBeVisible();
  await expect(page.getByTestId('flow.shape.gate')).toContainText('blocked');
  await expect(page.getByTestId('flow.shape.promote')).toBeDisabled();

  await page.getByTestId('flow.shape.prompt').fill('Users need to sort the catalog by title.');
  await page.getByTestId('flow.shape.prompt').press('Enter');
  await expect(page.getByTestId('flow.contract')).toBeVisible();

  await page.getByTestId('flow.shape.addCriterion').fill('Catalog sorts by title ascending');
  await page.getByTestId('flow.shape.addCriterion').press('Enter');
  await page.getByRole('button', { name: 'Confirm' }).click();

  await expect(page.getByTestId('flow.shape.gate')).toContainText('satisfied');
  await expect(page.getByTestId('flow.shape.promote')).toBeEnabled();
});

test('settings labels membership handoff and the local policy draft honestly', async ({ page }) => {
  await mockSelectedProject(page);
  await page.goto('/');

  await page.getByTestId('flow.userMenu').click();
  await page.getByTestId('flow.nav.settings').click();
  await expect(page.getByTestId('flow.settings.hub')).toBeVisible();
  await expect(page.getByRole('tab')).toHaveCount(6);
  await expect(page.getByTestId('flow.settings.panel.project')).toContainText('IronDeveloper');
  await expect(page.getByTestId('flow.settings.agents')).toHaveCount(0);
  await page.getByTestId('flow.settings.section.safety').click();
  await expect(page.getByTestId('flow.settings.banner')).toContainText('Tenant membership and roles live under Library > Members');
  await expect(page.getByTestId('flow.settings.banner')).toContainText('approval policy below remains a local draft');
  await expect(page.getByTestId('flow.settings.savePolicy')).toBeVisible();
  await page.getByTestId('flow.settings.section.runtime').click();
  await expect(page.getByTestId('flow.settings.runtime')).toContainText('LocalTest');
});

test('library members lists backend-owned project membership', async ({ page }) => {
  await mockSelectedProject(page);
  await mockMembersDirectory(page);
  await page.goto('/');

  await page.getByTestId('flow.nav.library').click();
  await page.getByTestId('flow.library.nav.members').click();
  await expect(page).toHaveURL('/projects/7/library/members');
  await expect(page.getByTestId('flow.members.directory')).toBeVisible();
  await expect(page.getByTestId('flow.members.row.7')).toContainText('Dev User (you)');
  await expect(page.getByTestId('flow.members.row.8')).toContainText('Viewer User');
  await expect(page.getByTestId('flow.members.row.8')).toContainText('Project member');
});

test('governance deep link renders the timeline viewer inside the Library', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/governance/**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ data: { items: [], totalCount: 0 }, errors: [] })
    });
  });
  await page.goto('/governance/timeline');

  await expect(page.getByTestId('flow.shell')).toBeVisible();
  await expect(page.getByTestId('flow.governanceHost')).toBeVisible();
  await expect(page.getByTestId('flow.governance.compatibilityNotice')).toContainText('Legacy evidence view');
  await expect(page.locator('.fl-chips')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Governance Timeline' })).toBeVisible();
});

test('audit library route renders read-only ledger rows and filters', async ({ page }) => {
  await mockSelectedProject(page);
  const auditRequests: string[] = [];
  await page.route('**/irondev-api/api/v1/audit/ledger**', async (route) => {
    const url = route.request().url();
    auditRequests.push(url);
    const filtered = new URL(url).searchParams.get('actor');
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(auditLedgerResponse(filtered ?? undefined))
    });
  });
  await page.goto('/projects/7/library/audit');

  await expect(page.getByTestId('flow.library.auditLedger')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Project audit', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.audit.rows')).toContainText('AcceptedApprovalRecorded');
  await expect(page.getByTestId('flow.audit.rows')).toContainText('Alice Reviewer');
  await expect(page.getByTestId('flow.audit.rows')).toContainText('WI-42');
  await expect(page.getByTestId('flow.audit.rows')).toContainText('WorkflowContinuationInput');
  await expect(page.getByTestId('flow.audit.evidence').first()).toContainText('Accepted approval');
  await expect(page.getByTestId('flow.library.auditLedger')).not.toContainText('Not implemented');
  await expect(page.getByRole('button', { name: /approve|apply|continue/i })).toHaveCount(0);

  await page.getByTestId('flow.audit.filter.actor').fill('Alice');
  await page.getByTestId('flow.audit.filter.apply').click();

  await expect(page.getByTestId('flow.audit.rows')).toContainText('Filtered for Alice');
  expect(auditRequests.some((url) => new URL(url).searchParams.get('projectId') === '7')).toBeTruthy();
  expect(auditRequests.some((url) => new URL(url).searchParams.get('actor') === 'Alice')).toBeTruthy();
});

test('audit export previews applied filters before enabling JSON download', async ({ page }) => {
  await mockSelectedProject(page);
  await page.route('**/irondev-api/api/v1/audit/ledger**', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(auditLedgerResponse()) });
  });
  const exportRequests: string[] = [];
  await page.route('**/irondev-api/api/projects/7/audit/export**', async (route) => {
    exportRequests.push(route.request().url());
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        schemaVersion: '1', projectId: 7, projectName: 'IronDeveloper', generatedUtc: '2026-07-12T05:00:00Z',
        filters: { actor: 'Alice', take: 250 }, returnedCount: 2, take: 250, truncated: false,
        itemsSha256: 'a'.repeat(64), items: [], warnings: [],
        boundary: { readOnly: true, grantsAuthority: false, boundaryStatement: 'Read-only export. It grants no authority.' }
      })
    });
  });
  await page.goto('/projects/7/library/audit');

  await page.getByTestId('flow.audit.filter.actor').fill('Alice');
  await page.getByTestId('flow.audit.filter.apply').click();
  await page.getByTestId('flow.audit.export.open').click();
  await expect(page.getByTestId('flow.audit.export.dialog')).toContainText('Alice');
  await expect(page.getByTestId('flow.audit.export.download')).toBeDisabled();
  await page.getByTestId('flow.audit.export.generate').click();
  await expect(page.getByTestId('flow.audit.export.result')).toContainText('2');
  await expect(page.getByTestId('flow.audit.export.result')).toContainText('a'.repeat(64));
  await expect(page.getByTestId('flow.audit.export.download')).toBeEnabled();
  expect(new URL(exportRequests[0]).searchParams.get('actor')).toBe('Alice');
  expect(new URL(exportRequests[0]).searchParams.get('take')).toBe('250');
});

async function mockHealthyApi(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/health', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'healthy' }) });
  });
  await page.route('**/irondev-api/api/environment', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        environment: 'LocalTest',
        database: 'IronDeveloper_Test',
        weaviatePrefix: 'irondev_test',
        isTestEnvironment: true,
        workspaceRoot: 'C:\\IronDevTestWorkspaces\\',
        logsRoot: 'C:\\IronDevTestLogs\\',
        dangerRealRepoWritesEnabled: false
      })
    });
  });
}

async function mockSelectedProject(page: import('@playwright/test').Page) {
  const boardTickets = [
    { id: 42, tenantId: 3, projectId: 7, title: 'Add book sorting to catalog', status: 'Draft', acceptanceCriteria: null }
  ];
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
  await mockHealthyApi(page);
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ userId: 7, email: 'dev@iron.dev', displayName: 'Dev User', selectedTenantId: 3 })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
    });
  });
  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'IronDeveloper', description: 'Dogfood project' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/select', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ projectId: 7 }) });
  });
  await page.route('**/irondev-api/api/projects/7/tickets', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(boardTickets)
    });
  });
  await mockProjectBoard(page, { projectName: 'IronDeveloper', tickets: boardTickets });
  await page.route('**/irondev-api/api/projects/7/chat/complete', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        response: 'Proposed criteria are ready — confirm them in the contract.',
        contextSummary: 'Shaping context',
        linkedFilePaths: 'src/Catalog/CatalogService.cs'
      })
    });
  });
}

async function mockMembersDirectory(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/api/projects/7/members**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        tenantId: 3,
        projectId: 7,
        projectName: 'IronDeveloper',
        currentUserId: 7,
        canManageTenantUsers: true,
        canManageProjectMembers: true,
        canManageChannels: true,
        tenantMembershipStatus: '2 active tenant users',
        projectMembershipStatus: '2 active members',
        channelMembershipStatus: '0 active channels',
        members: [
          {
            userId: 7,
            displayName: 'Dev User',
            email: 'dev@iron.dev',
            tenantRole: 'Owner',
            projectRole: 'Owner',
            isProjectMember: true,
            isActive: true,
            isCurrentUser: true,
            projectAccessStatus: 'Project member',
            channelMembershipSummary: 'No explicit memberships'
          },
          {
            userId: 8,
            displayName: 'Viewer User',
            email: 'viewer@iron.dev',
            tenantRole: 'Viewer',
            projectRole: 'Viewer',
            isProjectMember: true,
            isActive: true,
            isCurrentUser: false,
            projectAccessStatus: 'Project member',
            channelMembershipSummary: 'No explicit memberships'
          }
        ],
        channels: [],
        boundary: 'Project membership controls visibility only. It does not grant approval, workflow, tool, or source mutation authority.'
      })
    });
  });
}

async function mockLoginToSingleTenantChooser(page: import('@playwright/test').Page) {
  await page.route('**/irondev-api/api/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'base-token', userId: 7, displayName: 'Local Test User' })
    });
  });
  await page.route('**/irondev-api/api/auth/me**', async (route) => {
    const authorization = route.request().headers().authorization ?? '';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        userId: 7,
        email: 'bob@irondev.local',
        displayName: 'Local Test User',
        selectedTenantId: authorization.includes('tenant-token') ? 3 : null
      })
    });
  });
  await page.route('**/irondev-api/api/tenants', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 3, name: 'Local Test Tenant', slug: 'local-test' }])
    });
  });
  await page.route('**/irondev-api/api/tenants/select', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'tenant-token', userId: 7, displayName: 'Local Test User' })
    });
  });
  await page.route('**/irondev-api/api/projects', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
    });
  });
  await page.route('**/irondev-api/api/projects/7/provisioning/readiness', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        projectId: 7,
        isReady: true,
        blockedCount: 0,
        blockedStates: [],
        checks: [],
        nextAction: { kind: 'OpenBoard', checkCode: null, allowed: true, reasonCode: null, label: 'Open Board', nextSafeAction: 'Open the project Board.' },
        proposedProfile: null,
        boundary: 'Readiness is computed from stored truth and scan evidence.'
      })
    });
  });
  await mockProjectBoard(page, { projectName: 'BookSeller' });
}

function auditLedgerResponse(filteredActor?: string) {
  return {
    status: 'ok',
    boundary: {
      readOnly: true,
      grantsAuthority: false,
      canApprove: false,
      canContinueWorkflow: false,
      canApplySource: false,
      exposesRawPayloadJson: false,
      boundaryStatement: 'The audit ledger is read-only traceability. It does not approve, continue, apply, or grant authority.'
    },
    warnings: ['Rows do not approve, continue, apply, release, deploy, or grant authority.'],
    issues: [],
    returnedCount: 2,
    take: 100,
    items: [
      {
        ledgerId: 'accepted-approval:aaa',
        timeUtc: '2026-07-10T19:00:00Z',
        projectId: 7,
        projectName: 'IronDeveloper',
        workItemId: 42,
        workItemTitle: filteredActor ? `Filtered for ${filteredActor}` : 'Add book sorting to catalog',
        source: 'AcceptedApproval',
        actorId: '8',
        actorDisplayName: 'Alice Reviewer',
        action: 'AcceptedApprovalRecorded',
        outcome: 'WorkflowContinuationInput',
        summary: 'Accepted approval recorded for workflow continuation.',
        correlationId: 'run-42',
        evidenceLinks: [{ label: 'Accepted approval', href: '/governance/accepted-approvals?targetId=run-42' }]
      },
      {
        ledgerId: 'member-added:1',
        timeUtc: '2026-07-10T18:30:00Z',
        projectId: 7,
        projectName: 'IronDeveloper',
        workItemId: null,
        workItemTitle: null,
        source: 'ProjectMembership',
        actorId: '7',
        actorDisplayName: 'Dev User',
        action: 'ProjectMemberAdded',
        outcome: 'Owner',
        summary: 'Dev User joined as Owner.',
        correlationId: 'project:7',
        evidenceLinks: [{ label: 'Project members', href: '/projects/7/library/members' }]
      }
    ]
  };
}
