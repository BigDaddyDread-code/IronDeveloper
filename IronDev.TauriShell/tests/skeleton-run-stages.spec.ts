import { expect, test, type Page } from '@playwright/test';
import { mockProjectBoard } from './helpers/mockBoard';

// P0-7: Build and Review stages consume the walking-skeleton loop through its
// governed endpoints. These tests mock the backend and assert the UI's side of
// the boundary: it renders what the backend recorded, requests through governed
// surfaces, shows refusals honestly, and never invents an unblocked state.

const RUN_ID = 'run-777';
const PACKAGE_HASH = 'a'.repeat(64);
const APPROVAL_PROJECT_GUID = '00000007-0000-0000-0000-000000000000';

test('build stage renders the halted run and review renders the matrix and gate', async ({ page }) => {
  await mockTicketWorkspace(page);
  await mockSkeletonRun(page, { continuationUnblocked: false });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();

  await expect(page.getByTestId('flow.build.status')).toContainText('PausedForApproval');
  await expect(page.getByTestId('flow.build.testAuthoring')).toContainText('1 test(s) authored');
  await expect(page.getByTestId('flow.build.timeline')).toContainText('ApprovalRequiredHalt');
  await expect(page.getByTestId('flow.build.gate')).toContainText('Halt is not approval');

  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.matrix')).toContainText('Catalog sorts by title ascending');
  await expect(page.getByTestId('flow.review.matrix')).toContainText('tests/skeleton/SortTests.cs');
  await expect(page.getByTestId('flow.review.requirement')).toContainText('skeleton-run.continue');
  await expect(page.getByTestId('flow.review.gate')).toContainText('Human gate: locked');
  await expect(page.getByTestId('flow.review.requestApply')).toBeDisabled();
});

test('a self-repaired run says so honestly in build and review, and the gate is unchanged', async ({ page }) => {
  await mockTicketWorkspace(page);
  await mockSkeletonRun(page, { continuationUnblocked: false, withRepair: true });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();

  // Build stage: the gate proposal is explicitly the REPAIRED proposal, the
  // attempt history is listed, the failed original is preserved as history,
  // and the boundary is stated in place.
  await expect(page.getByTestId('flow.build.proposal')).toContainText('Gate proposal (repaired)');
  await expect(page.getByTestId('flow.build.proposal')).toContainText('-repair-2');
  await expect(page.getByTestId('flow.build.repairAttempt.2')).toContainText('repaired after BuildFailed');
  await expect(page.getByTestId('flow.build.repairAttempt.2')).toContainText("on 'dotnet build'");
  await expect(page.getByTestId('flow.build.repairAttempt.2')).toContainText('OpenAI/gpt-4o-mini');
  await expect(page.getByTestId('flow.build.initialProposal')).toContainText('failed and is preserved as history');
  await expect(page.getByTestId('flow.build.repairBoundary')).toContainText('not authority');

  // Review stage: the repaired-run note is present — and the human gate is
  // exactly the gate, still locked.
  await page.getByTestId('flow.build.toReview').click();
  await expect(page.getByTestId('flow.review.repairedNote')).toContainText('self-repaired once');
  await expect(page.getByTestId('flow.review.repairedNote')).toContainText('the gate below is unchanged');
  await expect(page.getByTestId('flow.review.gate')).toContainText('Human gate: locked');
  await expect(page.getByTestId('flow.review.requestApply')).toBeDisabled();
});

test('a run with no repair renders no repair chrome at all', async ({ page }) => {
  await mockTicketWorkspace(page);
  await mockSkeletonRun(page, { continuationUnblocked: false });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();

  await expect(page.getByTestId('flow.build.proposal')).not.toContainText('repaired');
  await expect(page.getByTestId('flow.build.repairAttempts')).toHaveCount(0);

  await page.getByTestId('flow.build.toReview').click();
  await expect(page.getByTestId('flow.review.repairedNote')).toHaveCount(0);
});

test('an uncovered criterion is rendered as UNCOVERED, not elided', async ({ page }) => {
  await mockTicketWorkspace(page);
  await mockSkeletonRun(page, { continuationUnblocked: false, uncovered: true });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.matrix')).toContainText('Catalog paging keeps sort order');
  await expect(page.getByTestId('flow.review.uncovered')).toContainText('UNCOVERED');
  await expect(page.getByTestId('flow.review.matrix')).toContainText('Catalog paging keeps sort order');
});

