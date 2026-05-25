import type { ApiStatus } from '../api/types';
import { StatusBadge } from './StatusBadge';

interface WorkspaceHeaderProps {
  apiStatus: ApiStatus;
  projectId: number;
  isRefreshing: boolean;
  onRefresh: () => void;
}

export function WorkspaceHeader({ apiStatus, projectId, isRefreshing, onRefresh }: WorkspaceHeaderProps) {
  return (
    <header className="workspace-header" data-testid="shell.header">
      <div>
        <p className="eyebrow">IRONDEV SHELL SPIKE</p>
        <h1>IronDev</h1>
        <p className="workspace-header__subtitle">Tauri + React cockpit proof against IronDev.Api</p>
      </div>

      <div className="workspace-header__meta">
        <div className="api-status" data-testid="app.apiStatus">
          <StatusBadge status={apiStatus.status} data-testid={`api.status.${apiStatus.status}`}>
            {apiStatus.status}
          </StatusBadge>
          <span>{apiStatus.baseUrl}</span>
        </div>
        <span className="project-pill">Project {projectId}</span>
        <button className="command-button" data-testid="ticket.command.refresh" onClick={onRefresh} disabled={isRefreshing}>
          {isRefreshing ? 'Refreshing...' : 'Refresh'}
        </button>
      </div>
    </header>
  );
}
