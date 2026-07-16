import type { Page, Route } from '@playwright/test';

export const readyBoardReadiness = {
  projectId: 7,
  isReady: true,
  blockedCount: 0,
  blockedStates: [] as string[],
  checks: [] as unknown[],
  nextAction: {
    kind: 'OpenBoard',
    checkCode: null,
    allowed: true,
    reasonCode: null,
    label: 'Open Board',
    nextSafeAction: 'Open the project Board.'
  },
  proposedProfile: null,
  boundary: 'Backend readiness truth.'
};

export interface RunReadinessFixture extends Record<string, unknown> {
  projectId: number;
  projectSetupReady: boolean;
  executionReady: boolean;
  completionCapabilityReady: boolean;
  readyToRun: boolean;
  state: string;
  blockedCount: number;
  provisioning: Record<string, unknown> | null;
  agents: Array<Record<string, unknown>>;
  blockers: Array<Record<string, unknown>>;
  completionCapability: Record<string, unknown> | null;
  nextAction: {
    kind: string;
    label: string;
    nextSafeAction: string;
    targetProductRoute: string;
    command?: string;
  };
  boundary: string;
}

export function readyToRunReadiness(projectId = 7): RunReadinessFixture {
  return {
    projectId,
    projectSetupReady: true,
    executionReady: true,
    completionCapabilityReady: true,
    readyToRun: true,
    state: 'ReadyToRun',
    blockedCount: 0,
    provisioning: null,
    agents: [],
    blockers: [],
    completionCapability: null,
    nextAction: {
      kind: 'StartRun',
      label: 'Ready to run',
      nextSafeAction: 'Open a Work Item and start a governed run.',
      targetProductRoute: ''
    },
    boundary: 'Backend run readiness truth.'
  };
}

export function projectWorkSessionRequiredReadiness(projectId = 7): RunReadinessFixture {
  const command = '.\\tools\\localtest\\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply';
  return {
    ...readyToRunReadiness(projectId),
    completionCapabilityReady: false,
    readyToRun: false,
    state: 'ProjectWorkSessionRequired',
    completionCapability: {
      projectId,
      isReady: false,
      state: 'Disabled',
      reasonCode: 'ProjectApplyCapabilityDisabled',
      reason: 'Controlled sandbox apply is disabled for this API session.'
    },
    nextAction: {
      kind: 'RestartProjectWorkSession',
      label: 'Project-work session required',
      nextSafeAction: 'This session can build, test and review work, but controlled sandbox apply is disabled. Restart IronDev in sandbox-apply mode before beginning this Work Item.',
      targetProductRoute: '',
      command
    }
  };
}

export function runConfigurationRequiredReadiness(projectId = 7): RunReadinessFixture {
  const roles = [[4, 'Analyst'], [1, 'Builder'], [2, 'Tester'], [3, 'Critic']] as const;
  return {
    ...readyToRunReadiness(projectId),
    executionReady: false,
    readyToRun: false,
    state: 'RunConfigurationRequired',
    blockedCount: roles.length,
    blockers: roles.map(([role, label]) => ({
      role,
      effectiveProvider: 'fake',
      effectiveModel: 'gpt-4o',
      connectionId: 'deployment-default',
      sourceLayer: 'BuiltIn',
      reasonCode: 'RunAgentProviderNotExecutable',
      reason: `Provider 'fake' cannot execute ${label}.`,
      nextSafeAction: 'Test an executable connection and publish the project profile.'
    })),
    nextAction: {
      kind: 'ConfigureRunAgents',
      label: 'Configure run agents',
      nextSafeAction: 'Open AI Connections, test an executable connection, then publish each project agent profile.',
      targetProductRoute: `/projects/${projectId}/library/settings/agents`
    }
  };
}

export function setupIncompleteRunReadiness(
  projectId = 7,
  provisioning: Record<string, unknown> | null = null
): RunReadinessFixture {
  return {
    ...readyToRunReadiness(projectId),
    projectSetupReady: false,
    readyToRun: false,
    state: 'SetupIncomplete',
    provisioning,
    nextAction: {
      kind: 'ResolveProjectSetup',
      label: 'Resolve project setup',
      nextSafeAction: 'Complete project setup and re-check readiness.',
      targetProductRoute: `/projects/${projectId}/setup`
    }
  };
}

