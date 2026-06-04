import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { CommandButton } from '../../components/CommandButton';
import { ProjectSelector } from '../../components/ProjectSelector';
import { Surface } from '../../design-system/Surface';
import { MetadataGrid } from '../../design-system/metadata/MetadataGrid';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { useWorkspaceNavigation } from '../../state/useWorkspaceNavigation';

interface HomeRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function HomeRoute({ route, onRouteReady }: HomeRouteProps) {
  const session = useSessionContext();
  const project = useProjectContext();
  const navigation = useWorkspaceNavigation();

  const projectLabel = project.selectedProjectName ?? (project.selectedProjectId ? `Project ${project.selectedProjectId}` : 'Project required');
  const readinessLabel =
    session.apiStatus.status === 'connected'
      ? project.selectedProjectId
        ? 'Ready'
        : 'Project selection required'
      : 'API connection required';

  const routeSummary = useMemo(
    () => [
      { label: projectLabel, testId: 'home.summary.project' },
      { label: readinessLabel, testId: 'home.summary.readiness' }
    ],
    [projectLabel, readinessLabel]
  );

  useEffect(() => {
    onRouteReady?.({
      workspaceCommands: [],
      workspaceBlockReason: null,
      workspaceSummaryChips: routeSummary
    });
  }, [onRouteReady, routeSummary]);

  const projectActionBlockedReason = project.selectedProjectId ? null : 'Select a project before starting the primary flow.';

  return (
    <main className="product-workspace product-workspace--home" data-testid="home.workspace" aria-label={route.label}>
      <div className="product-grid product-grid--home">
        <Surface testId="home.projectSummary">
          <div className="section-heading">
            <p className="eyebrow">Selected project</p>
            <h3>{projectLabel}</h3>
          </div>
          <MetadataGrid
            items={[
              { label: 'API', value: session.apiStatus.status },
              { label: 'Environment', value: session.environmentInfo?.environment ?? 'Unknown' },
              { label: 'Project source', value: project.projectSelectionMode },
              { label: 'Tenant', value: project.tenants.find((tenant) => tenant.id === project.selectedTenantId)?.name ?? 'Not selected' }
            ]}
          />
          {!project.selectedProjectId && session.tokenConfigured ? (
            <div className="home-project-picker" data-testid="home.projectSelector">
              <div>
                <h3>Select a project to continue</h3>
                <p className="state-muted">Project-scoped workspaces use this selected project for Chat, Build, Tickets, Knowledge, and Runs.</p>
              </div>
              <ProjectSelector
                projects={project.projects}
                selectedProjectId={project.selectedProjectId}
                isBusy={project.isRefreshing}
                onSelectProject={project.selectProjectContext}
              />
            </div>
          ) : null}
        </Surface>

        <Surface testId="home.systemReadiness">
          <div className="section-heading">
            <p className="eyebrow">System readiness</p>
            <h3>{readinessLabel}</h3>
          </div>
          <p className="state-muted">
            {project.selectedProjectId
              ? 'Open Tickets for planned work, Build for sandbox execution, or Chat to ask a project-aware question.'
              : 'Select a project before starting project-aware work.'}
          </p>
        </Surface>

        <Surface testId="home.suggestedActions">
          <div className="section-heading">
            <p className="eyebrow">Suggested next actions</p>
            <h3>Move work forward safely</h3>
          </div>
          <div className="home-flow-actions" data-testid="home.flowActions">
            <CommandButton
              type="button"
              variant="primary"
              testId="home.action.reviewProjectState"
              disabled={Boolean(projectActionBlockedReason)}
              title={projectActionBlockedReason ?? undefined}
              onClick={() => navigation.navigateToWorkspace('chat')}
            >
              Open Chat
            </CommandButton>
            <CommandButton
              type="button"
              variant="secondary"
              testId="home.action.continueBuild"
              disabled={Boolean(projectActionBlockedReason)}
              title={projectActionBlockedReason ?? undefined}
              onClick={() => navigation.navigateToWorkspace('build')}
            >
              Open Build
            </CommandButton>
            <CommandButton
              type="button"
              variant="secondary"
              testId="home.action.openTickets"
              disabled={Boolean(projectActionBlockedReason)}
              title={projectActionBlockedReason ?? undefined}
              onClick={() => navigation.navigateToWorkspace('tickets')}
            >
              Open Tickets
            </CommandButton>
          </div>
          <p className="state-muted" data-testid="home.flowActions.hint">
            {projectActionBlockedReason ?? 'Start with Chat, continue the discussion into Build, then review the sandbox evidence before approval.'}
          </p>
        </Surface>
      </div>
    </main>
  );
}
