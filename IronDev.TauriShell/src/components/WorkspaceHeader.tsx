import type { ApiStatus } from '../api/types';
import { ApiStatusBadge } from './ApiStatusBadge';
import { CommandButton } from './CommandButton';

interface WorkspaceHeaderProps {
  apiStatus: ApiStatus;
  projectId: number | null;
  projectName: string | null;
  projectStatus: 'selected' | 'missing' | 'fallback';
  ticketCount: number;
  tokenConfigured: boolean;
  userDisplayName: string | null;
  tenantName: string | null;
  isRefreshing: boolean;
  createBlockedReason: string | null;
  onRefresh: () => void;
  onCreateTicket: () => void;
}

export function WorkspaceHeader({
  apiStatus,
  projectId,
  projectName,
  projectStatus,
  ticketCount,
  tokenConfigured,
  userDisplayName,
  tenantName,
  isRefreshing,
  createBlockedReason,
  onRefresh,
  onCreateTicket
}: WorkspaceHeaderProps) {
  const projectLabel =
    projectStatus === 'selected'
      ? projectName ?? `Project ${projectId}`
      : projectStatus === 'fallback'
        ? `Fallback project ${projectId}`
        : 'Project required';

  return (
    <header className="workspace-header" data-testid="app.header">
      <div className="workspace-header__identity">
        <p className="eyebrow">IRONDEV WORKSPACE</p>
        <h1>IronDev</h1>
        <p className="workspace-header__subtitle">Governed AI workstream control.</p>
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
          data-testid={
            projectStatus === 'selected'
              ? 'project.status.selected'
              : projectStatus === 'fallback'
                ? 'project.status.fallback'
                : 'project.status.missing'
          }
        >
          {projectLabel}
        </span>
        <CommandButton testId="ticket.command.refresh" onClick={onRefresh} disabled={isRefreshing}>
          {isRefreshing ? 'Refreshing...' : 'Refresh'}
        </CommandButton>
        <CommandButton
          testId="ticket.command.create"
          variant="primary"
          onClick={onCreateTicket}
          disabled={Boolean(createBlockedReason) || isRefreshing}
          title={createBlockedReason ?? undefined}
        >
          Create Ticket
        </CommandButton>
        {createBlockedReason ? (
          <span className="command-blocked-reason" data-testid="ticket.create.blockedReason">
            {createBlockedReason}
          </span>
        ) : null}
      </div>
    </header>
  );
}
