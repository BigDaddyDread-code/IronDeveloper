import { forwardRef } from 'react';
import type { ProjectSummary } from '../../api/types';

interface ProjectTileProps {
  project: ProjectSummary;
  isOpening: boolean;
  onOpen: () => void;
}

export const ProjectTile = forwardRef<HTMLButtonElement, ProjectTileProps>(function ProjectTile(
  { project, isOpening, onOpen },
  ref
) {
  const projectId = project.id ?? -1;
  const name = project.name ?? `Project ${projectId}`;
  const phase = project.lifecyclePhase?.trim() || 'Legacy project';
  const readiness = formatReadiness(project.executionReadiness);

  return (
    <button
      ref={ref}
      type="button"
      className="fl-project-tile"
      aria-label={`Open ${name} in Workbench. Lifecycle ${phase}. ${readiness}`}
      data-testid={`flow.chooser.project.${projectId}`}
      disabled={isOpening}
      onClick={onOpen}
    >
      <span className="fl-project-tile__name">{name}</span>
      <span className="fl-project-tile__path" data-testid={`flow.chooser.phase.${projectId}`}>Lifecycle: {phase}</span>
      <span className="fl-project-tile__path" data-testid={`flow.chooser.executionReadiness.${projectId}`}>{readiness}</span>
      <span className="fl-project-tile__status" data-testid={`flow.chooser.open.${projectId}`}>
        {isOpening ? 'Opening...' : 'Open Workbench'}
      </span>
    </button>
  );
});

function formatReadiness(value: string | null | undefined) {
  switch (value) {
    case 'Ready': return 'Execution: ready';
    case 'ValidationRequired': return 'Execution: validation required';
    default: return 'Execution: not configured';
  }
}
