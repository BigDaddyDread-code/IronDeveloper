import { expect, test, type Page, type Route } from '@playwright/test';

test('Members shows tenant identities and backend-owned channel scope', async ({ page }) => {
  await mockMembersWorkspace(page);
  await page.goto('/projects/7/library/members');

  const directory = page.getByTestId('flow.members.directory');
  await expect(directory).toBeVisible();
  await expect(directory).toContainText('Your tenant roleOwner');
  await expect(directory).toContainText('Project membershipNot implemented');
  await expect(directory).toContainText('Channel membership2 active channels');
  await expect(page.getByTestId('flow.members.row.7')).toContainText('Bob Developer (you)');
  await expect(page.getByTestId('flow.members.row.7')).toContainText('Tenant scoped');
  await expect(page.getByTestId('flow.members.row.8')).toContainText('Alice Reviewer');
  await expect(page.getByTestId('flow.members.add.toggle')).toBeVisible();
  await expect(page.getByTestId('flow.members.channel.101')).toContainText('General');
  await expect(page.getByTestId('flow.members.channel.101')).toContainText('Project visible');
  await expect(page.getByTestId('flow.members.channel.102')).toContainText('Members only');
});

test('channel role, notification, add, and removal use explicit backend mutations', async ({ page }) => {
  const state = await mockMembersWorkspace(page);
  await page.goto('/projects/7/library/members');

  await page.getByTestId('flow.members.channel.101.role.8').selectOption('Moderator');
  await page.getByTestId('flow.members.channel.101.notifications.8').selectOption('All');
  await page.getByTestId('flow.members.channel.101.save.8').click();
  await expect(page.getByTestId('flow.members.notice')).toContainText("Alice Reviewer's General membership was saved.");

  await page.getByTestId('flow.members.channel.102.add.member').selectOption('8');
  await page.getByTestId('flow.members.channel.102.add.role').selectOption('Member');
  await page.getByTestId('flow.members.channel.102.add.notifications').selectOption('Mentions');
  await page.getByTestId('flow.members.channel.102.add.submit').click();
  await expect(page.getByTestId('flow.members.channel.102.member.8')).toContainText('Alice Reviewer');

  await page.getByTestId('flow.members.channel.102.remove.8').click();
  await expect(page.getByTestId('flow.members.channel.remove.confirm')).toContainText('does not alter tenant access');
  await page.getByTestId('flow.members.channel.remove.submit').click();
  await expect(page.getByTestId('flow.members.channel.102.member.8')).toHaveCount(0);
  expect(state.channelPutRequests).toBe(2);
  expect(state.channelDeleteRequests).toBe(1);
});

test('the last channel owner refusal restores backend membership truth', async ({ page }) => {
  await mockMembersWorkspace(page, { refuseLastChannelOwnerChange: true });
  await page.goto('/projects/7/library/members');

  await page.getByTestId('flow.members.channel.102.role.7').selectOption('Member');
  await page.getByTestId('flow.members.channel.102.save.7').click();

  await expect(page.getByTestId('flow.members.error')).toContainText("The channel's last owner cannot be demoted or removed.");
  await expect(page.getByTestId('flow.members.channel.102.role.7')).toHaveValue('Owner');
  await expect(page.getByTestId('flow.members.notice')).toHaveCount(0);
});

test('an eligible owner adds a tenant member through an explicit form', async ({ page }) => {
  const state = await mockMembersWorkspace(page);
  await page.goto('/projects/7/library/members');

  await page.getByTestId('flow.members.add.toggle').click();
  await page.getByTestId('flow.members.add.email').fill('sam@irondev.local');
  await page.getByTestId('flow.members.add.name').fill('Sam Operator');
  await page.getByTestId('flow.members.add.password').fill('local-only-password');
  await page.getByTestId('flow.members.add.role').selectOption('Operator');
  await page.getByTestId('flow.members.add.submit').click();

  await expect(page.getByTestId('flow.members.notice')).toContainText('Sam Operator was added to the tenant.');
  await expect(page.getByTestId('flow.members.row.9')).toContainText('Operator');
  expect(state.addRequests).toBe(1);
  expect(state.lastAddBody).toMatchObject({
    email: 'sam@irondev.local',
    displayName: 'Sam Operator',
    password: 'local-only-password',
    role: 'Operator'
  });
});

