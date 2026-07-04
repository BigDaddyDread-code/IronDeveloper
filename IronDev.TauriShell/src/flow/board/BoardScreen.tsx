import { useEffect, useState } from 'react';
import type { ProjectTicket } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { WorkItemStage, stageLabels, stageOrder } from '../flowTypes';

interface BoardScreenProps {
  onOpenWorkItem: (ticket: ProjectTicket | null) => void;
  onOpenBatch: () => void;
}

function stageForTicket(ticket: ProjectTicket): WorkItemStage {
  const status = (ticket.status ?? '').toLowerCase();
  if (status.includes('done') || status.includes('closed') || status.includes('complete')) {
    return 'done';
  }
  if (status.includes('review')) {
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

export function BoardScreen({ onOpenWorkItem, onOpenBatch }: BoardScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

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

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
        <div>
          <h1 className="fl-h1">Board</h1>
          <p className="fl-sub">Work items flow left to right. A card moves when its gate is satisfied, never before.</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="fl-btn" onClick={onOpenBatch} data-testid="flow.board.batch">
            Run queue
          </button>
          <button className="fl-btn fl-pri" onClick={() => onOpenWorkItem(null)} data-testid="flow.board.new">
            New work item
          </button>
        </div>
      </div>

      {loadState === 'error' ? <div className="fl-error">Tickets did not load: {errorMessage}</div> : null}

      <div className="fl-board" data-testid="flow.board.columns">
        {stageOrder.map((stage) => {
          const columnTickets = tickets.filter((ticket) => stageForTicket(ticket) === stage);
          return (
            <div className="fl-col" key={stage}>
              <div className="fl-colh">
                {stageLabels[stage]} <span>{columnTickets.length}</span>
              </div>
              {loadState === 'loading' ? (
                <p className="fl-empty">Loading…</p>
              ) : columnTickets.length === 0 ? (
                <p className="fl-empty">—</p>
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
