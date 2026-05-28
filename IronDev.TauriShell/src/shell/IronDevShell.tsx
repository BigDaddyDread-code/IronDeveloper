import { useCallback, useMemo, useState } from 'react';
import { AppShell } from '../components/AppShell';
import { WorkspaceRoute, WorkspaceRouteMeta, routeForId, workspaceRoutes } from '../app/routes';
import { useProjectContext } from '../state/useProjectContext';
import { useSessionContext } from '../state/useSessionContext';
import { useWorkspaceNavigation } from '../state/useWorkspaceNavigation';
import { RunReportsRoute } from '../features/runReports/RunReportsRoute';
import { TicketsRoute } from '../features/tickets/TicketsRoute';
import { ChatToBuildPage } from '../features/chatToBuild/ChatToBuildPage';
import { HomeRoute } from '../features/home/HomeRoute';
import { KnowledgeRoute } from '../features/knowledge/KnowledgeRoute';
import { SettingsRoute } from '../features/settings/SettingsRoute';
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
  const navigation = useWorkspaceNavigation();
  const [routeMeta, setRouteMeta] = useState<WorkspaceRouteMeta>(defaultWorkspaceRouteMeta);
  const session = useSessionContext();
  const project = useProjectContext();

  const onRouteReady = useCallback(
    (next: WorkspaceRouteMeta) => {
      setRouteMeta((previous) => (sameRouteMeta(previous, next) ? previous : next));
    },
    []
  );

  const onWorkspaceNavigate = useCallback((routeId: WorkspaceRoute['id']) => {
    navigation.navigateToWorkspace(routeId);
    setRouteMeta(defaultWorkspaceRouteMeta);
  }, [navigation]);

  const activeRoute = useMemo(() => routeForId(navigation.activeRouteId), [navigation.activeRouteId]);
  const routeWorkspace = useMemo(() => {
    switch (activeRoute.id) {
      case 'home':
        return <HomeRoute route={activeRoute} onRouteReady={onRouteReady} />;
      case 'chat':
        return <ChatToBuildPage route={activeRoute} surface="chat" onRouteReady={onRouteReady} />;
      case 'build':
        return <ChatToBuildPage route={activeRoute} surface="build" onRouteReady={onRouteReady} />;
      case 'knowledge':
        return <KnowledgeRoute route={activeRoute} onRouteReady={onRouteReady} />;
      case 'runs':
        return <RunReportsRoute route={activeRoute} onRouteReady={onRouteReady} />;
      case 'settings':
        return <SettingsRoute route={activeRoute} onRouteReady={onRouteReady} />;
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

  const safeCommands = routeMeta.workspaceCommands ?? [];

  return (
    <AppShell
      header={
        <WorkspaceHeader
          apiStatus={session.apiStatus}
          environmentInfo={session.environmentInfo}
          projectId={project.selectedProjectId}
          projectName={project.selectedProjectName}
          projectStatus={projectStatus}
          workspaceLabel={activeRoute.label}
          workspaceSummaryChips={routeMeta.workspaceSummaryChips}
          userDisplayName={project.userProfile?.displayName ?? null}
          tokenConfigured={session.tokenConfigured}
          tenantName={project.tenants.find((tenant) => tenant.id === project.selectedTenantId)?.name ?? null}
          commands={safeCommands}
          blockedReason={routeMeta.workspaceBlockReason}
          blockedReasonTestId={routeMeta.blockReasonTestId}
          projectLabel={projectLabel}
        />
      }
      navigation={
        <WorkspaceNav
          activeRouteId={activeRoute.id}
          onSelect={onWorkspaceNavigate}
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