test('requesting a critic review surfaces findings and the findings gate blocks until dispositioned', async ({ page }) => {
  await mockTicketWorkspace(page);
  const state = await mockSkeletonRun(page, { continuationUnblocked: false });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await page.getByTestId('flow.review.requestCritic').click();
  expect(state.criticReviewRequested).toBe(true);

  await expect(page.getByTestId('flow.review.criticVerdict')).toContainText('RequestChanges');
  await expect(page.getByTestId('flow.review.findings')).toContainText('Sort ignores culture');
  await expect(page.getByTestId('flow.review.findingsGate')).toContainText('await a human disposition');

  await page.getByTestId('flow.review.dispositionReason').fill('Locale nuance acceptable for the sandbox catalog.');
  await page.getByTestId('flow.review.recordDisposition').click();

  expect(state.dispositionRequests[0].findingId).toBe('f-1');
  expect(state.dispositionRequests[0].reason).toContain('Locale nuance');
  await expect(page.getByTestId('flow.review.disposition')).toContainText('AcceptRisk');
  await expect(page.getByTestId('flow.review.findingsGate')).toContainText('carries a human disposition');
});

test('a finding with no disposition warns at the human gate', async ({ page }) => {
  await mockTicketWorkspace(page);
  await mockSkeletonRun(page, { continuationUnblocked: false, withFinding: true });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.requirement')).toContainText('skeleton-run.continue');
  await expect(page.getByText('the backend will refuse continuation until every finding is answered')).toBeVisible();
});

test('recording an approval requires the ceremony, posts the reason as evidence, and continuation consumes it', async ({ page }) => {
  await mockTicketWorkspace(page);
  const state = await mockSkeletonRun(page, { continuationUnblocked: false });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  // APPROVAL-UX-1: one click opens the ceremony, it does not record. The
  // delegated-policy truth is stated at the gate.
  await expect(page.getByTestId('flow.review.delegatedPolicy')).toContainText('Delegated approval: none exists');
  await page.getByTestId('flow.review.recordApproval').click();
  await expect(page.getByTestId('flow.review.approvalCeremony')).toBeVisible();

  // Incomplete ceremony cannot record, and says why.
  await expect(page.getByTestId('flow.review.confirmApproval')).toBeDisabled();
  await expect(page.getByTestId('flow.review.ceremonyUnmet')).toContainText('a reason is required');

  await page.getByTestId('flow.review.approvalReason').fill('Package reviewed end to end; criteria covered; no findings.');
  await expect(page.getByTestId('flow.review.confirmApproval')).toBeDisabled();
  await page.getByTestId('flow.review.approvalHashConfirmation').fill(PACKAGE_HASH.slice(0, 8));
  await page.getByTestId('flow.review.confirmApproval').click();

  await expect(page.getByTestId('flow.review.gate')).toContainText('Recording is not continuation');
  expect(state.approvalRequestBody.approvalTargetHash).toBe(PACKAGE_HASH);
  expect(state.approvalRequestBody.capabilityCode).toBe('skeleton-run.continue');
  // The typed reason rides as durable labeled evidence on the approval record,
  // encoded to the backend's reference alphabet (letters, digits, -_.: only —
  // the real API refuses spaces; DOGFOOD-2 finding F-L).
  expect(state.approvalRequestBody.evidenceReferences).toContain(
    'human-reason:Package-reviewed-end-to-end-criteria-covered-no-findings.'
  );

  state.continuationUnblocked = true;
  await page.getByTestId('flow.review.requestContinuation').click();

  await expect(page.getByTestId('flow.review.gate')).toContainText('Continuation allowed');
  await expect(page.getByTestId('flow.review.requestApply')).toBeEnabled();
});

test('a refused apply is shown honestly and the loop stays incomplete', async ({ page }) => {
  await mockTicketWorkspace(page);
  const state = await mockSkeletonRun(page, { continuationUnblocked: true, applyRefusedReason: 'ApplyDisabled' });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.requestApply')).toBeEnabled();
  await page.getByTestId('flow.review.requestApply').click();

  await expect(page.getByTestId('flow.review.gate')).toContainText('Skeleton apply is disabled');
  expect(state.applyRequested).toBe(true);
});

