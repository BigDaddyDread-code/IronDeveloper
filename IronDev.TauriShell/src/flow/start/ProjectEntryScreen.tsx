import { useCallback, useEffect, useRef, useState } from 'react';
import type { ProjectSummary } from '../../api/types';
import { IronDevBrand } from '../../components/IronDevBrand';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { ConnectProjectScreen } from './ConnectProjectScreen';
import { ProjectTile } from './ProjectTile';
import type { ProjectTileReadiness } from './projectEntryTypes';
import { isReadyReadiness } from './projectEntryTypes';

interface ProjectEntryScreenProps {
  onOpenBoard: (projectId: number) => void;
  onOpenProvisioning: (projectId: number) => void;
  onOpenSettings: () => void;
  initialScreen?: 'grid' | 'connect';
  onOpenConnect?: () => void;
  onBackFromConnect?: () => void;
}

function initials(name: string | null | undefined): string {
  if (!name) {
    return '.';
  }

  return name
    .split(/\s+/)
    .map((part) => part[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

function describeError(error: unknown): string {
  return error instanceof Error ? error.message : 'status unavailable';
}

export function ProjectEntryScreen({
  onOpenBoard,
  onOpenProvisioning,
  onOpenSettings,
  initialScreen = 'grid',
  onOpenConnect,
  onBackFromConnect
}: ProjectEntryScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const connectTileRef = useRef<HTMLButtonElement | null>(null);
  const readinessRequests = useRef<Record<number, Promise<ProjectTileReadiness>>>({});
  const [screen, setScreen] = useState<'grid' | 'connect'>(initialScreen);
  const [readinessById, setReadinessById] = useState<Record<number, ProjectTileReadiness>>({});
  const [openingProjectId, setOpeningProjectId] = useState<number | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);

  const loadReadiness = useCallback(
    (projectId: number): Promise<ProjectTileReadiness> => {
      const pending = readinessRequests.current[projectId];
      if (pending) {
        return pending;
      }

      const request = session.client
        .getProvisioningReadiness(projectId)
        .then((readiness): ProjectTileReadiness => ({ kind: 'loaded', readiness }))
        .catch((error: unknown): ProjectTileReadiness => ({ kind: 'error', message: describeError(error) }))
        .then((result) => {
          setReadinessById((previous) => ({ ...previous, [projectId]: result }));
          delete readinessRequests.current[projectId];
          return result;
        });

      readinessRequests.current[projectId] = request;
      return request;
    },
    [session.client]
  );

  useEffect(() => {
    setScreen(initialScreen);
  }, [initialScreen]);

  useEffect(() => {
    const projectIds = project.projects
      .map((candidate) => candidate.id)
      .filter((projectId): projectId is number => typeof projectId === 'number' && Number.isFinite(projectId));

    setReadinessById((previous) => {
      const next: Record<number, ProjectTileReadiness> = {};
      for (const projectId of projectIds) {
        next[projectId] = previous[projectId] ?? { kind: 'loading' };
      }
      return next;
    });

    for (const projectId of projectIds) {
      if (!readinessRequests.current[projectId]) {
        void loadReadiness(projectId);
      }
    }
  }, [loadReadiness, project.projects]);

  const openProject = async (candidate: ProjectSummary) => {
    const projectId = candidate.id;
    if (projectId === undefined || projectId === null || !Number.isFinite(projectId)) {
      setPageError(`${candidate.name ?? 'Project'} could not be opened. Retry or check the connection.`);
      return;
    }

    setOpeningProjectId(projectId);
    setPageError(null);

    try {
      const currentReadiness = readinessById[projectId];
      const readiness =
        currentReadiness?.kind === 'loaded' || currentReadiness?.kind === 'error'
          ? currentReadiness
          : await loadReadiness(projectId);

      await project.selectProjectContext(projectId);

      if (isReadyReadiness(readiness)) {
        onOpenBoard(projectId);
      } else {
        onOpenProvisioning(projectId);
      }
    } catch {
      setPageError(`${candidate.name ?? 'Project'} could not be opened. Retry or check the connection.`);
    } finally {
      setOpeningProjectId(null);
    }
  };

  const showConnect = () => {
    setPageError(null);
    setScreen('connect');
    onOpenConnect?.();
  };

  const showGrid = () => {
    setScreen('grid');
    onBackFromConnect?.();
    window.setTimeout(() => connectTileRef.current?.focus(), 0);
  };

  const displayName = project.userProfile?.displayName ?? 'Signed in';
  const workbench = session.environmentInfo?.workbench;
  const healthLabel = session.apiStatus.status === 'connected'
    ? `API connected${workbench ? ` / ${workbench.mode} ${workbench.version} / ${workbench.previewId}` : ''}`
    : `API ${session.apiStatus.status}`;

  return (
    <main className="fl-root fl-project-entry" data-testid="flow.chooser">
      <header className="fl-project-entry__header">
        <IronDevBrand />
        <div className="fl-userbit">
          <details className="fl-project-health">
            <summary data-testid="flow.projectEntry.health">{healthLabel}</summary>
            <dl>
              <div>
                <dt>Workbench</dt>
                <dd data-testid="flow.projectEntry.workbenchIdentity">
                  {workbench ? `${workbench.mode} ${workbench.version} / ${workbench.previewId}` : 'Unknown'}
                </dd>
              </div>
              <div>
                <dt>Commit</dt>
                <dd>{workbench?.apiCommit ?? 'Unknown'}</dd>
              </div>
              <div>
                <dt>Environment</dt>
                <dd>{session.environmentInfo?.environment ?? 'Unknown'}</dd>
              </div>
              <div>
                <dt>API</dt>
                <dd>{session.config.apiBaseUrl}</dd>
              </div>
            </dl>
            <button className="fl-btn fl-mini" type="button" onClick={onOpenSettings}>
              Connection settings
            </button>
          </details>
          <span>{displayName}</span>
          <span className="fl-avatar">{initials(project.userProfile?.displayName)}</span>
        </div>
      </header>

      <section className="fl-project-entry__body">
        {screen === 'connect' ? (
          <ConnectProjectScreen onBack={showGrid} onProjectCreated={onOpenProvisioning} />
        ) : (
          <>
            <div className="fl-auth-intro">
              <p className="fl-plabel">Project</p>
              <h1 className="fl-h1">Choose a project</h1>
              <p className="fl-sub">
                {project.projects.length === 0
                  ? 'No projects are connected yet.'
                  : 'Open an existing project or connect a local repository.'}
              </p>
            </div>

            {pageError ? (
              <div className="fl-error" data-testid="flow.projectEntry.error" role="alert">
                {pageError}
              </div>
            ) : null}

            {project.isRefreshing && project.projects.length === 0 ? (
              <div className="fl-project-grid" data-testid="flow.projectEntry.skeletons" aria-label="Loading projects">
                <div className="fl-project-tile fl-project-tile--skeleton" />
                <div className="fl-project-tile fl-project-tile--skeleton" />
                <div className="fl-project-tile fl-project-tile--skeleton" />
              </div>
            ) : (
              <div className="fl-project-grid" data-testid="flow.chooser.list">
                {project.projects.map((candidate) => {
                  const projectId = candidate.id ?? -1;
                  return (
                    <ProjectTile
                      key={projectId}
                      project={candidate}
                      readiness={readinessById[projectId]}
                      isOpening={openingProjectId === projectId}
                      onOpen={() => void openProject(candidate)}
                    />
                  );
                })}
                <button
                  ref={connectTileRef}
                  type="button"
                  className="fl-project-tile fl-project-tile--connect"
                  data-testid="flow.projectEntry.connect"
                  aria-label="Connect another project. Add a local repository"
                  onClick={showConnect}
                >
                  <span className="fl-project-tile__plus" aria-hidden="true">
                    +
                  </span>
                  <span className="fl-project-tile__name">Connect another project</span>
                  <span className="fl-project-tile__path">Add a local repository</span>
                </button>
              </div>
            )}
          </>
        )}
      </section>
    </main>
  );
}
