import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface PreflightGateProps {
  onOpenSettings: () => void;
}

export function PreflightGate({ onOpenSettings }: PreflightGateProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const offline = project.accessStatus === 'apiOffline';

  return (
    <main className="fl-root" data-testid="flow.preflight">
      <div style={{ maxWidth: 560, margin: '12vh auto', padding: 24 }}>
        <h1 className="fl-h1">Cannot reach the IronDev API</h1>
        <p className="fl-sub" data-testid="flow.preflight.detail">
          {offline
            ? `No response from ${session.config.apiBaseUrl}. The backend is probably not running.`
            : `The API at ${session.config.apiBaseUrl} answered with an error.`}
        </p>
        <div style={{ margin: '16px 0', padding: 12, background: 'var(--fl-panel, #f6f6f4)', borderRadius: 8 }}>
          <p style={{ margin: 0, fontWeight: 600 }}>Next safe action</p>
          <p style={{ margin: '6px 0 0' }}>
            Start the LocalTest API, then retry:
            <code style={{ display: 'block', marginTop: 6 }}>.\tools\localtest\start-localtest.ps1</code>
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            className="fl-btn fl-pri"
            data-testid="flow.preflight.retry"
            disabled={project.isRefreshing}
            onClick={() => void project.refreshProjectContext()}
          >
            {project.isRefreshing ? 'Checking...' : 'Retry'}
          </button>
          <button className="fl-btn" data-testid="flow.preflight.settings" onClick={onOpenSettings}>
            Connection settings
          </button>
        </div>
        <p className="fl-sub" style={{ marginTop: 16 }}>
          Preflight is read-only reporting. It fixes nothing itself, and a reachable API grants nothing.
        </p>
      </div>
    </main>
  );
}
