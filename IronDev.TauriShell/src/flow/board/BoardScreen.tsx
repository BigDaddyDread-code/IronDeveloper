import { useEffect, useMemo, useState } from 'react';
import type { ProjectBoardItemReadModel, ProjectBoardReadModel } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { BatchScreen } from '../batch/BatchScreen';

interface BoardScreenProps {
  onOpenWorkItem: (workItemId: number | null) => void;
  onOpenProvisioning: () => void;
}

type BoardFilter = 'all' | 'attention' | 'active';

const columns = [
  { stage: 'Shape', label: 'Shape' },
  { stage: 'Ticket', label: 'Ticket' },
  { stage: 'Build', label: 'Build' },
  { stage: 'Review', label: 'Review' },
  { stage: 'Done', label: 'Done' }
] as const;

function emptyColumnMessage(stage: string): string {
  if (stage === 'Shape') return 'No draft work items. Next safe action: start new work from Workshop or the Board.';
  if (stage === 'Ticket') return 'No confirmed tickets. Next safe action: confirm acceptance criteria on shaped work.';
  if (stage === 'Build') return 'No governed runs are building. Next safe action: open an eligible ticket.';
  if (stage === 'Review') return 'No work is waiting at review. Next safe action: inspect active builds.';
  return 'No applied tickets yet. Next safe action: complete controlled apply through the Work Item.';
}

function formatEventTime(value: string | undefined): string {
  if (!value) return 'Time unavailable';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return 'Time unavailable';
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  }).format(parsed);
}

function stateTone(item: ProjectBoardItemReadModel): string {
  if (item.stage === 'Done') return 'fl-tag fl-green';
  if (item.needsAttention) return 'fl-tag fl-redt';
  if (item.stage === 'Build') return 'fl-tag fl-bluet';
  return 'fl-tag fl-amber';
}

function itemId(item: ProjectBoardItemReadModel): number | null {
  return typeof item.workItemId === 'number' ? item.workItemId : null;
}

