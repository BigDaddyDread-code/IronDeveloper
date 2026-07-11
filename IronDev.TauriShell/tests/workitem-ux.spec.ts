import { expect, test, type Page, type Route } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';
import { mockProjectWorkItem, workItemProjection } from './helpers/mockWorkItem';

test('Work Item renders backend stage, gate, contract, and primary action truth', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, {
    stage: 'Build',
    state: 'Failed',
    statusSummary: 'The latest governed run failed during test execution.',
    gateState: 'Blocked',
    gateReason: 'Tests failed in the disposable workspace.',
    nextSafeAction: 'Inspect failure evidence before deciding whether retry is safe.',
    technicalDetails: ['dotnet test returned exit code 1'],
    primaryActionKind: 'RepairOrRetry',
    primaryActionLabel: 'Review failure',
    criterionCount: 2,
    affectedFiles: ['src/Catalog.cs', 'tests/CatalogTests.cs']
  });

  await page.goto('/projects/7/work-items/42');

  await expect(page.getByTestId('flow.workItem.state')).toContainText('Build');
  await expect(page.getByTestId('flow.workItem.state')).toContainText('Failed');
  await expect(page.getByTestId('flow.workItem.gate')).toContainText('Tests failed in the disposable workspace.');
  await expect(page.getByTestId('flow.workItem.gate')).toContainText('Inspect failure evidence');
  await expect(page.getByTestId('flow.workItem.primaryAction')).toHaveText('Review failure');
  await expect(page.getByTestId('flow.contract.summary')).toContainText('2 criteria · 2 affected files');

  await page.getByText('Technical details', { exact: true }).click();
  await expect(page.getByText('dotnet test returned exit code 1')).toBeVisible();
});

test('Work Item projection failure offers retry and never reconstructs lifecycle truth', async ({ page }) => {
  await mockWorkspace(page);
  let attempts = 0;
  let allowSuccess = false;
  await page.route('**/irondev-api/api/projects/7/work-items/42', (route) => {
    attempts += 1;
    if (!allowSuccess) {
      return json(route, { error: 'Projection unavailable' }, 503);
    }
    return json(route, workItemProjection());
  });

  await page.goto('/projects/7/work-items/42');

  await expect(page.getByTestId('flow.workItemProjection.error')).toBeVisible();
  await expect(page.getByTestId('flow.stagerail')).toHaveCount(0);
  allowSuccess = true;
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByTestId('flow.workItem')).toBeVisible();
  expect(attempts).toBeGreaterThanOrEqual(2);
});

test('failed partial apply names missing recovery evidence without offering retry', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, {
    stage: 'Build',
    state: 'Failed',
    gateState: 'Blocked',
    gateReason: 'Post-apply validation failed after source mutation began.',
    nextSafeAction: 'Inspect recovery evidence before any retry decision.',
    primaryActionKind: 'RepairOrRetry',
    primaryActionLabel: 'Review failure',
    applyRecovery: {
      status: 'RecoveryEvidenceMissing',
      required: true,
      applyAttemptObserved: true,
      partialMutationPossible: true,
      succeededStageCount: 1,
      failedStageCount: 1,
      failedStages: ['PostApplyValidation'],
      technicalDetails: ['dotnet test returned exit code 1'],
      existingReceiptCount: 1,
      missingReceiptCount: 1,
      reason: 'A partial apply is possible because a stage succeeded before validation failed.',
      nextSafeAction: 'Inspect source state and supply rollback evidence before retrying apply.',
      retryAllowed: false,
      humanReviewRequired: true,
      boundary: 'Inspection does not retry apply or execute rollback.'
    }
  });

  await page.goto('/projects/7/work-items/42');

  const recovery = page.getByTestId('flow.workItem.applyRecovery');
  await expect(recovery).toContainText('Recovery evidence required');
  await expect(recovery).toContainText('Succeeded stages');
  await expect(recovery).toContainText('Missing receipts');
  await expect(recovery).toContainText('supply rollback evidence before retrying apply');
  await recovery.getByText('Failure details', { exact: true }).click();
  await expect(recovery).toContainText('dotnet test returned exit code 1');
  await expect(page.getByRole('button', { name: 'Retry apply' })).toHaveCount(0);
  if (process.env.IRONDEV_VISUAL_CAPTURE === '1') {
    await page.screenshot({ path: 'reports/visual-smoke/apply-recovery-1.png', fullPage: true });
  }
});

