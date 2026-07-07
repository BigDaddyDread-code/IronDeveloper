import { useEffect, useState } from 'react';
import type { PlannedSurfaceProbe } from '../../api/ironDevApi';
import { useSessionContext } from '../../state/useSessionContext';

// AFFORDANCE-1: universal screen state 6 (full-ux-map §5.2), rendered from a REAL backend
// response. The panel probes its planned route and shows exactly what the backend said:
// a 501 refusal envelope (still planned), an unexpected success (the surface became real
// and this panel is stale — said loudly), or an error (backend truth unavailable — shown,
// never papered over). No mock mode exists; nothing here is invented client-side.

interface NotImplementedPanelProps {
  /** Human title of the surface, e.g. "Audit ledger". */
  title: string;
  method?: 'GET' | 'POST';
  /** The planned route to probe; null when a prerequisite is missing (e.g. no project selected). */
  path: string | null;
  /** Shown instead of probing when path is null. */
  missingPrerequisite?: string;
  testId: string;
}

export function NotImplementedPanel({ title, method = 'GET', path, missingPrerequisite, testId }: NotImplementedPanelProps) {
  const session = useSessionContext();
  const [probe, setProbe] = useState<PlannedSurfaceProbe | null>(null);

  useEffect(() => {
    setProbe(null);
    if (path === null) {
      return;
    }
    const controller = new AbortController();
    void session.client.probePlannedSurface(path, method, controller.signal).then((outcome) => {
      if (!controller.signal.aborted) {
        setProbe(outcome);
      }
    });
    return () => controller.abort();
  }, [session.client, path, method]);

  const source = path === null ? null : `${method} ${path}`;

  return (
    <div className="fl-panel-box" data-testid={testId}>
      <p className="fl-plabel">{title}</p>

      {path === null ? (
        <p className="fl-empty" data-testid={`${testId}.prerequisite`}>
          {missingPrerequisite ?? 'A prerequisite is missing before this surface can be probed.'}
        </p>
      ) : probe === null ? (
        <p className="fl-empty">Asking the backend…</p>
      ) : probe.kind === 'notImplemented' ? (
        <div data-testid={`${testId}.notImplemented`}>
          <div className="fl-qbox">
            <span>
              <strong style={{ fontSize: 12.5 }}>Not implemented</strong>
              <span style={{ display: 'block', fontSize: 12.5 }}>{probe.envelope.detail}</span>
            </span>
          </div>
          <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)', marginTop: 8 }}>
            Planned: <strong>{probe.envelope.plannedSlice}</strong>. No UI workaround exists.
          </p>
          <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }}>Next safe action: {probe.envelope.nextSafeAction}</p>
          <p style={{ fontSize: 11.5, color: 'var(--fl-muted)' }} data-testid={`${testId}.source`}>
            Source: {source} → 501 NotImplemented · {probe.envelope.boundary}
          </p>
        </div>
      ) : probe.kind === 'ready' ? (
        <div className="fl-error" data-testid={`${testId}.stale`}>
          This endpoint now returns truth ({source} succeeded) — this planned-surface panel is stale. The real screen
          should replace it; until then, nothing is rendered rather than rendering it wrong.
        </div>
      ) : (
        <div className="fl-error" data-testid={`${testId}.error`}>
          Backend truth unavailable for {source}
          {probe.status !== null ? ` (HTTP ${probe.status})` : ''}: {probe.message} Nothing is shown rather than
          inventing state.
        </div>
      )}
    </div>
  );
}
