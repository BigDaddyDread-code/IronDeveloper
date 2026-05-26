import { useCallback, useMemo, useState } from 'react';
import { AppShell } from '../components/AppShell';
import { WorkspaceRoute, WorkspaceRouteMeta, routeForId, workspaceRoutes } from '../app/routes';
import { useProjectContext } from '../state/useProjectContext';
import { useSessionContext } from '../state/useSessionContext';
import { RunReportsRoute } from '../features/runReports/RunReportsRoute';
import { PromotionReviewRoute } from '../features/runReports/PromotionReviewRoute';
import { TicketsRoute } from '../features/tickets/TicketsRoute';
import { WorkspaceHeader } from './WorkspaceHeader';
import { WorkspaceNav } from './WorkspaceNav';
import { StatusFooter } from './StatusFooter';

const defaultWorkspaceRouteMeta: WorkspaceRouteMeta = {
  workspaceCommands: [],
  workspaceBlockReason: null,
  workspaceSummaryChips: [],
  blockReasonTestId: undefined
};

export function IronDevShell() {
  const [activeRouteId, setActiveRouteId] = useState<WorkspaceRoute['id']>(() => workspaceRoutes[0]?.id ?? 'tickets');
  const [routeMeta, setRouteMeta] = useState<WorkspaceRouteMeta>(defaultWorkspaceRouteMeta);
  const session = useSessionContext();
  const project = useProjectContext();

  const onRouteReady = useCallback(
    (next: WorkspaceRouteMeta) => {
      setRouteMeta((previous) => (sameRouteMeta(previous, next) ? previous : next));
    },
    []
  );

  const onRouteChange = useCallback((routeId: WorkspaceRoute['id']) => {
    setActiveRouteId(routeId);
    setRouteMeta(defaultWorkspaceRouteMeta);
  }, []);

  const activeRoute = useMemo(() => routeForId(activeRouteId), [activeRouteId]);
  const routeWorkspace = useMemo(() => {
    switch (activeRoute.id) {
      case 'run-reports':
        return <RunReportsRoute route={activeRoute} onRouteReady={onRouteReady} />;
      case 'promotion-review':
        return <PromotionReviewRoute route={activeRoute} onRouteReady={onRouteReady} />;
      default:
        return <TicketsRoute route={activeRoute} onRouteReady={onRouteReady} />;
    }
  }, [activeRoute, onRouteReady]);

  const projectStatus =
    project.projectSelectionMode === 'api'
      ? 'selected'
      : project.projectSelectionMode === 'fallback-config'
        ? 'fallback'
        : 'missing';

  const projectLabel =
    projectStatus === 'selected'
      ? project.selectedProjectName ?? `Project ${project.selectedProjectId}`
      : projectStatus === 'fallback'
        ? `Fallback project ${project.selectedProjectId}`
        : 'Project required';

  const headerSummary = useMemo<WorkspaceRouteMeta['workspaceSummaryChips']>(() => {
    return routeMeta.workspaceSummaryChips.length > 0 ? routeMeta.workspaceSummaryChips : [{ label: `${activeRoute.label} context` }];
  }, [activeRoute.label, routeMeta.workspaceSummaryChips]);

  const safeCommands = routeMeta.workspaceCommands ?? [];

  return (
    <AppShell
      header={
        <WorkspaceHeader
          apiStatus={session.apiStatus}
          projectId={project.selectedProjectId}
          projectName={project.selectedProjectName}
          projectStatus={projectStatus}
          workspaceLabel={activeRoute.label}
          workspaceSummaryChips={[
            { label: `Route ${activeRoute.label} (${activeRoute.maturity})`, testId: 'workspace.summary.route' },
            ...headerSummary
          ]}
          userDisplayName={project.userProfile?.displayName ?? null}
          tokenConfigured={session.tokenConfigured}
          tenantName={project.tenants.find((tenant) => tenant.id === project.selectedTenantId)?.name ?? null}
          commands={safeCommands}
          blockedReason={routeMeta.workspaceBlockReason}
          blockedReasonTestId={routeMeta.blockReasonTestId}
          projectLabel={projectLabel}
          routeParity={activeRoute.parityStatus}
          routeMaturity={activeRoute.maturity}
        />
      }
      navigation={
        <WorkspaceNav
          activeRouteId={activeRouteId}
          onSelect={onRouteChange}
        />
      }
      footer={<StatusFooter apiStatus={session.apiStatus} />}
    >
      {routeWorkspace}
    </AppShell>
  );
}

function sameRouteMeta(left: WorkspaceRouteMeta, right: WorkspaceRouteMeta) {
  return (
    left.workspaceBlockReason === right.workspaceBlockReason &&
    left.blockReasonTestId === right.blockReasonTestId &&
    sameSummaryChips(left.workspaceSummaryChips, right.workspaceSummaryChips) &&
    sameRouteCommands(left.workspaceCommands, right.workspaceCommands)
  );
}

function sameSummaryChips(left: WorkspaceRouteMeta['workspaceSummaryChips'], right: WorkspaceRouteMeta['workspaceSummaryChips']) {
  if (left.length !== right.length) {
    return false;
  }

  for (let i = 0; i < left.length; i += 1) {
    if (left[i].label !== right[i].label || left[i].testId !== right[i].testId) {
      return false;
    }
  }

  return true;
}

function sameRouteCommands(
  left: WorkspaceRouteMeta['workspaceCommands'],
  right: WorkspaceRouteMeta['workspaceCommands']
) {
  if (left.length !== right.length) {
    return false;
  }

  for (let i = 0; i < left.length; i += 1) {
    const next = right[i];
    const current = left[i];

    if (
      current.id !== next.id ||
      current.label !== next.label ||
      current.intent !== next.intent ||
      current.shortcut !== next.shortcut ||
      current.disabled !== next.disabled ||
      current.busy !== next.busy ||
      current.disabledReason !== next.disabledReason ||
      current.testId !== next.testId
    ) {
      return false;
    }
  }

  return true;
}