test('interrupted pre-mutation apply submits a reasoned fresh retry attempt', async ({ page }) => {
  await mockWorkspace(page);
  await mockRunEvidence(page);
  let recoveryBody: Record<string, unknown> | null = null;
  await page.route('**/irondev-api/api/projects/7/tickets/42/skeleton-runs/run-42/apply-recovery', async (route) => {
    recoveryBody = await route.request().postDataJSON();
    return json(route, {
      runId: 'run-42', projectId: 7, ticketId: 42, status: 'Completed',
      currentNode: 'SkeletonApplyRecovery', requiresHumanApproval: false,
      message: 'Fresh retry attempt started.'
    });
  });
  await mockProjectWorkItem(page, {
    stage: 'Review',
    state: 'Completed',
    primaryActionKind: 'RecoverApply',
    primaryActionLabel: 'Recover apply',
    applyRecovery: {
      status: 'Interrupted', required: true, applyAttemptObserved: true,
      partialMutationPossible: false, succeededStageCount: 2, failedStageCount: 0,
      failedStages: [], technicalDetails: [], existingReceiptCount: 2, missingReceiptCount: 0,
      reason: 'The apply attempt was interrupted before source mutation was observed.',
      nextSafeAction: 'Choose resume or retry to create a fresh attempt.',
      retryAllowed: true, humanReviewRequired: true,
      applyAttemptId: 'run-42-apply-001', applyAttemptNumber: 1,
      attemptStatus: 'Interrupted', mutationState: 'NotObserved',
      availableActions: ['Resume', 'Retry', 'ManualReview', 'Abandon'],
      boundary: 'Recovery actions are constrained by durable evidence.'
    }
  });

  await page.goto('/projects/7/work-items/42');

  const recovery = page.getByTestId('flow.workItem.applyRecovery');
  await expect(recovery).toContainText('run-42-apply-001');
  await expect(recovery.getByRole('button', { name: 'Retry in new attempt' })).toBeDisabled();
  await page.getByTestId('flow.workItem.applyRecovery.reason').fill('Validation stopped before copy; create a clean preserved retry.');
  await recovery.getByRole('button', { name: 'Retry in new attempt' }).click();
  await expect.poll(() => recoveryBody).not.toBeNull();
  expect(recoveryBody).toEqual({ action: 'Retry', reason: 'Validation stopped before copy; create a clean preserved retry.' });
});

test('uncertain source mutation exposes manual review and abandon only', async ({ page }) => {
  await mockWorkspace(page);
  await mockRunEvidence(page);
  await mockProjectWorkItem(page, {
    stage: 'Review',
    state: 'Completed',
    primaryActionKind: 'RecoverApply',
    primaryActionLabel: 'Review uncertain apply',
    applyRecovery: {
      status: 'ManualReviewRequired', required: true, applyAttemptObserved: true,
      partialMutationPossible: true, succeededStageCount: 7, failedStageCount: 0,
      failedStages: [], technicalDetails: [], existingReceiptCount: 7, missingReceiptCount: 1,
      reason: 'Source mutation may have begun.',
      nextSafeAction: 'Inspect source state, then record manual review or abandon.',
      retryAllowed: false, humanReviewRequired: true,
      applyAttemptId: 'run-42-apply-001', applyAttemptNumber: 1,
      attemptStatus: 'Interrupted', mutationState: 'Uncertain',
      availableActions: ['ManualReview', 'Abandon'],
      boundary: 'Uncertain source mutation is never retried automatically.'
    }
  });

  await page.goto('/projects/7/work-items/42');

  const recovery = page.getByTestId('flow.workItem.applyRecovery');
  await expect(recovery).toContainText('Manual review required');
  await expect(recovery.getByRole('button', { name: 'Resume in new attempt' })).toHaveCount(0);
  await expect(recovery.getByRole('button', { name: 'Retry in new attempt' })).toHaveCount(0);
  await expect(recovery.getByRole('button', { name: 'Record manual review' })).toBeVisible();
  await expect(recovery.getByRole('button', { name: 'Abandon apply' })).toBeVisible();
});

