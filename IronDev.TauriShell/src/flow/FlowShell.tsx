import { useEffect, useRef, useState } from 'react';
import './flow.css';
import { IronDevApiError } from '../api/ironDevApi';
import type { ProjectTicket } from '../api/types';
import { routeForId } from '../app/routes';
import { IronDevBrand } from '../components/IronDevBrand';
import { SignInRoute } from '../features/auth/SignInRoute';
import { ChatRoute } from '../features/chatToBuild/ChatRoute';
import { SharedChannelRoute } from '../features/chatToBuild/SharedChannelRoute';
import { useProjectContext } from '../state/useProjectContext';
import { useSessionContext } from '../state/useSessionContext';
import { BoardScreen } from './board/BoardScreen';
import { RouteOutcomeScreen, type RouteOutcomeKind } from './components/RouteOutcomeScreen';
import { LegacyRouteNotice } from './components/LegacyRouteNotice';
import { ProjectNotificationsMenu } from './components/ProjectNotificationsMenu';
import { LibraryScreen } from './library/LibraryScreen';
import {
  chatChannelPath,
  chatSessionPath,
  libraryPath,
  legacyCanonicalPath,
  navigateProductPath,
  projectPath,
  settingsPath,
  useProductRoute,
  workItemPath,
  type ProductRouteKind
} from './navigation/productRoutes';
import { ProjectSetupScreen } from './projects/ProjectSetupScreen';
import { SettingsScreen } from './settings/SettingsScreen';
import { PreflightGate, ProjectChooser } from './start/StartGate';
import { TenantChooser } from './start/TenantChooser';
import { WorkItemScreen } from './workitem/WorkItemScreen';

type RouteProjectState = 'idle' | 'switching' | 'missing' | 'unavailable';
type WorkItemLoadState = 'idle' | 'loading' | 'ready' | 'notFound' | 'permission' | 'unavailable';

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

function routeOutcomeForError(error: unknown): WorkItemLoadState {
  if (error instanceof IronDevApiError) {
    if (error.status === 403) return 'permission';
    if (error.status === 404) return 'notFound';
  }
  return 'unavailable';
}

function GlobalOutcome(props: React.ComponentProps<typeof RouteOutcomeScreen>) {
  return (
    <main className="fl-root fl-outcome-root">
      <RouteOutcomeScreen {...props} />
    </main>
  );
}