interface BoardTicketFixture {
  id?: number;
  title?: string;
  status?: string;
  priority?: string;
  acceptanceCriteria?: string | null;
  blockedByTicketIds?: string | null;
}

interface BoardRunFixture {
  status: string;
  summary?: string;
  failureReason?: string | null;
}

export async function mockProjectBoard(
  page: Page,
  options: BoardFixtureOptions = {}
) {
  const projectId = options.projectId ?? 7;
  await page.route(`**/irondev-api/api/projects/${projectId}/board`, (route) =>
    json(route, projectBoardResponse(options))
  );
}

export interface BoardFixtureOptions {
    projectId?: number;
    projectName?: string;
    tickets?: BoardTicketFixture[];
    readiness?: Record<string, unknown>;
    runReadiness?: RunReadinessFixture;
    latestRuns?: Record<number, BoardRunFixture>;
}

export function projectBoardResponse(options: BoardFixtureOptions = {}) {
  const projectId = options.projectId ?? 7;
  const tickets = options.tickets ?? [];
  const readiness = { ...readyBoardReadiness, projectId, ...(options.readiness ?? {}) };
  const runReadiness = options.runReadiness ?? readyToRunReadiness(projectId);
  const items = tickets.map((ticket) => itemFrom(ticket, options.latestRuns?.[ticket.id ?? 0]));
  return {
    projectId,
    projectName: options.projectName ?? 'IronDeveloper',
    generatedUtc: '2026-07-11T01:00:00Z',
    readiness,
    runReadiness,
    items
  };
}

function itemFrom(ticket: BoardTicketFixture, run?: BoardRunFixture) {
  const ticketStatus = ticket.status ?? 'Draft';
  const state = run?.status ?? ticketStatus;
  const normalized = state.toLowerCase();
  const stage = run
    ? normalized === 'applied'
      ? 'Done'
      : ['pausedforapproval', 'completed', 'promoted'].includes(normalized)
        ? 'Review'
        : 'Build'
    : normalized.includes('applied') || normalized === 'done' || normalized === 'closed'
      ? 'Done'
      : normalized.includes('approval') || normalized.includes('review')
        ? 'Review'
        : normalized.includes('build') || normalized.includes('progress') || normalized.includes('failed') || normalized.includes('blocked')
          ? 'Build'
          : normalized.includes('draft') || normalized.includes('shape')
            ? 'Shape'
            : 'Ticket';
  const needsAttention = run
    ? ['pausedforapproval', 'completed', 'promoted', 'failed', 'cancelled'].includes(normalized)
    : Boolean(ticket.blockedByTicketIds) || normalized.includes('blocked') || normalized.includes('failed');
  const waitingOn = needsAttention
    ? { kind: 'Human', label: normalized === 'pausedforapproval' ? 'Human review' : 'Project team' }
    : run && ['created', 'running'].includes(normalized)
      ? { kind: 'IronDev', label: 'IronDev run' }
      : null;

  return {
    workItemId: ticket.id,
    title: ticket.title,
    stage,
    state,
    priority: ticket.priority ?? 'Medium',
    needsAttention,
    attentionReason: needsAttention
      ? run?.failureReason ?? run?.summary ?? `Work item state is ${state}.`
      : ticket.acceptanceCriteria
        ? null
        : 'Acceptance criteria have not been confirmed.',
    nextSafeAction: needsAttention
      ? 'Open the Work Item and review the backend evidence.'
      : ticket.acceptanceCriteria
        ? 'Open the Work Item and check build readiness.'
        : 'Shape the requirement and confirm acceptance criteria.',
    waitingOn,
    assignee: null,
    lastMeaningfulEventUtc: '2026-07-11T01:00:00Z',
    latestRun: run
      ? {
          runId: `run-${ticket.id}`,
          status: run.status,
          summary: run.summary ?? '',
          failureReason: run.failureReason ?? null,
          requiresHumanAction: needsAttention,
          updatedUtc: '2026-07-11T01:00:00Z'
        }
      : null
  };
}

function json(route: Route, body: unknown) {
  return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}