export function BoardScreen({ onOpenWorkItem, onOpenProvisioning }: BoardScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const [board, setBoard] = useState<ProjectBoardReadModel | null>(null);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [filter, setFilter] = useState<BoardFilter>('all');
  const [runQueueOpen, setRunQueueOpen] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (project.selectedProjectId === null) {
      setBoard(null);
      setLoadState('ready');
      return;
    }

    const controller = new AbortController();
    setLoadState('loading');
    setErrorMessage(null);
    session.client
      .getProjectBoard(project.selectedProjectId, controller.signal)
      .then((result) => {
        setBoard(result);
        setLoadState('ready');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setBoard(null);
        setErrorMessage(error instanceof Error ? error.message : 'The Board projection could not be loaded.');
        setLoadState('error');
      });

    return () => controller.abort();
  }, [session.client, project.selectedProjectId, reloadKey]);

  const items = board?.items ?? [];
  const attentionItems = useMemo(() => items.filter((item) => item.needsAttention === true), [items]);
  const visibleItems = useMemo(() => {
    if (filter === 'attention') return attentionItems;
    if (filter === 'active') return items.filter((item) => item.stage !== 'Done');
    return items;
  }, [attentionItems, filter, items]);
  const unknownStageCount = visibleItems.filter((item) => !columns.some((column) => column.stage === item.stage)).length;
  const readiness = board?.readiness;
  const firstAttentionId = attentionItems.map(itemId).find((id): id is number => id !== null);

  const primaryAction = firstAttentionId !== undefined
    ? {
        label: 'Review waiting item',
        testid: 'flow.cockpit.primary.review',
        run: () => onOpenWorkItem(firstAttentionId)
      }
    : readiness?.isReady === false
      ? {
          label: 'Complete project setup',
          testid: 'flow.cockpit.primary.setup',
          run: onOpenProvisioning
        }
      : {
          label: 'Start new work',
          testid: 'flow.board.new',
          run: () => onOpenWorkItem(null)
        };

  if (loadState === 'error') {
    return (
      <section className="fl-board-state" data-testid="flow.board.error" role="alert" aria-live="assertive">
        <p className="fl-eyebrow">Board unavailable</p>
        <h1 className="fl-h1">Current work could not be loaded.</h1>
        <p className="fl-sub">{errorMessage}</p>
        <p>The client will not reconstruct pipeline state from ticket strings.</p>
        <button className="fl-btn fl-pri" type="button" onClick={() => setReloadKey((value) => value + 1)} data-testid="flow.board.retry">
          Retry
        </button>
      </section>
    );
  }

  return (
    <div data-testid="flow.board">
      <header className="fl-board-head">
        <div>
          <p className="fl-eyebrow">Project Board</p>
          <h1 className="fl-h1">{board?.projectName ?? project.selectedProjectName ?? 'Board'}</h1>
          <p className="fl-sub">Shared work, current blockers, and the next safe action.</p>
        </div>
        <div className="fl-board-actions">
          {loadState === 'loading' ? (
            <span className="fl-tag" data-testid="flow.cockpit.badge" role="status" aria-live="polite">Loading Board...</span>
          ) : readiness ? (
            <span className={readiness.isReady ? 'fl-tag fl-green' : 'fl-tag fl-amber'} data-testid="flow.cockpit.badge">
              {readiness.isReady ? 'Ready to run' : `Setup incomplete · ${readiness.blockedCount ?? 0} blocker(s)`}
            </span>
          ) : null}
          <button
            className="fl-btn"
            type="button"
            aria-expanded={runQueueOpen}
            aria-controls="flow-run-queue"
            onClick={() => setRunQueueOpen((open) => !open)}
            data-testid="flow.board.batch"
          >
            Run queue
          </button>
          <button
            className="fl-btn fl-pri"
            type="button"
            onClick={primaryAction.run}
            data-testid={primaryAction.testid}
            disabled={loadState !== 'ready'}
          >
            {primaryAction.label}
          </button>
        </div>
      </header>

      {runQueueOpen ? (
        <section id="flow-run-queue" className="fl-run-queue" data-testid="flow.board.runQueue">
          <BatchScreen embedded />
        </section>
      ) : null}

      {readiness?.isReady === false ? (
        <section className="fl-board-blocked" data-testid="flow.cockpit.setup">
          <div>
            <strong>Governed runs are blocked until project setup is complete.</strong>
            <p>{readiness.nextAction?.nextSafeAction ?? 'Open setup to resolve the backend readiness checks.'}</p>
          </div>
          <button className="fl-btn" type="button" onClick={onOpenProvisioning} data-testid="flow.cockpit.setup.open">
            View setup details
          </button>
        </section>
      ) : null}

      {attentionItems.length > 0 ? (
        <section className="fl-board-attention" data-testid="flow.cockpit.attention">
          <div className="fl-board-section-title">
            <div>
              <p className="fl-eyebrow">Needs attention</p>
              <h2>{attentionItems.length} waiting {attentionItems.length === 1 ? 'item' : 'items'}</h2>
            </div>
          </div>
          <div className="fl-board-attention-list">
            {attentionItems.map((item) => {
              const id = itemId(item);
              return (
                <button
                  className="fl-attention-row"
                  type="button"
                  key={id ?? item.title}
                  onClick={() => id !== null && onOpenWorkItem(id)}
                  disabled={id === null}
                  data-testid={id === null ? undefined : `flow.board.attention.${id}`}
                >
                  <span className="fl-card-id">{id === null ? 'Work item' : `WI-${id}`}</span>
                  <span className="fl-attention-copy">
                    <strong>{item.title ?? 'Untitled work item'}</strong>
                    <span>{item.attentionReason ?? 'The backend marked this item as needing attention.'}</span>
                    <span className="fl-next-safe">Next: {item.nextSafeAction ?? 'Open the Work Item for current state.'}</span>
                  </span>
                  <span className="fl-waiting">{item.waitingOn?.label ?? 'Waiting state unavailable'}</span>
                </button>
              );
            })}
          </div>
        </section>
      ) : null}

      <div className="fl-board-toolbar">
        <div className="fl-segmented" role="group" aria-label="Board filter">
          {([
            ['all', `All ${items.length}`],
            ['attention', `Needs attention ${attentionItems.length}`],
            ['active', `Active ${items.filter((item) => item.stage !== 'Done').length}`]
          ] as const).map(([value, label]) => (
            <button
              type="button"
              key={value}
              className={filter === value ? 'active' : ''}
              aria-pressed={filter === value}
              onClick={() => setFilter(value)}
              data-testid={`flow.board.filter.${value}`}
            >
              {label}
            </button>
          ))}
        </div>
        <button className="fl-btn fl-mini" type="button" onClick={() => setReloadKey((value) => value + 1)} disabled={loadState === 'loading'}>
          Refresh
        </button>
      </div>

      {unknownStageCount > 0 ? (
        <p className="fl-error" data-testid="flow.board.unknownStage">
          {unknownStageCount} work item(s) returned an unsupported Board stage and were not placed in a column.
        </p>
      ) : null}

      <div className="fl-board" data-testid="flow.board.columns" aria-busy={loadState === 'loading'}>
        {columns.map((column) => {
          const columnItems = visibleItems.filter((item) => item.stage === column.stage);
          return (
            <section className="fl-col" key={column.stage} data-testid={`flow.board.column.${column.stage.toLowerCase()}`}>
              <div className="fl-colh">
                {column.label} <span>{columnItems.length}</span>
              </div>
              {loadState === 'loading' ? (
                <p className="fl-empty" data-testid={`flow.board.loading.${column.stage.toLowerCase()}`}>Loading...</p>
              ) : columnItems.length === 0 ? (
                <p className="fl-empty" data-testid={`flow.board.empty.${column.stage.toLowerCase()}`}>{emptyColumnMessage(column.stage)}</p>
              ) : (
                columnItems.map((item) => {
                  const id = itemId(item);
                  return (
                    <button
                      className="fl-card fl-work-card"
                      type="button"
                      key={id ?? item.title}
                      onClick={() => id !== null && onOpenWorkItem(id)}
                      disabled={id === null}
                      data-testid={id === null ? undefined : `flow.board.item.${id}`}
                    >
                      <span className="fl-card-id">{id === null ? 'Work item' : `WI-${id}`}</span>
                      <span className="fl-card-title">{item.title ?? 'Untitled work item'}</span>
                      <span className="fl-card-state">
                        <span className={stateTone(item)}>{item.state ?? 'Unknown'}</span>
                        <span>{item.priority ?? 'Priority unavailable'}</span>
                      </span>
                      {item.waitingOn?.label ? <span className="fl-card-meta">Waiting on {item.waitingOn.label}</span> : null}
                      {item.assignee?.displayName ? <span className="fl-card-meta">Assigned to {item.assignee.displayName}</span> : null}
                      <span className="fl-card-meta">Updated {formatEventTime(item.lastMeaningfulEventUtc)}</span>
                    </button>
                  );
                })
              )}
            </section>
          );
        })}
      </div>
    </div>
  );
}
