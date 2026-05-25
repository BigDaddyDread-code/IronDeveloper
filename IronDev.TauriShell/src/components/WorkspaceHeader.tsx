import type { ApiStatus } from '../api/types';
import { ApiStatusBadge } from './ApiStatusBadge';
import { CommandButton } from './CommandButton';

interface WorkspaceHeaderProps {
  apiStatus: ApiStatus;
  projectId: number;
  projectName: string | null;
  projectStatus: 'selected' | 'missing';
  projectSelectionMode: 'api' | 'fallback-config';
  ticketCount: number;
  tokenConfigured: boolean;
  userDisplayName: string | null;
  tenantName: string | null;
  isRefreshing: boolean;
  onRefresh: () => void;
}

export function WorkspaceHeader({
  apiStatus,
  projectId,
  projectName,
  projectStatus,
  projectSelectionMode,
  ticketCount,
  tokenConfigured,
  userDisplayName,
  tenantName,
  isRefreshing,
  onRefresh
}: WorkspaceHeaderProps) {
  return (
    <header className="workspace-header" data-testid="app.header">
      <div className="workspace-header__identity">
        <p className="eyebrow">IRONDEV COCKPIT</p>
        <h1>IronDev</h1>
        <p className="workspace-header__subtitle">Tauri + React shell proving the API-backed cockpit model.</p>
        <div className="workspace-header__summary">
          <span className="metadata-chip" data-testid="tickets.header">Workspace Tickets</span>
          <span className="metadata-chip">Tickets {ticketCount}</span>
          <span className="metadata-chip">{tokenConfigured ? 'Token configured' : 'Token missing'}</span>
          {userDisplayName ? <span className="metadata-chip">{userDisplayName}</span> : null}
          {tenantName ? <span className="metadata-chip">{tenantName}</span> : null}
        </div>
      </div>

      <div className="workspace-header__meta">
        <div className="api-status" data-testid="app.apiStatus">
          <ApiStatusBadge status={apiStatus.status} />
          <span>{apiStatus.baseUrl}</span>
        </div>
        <span
          className="project-pill"
          data-testid={projectStatus === 'selected' ? 'project.status.selected' : 'project.status.missing'}
        >
          {projectStatus === 'selected'
            ? `${projectName ?? `Project ${projectId}`}${projectSelectionMode === 'fallback-config' ? ' (fallback)' : ''}`
            : 'Project required'}
        </span>
        <CommandButton testId="ticket.command.refresh" onClick={onRefresh} disabled={isRefreshing}>
          {isRefreshing ? 'Refreshing...' : 'Refresh'}
        </CommandButton>
      </div>
    </header>
  );
}
