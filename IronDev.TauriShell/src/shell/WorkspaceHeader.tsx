import { CommandBar } from '../design-system/CommandBar';
import type { WorkspaceCommand } from '../app/routes';
import { ApiStatusBadge } from '../components/ApiStatusBadge';
import type { ApiStatus, EnvironmentInfo } from '../api/types';

interface HeaderSummaryChip {
  label: string;
  testId?: string;
}

interface WorkspaceHeaderProps {
  apiStatus: ApiStatus;
  environmentInfo: EnvironmentInfo | null;
  projectId: number | null;
  projectName: string | null;
  projectStatus: 'selected' | 'missing' | 'fallback';
  workspaceLabel: string;
  workspaceSummaryChips: HeaderSummaryChip[];
  userDisplayName: string | null;
  tokenConfigured: boolean;
  tenantName: string | null;
  commands: WorkspaceCommand[];
  blockedReason: string | null;
  blockedReasonTestId?: string;
  projectLabel: string;
}

export function WorkspaceHeader({
  apiStatus,
  environmentInfo,
  projectId,
  projectName,
  projectStatus,
  workspaceLabel,
  workspaceSummaryChips,
  userDisplayName,
  tokenConfigured,
  tenantName,
  commands,
  blockedReason,
  blockedReasonTestId,
  projectLabel
}: WorkspaceHeaderProps) {
  const summaryChips: HeaderSummaryChip[] = [
    ...workspaceSummaryChips
  ];

  const title =
    projectStatus === 'selected'
      ? projectName ?? `Project ${projectId}`
      : projectStatus === 'fallback'
        ? `Fallback project ${projectId}`
        : 'Project required';

  return (
    <header className="workspace-header" data-testid="app.header">
      <div className="workspace-header__identity">
        <p className="eyebrow">IronDev</p>
        <h1>{workspaceLabel}</h1>
        <p className="workspace-header__subtitle">
          Project-aware software work, sandbox execution, and traceable review
          {title ? ` - ${title}` : ''}
        </p>
        <div className="workspace-header__summary">
          {environmentInfo ? (
            <span
              className={`metadata-chip ${environmentInfo.isTestEnvironment ? 'metadata-chip--test' : ''}`.trim()}
              data-testid="environment.badge"
              title={`Database: ${environmentInfo.database || 'unknown'}`}
            >
              {environmentInfo.environment}
            </span>
          ) : null}
          {summaryChips.map((chip, index) => (
            <span key={`${chip.label}-${index}`} className="metadata-chip" data-testid={chip.testId}>
              {chip.label}
            </span>
          ))}
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
        <CommandBar commands={commands} />
        {blockedReason ? (
          <span className="command-blocked-reason" data-testid={blockedReasonTestId}>
            {blockedReason}
          </span>
        ) : null}
      </div>
    </header>
  );
}
