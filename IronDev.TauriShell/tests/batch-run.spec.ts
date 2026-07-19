import { expect, test, type Page } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';

// P2-7: the batch surface — define three linked BookSeller tickets, hit run,
// watch the system sequence them. The tests mock the governed endpoints and
// assert the UI's side: it renders the map's named edges, the plan's waves, the
// per-ticket loop state, and policy's advisory recommendation — and it never
// pretends to decide anything.

const MAP_ID = 'map-1';
const PLAN_ID = 'plan-1';
const BATCH_ID = 'batch-1';

test('three linked tickets: detect → sequence → run → advance', async ({ page }) => {
  await mockBatchWorkspace(page);
  const state = await mockBatch(page);

  await page.goto('/');
  await page.getByTestId('flow.board.batch').click();
  await expect(page.getByTestId('flow.batch')).toBeVisible();

  // 1 · select the three linked tickets
  await page.getByTestId('flow.batch.pick.42').click();
  await page.getByTestId('flow.batch.pick.43').click();
  await page.getByTestId('flow.batch.pick.44').click();

  // 2 · detect dependencies — the map renders each edge with its named reason
  await page.getByTestId('flow.batch.detect').click();
  await expect(page.getByTestId('flow.batch.map')).toContainText('WI-42 → WI-43');
  await expect(page.getByTestId('flow.batch.map')).toContainText('explicit-block');
  await expect(page.getByTestId('flow.batch.map')).toContainText('declares it is blocked by');

  // 3 · sequence into waves
  await page.getByTestId('flow.batch.plan').click();
  await expect(page.getByTestId('flow.batch.waves')).toContainText('Wave 1');
  await expect(page.getByTestId('flow.batch.waves')).toContainText('Wave 2');

  // 4 · run — wave 1 starts, the dependent waits with a named reason
  await page.getByTestId('flow.batch.run').click();
  await expect(page.getByTestId('flow.batch.runTickets')).toContainText('WI-42');
  await expect(page.getByTestId('flow.batch.runTickets')).toContainText('PausedForApproval');
  await expect(page.getByTestId('flow.batch.runTickets')).toContainText('waiting on ticket 42');

  // policy's advice is shown beside the halted ticket — and says it is advice
  await expect(page.getByTestId('flow.batch.recommendation.42')).toContainText('advisory');
  await expect(page.getByTestId('flow.batch.recommendation.42')).toContainText('policy cannot click');

  // 5 · the upstream applies out-of-band; advance starts the dependent
  state.appliedTicketIds.add(42);
  await page.getByTestId('flow.batch.advance').click();
  await expect(page.getByTestId('flow.batch.runTickets')).toContainText('WI-43');
  await expect(page.getByTestId('flow.batch.runTickets').locator('text=PausedForApproval')).toHaveCount(2);
});

test('an unschedulable plan shows the cycle blocker and offers no run', async ({ page }) => {
  await mockBatchWorkspace(page);
  await mockBatch(page, { cyclic: true });

  await page.goto('/');
  await page.getByTestId('flow.board.batch').click();
  await page.getByTestId('flow.batch.pick.42').click();
  await page.getByTestId('flow.batch.pick.43').click();
  await page.getByTestId('flow.batch.detect').click();
  await page.getByTestId('flow.batch.plan').click();

  await expect(page.getByTestId('flow.batch.cycle')).toContainText('cannot be placed in any wave');
  await expect(page.getByTestId('flow.batch.run')).toHaveCount(0);
});

interface BatchState {
  appliedTicketIds: Set<number>;
}

