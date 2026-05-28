import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { Surface } from '../../design-system/Surface';
import { MetadataGrid } from '../../design-system/metadata/MetadataGrid';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface HomeRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function HomeRoute({ route, onRouteReady }: HomeRouteProps) {
  const session = useSessionContext();
  const project = useProjectContext();

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

  return (
    <main className="product-workspace product-workspace--home" data-testid="home.workspace" aria-label={route.label}>
      <section className="workspace-page-heading">
        <p className="eyebrow">Project command centre</p>
        <h2>Home</h2>
        <p>Start here for the selected project, system readiness, and the next useful action.</p>
      </section>

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
          <ul className="product-action-list">
            <li>Review open work in Tickets.</li>
            <li>Ask Chat for current risks and context.</li>
            <li>Use Build only when the work is ready for sandbox execution.</li>
          </ul>
        </Surface>
      </div>
    </main>
  );
}