test('successful controlled apply opens the final report and receipt chain', async ({ page }) => {
  await mockTicketWorkspace(page);
  const state = await mockSkeletonRun(page, { continuationUnblocked: true });

  await openTicketStage(page);
  await page.getByTestId('flow.ticket.startRun').click();
  await page.getByTestId('flow.build.toReview').click();

  await expect(page.getByTestId('flow.review.requestApply')).toBeEnabled();
  await page.getByTestId('flow.review.requestApply').click();

  expect(state.applyRequested).toBe(true);
  await expect(page.getByTestId('flow.done.report')).toContainText('Applied');
  await expect(page.getByTestId('flow.done.report')).toContainText('Loop complete');
  await expect(page.getByTestId('flow.done.receipts')).toContainText('source-apply-receipt');
  await expect(page.getByText('commit, push, and release remain separate governed steps')).toBeVisible();
});

test('opening a seeded applied ticket hydrates the linked final report without copied ids', async ({ page }) => {
  await mockTicketWorkspace(page, { ticketStatus: 'Applied', latestRun: { status: 'passed' } });
  await mockSkeletonRun(page, { continuationUnblocked: true, initialApplied: true });
  await page.route('**/irondev-api/api/governance/**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ data: { items: [], totalCount: 0 }, errors: [] })
    });
  });

  await page.goto('/');

  await expect(page.getByTestId('flow.board.columns')).toContainText('Done');
  await expect(page.getByText('Add book sorting to catalog')).toBeVisible();

  await page.getByText('Add book sorting to catalog').click();

  await expect(page.getByTestId('flow.done.report')).toContainText('Applied');
  await expect(page.getByTestId('flow.done.receipts')).toContainText('source-apply-receipt');
  await page.getByTestId('flow.done.openGovernance').click();
  await expect(page.getByTestId('flow.governanceHost')).toBeVisible();
});

test('board does not treat Completed as Applied done state', async ({ page }) => {
  await mockTicketWorkspace(page, {
    tickets: [
      {
        id: 42,
        tenantId: 3,
        projectId: 7,
        title: 'Applied BookSeller ticket',
        status: 'Applied',
        acceptanceCriteria: 'Applied path has a controlled apply receipt.'
      },
      {
        id: 43,
        tenantId: 3,
        projectId: 7,
        title: 'Completed run without apply',
        status: 'Completed',
        acceptanceCriteria: 'Completed run must still not read as applied.'
      }
    ]
  });

  await page.goto('/');

  await expect(page.getByTestId('flow.board.column.done')).toContainText('Applied BookSeller ticket');
  await expect(page.getByTestId('flow.board.column.done')).not.toContainText('Completed run without apply');
  await expect(page.getByTestId('flow.board.column.ticket')).toContainText('Completed run without apply');
});

test('blocked ticket and empty board states name the next safe action', async ({ page }) => {
  await mockTicketWorkspace(page, {
    readiness: { isReady: false, message: 'Project profile is missing.', blockingIssues: ['Project profile is missing.'] }
  });

  await page.goto('/');

  await expect(page.getByTestId('flow.modelMode')).toContainText('Model mode: Deterministic LocalTest');
  await expect(page.getByTestId('flow.board.empty.done')).toContainText('No applied tickets yet');
  await expect(page.getByTestId('flow.board.empty.done')).toContainText('Next safe action');

  await page.getByText('Add book sorting to catalog').click();

  await expect(page.getByTestId('flow.ticket.readiness')).toContainText('Next safe action');
  await expect(page.getByTestId('flow.ticket.linkedRun')).toContainText('No linked run evidence yet');
  await expect(page.getByTestId('flow.ticket.startRun')).toBeDisabled();
  await expect(page.getByTestId('flow.ticket.gate')).toContainText('backend explains the block');
});

async function openTicketStage(page: Page) {
  await page.goto('/');
  await page.getByText('Add book sorting to catalog').click();
  await expect(page.getByTestId('flow.ticket.startRun')).toBeEnabled();
}

