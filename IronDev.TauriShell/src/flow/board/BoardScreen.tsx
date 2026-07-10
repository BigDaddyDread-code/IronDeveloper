import { useEffect, useState } from 'react';
import type { ProjectProvisioningReadinessUi, ProjectTicket } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { WorkItemStage, stageLabels, stageOrder } from '../flowTypes';

interface BoardScreenProps {
  onOpenWorkItem: (ticket: ProjectTicket | null) => void;
  onOpenBatch: () => void;
  onOpenProvisioning: () => void;
}

function stageForTicket(ticket: ProjectTicket): WorkItemStage {
  const status = (ticket.status ?? '').toLowerCase();
  if (status.includes('applied') || status.includes('done') || status.includes('closed')) {
    return 'done';
  }
  if (status.includes('approval') || status.includes('review')) {
    return 'review';
  }
  if (status.includes('build') || status.includes('progress')) {
    return 'build';
  }
  if (status.includes('draft') || status.includes('shap')) {
    return 'shape';
  }
  return 'ticket';
}

function emptyColumnMessage(stage: WorkItemStage): string {
  if (stage === 'shape') {
    return 'No draft work items. Next safe action: create a new work item from chat or the New work item button.';
  }
  if (stage === 'ticket') {
    return 'No ready tickets. Next safe action: confirm acceptance criteria on a draft ticket.';
  }
  if (stage === 'build') {
    return 'No active builds. Next safe action: open a ready ticket and start a governed run.';
  }
  if (stage === 'review') {
    return 'No human-gate runs. Next safe action: start a governed run and wait for the backend halt.';
  }
  return 'No applied tickets yet. Next safe action: complete continuation and controlled apply through the backend.';
}

function tagForTicket(ticket: ProjectTicket): { label: string; cls: string } {
  const stage = stageForTicket(ticket);
  if (stage === 'done') {
    return { label: 'complete', cls: 'fl-tag fl-green' };
  }
  if (stage === 'build') {
    return { label: 'in build', cls: 'fl-tag fl-bluet' };
  }
  if (stage === 'review') {
    return { label: 'in review', cls: 'fl-tag fl-redt' };
  }
  if (!ticket.acceptanceCriteria) {
    return { label: 'no criteria yet', cls: 'fl-tag fl-amber' };
  }
  return { label: ticket.priority ?? 'ready to assess', cls: 'fl-tag fl-bluet' };
}

