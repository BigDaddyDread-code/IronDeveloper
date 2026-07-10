import { useState } from 'react';
import './flow.css';
import type { ProjectTicket } from '../api/types';
import { SignInRoute } from '../features/auth/SignInRoute';
import { useProjectContext } from '../state/useProjectContext';
import { useSessionContext } from '../state/useSessionContext';
import { BatchScreen } from './batch/BatchScreen';
import { BoardScreen } from './board/BoardScreen';
import { FlowSurface } from './flowTypes';
import { GovernanceHost } from './library/GovernanceHost';
import { isGovernancePath } from './library/governanceRoutes';
import { AdminInviteSection, AuditSection, ProvisioningSection } from './library/PlannedSections';
import { SolutionExplorer } from './library/SolutionExplorer';
import { SettingsScreen } from './settings/SettingsScreen';
import { PreflightGate, ProjectChooser } from './start/StartGate';
import { TenantChooser } from './start/TenantChooser';
import { WorkItemScreen } from './workitem/WorkItemScreen';

type LibrarySection = 'explorer' | 'governance' | 'provisioning' | 'audit' | 'admin';

function initials(name: string | null | undefined): string {
  if (!name) {
    return '·';
  }
  return name
    .split(/\s+/)
    .map((part) => part[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

export function FlowShell() {
  const session = useSessionContext();
  const project = useProjectContext();

  // Deep links into governance viewers (the old shell's URLs) land directly in the
  // Library's governance section, so bookmarks and existing tests keep working.
  const [surface, setSurface] = useState<FlowSurface>(() =>
    isGovernancePath(window.location.pathname) ? 'library' : 'board'
  );
  const [librarySection, setLibrarySection] = useState<LibrarySection>(() =>
    isGovernancePath(window.location.pathname) ? 'governance' : 'explorer'
  );
  const [activeTicket, setActiveTicket] = useState<ProjectTicket | null>(null);

  // UX-START-0 — the entry sequence. Order matters: an unreachable API gets a
  // named preflight (not a mute error chip), auth gets the sign-in route, and a
  // missing project gets the chooser — no work-item flow exists outside a
  // selected project. Settings stays reachable as the escape hatch.
  if (surface !== 'settings') {
    if (project.accessStatus === 'apiOffline' || project.accessStatus === 'apiError') {
      return <PreflightGate onOpenSettings={() => setSurface('settings')} />;
    }
    if (
      project.accessStatus === 'authRequired' ||
      project.accessStatus === 'authInvalid'
    ) {
      return <SignInRoute onOpenSettings={() => setSurface('settings')} />;
    }
    if (project.accessStatus === 'tenantRequired') {
      return <TenantChooser onOpenSettings={() => setSurface('settings')} />;
    }
    if (project.accessStatus === 'projectRequired' || (project.accessStatus !== 'loading' && project.selectedProjectId === null)) {
      return (
        <ProjectChooser
          onOpenSettings={() => setSurface('settings')}
          onProjectCreated={() => {
            // A created project lands on readiness, never straight into work.
            setLibrarySection('provisioning');
            setSurface('library');
          }}
        />
      );
    }
  }

  const projectName =
    project.selectedProjectName ??
    (project.selectedProjectId !== null ? `Project ${project.selectedProjectId}` : 'No project selected');
  const modelMode = session.environmentInfo?.isTestEnvironment
    ? 'Deterministic-only local alpha preview; not a live model run'
    : 'Backend-reported per run; deterministic fallback is never silent';

  const openWorkItem = (ticket: ProjectTicket | null) => {
    setActiveTicket(ticket);
    setSurface('workitem');
  };

  return (
    <div className="fl-root" data-testid="flow.shell">
      <header className="fl-appbar">
        <div className="fl-brand">
          <span className="fl-brand-mark">I</span>
          IronDev <span style={{ color: 'var(--fl-line2)' }}>/</span>{' '}
          <span className="fl-brand-proj">{projectName}</span>
        </div>
        <nav className="fl-nav" aria-label="Surfaces">
          <button
            className={surface === 'board' ? 'fl-on' : ''}
            onClick={() => setSurface('board')}
            data-testid="flow.nav.board"
          >
            Board
          </button>
          <button
            className={surface === 'workitem' ? 'fl-on' : ''}
            onClick={() => setSurface('workitem')}
            data-testid="flow.nav.workitem"
          >
            Work item
          </button>
          <button
            className={surface === 'batch' ? 'fl-on' : ''}
            onClick={() => setSurface('batch')}
            data-testid="flow.nav.batch"
          >
            Batch
          </button>
          <button
            className={surface === 'library' ? 'fl-on' : ''}
            onClick={() => setSurface('library')}
            data-testid="flow.nav.library"
          >
            Library
          </button>
          <button
            className={surface === 'settings' ? 'fl-on' : ''}
            onClick={() => setSurface('settings')}
            data-testid="flow.nav.settings"
          >
            Settings
          </button>
        </nav>
        <div className="fl-userbit">
          <span data-testid="flow.modelMode">Model mode: {modelMode}</span>
          <span>{session.apiStatus.status === 'connected' ? 'API connected' : `API ${session.apiStatus.status}`}</span>
          <span className="fl-avatar">{initials(project.userProfile?.displayName)}</span>
        </div>
      </header>

      <main className="fl-main">
        {surface === 'board' ? (
          <BoardScreen
            onOpenWorkItem={openWorkItem}
            onOpenBatch={() => setSurface('batch')}
            onOpenProvisioning={() => {
              setLibrarySection('provisioning');
              setSurface('library');
            }}
          />
        ) : null}
        {surface === 'batch' ? <BatchScreen /> : null}
        {surface === 'workitem' ? (
          <WorkItemScreen
            ticket={activeTicket}
            onTicketCreated={setActiveTicket}
            onBackToBoard={() => setSurface('board')}
            onOpenGovernanceLibrary={() => {
              setLibrarySection('governance');
              setSurface('library');
            }}
          />
        ) : null}
        {surface === 'library' ? (
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
              <div>
                <h1 className="fl-h1">Library</h1>
                <p className="fl-sub">Reference, not workflow. Read-only truth — nothing here grants authority.</p>
              </div>
              <div className="fl-nav">
                <button
                  className={librarySection === 'explorer' ? 'fl-on' : ''}
                  onClick={() => setLibrarySection('explorer')}
                  data-testid="flow.library.explorer"
                >
                  Solution explorer
                </button>
                <button
                  className={librarySection === 'governance' ? 'fl-on' : ''}
                  onClick={() => setLibrarySection('governance')}
                  data-testid="flow.library.governance"
                >
                  Governance
                </button>
                <button
                  className={librarySection === 'provisioning' ? 'fl-on' : ''}
                  onClick={() => setLibrarySection('provisioning')}
                  data-testid="flow.library.nav.provisioning"
                >
                  Provisioning
                </button>
                <button
                  className={librarySection === 'audit' ? 'fl-on' : ''}
                  onClick={() => setLibrarySection('audit')}
                  data-testid="flow.library.nav.audit"
                >
                  Audit
                </button>
                <button
                  className={librarySection === 'admin' ? 'fl-on' : ''}
                  onClick={() => setLibrarySection('admin')}
                  data-testid="flow.library.nav.admin"
                >
                  Admin
                </button>
              </div>
            </div>
            {librarySection === 'explorer' ? <SolutionExplorer /> : null}
            {librarySection === 'governance' ? <GovernanceHost /> : null}
            {librarySection === 'provisioning' ? <ProvisioningSection /> : null}
            {librarySection === 'audit' ? <AuditSection /> : null}
            {librarySection === 'admin' ? <AdminInviteSection /> : null}
          </div>
        ) : null}
        {surface === 'settings' ? <SettingsScreen /> : null}
      </main>
    </div>
  );
}
