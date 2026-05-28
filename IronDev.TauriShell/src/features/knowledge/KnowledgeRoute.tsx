import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { Surface } from '../../design-system/Surface';
import { useProjectContext } from '../../state/useProjectContext';

interface KnowledgeRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function KnowledgeRoute({ route, onRouteReady }: KnowledgeRouteProps) {
  const project = useProjectContext();
  const routeSummary = useMemo(
    () => [{ label: project.selectedProjectName ?? (project.selectedProjectId ? `Project ${project.selectedProjectId}` : 'Project required'), testId: 'knowledge.summary.project' }],
    [project.selectedProjectId, project.selectedProjectName]
  );

  useEffect(() => {
    onRouteReady?.({
      workspaceCommands: [],
      workspaceBlockReason: null,
      workspaceSummaryChips: routeSummary
    });
  }, [onRouteReady, routeSummary]);

  return (
    <main className="product-workspace" data-testid="knowledge.workspace" aria-label={route.label}>
      <section className="workspace-page-heading">
        <p className="eyebrow">Project knowledge</p>
        <h2>Knowledge</h2>
        <p>Documents, saved discussions, plans, decisions, indexed knowledge, and retrieval status live here.</p>
      </section>
      <div className="product-grid">
        <Surface testId="knowledge.documents">
          <div className="section-heading">
            <p className="eyebrow">Documents</p>
            <h3>Project documents and discussions</h3>
          </div>
          <p className="state-muted">Document browsing and editing will move into this workspace as the knowledge surface is built out.</p>
        </Surface>
        <Surface testId="knowledge.decisions">
          <div className="section-heading">
            <p className="eyebrow">Decisions</p>
            <h3>Accepted project decisions</h3>
          </div>
          <p className="state-muted">Decisions remain part of project knowledge until they need their own dedicated workspace.</p>
        </Surface>
        <Surface testId="knowledge.retrieval">
          <div className="section-heading">
            <p className="eyebrow">Retrieval</p>
            <h3>Search and index status</h3>
          </div>
          <p className="state-muted">Search, reindex, and retrieval diagnostics will be wired here without changing the core workflow.</p>
        </Surface>
      </div>
    </main>
  );
}
