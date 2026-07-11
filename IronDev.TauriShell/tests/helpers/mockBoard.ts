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
    latestRuns?: Record<number, BoardRunFixture>;
}

export function projectBoardResponse(options: BoardFixtureOptions = {}) {
  const projectId = options.projectId ?? 7;
  const tickets = options.tickets ?? [];
  const readiness = { ...readyBoardReadiness, projectId, ...(options.readiness ?? {}) };
  const items = tickets.map((ticket) => itemFrom(ticket, options.latestRuns?.[ticket.id ?? 0]));
  return {
    projectId,
    projectName: options.projectName ?? 'IronDeveloper',
    generatedUtc: '2026-07-11T01:00:00Z',
    readiness,
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