test('execution proof renders durable events separately from artifact evidence', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, {
    stage: 'Review',
    state: 'PausedForApproval',
    gateState: 'Blocked',
    gateReason: 'Human review is required for the current package.',
    nextSafeAction: 'Review findings and complete the approval ceremony.',
    primaryActionKind: 'Review',
    primaryActionLabel: 'Review waiting work',
    executionProof: {
      status: 'ExecutionObserved',
      hasRunRecord: true,
      executionStarted: true,
      executionCompleted: false,
      startedUtc: '2026-07-11T01:00:00Z',
      completedUtc: null,
      durableExecutionEventCount: 1,
      durableExecutionEvents: ['SkeletonEvidencePackaged'],
      buildAndTestExecutionObserved: true,
      applyExecutionObserved: false,
      loopVerified: false,
      artifactEvidenceObserved: true,
      artifactEvidenceProvesExecution: false,
      gaps: ['Critic package hash could not be verified.'],
      reason: 'Durable execution events were observed, but the report names 1 evidence gap.',
      nextSafeAction: 'Inspect and resolve the named evidence gaps before treating the loop as verified.',
      boundary: 'Artifacts and selected tests do not prove execution by themselves.'
    }
  });

  await page.goto('/projects/7/work-items/42');

  const proof = page.getByTestId('flow.workItem.executionProof');
  await expect(proof).toContainText('Execution observed');
  await expect(proof).toContainText('Not verified');
  await expect(proof).toContainText('SkeletonEvidencePackaged');
  await proof.getByText('Evidence gaps', { exact: true }).click();
  await expect(proof).toContainText('Critic package hash could not be verified.');
  await expect(proof).toContainText('do not prove execution by themselves');
  if (process.env.IRONDEV_VISUAL_CAPTURE === '1') {
    await page.screenshot({ path: 'reports/visual-smoke/execution-proof-1.png', fullPage: true });
  }
});

test('Work Item authority names eligible humans and actor attribution', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, {
    stage: 'Review',
    state: 'Completed',
    primaryActionKind: 'Apply',
    primaryActionLabel: 'Review controlled apply',
    authority: {
      currentUserId: 7,
      currentUserEligibleToContinue: true,
      soloApprovalExceptionAllowed: false,
      selfApprovalPolicy: 'A different eligible human must approve before this user can continue workflow.',
      acceptedApprovalActorId: '8',
      acceptedApprovalActorDisplayName: 'Alice Reviewer',
      continuationRequestedByUserId: '7',
      soloApprovalExceptionUsed: false,
      eligibleApprovers: [
        { userId: 8, displayName: 'Alice Reviewer', email: 'alice@irondev.local', projectRole: 'Contributor' },
        { userId: 7, displayName: 'Bob Developer', email: 'bob@irondev.local', projectRole: 'Owner' }
      ],
      boundary: 'Eligible reviewer and approver lists come from backend project membership.'
    }
  });

  await page.goto('/projects/7/work-items/42');

  const authority = page.getByTestId('flow.workItem.authority');
  await expect(authority).toContainText('Eligible');
  await expect(authority).toContainText('Different human required');
  await expect(authority).toContainText('Alice Reviewer');
  await expect(authority).toContainText('Bob Developer');
  await expect(authority).toContainText('Continued by');
  await expect(authority).toContainText('User 7');
  await expect(authority).toContainText('backend project membership');
});

