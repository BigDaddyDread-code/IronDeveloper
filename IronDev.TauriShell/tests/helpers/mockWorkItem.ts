import type { Page, Route } from '@playwright/test';

export interface MockWorkItemProjectionOptions {
  projectId?: number;
  workItemId?: number;
  title?: string;
  stage?: string;
  state?: string;
  statusSummary?: string;
  gateState?: string;
  gateReason?: string;
  nextSafeAction?: string;
  technicalDetails?: string[];
  primaryActionKind?: string;
  primaryActionLabel?: string;
  primaryActionAllowed?: boolean;
  criterionCount?: number;
  affectedFiles?: string[];
  linkedChatSessionId?: number | null;
  ticket?: Record<string, unknown>;
  applyRecovery?: Record<string, unknown>;
  executionProof?: Record<string, unknown>;
  collaboration?: Record<string, unknown>;
  authority?: Record<string, unknown>;
}

export function workItemProjection(options: MockWorkItemProjectionOptions = {}) {
  const projectId = options.projectId ?? 7;
  const workItemId = options.workItemId ?? 42;
  const affectedFiles = options.affectedFiles ?? ['src/Catalog.cs'];
  return {
    projectId,
    workItemId,
    title: options.title ?? 'Add book sorting to catalog',
    stage: options.stage ?? 'Ticket',
    state: options.state ?? 'Draft',
    statusSummary: options.statusSummary ?? 'The ticket is ready for a governed run.',
    lastMeaningfulEventUtc: '2026-07-10T08:00:00Z',
    ticket: options.ticket ?? {
      id: workItemId,
      projectId,
      title: options.title ?? 'Add book sorting to catalog',
      status: options.state ?? 'Draft',
      acceptanceCriteria: 'Catalog sorts by title ascending',
      linkedFilePaths: affectedFiles.join('\n')
    },
    contract: {
      acceptanceCriterionCount: options.criterionCount ?? 1,
      affectedFileCount: affectedFiles.length,
      hasAcceptanceCriteria: (options.criterionCount ?? 1) > 0,
      affectedFiles,
      sourceChatSessionId: options.linkedChatSessionId ?? null,
      sourceChatMessageId: null,
      sourceDocumentVersionId: null
    },
    collaboration: options.collaboration ?? {
      revision: 0,
      assignee: null,
      followers: [],
      waitingOn: null,
      linkedChatSessionId: options.linkedChatSessionId ?? null,
      recentActivity: []
    },
    authority: options.authority ?? {
      currentUserId: 7,
      currentUserEligibleToContinue: true,
      soloApprovalExceptionAllowed: false,
      selfApprovalPolicy: 'A different eligible human must approve before this user can continue workflow.',
      acceptedApprovalActorId: '',
      acceptedApprovalActorDisplayName: '',
      continuationRequestedByUserId: '',
      soloApprovalExceptionUsed: false,
      eligibleApprovers: [
        { userId: 8, displayName: 'Alice Reviewer', email: 'alice@irondev.local', projectRole: 'Contributor' },
        { userId: 7, displayName: 'Bob Developer', email: 'bob@irondev.local', projectRole: 'Owner' }
      ],
      boundary: 'Eligible reviewer and approver lists come from backend project membership.'
    },
    latestRun: null,
    gate: {
      state: options.gateState ?? 'Open',
      reason: options.gateReason ?? 'Build readiness is satisfied.',
      nextSafeAction: options.nextSafeAction ?? 'Start a governed run when you are ready.',
      technicalDetails: options.technicalDetails ?? []
    },
    primaryAction: {
      kind: options.primaryActionKind ?? 'StartRun',
      label: options.primaryActionLabel ?? 'Start governed run',
      allowed: options.primaryActionAllowed ?? true,
      reason: options.gateReason ?? 'Build readiness is satisfied.'
    },
    applyRecovery: options.applyRecovery ?? {
      status: 'NotRequired',
      required: false,
      applyAttemptObserved: false,
      partialMutationPossible: false,
      succeededStageCount: 0,
      failedStageCount: 0,
      failedStages: [],
      technicalDetails: [],
      existingReceiptCount: 0,
      missingReceiptCount: 0,
      reason: 'No apply attempt or refusal was recorded for the latest run.',
      nextSafeAction: 'No apply recovery action is required.',
      retryAllowed: false,
      humanReviewRequired: false,
      boundary: 'Inspection only.'
    },
    executionProof: options.executionProof ?? {
      status: 'NoRun',
      hasRunRecord: false,
      executionStarted: false,
      executionCompleted: false,
      startedUtc: null,
      completedUtc: null,
      durableExecutionEventCount: 0,
      durableExecutionEvents: [],
      buildAndTestExecutionObserved: false,
      applyExecutionObserved: false,
      loopVerified: false,
      artifactEvidenceObserved: false,
      artifactEvidenceProvesExecution: false,
      gaps: [],
      reason: 'No durable run record exists for this Work Item.',
      nextSafeAction: 'Start a governed run when build readiness allows it.',
      boundary: 'Artifacts alone do not prove execution.'
    },
    evidenceLinks: {
      runReportApiPath: null,
      criticPackageApiPath: null,
      governanceLibraryPath: `/projects/${projectId}/library/governance`
    },
    boundary: 'Mocked backend Work Item truth.'
  };
}

export async function mockProjectWorkItem(
  page: Page,
  options: MockWorkItemProjectionOptions | (() => MockWorkItemProjectionOptions) = {}
) {
  const initial = typeof options === 'function' ? options() : options;
  const projectId = initial.projectId ?? 7;
  const workItemId = initial.workItemId ?? 42;
  await page.route(`**/irondev-api/api/projects/${projectId}/work-items/${workItemId}`, (route: Route) => {
    const current = typeof options === 'function' ? options() : options;
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(workItemProjection(current))
    });
  });
}
