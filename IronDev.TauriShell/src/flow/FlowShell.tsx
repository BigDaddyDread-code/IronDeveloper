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
import { SolutionExplorer } from './library/SolutionExplorer';
import { SettingsScreen } from './settings/SettingsScreen';
import { WorkItemScreen } from './workitem/WorkItemScreen';

type LibrarySection = 'explorer' | 'governance';

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

  const needsSignIn =
    surface !== 'settings' && (project.accessStatus === 'authRequired' || project.accessStatus === 'authInvalid');

  if (needsSignIn) {
    return <SignInRoute />;
  }

  const projectName =
    project.selectedProjectName ??
    (project.selectedProjectId !== null ? `Project ${project.selectedProjectId}` : 'No project selected');

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
          <span>{session.apiStatus.status === 'connected' ? 'API connected' : `API ${session.apiStatus.status}`}</span>
          <span className="fl-avatar">{initials(project.userProfile?.displayName)}</span>
        </div>
      </header>

      <main className="fl-main">
        {surface === 'board' ? <BoardScreen onOpenWorkItem={openWorkItem} onOpenBatch={() => setSurface('batch')} /> : null}
        {surface === 'batch' ? <BatchScreen /> : null}
        {surface === 'workitem' ? (
          <WorkItemScreen
            ticket={activeTicket}
            onTicketCreated={setActiveTicket}
            onBackToBoard={() => setSurface('board')}
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
              </div>
            </div>
            {librarySection === 'explorer' ? <SolutionExplorer /> : <GovernanceHost />}
          </div>
        ) : null}
        {surface === 'settings' ? <SettingsScreen /> : null}
      </main>
    </div>
  );
}