interface SkeletonMockState {
  continuationUnblocked: boolean;
  applyRefusedReason?: string;
  applied: boolean;
  applyRequested: boolean;
  approvalRequestBody: Record<string, unknown>;
  criticReviews: unknown[];
  findingDispositions: { findingId: string; disposition: string; reason: string; decidedByUserId: string }[];
  criticReviewRequested: boolean;
  dispositionRequests: { findingId: string; disposition: string; reason: string }[];
  uncovered: boolean;
}

async function mockSkeletonRun(
  page: Page,
  options: {
    continuationUnblocked: boolean;
    applyRefusedReason?: string;
    initialApplied?: boolean;
    withFinding?: boolean;
    uncovered?: boolean;
    withRepair?: boolean;
  }
): Promise<SkeletonMockState> {
  const state: SkeletonMockState = {
    continuationUnblocked: options.continuationUnblocked,
    applyRefusedReason: options.applyRefusedReason,
    applied: options.initialApplied ?? false,
    applyRequested: false,
    approvalRequestBody: {},
    criticReviews: options.withFinding
      ? [
          {
            criticAgentRunId: 'critic-run-1',
            reviewId: 'critic-review-1',
            verdict: 'RequestChanges',
            findingCount: 1,
            blockingFindingCount: 0,
            findingIds: ['f-1'],
            packageSha256: PACKAGE_HASH,
            groundTruthCheckCount: 5,
            groundTruthMismatchCount: 0
          }
        ]
      : [],
    findingDispositions: [],
    criticReviewRequested: false,
    dispositionRequests: [],
    uncovered: options.uncovered ?? false
  };

  const runDto = (status: string, message: string | null = null) => ({
    runId: RUN_ID,
    projectId: 7,
    ticketId: 42,
    status,
    currentNode: 'SkeletonRun',
    requiresHumanApproval: status === 'PausedForApproval',
    message
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(runDto('PausedForApproval')) });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/report`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: RUN_ID,
        projectId: 7,
        ticketId: 42,
        status: state.applied ? 'Applied' : state.continuationUnblocked ? 'Completed' : 'PausedForApproval',
        summary: state.applied ? 'Applied through the governed workspace spine.' : 'Halted for approval.',
        timeline: [
          { timestampUtc: '2026-07-04T10:00:00Z', eventType: 'RunStarted', message: 'Skeleton run started for ticket 42.' },
          { timestampUtc: '2026-07-04T10:01:00Z', eventType: 'TestsAuthored', message: '1 test file(s) authored.' },
          { timestampUtc: '2026-07-04T10:02:00Z', eventType: 'ApprovalRequiredHalt', message: 'Halted for approval. Halt is not approval.' }
        ],
        proposal: options.withRepair
          ? {
              proposalId: `prop-${RUN_ID}-repair-2`,
              fileChangeCount: 1,
              evidenceRef: 'evidence/proposal-repair-2.json',
              evidenceExistsOnDisk: true,
              modelProvider: 'OpenAI',
              modelName: 'gpt-4o-mini'
            }
          : { proposalId: `prop-${RUN_ID}`, fileChangeCount: 1, evidenceRef: 'evidence/proposal.json', evidenceExistsOnDisk: true },
        initialProposal: options.withRepair
          ? { proposalId: `prop-${RUN_ID}`, fileChangeCount: 1, evidenceRef: 'evidence/proposal.json', evidenceExistsOnDisk: true }
          : null,
        repairAttempts: options.withRepair
          ? [
              {
                attemptNumber: 2,
                failureKind: 'BuildFailed',
                failedCommand: 'dotnet build',
                repairProposalId: `prop-${RUN_ID}-repair-2`,
                modelProvider: 'OpenAI',
                modelName: 'gpt-4o-mini',
                repairProposalEvidenceExistsOnDisk: true
              }
            ]
          : [],
        testAuthoring: { authored: true, authoredTestCount: 1, skippedReason: '' },
        criticPackage: {
          packageId: `critic-pkg-${RUN_ID}`,
          packagePath: 'evidence/critic-package.json',
          existsOnDisk: true,
          announcedSha256: PACKAGE_HASH,
          sha256OnDisk: PACKAGE_HASH,
          hashVerified: true,
          criterionCount: state.uncovered ? 2 : 1,
          uncoveredCriterionCount: state.uncovered ? 1 : 0
        },
        approval: {
          targetKind: 'workflow-continuation-request',
          targetId: RUN_ID,
          targetHash: PACKAGE_HASH,
          capabilityCode: 'skeleton-run.continue',
          haltObserved: true,
          continuationUnblocked: state.continuationUnblocked,
          acceptedApprovalId: state.continuationUnblocked ? 'b2c3d4e5-0000-0000-0000-000000000001' : ''
        },
        criticReviews: state.criticReviews,
        findingDispositions: state.findingDispositions,
        apply: state.applied
          ? {
              applied: true,
              workspacePath: 'C:/IronDevTestWorkspaces/demo-run',
              refusedReason: '',
              stages: [
                { stage: 'pre-state', succeeded: true, errors: '' },
                { stage: 'copy-only-apply', succeeded: true, errors: '' },
                { stage: 'post-state', succeeded: true, errors: '' }
              ],
              receipts: [
                { name: 'source-apply-receipt', path: 'evidence/source-apply-receipt.json', existsOnDisk: true },
                { name: 'post-state-receipt', path: 'evidence/post-state-receipt.json', existsOnDisk: true }
              ]
            }
          : null,
        gaps: [],
        loopComplete: state.applied,
        boundary: 'A report is reconstruction from durable evidence.'
      })
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/critic-package`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        packageId: `critic-pkg-${RUN_ID}`,
        runId: RUN_ID,
        proposalId: `prop-${RUN_ID}`,
        ticketId: 42,
        projectId: 7,
        ticketTitle: 'Add book sorting to catalog',
        acceptanceCriteria: 'Catalog sorts by title ascending',
        proposalSummary: 'Add book sorting.',
        proposalRationale: 'Users need ordered catalogs.',
        changes: [
          {
            filePath: 'src/SortOptions.cs',
            description: 'New sort options enum.',
            isNewFile: true,
            isDeletion: false,
            diff: '+public enum SortOptions { Title }',
            fullContentAfter: 'public enum SortOptions { Title }'
          }
        ],
        authoredTests: [
          {
            relativePath: 'tests/skeleton/SortTests.cs',
            content: 'public class SortTests { }',
            coversCriterion: 'Catalog sorts by title ascending'
          }
        ],
        criterionCoverage: state.uncovered
          ? [
              { criterion: 'Catalog sorts by title ascending', covered: true, coveringTests: ['tests/skeleton/SortTests.cs'] },
              { criterion: 'Catalog paging keeps sort order', covered: false, coveringTests: [] }
            ]
          : [{ criterion: 'Catalog sorts by title ascending', covered: true, coveringTests: ['tests/skeleton/SortTests.cs'] }],
        commandResults: [{ displayName: 'dotnet build', exitCode: 0, timedOut: false, durationMs: 4000 }],
        evidenceRefs: [],
        workspaceRunSucceeded: true,
        boundary: 'This package is review material for the independent critic.'
      })
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/critic-review`, async (route) => {
    state.criticReviewRequested = true;
    state.criticReviews = [
      {
        criticAgentRunId: 'critic-run-1',
        reviewId: 'critic-review-1',
        verdict: 'RequestChanges',
        findingCount: 1,
        blockingFindingCount: 0,
        findingIds: ['f-1'],
        packageSha256: PACKAGE_HASH,
        groundTruthCheckCount: 5,
        groundTruthMismatchCount: 0
      }
    ];
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        succeeded: true,
        failureReason: '',
        criticAgentRunId: 'critic-run-1',
        reviewId: 'critic-review-1',
        verdict: 'RequestChanges',
        findings: [
          {
            findingId: 'f-1',
            severity: 'High',
            title: 'Sort ignores culture',
            problem: 'The diff compares titles ordinally.',
            whyItMatters: 'Criterion says alphabetical for users.',
            requiredFix: 'Use culture-aware comparison.',
            blocksMerge: false
          }
        ],
        groundTruth: { checks: [], mismatches: [], boundary: 'Ground truth is evidence, not judgment.' },
        boundary: 'Critic findings are advisory.'
      })
    });
  });

  await page.route(
    `**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/findings/*/disposition`,
    async (route) => {
      const body = route.request().postDataJSON() as { disposition: string; reason: string };
      const findingId = route.request().url().split('/findings/')[1].split('/disposition')[0];
      state.dispositionRequests.push({ findingId, disposition: body.disposition, reason: body.reason });
      state.findingDispositions = [
        { findingId, disposition: body.disposition, reason: body.reason, decidedByUserId: '7' }
      ];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          succeeded: true,
          failureReason: '',
          findingId,
          disposition: body.disposition,
          boundary: 'A disposition is a human decision about a finding; it is not approval.'
        })
      });
    }
  );

  await page.route(`**/irondev-api/api/v1/projects/${APPROVAL_PROJECT_GUID}/accepted-approvals`, async (route) => {
    state.approvalRequestBody = route.request().postDataJSON() as Record<string, unknown>;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'created',
        data: { acceptedApprovalId: 'b2c3d4e5-0000-0000-0000-000000000001' },
        acceptedApprovalId: 'b2c3d4e5-0000-0000-0000-000000000001',
        warnings: [],
        errors: []
      })
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/continue`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        state.continuationUnblocked
          ? runDto('Completed', 'Continuation allowed by accepted approval. Approval is not apply permission.')
          : runDto('PausedForApproval', 'No live accepted approval satisfies this requirement. Halt is not approval.')
      )
    });
  });

  await page.route(`**/irondev-api/api/projects/7/tickets/42/skeleton-runs/${RUN_ID}/apply`, async (route) => {
    state.applyRequested = true;
    if (!state.applyRefusedReason) {
      state.applied = true;
      state.continuationUnblocked = true;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        state.applyRefusedReason
          ? runDto('Completed', 'Apply refused: Skeleton apply is disabled. Set SkeletonApply:Enabled=true for sandbox projects only.')
          : runDto('Applied', 'Applied through the governed workspace spine.')
      )
    });
  });

  return state;
}

