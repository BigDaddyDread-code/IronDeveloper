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
  const phase = project.lifecyclePhase?.trim() || 'Project';

  return (
    <button
      ref={ref}
      type="button"
      className="fl-project-tile fl-project-tile--ready"
      aria-label={`Open ${name} in Workbench. ${phase}`}
      data-testid={`flow.chooser.project.${projectId}`}
      disabled={isOpening}
      onClick={onOpen}
    >
      <span className="fl-project-tile__name">{name}</span>
      <span className="fl-project-tile__path">{phase}</span>
      <span className="fl-project-tile__status" data-testid={`flow.chooser.phase.${projectId}`}>
        {isOpening ? 'Opening...' : 'Open Workbench'}
      </span>
    </button>
  );
});