test('Discuss in Workshop routes to the exact backend-linked session', async ({ page }) => {
  await mockWorkspace(page);
  await mockProjectWorkItem(page, { linkedChatSessionId: 9007 });

  await page.goto('/projects/7/work-items/42');
  await page.getByRole('button', { name: 'Discuss in Workshop' }).click();

  await expect(page).toHaveURL('/projects/7/workshop/sessions/9007');
});

test('Work Item ownership saves assignee, followers, waiting-on, and attributed activity', async ({ page }) => {
  await mockWorkspace(page);
  let savedBody: Record<string, unknown> | null = null;
  await mockProjectWorkItem(page, () => ({
    collaboration: savedBody ? {
      revision: 2,
      assignee: { kind: 'Human', userId: 8, displayName: 'Alice Reviewer' },
      followers: [{ kind: 'Human', userId: 7, displayName: 'Bob Developer' }],
      waitingOn: { kind: 'Role', userId: null, displayName: 'Approver' },
      linkedChatSessionId: null,
      recentActivity: [{ timestampUtc: '2026-07-11T02:00:00Z', kind: 'CollaborationChanged', summary: 'Work Item ownership and attention were updated.', actor: { kind: 'Human', userId: 7, displayName: 'Bob Developer' } }]
    } : undefined
  }));
  await page.route('**/irondev-api/api/projects/7/work-items/42/collaboration', (route) => {
    savedBody = route.request().postDataJSON() as Record<string, unknown>;
    return json(route, { revision: 2 });
  });

  await page.goto('/projects/7/work-items/42');
  await page.getByTestId('flow.workItem.collaboration.edit').click();
  const form = page.getByTestId('flow.workItem.collaboration.form');
  await form.getByLabel('Assignee').selectOption('8');
  await form.getByLabel('Waiting on').selectOption('role:Approver');
  await form.getByRole('checkbox', { name: 'Bob Developer' }).check();
  await page.getByTestId('flow.workItem.collaboration.save').click();

  await expect(page.getByTestId('flow.workItem.collaboration')).toContainText('Alice Reviewer');
  await expect(page.getByTestId('flow.workItem.collaboration')).toContainText('Approver');
  await expect(page.getByTestId('flow.workItem.collaboration')).toContainText('Bob Developer');
  expect(savedBody).toMatchObject({ expectedRevision: 0, assigneeUserId: 8, followerUserIds: [7], waitingOnKind: 'Role', waitingOnLabel: 'Approver' });
});

test('stale Work Item assignment preserves the attempted edit and offers reload and compare', async ({ page }) => {
  await mockWorkspace(page);
  let concurrentWrite = false;
  await mockProjectWorkItem(page, () => ({
    collaboration: concurrentWrite ? {
      revision: 1,
      assignee: { kind: 'Human', userId: 7, displayName: 'Bob Developer' },
      followers: [],
      waitingOn: null,
      linkedChatSessionId: null,
      recentActivity: []
    } : undefined
  }));
  await page.route('**/irondev-api/api/projects/7/work-items/42/collaboration', (route) => {
    concurrentWrite = true;
    return json(route, { code: 'StaleWrite', expectedRevision: 0, currentRevision: 1, currentState: {}, nextSafeAction: 'Reload the Work Item, compare current ownership with your attempted change, then submit again from the new revision.' }, 409);
  });

  await page.goto('/projects/7/work-items/42');
  await page.getByTestId('flow.workItem.collaboration.edit').click();
  await page.getByTestId('flow.workItem.collaboration.form').getByLabel('Assignee').selectOption('8');
  await page.getByTestId('flow.workItem.collaboration.save').click();

  await expect(page.getByTestId('flow.workItem.collaboration.form')).toContainText('Attempted version 0; current version 1');
  await page.getByTestId('flow.workItem.collaboration.reload').click();
  await expect(page.getByTestId('flow.workItem.collaboration')).toContainText('Bob Developer');
});