export function BoardScreen({ onOpenWorkItem, onOpenBatch, onOpenProvisioning }: BoardScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // UX-START-1 — the cockpit header reads live provisioning readiness: the same
  // truth the run start enforces. Backend truth or a spinner, never inference.
  const [readiness, setReadiness] = useState<ProjectProvisioningReadinessUi | null>(null);
  const [readinessState, setReadinessState] = useState<'loading' | 'ready' | 'error'>('loading');

  useEffect(() => {
    if (project.selectedProjectId === null) {
      setReadiness(null);
      setReadinessState('ready');
      return;
    }
    const controller = new AbortController();
    setReadinessState('loading');
    session.client
      .getProvisioningReadiness(project.selectedProjectId, controller.signal)
      .then((result) => {
        setReadiness(result);
        setReadinessState('ready');
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setReadiness(null);
          setReadinessState('error');
        }
      });
    return () => controller.abort();
  }, [session.client, project.selectedProjectId]);

  useEffect(() => {
    if (project.selectedProjectId === null) {
      setTickets([]);
      setLoadState('ready');
      return;
    }
    const controller = new AbortController();
    setLoadState('loading');
    session.client
      .getProjectTickets(project.selectedProjectId, controller.signal)
      .then((result) => {
        setTickets(result.tickets ?? []);
        setLoadState('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }
        setErrorMessage(error instanceof Error ? error.message : 'Could not load tickets.');
        setLoadState('error');
      });
    return () => controller.abort();
  }, [session.client, project.selectedProjectId]);

  // The human's queue outranks new work: gate-waiting items first, then setup,
  // then new work. One primary action — the cockpit never makes the user hunt.
  const attentionTickets = tickets.filter((ticket) => stageForTicket(ticket) === 'review');
  const primaryAction: { label: string; testid: string; run: () => void } =
    attentionTickets.length > 0
      ? { label: 'Review waiting item', testid: 'flow.cockpit.primary.review', run: () => onOpenWorkItem(attentionTickets[0]) }
      : readinessState === 'ready' && readiness !== null && !readiness.isReady
        ? { label: 'Complete project setup', testid: 'flow.cockpit.primary.setup', run: onOpenProvisioning }
        : { label: 'Start new work item', testid: 'flow.cockpit.primary.new', run: () => onOpenWorkItem(null) };

  const blockedChecks = (readiness?.checks ?? []).filter((check) => check.blocking);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
        <div>
          <h1 className="fl-h1">{project.selectedProjectName ?? 'Board'}</h1>
          <p className="fl-sub">Work items flow left to right. A card moves when its gate is satisfied, never before.</p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {readinessState === 'loading' ? (
            <span className="fl-tag" data-testid="flow.cockpit.badge">checking readiness…</span>
          ) : readiness !== null ? (
            <span
              className={readiness.isReady ? 'fl-tag fl-green' : 'fl-tag fl-amber'}
              data-testid="flow.cockpit.badge"
            >
              {readiness.isReady ? 'Ready to run' : `Setup incomplete · ${readiness.blockedCount} blocker(s)`}
            </span>
          ) : readinessState === 'error' ? (
            <span className="fl-tag" data-testid="flow.cockpit.badge">readiness unavailable</span>
          ) : null}
          <button className="fl-btn" onClick={onOpenBatch} data-testid="flow.board.batch">
            Run queue
          </button>
          <button className="fl-btn" onClick={() => onOpenWorkItem(null)} data-testid="flow.board.new">
            New work item
          </button>
          <button className="fl-btn fl-pri" onClick={primaryAction.run} data-testid={primaryAction.testid}>
            {primaryAction.label}
          </button>
        </div>
      </div>

      {blockedChecks.length > 0 ? (
        <div className="fl-col" style={{ margin: '12px 0', padding: 12 }} data-testid="flow.cockpit.setup">
          <div className="fl-colh">Setup incomplete — the backend will refuse governed runs</div>
          {blockedChecks.map((check) => (
            <p className="fl-empty" key={check.name} data-testid={`flow.cockpit.setup.${check.name.replace(/\s+/g, '-').toLowerCase()}`}>
              <strong>{check.name}</strong> · {check.state} — {check.evidence} {check.remedy}
            </p>
          ))}
          <p className="fl-sub">
            You can shape work and draft tickets now; governed runs unlock when backend readiness is satisfied.{' '}
            <button className="fl-btn" onClick={onOpenProvisioning} data-testid="flow.cockpit.setup.open">
              Open project setup
            </button>
          </p>
        </div>
      ) : null}

      {attentionTickets.length > 0 ? (
        <div className="fl-col" style={{ margin: '12px 0', padding: 12 }} data-testid="flow.cockpit.attention">
          <div className="fl-colh">Needs your attention</div>
          {attentionTickets.map((ticket) => (
            <button className="fl-card" key={ticket.id} onClick={() => onOpenWorkItem(ticket)}>
              <p className="fl-card-title">{ticket.title ?? `Work item ${ticket.id}`}</p>
              <span className="fl-sub">
                Status: {ticket.status ?? 'unknown'} — waiting at the human gate. Open to review, disposition, and
                decide.
              </span>
            </button>
          ))}
        </div>
      ) : null}

      {loadState === 'error' ? <div className="fl-error">Tickets did not load: {errorMessage}</div> : null}

      <div className="fl-board" data-testid="flow.board.columns">
        {stageOrder.map((stage) => {
          const columnTickets = tickets.filter((ticket) => stageForTicket(ticket) === stage);
          return (
            <div className="fl-col" key={stage} data-testid={`flow.board.column.${stage}`}>
              <div className="fl-colh">
                {stageLabels[stage]} <span>{columnTickets.length}</span>
              </div>
              {loadState === 'loading' ? (
                <p className="fl-empty" data-testid={`flow.board.loading.${stage}`}>
                  Loading {stageLabels[stage].toLowerCase()} work items from the API...
                </p>
              ) : columnTickets.length === 0 ? (
                <p className="fl-empty" data-testid={`flow.board.empty.${stage}`}>
                  {emptyColumnMessage(stage)}
                </p>
              ) : (
                columnTickets.map((ticket) => {
                  const tag = tagForTicket(ticket);
                  return (
                    <button className="fl-card" key={ticket.id} onClick={() => onOpenWorkItem(ticket)}>
                      <span className="fl-card-id">WI-{ticket.id}</span>
                      <p className="fl-card-title">{ticket.title ?? 'Untitled work item'}</p>
                      <span className={tag.cls}>{tag.label}</span>
                    </button>
                  );
                })
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