async function mockBatch(page: Page, options: { cyclic?: boolean } = {}): Promise<BatchState> {
  const state: BatchState = { appliedTicketIds: new Set() };

  await page.route('**/irondev-api/api/projects/7/batch-maps', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        succeeded: true,
        failureReason: '',
        mapId: MAP_ID,
        detectedAtUtc: '2026-07-04T10:00:00Z',
        map: {
          projectId: 7,
          ticketIds: [42, 43, 44],
          edges: [
            {
              fromTicketId: 42,
              toTicketId: 43,
              kind: 'explicit-block',
              reason: 'Ticket 43 declares it is blocked by ticket 42 (BlockedByTicketIds).',
              sharedPaths: []
            }
          ],
          warnings: [],
          boundary: 'A dependency map is advisory evidence.'
        }
      })
    });
  });

  await page.route('**/irondev-api/api/projects/7/batch-plans', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        succeeded: true,
        failureReason: '',
        planId: PLAN_ID,
        plannedAtUtc: '2026-07-04T10:01:00Z',
        plan: options.cyclic
          ? {
              projectId: 7,
              mapId: MAP_ID,
              waves: [],
              cycleBlockers: [
                { ticketIds: [42, 43], detail: 'Tickets 42, 43 cannot be placed in any wave: their dependencies form a cycle.' }
              ],
              warnings: [],
              schedulable: false,
              boundary: 'A batch plan is a proposal.'
            }
          : {
              projectId: 7,
              mapId: MAP_ID,
              waves: [
                { waveNumber: 1, ticketIds: [42, 44] },
                { waveNumber: 2, ticketIds: [43] }
              ],
              cycleBlockers: [],
              warnings: [],
              schedulable: true,
              boundary: 'A batch plan is a proposal.'
            }
      })
    });
  });

  const statusBody = () => ({
    batchId: BATCH_ID,
    planId: PLAN_ID,
    projectId: 7,
    requestedByUserId: '7',
    startedAtUtc: '2026-07-04T10:02:00Z',
    boundary: 'A batch run composes single-ticket loops.',
    batchComplete: false,
    tickets: [42, 44, 43].map((ticketId) => {
      const applied = state.appliedTicketIds.has(ticketId);
      const isDependent = ticketId === 43;
      const upstreamApplied = state.appliedTicketIds.has(42);
      const started = !isDependent || upstreamApplied;
      return {
        ticketId,
        wave: isDependent ? 2 : 1,
        runId: started ? `run-t${ticketId}` : '',
        runStatus: applied ? 'Applied' : started ? 'PausedForApproval' : '',
        eligible: !started && (!isDependent || upstreamApplied),
        waitingOn: isDependent && !upstreamApplied ? ['ticket 42 (PausedForApproval)'] : []
      };
    })
  });

  await page.route('**/irondev-api/api/projects/7/batch-runs', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ succeeded: true, failureReason: '', startedRuns: { '42': 'run-t42', '44': 'run-t44' }, status: statusBody() })
    });
  });

  await page.route(`**/irondev-api/api/projects/7/batch-runs/${BATCH_ID}/advance`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ succeeded: true, failureReason: '', startedRuns: {}, status: statusBody() })
    });
  });

  await page.route(/\/gate-recommendation$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: 'run-t42',
        tier: 'Low',
        recommendation: 'policy-would-approve-advisory-only',
        reasons: ['[pass] every check passed'],
        measurementInput: { measurementId: 'measure-1', catchRate: 1, controlClean: true, reExecutionAvailable: true, verified: true, measuredAtUtc: '2026-07-04T09:00:00Z' },
        boundary: 'A gate recommendation is advice, not approval: policy cannot click.'
      })
    });
  });

  return state;
}

async function mockBatchWorkspace(page: Page) {
  const boardTickets = [
    { id: 42, tenantId: 3, projectId: 7, title: 'Add catalog sort core', status: 'Draft', acceptanceCriteria: 'Sorts by title' },
    { id: 43, tenantId: 3, projectId: 7, title: 'Wire sort into catalog page', status: 'Draft', acceptanceCriteria: 'Page uses sort' },
    { id: 44, tenantId: 3, projectId: 7, title: 'Add sort docs', status: 'Draft', acceptanceCriteria: 'Docs updated' }
  ];
  await page.addInitScript(() => {
    window.localStorage.setItem('irondev.token', 'test-token');
    window.localStorage.setItem('irondev.tenantId', '3');
    window.localStorage.setItem('irondev.selectedProjectId', '7');
  });
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
      body: JSON.stringify([{ id: 7, tenantId: 3, name: 'BookSeller', description: 'Dogfood project' }])
    });
  });
  await page.route('**/irondev-api/api/workbench/projects/7/open', async (route) => {
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
  await mockProjectBoard(page, { projectName: 'BookSeller', tickets: boardTickets });
}