test('role changes require Save and removal requires confirmation', async ({ page }) => {
  const state = await mockMembersWorkspace(page);
  await page.goto('/projects/7/library/members');

  await page.getByTestId('flow.members.role.8').selectOption('Reviewer');
  expect(state.roleRequests).toBe(0);
  await page.getByTestId('flow.members.role.save.8').click();
  await expect(page.getByTestId('flow.members.notice')).toContainText("Alice Reviewer's tenant role is now Reviewer.");
  expect(state.roleRequests).toBe(1);

  await page.getByTestId('flow.members.remove.8').click();
  await expect(page.getByTestId('flow.members.remove.confirm')).toContainText('Authored messages, versions, decisions, and receipts retain their attribution.');
  expect(state.removeRequests).toBe(0);
  await page.getByTestId('flow.members.remove.submit').click();
  await expect(page.getByTestId('flow.members.notice')).toContainText('Alice Reviewer was removed from the tenant.');
  await expect(page.getByTestId('flow.members.row.8')).toHaveCount(0);
  expect(state.removeRequests).toBe(1);
});

test('the last-owner refusal reloads the backend role and never shows fake success', async ({ page }) => {
  const state = await mockMembersWorkspace(page, { refuseLastOwnerDemotion: true });
  await page.goto('/projects/7/library/members');

  await page.getByTestId('flow.members.role.7').selectOption('Viewer');
  await page.getByTestId('flow.members.role.save.7').click();

  await expect(page.getByTestId('flow.members.error')).toContainText("The tenant's last owner cannot be demoted or removed.");
  await expect(page.getByTestId('flow.members.role.7')).toHaveValue('Owner');
  await expect(page.getByTestId('flow.members.notice')).toHaveCount(0);
  expect(state.roleRequests).toBe(1);
});

test('an accepted change with a failed reload never reports false failure or success', async ({ page }) => {
  await mockMembersWorkspace(page, { failRefreshAfterMutation: true });
  await page.goto('/projects/7/library/members');

  await page.getByTestId('flow.members.role.8').selectOption('Reviewer');
  await page.getByTestId('flow.members.role.save.8').click();

  await expect(page.getByTestId('flow.members.error')).toContainText('The backend accepted the change');
  await expect(page.getByTestId('flow.members.error')).toContainText('could not be reloaded');
  await expect(page.getByTestId('flow.members.notice')).toHaveCount(0);
});

test('a Viewer receives the readable directory without administration controls', async ({ page }) => {
  await mockMembersWorkspace(page, { canAdminister: false, currentUserId: 8, currentRole: 'Viewer' });
  await page.goto('/projects/7/library/members');

  await expect(page.getByTestId('flow.members.readOnly')).toContainText('requires Owner or Tenant admin');
  await expect(page.getByTestId('flow.members.row.7')).toBeVisible();
  await expect(page.getByTestId('flow.members.row.8')).toContainText('(you)');
  await expect(page.getByTestId('flow.members.add.toggle')).toHaveCount(0);
  await expect(page.locator('[data-testid^="flow.members.role."]')).toHaveCount(0);
  await expect(page.locator('[data-testid^="flow.members.remove."]')).toHaveCount(0);
});