async function mockWorkspace(page: Page) {
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
    json(route, { userId: 7, email: 'bob@irondev.local', displayName: 'Bob', selectedTenantId: 3 })
  );
  await page.route('**/irondev-api/api/tenants', (route) =>
    json(route, [{ id: 3, name: 'IronDev Local', slug: 'irondev-local' }])
  );
  await page.route('**/irondev-api/api/projects', (route) =>
    json(route, [{ id: 7, tenantId: 3, name: 'BookSeller', localPath: 'C:\\repos\\BookSeller' }])
  );
  await page.route('**/irondev-api/api/projects/7/select', (route) => json(route, { projectId: 7 }));

  const ticket = {
    id: 42,
    tenantId: 3,
    projectId: 7,
    title: 'Add book sorting to catalog',
    status: 'Draft',
    acceptanceCriteria: 'Catalog sorts by title ascending\nPaging preserves the selected order.',
    linkedFilePaths: 'src/Catalog.cs\ntests/CatalogTests.cs'
  };
  await page.route('**/irondev-api/api/projects/7/tickets/42', (route) => json(route, ticket));
  await page.route('**/irondev-api/api/projects/7/tickets', (route) => json(route, [ticket]));
  await mockProjectBoard(page, { projectName: 'BookSeller', tickets: [ticket] });
  await page.route('**/irondev-api/api/projects/7/tickets/42/build-readiness', (route) =>
    json(route, { isReady: true, message: 'Ready to build.', blockingIssues: [] })
  );
  await page.route('**/irondev-api/api/projects/7/tickets/42/evidence-summary', (route) =>
    json(route, {
      ticketId: 42,
      status: 'loaded',
      message: 'No linked execution evidence is available yet.',
      latestRun: null,
      blockedActions: [],
      nextSafeAction: 'Start governed run'
    })
  );
  await page.route('**/irondev-api/api/projects/7/chat/sessions', (route) => json(route, []));
  await page.route('**/irondev-api/api/projects/7/members', (route) => json(route, {
    projectId: 7,
    projectName: 'BookSeller',
    tenantId: 3,
    currentUserTenantRole: 'Owner',
    canAdministerTenantMembership: true,
    canAdministerProjectMembership: true,
    canAdministerChannelMembership: true,
    availableTenantRoles: ['Owner', 'Viewer'],
    availableProjectRoles: ['Owner', 'Contributor', 'Viewer'],
    availableChannelRoles: [],
    availableNotificationLevels: [],
    projectMembershipStatus: '2 active members',
    channelMembershipStatus: 'No active channels',
    members: [
      { userId: 7, displayName: 'Bob Developer', email: 'bob@irondev.local', tenantRole: 'Owner', projectRole: 'Owner', isProjectMember: true, isActive: true, isCurrentUser: true, projectAccessStatus: 'Project member', channelMembershipSummary: 'No explicit memberships' },
      { userId: 8, displayName: 'Alice Reviewer', email: 'alice@irondev.local', tenantRole: 'Reviewer', projectRole: 'Contributor', isProjectMember: true, isActive: true, isCurrentUser: false, projectAccessStatus: 'Project member', channelMembershipSummary: 'No explicit memberships' }
    ],
    channels: [],
    boundary: 'Project membership controls visibility only.'
  }));
}

async function mockRunEvidence(page: Page) {
  await page.route('**/irondev-api/api/projects/7/tickets/42/evidence-summary', (route) =>
    json(route, {
      ticketId: 42,
      status: 'loaded',
      message: 'Latest governed run loaded.',
      latestRun: { runId: 'run-42', status: 'Completed', recommendation: 'Recover apply from backend truth.' },
      blockedActions: [],
      nextSafeAction: 'Review apply recovery'
    })
  );
  await page.route('**/irondev-api/api/projects/7/tickets/42/skeleton-runs/run-42/report', (route) =>
    json(route, {
      runId: 'run-42', projectId: 7, ticketId: 42, status: 'Completed', summary: 'Apply recovery required.',
      timeline: [], criticReviews: [], findingDispositions: [], repairAttempts: [], revisionAttempts: [],
      gaps: [], loopComplete: false, boundary: 'Read-only report.'
    })
  );
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
