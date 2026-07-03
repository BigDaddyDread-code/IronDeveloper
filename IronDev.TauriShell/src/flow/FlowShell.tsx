import { useState } from 'react';
import './flow.css';
import type { ProjectTicket } from '../api/types';
import { SignInRoute } from '../features/auth/SignInRoute';
import { useProjectContext } from '../state/useProjectContext';
import { useSessionContext } from '../state/useSessionContext';
import { BoardScreen } from './board/BoardScreen';
import { FlowSurface } from './flowTypes';
import { SettingsScreen } from './settings/SettingsScreen';
import { WorkItemScreen } from './workitem/WorkItemScreen';

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

  const [surface, setSurface] = useState<FlowSurface>('board');
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
    <div className="fl-root">
      <header className="fl-appbar">
        <div className="fl-brand">
          <span className="fl-brand-mark">I</span>
          IronDev <span style={{ color: 'var(--fl-line2)' }}>/</span>{' '}
          <span className="fl-brand-proj">{projectName}</span>
        </div>
        <nav className="fl-nav" aria-label="Surfaces">
          <button className={surface === 'board' ? 'fl-on' : ''} onClick={() => setSurface('board')}>
            Board
          </button>
          <button className={surface === 'workitem' ? 'fl-on' : ''} onClick={() => setSurface('workitem')}>
            Work item
          </button>
          <button className={surface === 'library' ? 'fl-on' : ''} onClick={() => setSurface('library')}>
            Library
          </button>
          <button className={surface === 'settings' ? 'fl-on' : ''} onClick={() => setSurface('settings')}>
            Settings
          </button>
        </nav>
        <div className="fl-userbit">
          <span>{session.apiStatus.status === 'connected' ? 'API connected' : `API ${session.apiStatus.status}`}</span>
          <span className="fl-avatar">{initials(project.userProfile?.displayName)}</span>
        </div>
      </header>

      <main className="fl-main">
        {surface === 'board' ? <BoardScreen onOpenWorkItem={openWorkItem} /> : null}
        {surface === 'workitem' ? (
          <WorkItemScreen
            ticket={activeTicket}
            onTicketCreated={setActiveTicket}
            onBackToBoard={() => setSurface('board')}
          />
        ) : null}
        {surface === 'library' ? (
          <div>
            <h1 className="fl-h1">Library</h1>
            <p className="fl-sub">Reference, not workflow. The solution explorer and governance viewers re-home here.</p>
            <div className="fl-panel-box">
              <p className="fl-plabel">Coming next</p>
              <div className="fl-chips">
                <span className="fl-chip">Solution explorer — needs the code-index list endpoint</span>
                <span className="fl-chip">ADRs and standards</span>
                <span className="fl-chip">Governance timeline and the 17 existing viewers</span>
              </div>
            </div>
          </div>
        ) : null}
        {surface === 'settings' ? <SettingsScreen /> : null}
      </main>
    </div>
  );
}
