import { useCallback, useEffect, useState } from 'react';
import type {
  ProjectTicket,
  SkeletonBatchMap,
  SkeletonBatchPlan,
  SkeletonBatchRunStatus,
  SkeletonGateRecommendation
} from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

// P2-7 — the batch surface: define linked tickets, hit run, watch the system
// sequence them. Map → plan → run → advance, each step a REQUEST to a governed
// endpoint. Nothing here decides: the backend detects, sequences, starts, and
// every per-ticket gate stays human — the gate recommendation shown beside a
// halted ticket is advice, and the value itself says so.

type Phase = 'select' | 'mapped' | 'planned' | 'running';

function ticketStatusTone(status: string): string {
  if (status === 'Applied') return 'var(--fl-acc-ink)';
  if (status === 'PausedForApproval' || status === 'Completed') return 'var(--fl-gate-ink)';
  if (status === 'Failed' || status === 'Cancelled') return 'var(--fl-gate-ink)';
  return 'var(--fl-ink2)';
}

export function BatchScreen() {
  const session = useSessionContext();
  const project = useProjectContext();

  const [tickets, setTickets] = useState<ProjectTicket[]>([]);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [phase, setPhase] = useState<Phase>('select');
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [map, setMap] = useState<SkeletonBatchMap | null>(null);
  const [mapId, setMapId] = useState<string>('');
  const [plan, setPlan] = useState<SkeletonBatchPlan | null>(null);
  const [planId, setPlanId] = useState<string>('');
  const [runStatus, setRunStatus] = useState<SkeletonBatchRunStatus | null>(null);
  const [recommendations, setRecommendations] = useState<Record<number, SkeletonGateRecommendation>>({});

  useEffect(() => {
    if (project.selectedProjectId === null) {
      return;
    }
    const controller = new AbortController();
    session.client
      .getProjectTickets(project.selectedProjectId, controller.signal)
      .then((result) => setTickets(result.tickets ?? []))
      .catch(() => undefined);
    return () => controller.abort();
  }, [session.client, project.selectedProjectId]);

  const projectId = project.selectedProjectId;

  const toggle = (ticketId: number) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(ticketId)) {
        next.delete(ticketId);
      } else {
        next.add(ticketId);
      }
      return next;
    });

  const detectMap = useCallback(async () => {
    if (projectId === null || selected.size < 2 || busy) return;
    setBusy('map');
    setError(null);
    try {
      const outcome = await session.client.detectBatchMap(projectId, [...selected]);
      if (!outcome.succeeded || !outcome.map) {
        setError(outcome.failureReason);
        return;
      }
      setMap(outcome.map);
      setMapId(outcome.mapId);
      setPhase('mapped');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Detection failed.');
    } finally {
      setBusy(null);
    }
  }, [projectId, selected, busy, session.client]);

  const planBatch = useCallback(async () => {
    if (projectId === null || !mapId || busy) return;
    setBusy('plan');
    setError(null);
    try {
      const outcome = await session.client.planBatch(projectId, mapId);
      if (!outcome.succeeded || !outcome.plan) {
        setError(outcome.failureReason);
        return;
      }
      setPlan(outcome.plan);
      setPlanId(outcome.planId);
      setPhase('planned');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Planning failed.');
    } finally {
      setBusy(null);
    }
  }, [projectId, mapId, busy, session.client]);

  const refreshRecommendations = useCallback(
    async (status: SkeletonBatchRunStatus) => {
      if (projectId === null) return;
      const halted = status.tickets.filter((t) => t.runStatus === 'PausedForApproval' && t.runId);
      const next: Record<number, SkeletonGateRecommendation> = {};
      await Promise.all(
        halted.map(async (t) => {
          try {
            next[t.ticketId] = await session.client.getGateRecommendation(projectId, t.ticketId, t.runId);
          } catch {
            // recommendation is advisory — its absence blocks nothing
          }
        })
      );
      setRecommendations(next);
    },
    [projectId, session.client]
  );

  const startRun = useCallback(async () => {
    if (projectId === null || !planId || busy) return;
    setBusy('run');
    setError(null);
    try {
      const outcome = await session.client.startBatchRun(projectId, planId);
      if (!outcome.succeeded || !outcome.status) {
        setError(outcome.failureReason);
        return;
      }
      setRunStatus(outcome.status);
      setPhase('running');
      await refreshRecommendations(outcome.status);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Run failed to start.');
    } finally {
      setBusy(null);
    }
  }, [projectId, planId, busy, session.client, refreshRecommendations]);

  const advance = useCallback(async () => {
    if (projectId === null || !runStatus || busy) return;
    setBusy('advance');
    setError(null);
    try {
      const outcome = await session.client.advanceBatchRun(projectId, runStatus.batchId);
      if (outcome.status) {
        setRunStatus(outcome.status);
        await refreshRecommendations(outcome.status);
      } else if (!outcome.succeeded) {
        setError(outcome.failureReason);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Advance failed.');
    } finally {
      setBusy(null);
    }
  }, [projectId, runStatus, busy, session.client, refreshRecommendations]);

  const titleFor = (ticketId: number) => tickets.find((t) => t.id === ticketId)?.title ?? `WI-${ticketId}`;

  return (
    <div data-testid="flow.batch">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
        <div>
          <h1 className="fl-h1">Batch</h1>
          <p className="fl-sub">
            Define linked work items, hit run — the system works out the order. Every gate stays human, per ticket.
          </p>
        </div>
      </div>

      {error ? <div className="fl-error" data-testid="flow.batch.error">{error}</div> : null}

      <div className="fl-cols">
        <div className="fl-panel-box">
          <p className="fl-plabel">1 · Select linked work items</p>
          <p className="fl-empty" style={{ marginTop: 0 }}>
            Pick two or more. Dependencies come from each ticket&apos;s declared blocks and predicted file footprint.
          </p>
          <div data-testid="flow.batch.tickets">
            {tickets.map((ticket) => (
              <label className="fl-qbox" key={ticket.id} style={{ cursor: 'pointer' }}>
                <input
                  type="checkbox"
                  checked={ticket.id !== undefined && selected.has(ticket.id)}
                  onChange={() => ticket.id !== undefined && toggle(ticket.id)}
                  data-testid={`flow.batch.pick.${ticket.id}`}
                  disabled={phase !== 'select'}
                />
                <span style={{ marginLeft: 8 }}>
                  <strong style={{ fontSize: 12.5 }}>WI-{ticket.id}</strong> {ticket.title}
                </span>
              </label>
            ))}
          </div>
          <button
            className="fl-btn fl-pri"
            style={{ marginTop: 10 }}
            disabled={selected.size < 2 || phase !== 'select' || busy !== null}
            onClick={() => void detectMap()}
            data-testid="flow.batch.detect"
          >
            {busy === 'map' ? 'Detecting…' : 'Detect dependencies'}
          </button>
        </div>

        <div className="fl-panel-box">
          <p className="fl-plabel">2 · The map</p>
          {map === null ? (
            <p className="fl-empty">Dependencies appear here once detected — every edge names its reason.</p>
          ) : (
            <div data-testid="flow.batch.map">
              {map.edges.length === 0 ? (
                <p className="fl-empty">No dependencies — every selected ticket is independent.</p>
              ) : (
                map.edges.map((edge, index) => (
                  <div className="fl-qbox" key={index}>
                    <span>
                      <strong style={{ fontSize: 12.5 }}>
                        WI-{edge.fromTicketId} → WI-{edge.toTicketId}
                      </strong>{' '}
                      <span className="fl-tag fl-bluet">{edge.kind}</span>
                      <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>{edge.reason}</span>
                    </span>
                  </div>
                ))
              )}
              {map.warnings.map((warning) => (
                <div className="fl-qbox" key={warning} style={{ color: 'var(--fl-gate-ink)' }}>
                  <span style={{ fontSize: 12.5 }}>{warning}</span>
                </div>
              ))}
              <button
                className="fl-btn fl-pri"
                style={{ marginTop: 10 }}
                disabled={phase === 'running' || busy !== null || (phase !== 'mapped' && phase !== 'planned')}
                onClick={() => void planBatch()}
                data-testid="flow.batch.plan"
              >
                {busy === 'plan' ? 'Sequencing…' : 'Sequence into waves'}
              </button>
            </div>
          )}
        </div>
      </div>

      {plan ? (
        <div className="fl-panel-box" style={{ marginTop: 12 }}>
          <p className="fl-plabel">3 · The plan</p>
          {!plan.schedulable ? (
            <div className="fl-error" data-testid="flow.batch.cycle">
              {plan.cycleBlockers.map((blocker) => (
                <div key={blocker.detail}>{blocker.detail}</div>
              ))}
            </div>
          ) : (
            <>
              <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }} data-testid="flow.batch.waves">
                {plan.waves.map((wave) => (
                  <div className="fl-qbox" key={wave.waveNumber} style={{ minWidth: 160 }}>
                    <span>
                      <strong style={{ fontSize: 12.5 }}>Wave {wave.waveNumber}</strong>
                      {wave.ticketIds.map((ticketId) => (
                        <span key={ticketId} style={{ display: 'block', fontSize: 12.5 }}>
                          WI-{ticketId} · {titleFor(ticketId)}
                        </span>
                      ))}
                    </span>
                  </div>
                ))}
              </div>
              {phase !== 'running' ? (
                <button
                  className="fl-btn fl-pri"
                  style={{ marginTop: 10 }}
                  disabled={busy !== null}
                  onClick={() => void startRun()}
                  data-testid="flow.batch.run"
                >
                  {busy === 'run' ? 'Starting…' : 'Run the batch'}
                </button>
              ) : null}
            </>
          )}
        </div>
      ) : null}

      {runStatus ? (
        <div className="fl-panel-box" style={{ marginTop: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
            <p className="fl-plabel">4 · Running — each ticket runs its own governed loop</p>
            <span
              className={runStatus.batchComplete ? 'fl-tag fl-green' : 'fl-tag fl-bluet'}
              data-testid="flow.batch.completeState"
            >
              {runStatus.batchComplete ? 'batch complete' : 'in progress'}
            </span>
          </div>
          <div data-testid="flow.batch.runTickets">
            {runStatus.tickets.map((ticket) => (
              <div className="fl-qbox" key={ticket.ticketId}>
                <span style={{ width: '100%' }}>
                  <strong style={{ fontSize: 12.5 }}>
                    WI-{ticket.ticketId} · wave {ticket.wave} ·{' '}
                    <span style={{ color: ticketStatusTone(ticket.runStatus) }}>
                      {ticket.runStatus || (ticket.eligible ? 'eligible' : 'waiting')}
                    </span>
                  </strong>
                  {ticket.waitingOn.length > 0 ? (
                    <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>
                      waiting on {ticket.waitingOn.join(', ')}
                    </span>
                  ) : null}
                  {recommendations[ticket.ticketId] ? (
                    <span
                      style={{ display: 'block', fontSize: 12, color: 'var(--fl-ink2)', marginTop: 4 }}
                      data-testid={`flow.batch.recommendation.${ticket.ticketId}`}
                    >
                      Policy (advisory): {recommendations[ticket.ticketId].tier} —{' '}
                      {recommendations[ticket.ticketId].recommendation}. Open the work item to decide — policy cannot click.
                    </span>
                  ) : null}
                </span>
              </div>
            ))}
          </div>
          <button
            className="fl-btn"
            style={{ marginTop: 10 }}
            disabled={busy !== null || runStatus.batchComplete}
            onClick={() => void advance()}
            data-testid="flow.batch.advance"
          >
            {busy === 'advance' ? 'Advancing…' : 'Advance (start newly eligible tickets)'}
          </button>
          <p style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
            Advancing starts only tickets whose upstreams have applied. Approve, review, and apply each halted ticket from
            its work item — the batch changes when runs start, never who decides.
          </p>
        </div>
      ) : null}
    </div>
  );
}