async function mockTicketWorkspace(
  page: Page,
  options: {
    ticketStatus?: string;
    tickets?: Array<{
      id: number;
      tenantId: number;
      projectId: number;
      title: string;
      status: string;
      acceptanceCriteria: string;
    }>;
    latestRun?: { status: string };
    readiness?: { isReady: boolean; message: string; blockingIssues: string[] };
  } = {}
) {
  const boardTickets = options.tickets ?? [
    {
      id: 42,
      tenantId: 3,
      projectId: 7,
      title: 'Add book sorting to catalog',
      status: options.ticketStatus ?? 'Draft',
      acceptanceCriteria: 'Catalog sorts by title ascending'
    }
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
  const detailTicket = boardTickets.find((ticket) => ticket.id === 42) ?? boardTickets[0];
  await page.route('**/irondev-api/api/projects/7/tickets/42', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detailTicket) })
  );
  await mockProjectBoard(page, {
    projectName: 'IronDeveloper',
    tickets: boardTickets,
    latestRuns: options.latestRun ? { 42: options.latestRun } : undefined
  });
  await page.route('**/irondev-api/api/projects/7/tickets/42/build-readiness', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(options.readiness ?? { isReady: true, message: 'Ready to build.', blockingIssues: [] })
    });
  });
  await mockTicketEvidenceSummary(page, options.latestRun ?? null);
}

async function mockTicketEvidenceSummary(page: Page, latestRun: { status: string } | null) {
  await page.route('**/irondev-api/api/projects/7/tickets/42/evidence-summary', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ticketId: 42,
        status: 'loaded',
        message: latestRun ? 'Execution evidence is available for this ticket.' : 'No linked execution evidence is available yet.',
        latestRun: latestRun
          ? {
              runId: RUN_ID,
              traceId: null,
              title: 'Skeleton run started for ticket 42.',
              status: latestRun.status,
              recommendation: latestRun.status === 'needsHumanReview' ? 'Human review required.' : 'Review latest run.',
              startedUtc: '2026-07-04T10:00:00Z',
              completedUtc: '2026-07-04T10:05:00Z'
            }
          : null,
        latestPromotionPackage: null,
        linkedTraceCount: 0,
        linkedDocumentCount: 0,
        linkedDecisionCount: 0,
        linkedRunCount: latestRun ? 1 : 0,
        hasBlockingWarnings: !latestRun,
        blockedActions: latestRun ? [] : ['No execution run is linked to this ticket yet.'],
        nextSafeAction: latestRun ? 'Review latest run' : 'Start disposable run'
      })
    });
  });
}
