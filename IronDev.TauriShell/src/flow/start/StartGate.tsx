import { useEffect, useState } from 'react';
import type { ProjectProvisioningReadinessUi, ProjectSummary } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

// UX-START-0 — the session front door. The project is the authority boundary:
// no project, no work item; no readiness, no run. Everything rendered here is
// backend truth or an honest spinner — the chooser never infers readiness, and
// selecting a project changes context, never authority.

interface PreflightGateProps {
  onOpenSettings: () => void;
}

/**
 * The API is unreachable or erroring. A mute error chip is a dead end; the
 * front door names the URL it tried, the likely cause, and the next safe action.
 */
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
            Start the local stack, then retry:
            <code style={{ display: 'block', marginTop: 6 }}>.\Scripts\demo\start-v0.1-demo.ps1</code>
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            className="fl-btn fl-pri"
            data-testid="flow.preflight.retry"
            disabled={project.isRefreshing}
            onClick={() => void project.refreshProjectContext()}
          >
            {project.isRefreshing ? 'Checking…' : 'Retry'}
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

interface ProjectChooserProps {
  onOpenSettings: () => void;
  onProjectCreated: () => void;
}

type ReadinessState =
  | { kind: 'loading' }
  | { kind: 'loaded'; readiness: ProjectProvisioningReadinessUi }
  | { kind: 'error'; message: string };

/**
 * Signed in, no project selected. The user must choose or create a governed
 * project before any work-item flow appears. Badges are the backend's own
 * provisioning readiness, fetched per project — truth or a spinner, never a guess.
 */
export function ProjectChooser({ onOpenSettings, onProjectCreated }: ProjectChooserProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const [readinessById, setReadinessById] = useState<Record<number, ReadinessState>>({});
  const [isCreating, setIsCreating] = useState(false);
  const [createOpen, setCreateOpen] = useState(project.projects.length === 0);
  const [name, setName] = useState('');
  const [localPath, setLocalPath] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    for (const candidate of project.projects) {
      const projectId = candidate.id;
      if (projectId === undefined || projectId === null) {
        continue;
      }
      setReadinessById((previous) => ({ ...previous, [projectId]: { kind: 'loading' } }));
      session.client
        .getProvisioningReadiness(projectId, controller.signal)
        .then((readiness) => {
          setReadinessById((previous) => ({ ...previous, [projectId]: { kind: 'loaded', readiness } }));
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) {
            return;
          }
          setReadinessById((previous) => ({
            ...previous,
            [projectId]: { kind: 'error', message: error instanceof Error ? error.message : 'readiness unavailable' }
          }));
        });
    }
    return () => controller.abort();
  }, [session.client, project.projects]);

  const createProject = async () => {
    if (name.trim().length === 0 || localPath.trim().length === 0) {
      setCreateError('A project needs a name and a local repository path.');
      return;
    }
    setIsCreating(true);
    setCreateError(null);
    try {
      const created = await session.client.createProject(name.trim(), localPath.trim());
      if (created.id === undefined || created.id === null) {
        setCreateError('The backend did not return a project id.');
        return;
      }
      project.selectProjectContext(created.id);
      // A created project is a shell, not readiness — land on the readiness
      // screen, never straight into governed work.
      onProjectCreated();
    } catch (error: unknown) {
      setCreateError(error instanceof Error ? error.message : 'The project could not be created.');
    } finally {
      setIsCreating(false);
    }
  };

  return (
    <main className="fl-root" data-testid="flow.chooser">
      <div style={{ maxWidth: 640, margin: '8vh auto', padding: 24 }}>
        <h1 className="fl-h1">
          {project.userProfile?.displayName ? `Welcome, ${project.userProfile.displayName}` : 'Welcome'}
        </h1>
        <p className="fl-sub">
          Choose a project. The project is the boundary — work items, readiness, receipts, and apply policy all live
          inside it. Selecting a project changes context, not authority.
        </p>

        {project.projects.length === 0 ? (
          <p className="fl-empty" data-testid="flow.chooser.empty">
            No projects yet. Create your first governed project.
          </p>
        ) : (
          <div style={{ display: 'grid', gap: 8, margin: '16px 0' }} data-testid="flow.chooser.list">
            {project.projects.map((candidate: ProjectSummary) => {
              const projectId = candidate.id ?? -1;
              const state = readinessById[projectId];
              return (
                <button
                  key={projectId}
                  className="fl-card"
                  style={{ textAlign: 'left' }}
                  data-testid={`flow.chooser.project.${projectId}`}
                  onClick={() => project.selectProjectContext(projectId)}
                >
                  <p className="fl-card-title" style={{ marginBottom: 4 }}>{candidate.name ?? `Project ${projectId}`}</p>
                  <span className="fl-sub" style={{ display: 'block' }}>{candidate.localPath ?? 'no repository path set'}</span>
                  {state === undefined || state.kind === 'loading' ? (
                    <span className="fl-tag" data-testid={`flow.chooser.readiness.${projectId}`}>checking readiness…</span>
                  ) : state.kind === 'loaded' ? (
                    <span
                      className={state.readiness.isReady ? 'fl-tag fl-green' : 'fl-tag fl-amber'}
                      data-testid={`flow.chooser.readiness.${projectId}`}
                    >
                      {state.readiness.isReady
                        ? 'Ready to run'
                        : `Setup incomplete · ${state.readiness.blockedStates.length} blocker(s)`}
                    </span>
                  ) : (
                    <span className="fl-tag" data-testid={`flow.chooser.readiness.${projectId}`}>
                      readiness unavailable: {state.message}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        )}

        {createOpen ? (
          <div style={{ marginTop: 16, padding: 16, background: 'var(--fl-panel, #f6f6f4)', borderRadius: 8 }}>
            <h2 style={{ marginTop: 0 }}>Create project</h2>
            <label style={{ display: 'block', marginBottom: 8 }}>
              Project name
              <input
                data-testid="flow.chooser.create.name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                style={{ display: 'block', width: '100%' }}
              />
            </label>
            <label style={{ display: 'block', marginBottom: 8 }}>
              Local repository path
              <input
                data-testid="flow.chooser.create.path"
                value={localPath}
                onChange={(event) => setLocalPath(event.target.value)}
                placeholder="C:\\path\\to\\repo"
                style={{ display: 'block', width: '100%' }}
              />
            </label>
            {createError ? <div className="fl-error" data-testid="flow.chooser.create.error">{createError}</div> : null}
            <button
              className="fl-btn fl-pri"
              data-testid="flow.chooser.create.submit"
              disabled={isCreating}
              onClick={() => void createProject()}
            >
              {isCreating ? 'Creating…' : 'Create project'}
            </button>
            <p className="fl-sub" style={{ marginTop: 8 }}>
              A project shell is not readiness. You will land on the readiness screen — a repo path is not safe until
              checked, and a detected command is not confirmed truth.
            </p>
          </div>
        ) : (
          <button className="fl-btn" data-testid="flow.chooser.create.open" onClick={() => setCreateOpen(true)}>
            + Create new project
          </button>
        )}

        <p className="fl-sub" style={{ marginTop: 24 }}>
          <button className="fl-btn" data-testid="flow.chooser.settings" onClick={onOpenSettings}>
            Connection settings
          </button>
        </p>
      </div>
    </main>
  );
}