test('member-directory failure preserves the route and retries backend truth', async ({ page }) => {
  const state = await mockMembersWorkspace(page, { directoryFailures: 2 });
  await page.goto('/projects/7/library/members');

  await expect(page.getByRole('heading', { name: 'Members are unavailable', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome')).toContainText('Member directory unavailable.');
  await expect(page).toHaveURL('/projects/7/library/members');
  await page.getByTestId('flow.routeOutcome.primary').click();

  await expect(page.getByTestId('flow.members.directory')).toBeVisible();
  expect(state.directoryRequests).toBe(3);
});

test('an unknown project returns an honest Members 404', async ({ page }) => {
  await mockMembersWorkspace(page, { directoryErrorStatus: 404 });
  await page.goto('/projects/7/library/members');

  await expect(page.getByRole('heading', { name: 'Project member directory not found', exact: true })).toBeVisible();
  await expect(page.getByTestId('flow.routeOutcome.kind')).toContainText('404');
  await expect(page.locator('body')).not.toContainText('Alice Reviewer');
});

test.describe('narrow Members', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('keeps the directory and add-member ceremony inside the viewport', async ({ page }) => {
    await mockMembersWorkspace(page);
    await page.goto('/projects/7/library/members');
    await expect(page.getByTestId('flow.members.directory')).toBeVisible();
    await page.getByTestId('flow.members.add.toggle').click();
    await expect(page.getByTestId('flow.members.add.form')).toBeVisible();

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  });
});

interface MembersMockOptions {
  canAdminister?: boolean;
  currentUserId?: number;
  currentRole?: string;
  directoryFailures?: number;
  directoryErrorStatus?: number;
  refuseLastOwnerDemotion?: boolean;
  failRefreshAfterMutation?: boolean;
  refuseLastChannelOwnerChange?: boolean;
}

interface MembersMockState {
  directoryRequests: number;
  addRequests: number;
  roleRequests: number;
  removeRequests: number;
  channelPutRequests: number;
  channelDeleteRequests: number;
  lastAddBody: Record<string, unknown> | null;
}

const availableTenantRoles = ['Owner', 'TenantAdmin', 'Approver', 'Reviewer', 'Operator', 'Viewer', 'Member'];

async function mockMembersWorkspace(page: Page, options: MembersMockOptions = {}): Promise<MembersMockState> {
  const state: MembersMockState = {
    directoryRequests: 0,
    addRequests: 0,
    roleRequests: 0,
    removeRequests: 0,
    channelPutRequests: 0,
    channelDeleteRequests: 0,
    lastAddBody: null
  };
  let failuresRemaining = options.directoryFailures ?? 0;
  let failNextDirectory = false;
  const currentUserId = options.currentUserId ?? 7;
  const members = [
    {
      userId: 7,
      displayName: 'Bob Developer',
      email: 'bob@irondev.local',
      tenantRole: 'Owner',
      isActive: true,
      isCurrentUser: currentUserId === 7,
      projectAccessStatus: 'Tenant scoped',
      channelMembershipSummary: 'General, Product planning'
    },
    {
      userId: 8,
      displayName: 'Alice Reviewer',
      email: 'alice@irondev.local',
      tenantRole: 'Viewer',
      isActive: true,
      isCurrentUser: currentUserId === 8,
      projectAccessStatus: 'Tenant scoped',
      channelMembershipSummary: 'General'
    }
  ];
  const channels = [
    {
      channelId: 101,
      name: 'General',
      description: 'Project-wide engineering discussion.',
      channelKind: 'General',
      visibility: 'Project',
      memberCount: 2,
      members: [
        { userId: 7, channelRole: 'Owner', notificationLevel: 'All' },
        { userId: 8, channelRole: 'Member', notificationLevel: 'Mentions' }
      ],
      boundary: 'Channel membership controls visibility and moderation only.'
    },
    {
      channelId: 102,
      name: 'Product planning',
      description: 'Restricted product and UX discussion.',
      channelKind: 'Custom',
      visibility: 'MembersOnly',
      memberCount: 1,
      members: [{ userId: 7, channelRole: 'Owner', notificationLevel: 'Mentions' }],
      boundary: 'Channel membership controls visibility and moderation only.'
    }
  ];

  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });

  await page.route('**/irondev-api/health', (route) => json(route, { status: 'healthy' }));
  await page.route('**/irondev-api/api/environment', (route) =>
    json(route, { environment: 'LocalTest', database: 'IronDeveloper_Test', isTestEnvironment: true })
  );
  await page.route('**/irondev-api/api/auth/me**', (route) =>
    json(route, { userId: currentUserId, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    json(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    json(route, [{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
  );
  await page.route('**/irondev-api/api/projects/7/select', (route) => json(route, { projectId: 7 }));
  await page.route('**/irondev-api/api/projects/7/tickets', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => json(route, []));

  await page.route(/\/irondev-api\/api\/tenants\/3\/users\/(\d+)\/role$/, async (route) => {
    state.roleRequests += 1;
    const userId = Number(route.request().url().match(/users\/(\d+)\/role$/)?.[1]);
    const body = route.request().postDataJSON() as { role: string };
    if (options.refuseLastOwnerDemotion && userId === 7 && body.role !== 'Owner') {
      return json(route, { error: "The tenant's last owner cannot be demoted or removed." }, 409);
    }
    const member = members.find((candidate) => candidate.userId === userId);
    if (member) member.tenantRole = body.role;
    failNextDirectory = options.failRefreshAfterMutation ?? false;
    return json(route, { message: 'Role updated.' });
  });
  await page.route(/\/irondev-api\/api\/tenants\/3\/users\/(\d+)$/, (route) => {
    state.removeRequests += 1;
    const userId = Number(route.request().url().match(/users\/(\d+)$/)?.[1]);
    const index = members.findIndex((candidate) => candidate.userId === userId);
    if (index >= 0) members.splice(index, 1);
    return json(route, { message: 'Membership removed.' });
  });
  await page.route(/\/irondev-api\/api\/tenants\/3\/users$/, (route) => {
    state.addRequests += 1;
    state.lastAddBody = route.request().postDataJSON() as Record<string, unknown>;
    const body = state.lastAddBody as { email: string; displayName: string; role: string };
    members.push({
      userId: 9,
      displayName: body.displayName,
      email: body.email,
      tenantRole: body.role,
      isActive: true,
      isCurrentUser: false,
      projectAccessStatus: 'Tenant scoped',
      channelMembershipSummary: 'No explicit memberships'
    });
    return json(route, { id: 9, ...body, isActive: true });
  });
  await page.route(/\/irondev-api\/api\/projects\/7\/channels\/(\d+)\/members\/(\d+)$/, (route) => {
    const match = route.request().url().match(/channels\/(\d+)\/members\/(\d+)$/);
    const channelId = Number(match?.[1]);
    const userId = Number(match?.[2]);
    const channel = channels.find((candidate) => candidate.channelId === channelId);
    if (!channel) return json(route, { error: 'Channel not found in this project.' }, 404);

    if (route.request().method() === 'DELETE') {
      state.channelDeleteRequests += 1;
      const index = channel.members.findIndex((membership) => membership.userId === userId);
      if (index >= 0) channel.members.splice(index, 1);
      channel.memberCount = channel.members.length;
      return json(route, { message: 'Channel membership removed.' });
    }

    state.channelPutRequests += 1;
    const body = route.request().postDataJSON() as { channelRole: string; notificationLevel: string };
    if (!body || typeof body !== 'object' || typeof body.channelRole !== 'string' || typeof body.notificationLevel !== 'string') {
      return json(route, { error: 'Malformed channel membership body.' }, 400);
    }
    const existing = channel.members.find((membership) => membership.userId === userId);
    if (options.refuseLastChannelOwnerChange && channelId === 102 && userId === 7 && body.channelRole !== 'Owner') {
      return json(route, { error: "The channel's last owner cannot be demoted or removed." }, 409);
    }
    if (existing) {
      existing.channelRole = body.channelRole;
      existing.notificationLevel = body.notificationLevel;
    } else {
      channel.members.push({ userId, ...body });
      channel.memberCount = channel.members.length;
    }
    return json(route, { message: 'Channel membership saved.' });
  });
  await page.route(/\/irondev-api\/api\/projects\/7\/members(?:\?[^#]*)?$/, (route) => {
    state.directoryRequests += 1;
    if (failNextDirectory) {
      failNextDirectory = false;
      return json(route, { error: 'Member directory unavailable after mutation.' }, 503);
    }
    if (options.directoryErrorStatus) {
      return json(route, { error: 'Project member directory not found.' }, options.directoryErrorStatus);
    }
    if (failuresRemaining > 0) {
      failuresRemaining -= 1;
      return json(route, { error: 'Member directory unavailable.' }, 503);
    }
    const visibleChannels = options.canAdminister === false
      ? channels.filter((channel) => channel.visibility === 'Project' || channel.members.some((member) => member.userId === currentUserId))
      : channels;
    for (const member of members) {
      const names = visibleChannels.filter((channel) => channel.members.some((membership) => membership.userId === member.userId)).map((channel) => channel.name);
      member.channelMembershipSummary = names.length > 0 ? names.join(', ') : 'No explicit memberships';
    }
    return json(route, {
      projectId: 7,
      projectName: 'BookSeller',
      tenantId: 3,
      currentUserTenantRole: options.currentRole ?? 'Owner',
      canAdministerTenantMembership: options.canAdminister ?? true,
      canAdministerChannelMembership: options.canAdminister ?? true,
      availableTenantRoles,
      availableChannelRoles: ['Owner', 'Moderator', 'Member', 'ReadOnly'],
      availableNotificationLevels: ['All', 'Mentions', 'None'],
      projectMembershipStatus: 'Not implemented',
      channelMembershipStatus: `${visibleChannels.length} active channels`,
      members,
      channels: visibleChannels,
      boundary: 'Channel membership controls visibility and moderation only. It is not project assignment, approval, workflow authority, tool authority, or source mutation permission.'
    });
  });

  return state;
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
