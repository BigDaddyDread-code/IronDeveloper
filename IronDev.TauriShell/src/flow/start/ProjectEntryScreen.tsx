import { useEffect, useRef, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { ProjectSummary } from '../../api/types';
import { IronDevBrand } from '../../components/IronDevBrand';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { ProjectTile } from './ProjectTile';
import { StartProjectScreen } from './StartProjectScreen';

interface ProjectEntryScreenProps {
  onOpenWorkbench: (projectId: number) => void;
  onOpenSettings: () => void;
  initialScreen?: 'grid' | 'new';
  onOpenNew?: () => void;
  onBackFromNew?: () => void;
}

function initials(name: string | null | undefined): string {
  if (!name) return '.';
  return name
    .split(/\s+/)
    .map((part) => part[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

export function ProjectEntryScreen({
  onOpenWorkbench,
  onOpenSettings,
  initialScreen = 'grid',
  onOpenNew,
  onBackFromNew
}: ProjectEntryScreenProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const startTileRef = useRef<HTMLButtonElement | null>(null);
  const [screen, setScreen] = useState<'grid' | 'new'>(initialScreen);
  const [openingProjectId, setOpeningProjectId] = useState<number | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);
  const [takeoverProject, setTakeoverProject] = useState<ProjectSummary | null>(null);

  useEffect(() => setScreen(initialScreen), [initialScreen]);

  const openProject = async (candidate: ProjectSummary, takeOver = false) => {
    const projectId = candidate.id;
    if (projectId === undefined || projectId === null || !Number.isFinite(projectId)) {
      setPageError(`${candidate.name ?? 'Project'} could not be opened. Retry or check the connection.`);
      return;
    }

    setOpeningProjectId(projectId);
    setPageError(null);
    setTakeoverProject(null);
    try {
      await project.selectProjectContext(projectId, takeOver);
      onOpenWorkbench(projectId);
    } catch (error) {
      if (isTakeoverRequired(error)) {
        setTakeoverProject(candidate);
        setPageError(`${candidate.name ?? 'Project'} is open in another writable session. Confirm takeover to continue.`);
      } else {
        setPageError(`${candidate.name ?? 'Project'} could not be opened. Retry or check the connection.`);
      }
    } finally {
      setOpeningProjectId(null);
    }
  };

  const showNew = () => {
    setPageError(null);
    setScreen('new');
    onOpenNew?.();
  };

  const showGrid = () => {
    setScreen('grid');
    onBackFromNew?.();
    window.setTimeout(() => startTileRef.current?.focus(), 0);
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
              <div><dt>Commit</dt><dd>{workbench?.apiCommit ?? 'Unknown'}</dd></div>
              <div><dt>Environment</dt><dd>{session.environmentInfo?.environment ?? 'Unknown'}</dd></div>
              <div><dt>API</dt><dd>{session.config.apiBaseUrl}</dd></div>
            </dl>
            <button className="fl-btn fl-mini" type="button" onClick={onOpenSettings}>Connection settings</button>
          </details>
          <span>{displayName}</span>
          <span className="fl-avatar">{initials(project.userProfile?.displayName)}</span>
        </div>
      </header>

      <section className="fl-project-entry__body">
        {screen === 'new' ? (
          <StartProjectScreen onBack={showGrid} onProjectStarted={onOpenWorkbench} />
        ) : (
          <>
            <div className="fl-auth-intro">
              <p className="fl-plabel">Project</p>
              <h1 className="fl-h1">Choose a project</h1>
              <p className="fl-sub">
                {project.projects.length === 0
                  ? 'Start a project, then shape the idea in Workbench.'
                  : 'Open an existing project or start a new one.'}
              </p>
            </div>

            {pageError ? (
              <div className="fl-error" data-testid="flow.projectEntry.error" role="alert">
                {pageError}
                {takeoverProject ? (
                  <button
                    className="fl-btn fl-mini"
                    data-testid="flow.projectEntry.takeover"
                    type="button"
                    onClick={() => void openProject(takeoverProject, true)}
                  >
                    Take over writable session
                  </button>
                ) : null}
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
                      isOpening={openingProjectId === projectId}
                      onOpen={() => void openProject(candidate)}
                    />
                  );
                })}
                <button
                  ref={startTileRef}
                  type="button"
                  className="fl-project-tile fl-project-tile--connect"
                  data-testid="flow.projectEntry.start"
                  aria-label="Start a new project"
                  onClick={showNew}
                >
                  <span className="fl-project-tile__plus" aria-hidden="true">+</span>
                  <span className="fl-project-tile__name">Start new project</span>
                  <span className="fl-project-tile__path">Shape an idea first</span>
                </button>
              </div>
            )}
          </>
        )}
      </section>
    </main>
  );
}

function isTakeoverRequired(error: unknown) {
  if (!(error instanceof IronDevApiError) || !error.body || typeof error.body !== 'object') return false;
  return (error.body as { error?: unknown }).error === 'workbench_lease_takeover_required';
}