export function FlowShell() {
  const session = useSessionContext();
  const project = useProjectContext();
  const currentRoute = useProductRoute();
  const [activeTicket, setActiveTicket] = useState<ProjectTicket | null>(null);
  const [routeProjectState, setRouteProjectState] = useState<RouteProjectState>('idle');
  const [workItemLoadState, setWorkItemLoadState] = useState<WorkItemLoadState>('idle');
  const [legacyRouteTransition, setLegacyRouteTransition] = useState<{
    sourcePath: string;
    canonicalPath: string;
  } | null>(null);
  const projectSelectionRequest = useRef<number | null>(null);
  const mainContentRef = useRef<HTMLElement | null>(null);
  const previousPathname = useRef(currentRoute.pathname);
  const previousProjectId = useRef<number | null>(project.selectedProjectId);
  const healthMenuRef = useRef<HTMLDetailsElement | null>(null);
  const userMenuRef = useRef<HTMLDetailsElement | null>(null);

  useEffect(() => {
    document.title = project.selectedProjectName ? `IronDev — ${project.selectedProjectName}` : 'IronDev';
    return () => { document.title = 'IronDev'; };
  }, [project.selectedProjectName]);

  const selectedProjectId = project.selectedProjectId;
  const hasProjectAccess =
    selectedProjectId !== null &&
    ['loadingTickets', 'ready', 'emptyTickets', 'error'].includes(project.accessStatus);

  useEffect(() => {
    if (previousProjectId.current !== project.selectedProjectId) {
      setActiveTicket(null);
      previousProjectId.current = project.selectedProjectId;
    }
  }, [project.selectedProjectId]);

  useEffect(() => {
    if (previousPathname.current !== currentRoute.pathname) {
      previousPathname.current = currentRoute.pathname;
      mainContentRef.current?.focus();
    }
  }, [currentRoute.pathname]);

  // Canonical URLs follow resolved entry state. Legacy workspace URLs are kept
  // as redirects, while governance evidence links retain their original paths.
  useEffect(() => {
    if (currentRoute.kind === 'root') {
      if (project.accessStatus === 'authRequired' || project.accessStatus === 'authInvalid') {
        navigateProductPath('/sign-in', true);
      } else if (project.accessStatus === 'tenantRequired') {
        navigateProductPath('/tenants/select', true);
      } else if (project.accessStatus === 'projectRequired') {
        navigateProductPath('/projects', true);
      } else if (hasProjectAccess && selectedProjectId !== null) {
        navigateProductPath(projectPath(selectedProjectId, 'board'), true);
      }
      return;
    }

    if (currentRoute.kind === 'signIn') {
      if (project.accessStatus === 'tenantRequired') {
        navigateProductPath('/tenants/select', true);
        return;
      }
      if (hasProjectAccess && selectedProjectId !== null) {
        navigateProductPath(projectPath(selectedProjectId, 'board'), true);
        return;
      }
      if (project.accessStatus === 'projectRequired') {
        navigateProductPath('/projects', true);
        return;
      }
    }

    if (currentRoute.kind === 'tenantSelect' && project.accessStatus !== 'tenantRequired') {
      navigateProductPath(
        hasProjectAccess && selectedProjectId !== null ? projectPath(selectedProjectId, 'board') : '/projects',
        true
      );
      return;
    }

    if (!currentRoute.compatibility || selectedProjectId === null || !hasProjectAccess) return;
    const canonicalPath = legacyCanonicalPath(currentRoute, selectedProjectId);
    if (canonicalPath === null) return;
    setLegacyRouteTransition((previous) =>
      previous?.sourcePath === currentRoute.pathname
        ? previous
        : { sourcePath: currentRoute.pathname, canonicalPath }
    );
    navigateProductPath(canonicalPath, true);
  }, [currentRoute, hasProjectAccess, project.accessStatus, selectedProjectId]);

  // A project-scoped deep link selects that project through the existing API.
  // Missing IDs become an honest route outcome rather than silently opening a
  // different project's Board.
  useEffect(() => {
    const routeProjectId = currentRoute.projectId;
    if (routeProjectId === null) {
      projectSelectionRequest.current = null;
      setRouteProjectState('idle');
      return;
    }
    if (project.selectedProjectId === routeProjectId) {
      projectSelectionRequest.current = null;
      setRouteProjectState('idle');
      return;
    }
    if (
      project.accessStatus === 'loading' ||
      project.accessStatus === 'authRequired' ||
      project.accessStatus === 'authInvalid' ||
      project.accessStatus === 'tenantRequired' ||
      project.accessStatus === 'apiOffline' ||
      project.accessStatus === 'apiError' ||
      project.isRefreshing
    ) {
      setRouteProjectState('switching');
      return;
    }

    const exists = project.projects.some((candidate) => candidate.id === routeProjectId);
    if (!exists) {
      setRouteProjectState('missing');
      return;
    }
    if (projectSelectionRequest.current === routeProjectId) return;

    projectSelectionRequest.current = routeProjectId;
    setRouteProjectState('switching');
    void project.selectProjectContext(routeProjectId).catch(() => {
      projectSelectionRequest.current = null;
      setRouteProjectState('unavailable');
    });
  }, [
    currentRoute.projectId,
    project.accessStatus,
    project.isRefreshing,
    project.projects,
    project.selectProjectContext,
    project.selectedProjectId
  ]);

  // Work Item URLs hydrate their ticket from backend truth. Board navigation can
  // pass the ticket it already loaded, avoiding a duplicate request.
  useEffect(() => {
    if (currentRoute.kind !== 'workItem') {
      setWorkItemLoadState('idle');
      return;
    }
    if (currentRoute.workItemId === 'new') {
      setActiveTicket(null);
      setWorkItemLoadState('ready');
      return;
    }
    if (
      currentRoute.projectId === null ||
      typeof currentRoute.workItemId !== 'number' ||
      project.selectedProjectId !== currentRoute.projectId
    ) {
      setWorkItemLoadState('loading');
      return;
    }
    if (
      activeTicket?.id === currentRoute.workItemId &&
      (activeTicket.projectId === undefined || activeTicket.projectId === currentRoute.projectId)
    ) {
      setWorkItemLoadState('ready');
      return;
    }

    const controller = new AbortController();
    setWorkItemLoadState('loading');
    session.client
      .getProjectTicket(currentRoute.projectId, currentRoute.workItemId, controller.signal)
      .then((ticket) => {
        setActiveTicket(ticket);
        setWorkItemLoadState('ready');
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setWorkItemLoadState(routeOutcomeForError(error));
      });
    return () => controller.abort();
  }, [
    activeTicket,
    currentRoute.kind,
    currentRoute.projectId,
    currentRoute.workItemId,
    project.selectedProjectId,
    session.client
  ]);

  const openSettings = () => {
    healthMenuRef.current?.removeAttribute('open');
    userMenuRef.current?.removeAttribute('open');
    if (hasProjectAccess && project.selectedProjectId !== null) {
      navigateProductPath(libraryPath(project.selectedProjectId, 'settings'));
    } else {
      navigateProductPath('/settings');
    }
  };

  const openProjectEntry = () => {
    userMenuRef.current?.removeAttribute('open');
    setActiveTicket(null);
    navigateProductPath('/projects');
  };

  const openProjectBoard = (projectId = project.selectedProjectId) => {
    if (projectId === null) return;
    navigateProductPath(projectPath(projectId, 'board'));
  };

  const openProjectSetup = (projectId = project.selectedProjectId) => {
    if (projectId === null) return;
    navigateProductPath(projectPath(projectId, 'setup'));
  };

  if (currentRoute.kind === 'settings') {
    return (
      <div className="fl-root">
        <main className="fl-main">
          {legacyRouteTransition ? <LegacyRouteNotice {...legacyRouteTransition} /> : null}
          <button className="fl-btn" type="button" onClick={() => navigateProductPath('/sign-in')}>
            Back to sign in
          </button>
          <SettingsScreen />
        </main>
      </div>
    );
  }

  if (project.accessStatus === 'apiOffline' || project.accessStatus === 'apiError') {
    return <PreflightGate onOpenSettings={openSettings} />;
  }
  if (project.accessStatus === 'authRequired' || project.accessStatus === 'authInvalid') {
    return <SignInRoute onOpenSettings={openSettings} />;
  }
  if (project.accessStatus === 'tenantRequired') {
    return <TenantChooser onOpenSettings={openSettings} />;
  }

  if (currentRoute.kind === 'notFound') {
    return (
      <GlobalOutcome
        kind="notFound"
        title="This route does not exist"
        message={`${currentRoute.pathname} is not an IronDev product route.`}
        nextSafeAction="Return to the project list and enter through a visible product route."
        actionLabel="Open projects"
        onAction={openProjectEntry}
      />
    );
  }

  if (routeProjectState === 'missing') {
    return (
      <GlobalOutcome
        kind="notFound"
        title="Project not found"
        message={`Project ${currentRoute.projectId} is not available to this account.`}
        nextSafeAction="Choose an accessible project. No project context was changed."
        actionLabel="Open projects"
        onAction={openProjectEntry}
      />
    );
  }
  if (routeProjectState === 'unavailable') {
    return (
      <GlobalOutcome
        kind="unavailable"
        title="Project could not be selected"
        message="The API did not complete the project-context change."
        nextSafeAction="Return to the project list and retry the selection."
        actionLabel="Open projects"
        onAction={openProjectEntry}
      />
    );
  }
  if (routeProjectState === 'switching') {
    return <main className="fl-root fl-route-loading" data-testid="flow.routeLoading">Loading project context...</main>;
  }

  // Do not render a clickable Board under the transient root URL. Waiting for
  // the canonical replacement prevents a stale root effect from overwriting a
  // Work Item navigation performed in the same frame.
  if (
    currentRoute.kind === 'root' ||
    currentRoute.kind === 'signIn' ||
    currentRoute.kind === 'tenantSelect'
  ) {
    return <main className="fl-root fl-route-loading" data-testid="flow.routeLoading">Opening project...</main>;
  }

  if (currentRoute.kind === 'projects' || currentRoute.kind === 'projectConnect') {
    return (
      <ProjectChooser
        initialScreen={currentRoute.kind === 'projectConnect' ? 'connect' : 'grid'}
        onOpenConnect={() => navigateProductPath('/projects/connect')}
        onBackFromConnect={() => navigateProductPath('/projects')}
        onOpenSettings={openSettings}
        onOpenBoard={openProjectBoard}
        onOpenProvisioning={openProjectSetup}
      />
    );
  }

  if (
    project.accessStatus === 'projectRequired' ||
    (project.accessStatus !== 'loading' && project.selectedProjectId === null)
  ) {
    return (
      <ProjectChooser
        onOpenConnect={() => navigateProductPath('/projects/connect')}
        onBackFromConnect={() => navigateProductPath('/projects')}
        onOpenSettings={openSettings}
        onOpenBoard={openProjectBoard}
        onOpenProvisioning={openProjectSetup}
      />
    );
  }

  if (currentRoute.kind === 'projectSetup' && project.selectedProjectId !== null) {
    return (
      <ProjectSetupScreen
        projectId={project.selectedProjectId}
        entryMode
        onBackToProjects={openProjectEntry}
        onOpenBoard={() => openProjectBoard()}
      />
    );
  }

  const activeProjectId = project.selectedProjectId;
  if (activeProjectId === null) return null;

  const displayedKind: ProductRouteKind = currentRoute.kind;
  const projectName = project.selectedProjectName ?? `Project ${activeProjectId}`;
  const currentTenant = project.tenants.find((tenant) => tenant.id === project.selectedTenantId);
  const currentWorkItemId = activeTicket?.id;
  const workItemAvailable = displayedKind === 'workItem' || typeof currentWorkItemId === 'number';
  const modelMode = session.environmentInfo?.isTestEnvironment
    ? 'Model mode: Deterministic LocalTest; not a live model run'
    : 'Model mode: Backend-reported per run; deterministic fallback is never silent';
  const workbench = session.environmentInfo?.workbench;

  const openWorkItem = (ticket: ProjectTicket | null) => {
    setActiveTicket(ticket);
    navigateProductPath(workItemPath(activeProjectId, ticket?.id ?? 'new'));
  };

  const openBoardWorkItem = (workItemId: number | null) => {
    setActiveTicket(null);
    navigateProductPath(workItemPath(activeProjectId, workItemId ?? 'new'));
  };

  const renderWorkItemOutcome = (kind: RouteOutcomeKind, title: string, message: string) => (
    <RouteOutcomeScreen
      kind={kind}
      title={title}
      message={message}
      nextSafeAction="Return to the Board and select an accessible work item."
      actionLabel="Back to Board"
      onAction={() => openProjectBoard()}
    />
  );

  return (
    <div className="fl-root" data-testid="flow.shell">
      <a className="fl-skip-link" href="#flow-main">Skip to main content</a>
      <header className="fl-appbar">
        <div className="fl-brand-with-project">
          <IronDevBrand /> <span className="fl-brand-separator">/</span>
          <button className="fl-project-switcher" data-testid="flow.projectSwitcher" type="button" onClick={openProjectEntry}>
            {projectName}
          </button>
        </div>

        <nav className="fl-nav fl-product-nav" aria-label="Project">
          <button
            className={displayedKind === 'board' ? 'fl-on' : ''}
            aria-current={displayedKind === 'board' ? 'page' : undefined}
            type="button"
            onClick={() => openProjectBoard()}
            data-testid="flow.nav.board"
          >
            Board
          </button>
          <button
            className={displayedKind === 'chat' ? 'fl-on' : ''}
            aria-current={displayedKind === 'chat' ? 'page' : undefined}
            type="button"
            onClick={() => navigateProductPath(projectPath(activeProjectId, 'chat'))}
            data-testid="flow.nav.workshop"
          >
            Workshop
          </button>
          <button
            className={displayedKind === 'workItem' ? 'fl-on' : ''}
            aria-current={displayedKind === 'workItem' ? 'page' : undefined}
            type="button"
            disabled={!workItemAvailable}
            title={workItemAvailable ? 'Open the current work item' : 'Select a work item from the Board first'}
            onClick={() => {
              if (typeof currentWorkItemId === 'number') {
                navigateProductPath(workItemPath(activeProjectId, currentWorkItemId));
              }
            }}
            data-testid="flow.nav.workitem"
          >
            Work Item
          </button>
          <button
            className={displayedKind === 'library' ? 'fl-on' : ''}
            aria-current={displayedKind === 'library' ? 'page' : undefined}
            type="button"
            onClick={() => navigateProductPath(projectPath(activeProjectId, 'library'))}
            data-testid="flow.nav.library"
          >
            Library
          </button>
        </nav>

        <div className="fl-userbit">
          <ProjectNotificationsMenu
            projectId={activeProjectId}
            onOpenChannel={(slug) => navigateProductPath(chatChannelPath(activeProjectId, slug))}
          />
          <details ref={healthMenuRef} className="fl-header-menu fl-project-health">
            <summary data-testid="flow.health">
              Health{workbench ? ` / ${workbench.mode} ${workbench.version} / ${workbench.previewId}` : ''}
            </summary>
            <dl>
              <div><dt>Status</dt><dd>{session.apiStatus.status}</dd></div>
              <div><dt>Workbench</dt><dd data-testid="flow.workbenchIdentity">{workbench ? `${workbench.mode} ${workbench.version}` : 'Unknown'}</dd></div>
              <div><dt>Preview</dt><dd>{workbench?.previewId ?? 'Unknown'}</dd></div>
              <div><dt>Commit</dt><dd>{workbench?.apiCommit ?? 'Unknown'}</dd></div>
              <div><dt>Environment</dt><dd>{session.environmentInfo?.environment ?? 'Unknown'}</dd></div>
              <div><dt>API</dt><dd>{session.config.apiBaseUrl}</dd></div>
              <div><dt>Execution</dt><dd data-testid="flow.modelMode">{modelMode}</dd></div>
            </dl>
          </details>
          <details ref={userMenuRef} className="fl-header-menu fl-user-menu">
            <summary data-testid="flow.userMenu">
              <span>{project.userProfile?.displayName ?? 'Account'}</span>
              <span className="fl-avatar">{initials(project.userProfile?.displayName)}</span>
            </summary>
            <div className="fl-header-popover">
              <strong>{project.userProfile?.displayName ?? 'Signed in'}</strong>
              <span>{project.userProfile?.email}</span>
              <span>{currentTenant?.name ?? 'No tenant selected'}</span>
              <button type="button" onClick={openProjectEntry}>Switch project</button>
              <button type="button" data-testid="flow.nav.settings" onClick={openSettings}>Settings</button>
              <button
                type="button"
                data-testid="flow.signOut"
                onClick={() => void session.signOut().then(() => navigateProductPath('/sign-in', true))}
              >
                Sign out
              </button>
            </div>
          </details>
        </div>
      </header>

      <main id="flow-main" className="fl-main" data-testid="flow.main" ref={mainContentRef} tabIndex={-1}>
        {legacyRouteTransition ? <LegacyRouteNotice {...legacyRouteTransition} /> : null}
        {displayedKind === 'board' ? (
          <BoardScreen
            onOpenWorkItem={openBoardWorkItem}
            onOpenProvisioning={() => openProjectSetup()}
            onConfigureRunAgents={() => navigateProductPath(settingsPath(activeProjectId, 'agents'))}
          />
        ) : null}
        {displayedKind === 'chat' && currentRoute.chatChannelId ? (
          <SharedChannelRoute
            projectId={activeProjectId}
            channelReference={currentRoute.chatChannelId}
            onOpenChannel={(slug) => navigateProductPath(chatChannelPath(activeProjectId, slug))}
            onOpenSession={(sessionId) => navigateProductPath(chatSessionPath(activeProjectId, sessionId))}
            onOpenDirect={() => navigateProductPath(projectPath(activeProjectId, 'chat'))}
          />
        ) : null}
        {displayedKind === 'chat' && !currentRoute.chatChannelId ? (
          <ChatRoute
            route={routeForId('chat')}
            requestedSessionId={currentRoute.chatSessionId}
            onOpenSession={(sessionId) => navigateProductPath(chatSessionPath(activeProjectId, sessionId))}
            onOpenChannel={(slug) => navigateProductPath(chatChannelPath(activeProjectId, slug))}
            onOpenLanding={() => navigateProductPath(projectPath(activeProjectId, 'chat'))}
            onOpenWorkItem={openWorkItem}
          />
        ) : null}
        {displayedKind === 'workItem' && workItemLoadState === 'loading' ? (
          <p className="fl-empty" data-testid="flow.workItem.loading">Loading work item from the API...</p>
        ) : null}
        {displayedKind === 'workItem' && workItemLoadState === 'notFound'
          ? renderWorkItemOutcome('notFound', 'Work item not found', 'The requested work item no longer exists or is outside this project.')
          : null}
        {displayedKind === 'workItem' && workItemLoadState === 'permission'
          ? renderWorkItemOutcome('permission', 'Work item access denied', 'The backend refused access to this work item.')
          : null}
        {displayedKind === 'workItem' && workItemLoadState === 'unavailable'
          ? renderWorkItemOutcome('unavailable', 'Work item unavailable', 'The work item could not be loaded from the API.')
          : null}
        {displayedKind === 'workItem' && (workItemLoadState === 'ready' || workItemLoadState === 'idle') ? (
          <WorkItemScreen
            ticket={activeTicket}
            onTicketCreated={(ticket) => {
              setActiveTicket(ticket);
              if (typeof ticket.id === 'number') navigateProductPath(workItemPath(activeProjectId, ticket.id), true);
            }}
            onBackToBoard={() => openProjectBoard()}
            onOpenGovernanceLibrary={() => navigateProductPath(libraryPath(activeProjectId, 'governance'))}
            onConfigureRunAgents={() => navigateProductPath(settingsPath(activeProjectId, 'agents'))}
            onConfigureProjectWorkConnection={() => navigateProductPath(settingsPath(activeProjectId, 'aiConnections'))}
            onDiscussInChat={(sessionId) => navigateProductPath(
              sessionId ? chatSessionPath(activeProjectId, sessionId) : projectPath(activeProjectId, 'chat')
            )}
          />
        ) : null}
        {displayedKind === 'library' ? (
          <LibraryScreen
            projectId={activeProjectId}
            section={currentRoute.librarySection ?? 'settings'}
            governanceSection={currentRoute.governanceSection}
            settingsSection={currentRoute.settingsSection}
            documentId={currentRoute.libraryDocumentId}
            documentVersionId={currentRoute.libraryDocumentVersionId}
            documentAction={currentRoute.libraryDocumentAction}
            toolId={currentRoute.libraryToolId}
            auditLedgerId={currentRoute.libraryAuditLedgerId}
            preserveGovernancePath={currentRoute.compatibility && currentRoute.librarySection === 'governance'}
            onBackToProjects={openProjectEntry}
            onOpenBoard={() => openProjectBoard()}
          />
        ) : null}
      </main>
    </div>
  );
}
