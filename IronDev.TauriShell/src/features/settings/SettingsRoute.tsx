import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { Surface } from '../../design-system/Surface';
import { MetadataGrid } from '../../design-system/metadata/MetadataGrid';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

interface SettingsRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function SettingsRoute({ route, onRouteReady }: SettingsRouteProps) {
  const session = useSessionContext();
  const project = useProjectContext();

  const routeSummary = useMemo(
    () => [
      { label: session.environmentInfo?.environment ?? 'Environment unknown', testId: 'settings.summary.environment' },
      { label: session.apiStatus.status, testId: 'settings.summary.api' }
    ],
    [session.apiStatus.status, session.environmentInfo?.environment]
  );

  useEffect(() => {
    onRouteReady?.({
      workspaceCommands: [],
      workspaceBlockReason: null,
      workspaceSummaryChips: routeSummary
    });
  }, [onRouteReady, routeSummary]);

  return (
    <main className="product-workspace" data-testid="settings.workspace" aria-label={route.label}>
      <section className="workspace-page-heading">
        <p className="eyebrow">Environment and services</p>
        <h2>Settings</h2>
        <p>Verify the local API, project path, service health, model profile state, and test environment state.</p>
      </section>
      <div className="product-grid">
        <Surface testId="settings.api">
          <div className="section-heading">
            <p className="eyebrow">API</p>
            <h3>Connection</h3>
          </div>
          <MetadataGrid
            items={[
              { label: 'Base URL', value: session.apiStatus.baseUrl },
              { label: 'Status', value: session.apiStatus.status },
              { label: 'Token', value: session.tokenConfigured ? 'Configured' : 'Missing' }
            ]}
          />
        </Surface>
        <Surface testId="settings.environment">
          <div className="section-heading">
            <p className="eyebrow">Environment</p>
            <h3>{session.environmentInfo?.environment ?? 'Unknown'}</h3>
          </div>
          <MetadataGrid
            items={[
              { label: 'Database', value: session.environmentInfo?.database ?? 'Unknown' },
              { label: 'Workspace root', value: session.environmentInfo?.workspaceRoot ?? 'Unknown' },
              { label: 'Test mode', value: session.environmentInfo?.isTestEnvironment ? 'Yes' : 'No' }
            ]}
          />
        </Surface>
        <Surface testId="settings.project">
          <div className="section-heading">
            <p className="eyebrow">Project</p>
            <h3>{project.selectedProjectName ?? 'Project required'}</h3>
          </div>
          <MetadataGrid
            items={[
              { label: 'Project id', value: project.selectedProjectId ?? 'Not selected' },
              { label: 'Selection mode', value: project.projectSelectionMode },
              { label: 'Access', value: project.accessStatus }
            ]}
          />
        </Surface>
      </div>
    </main>
  );
}
