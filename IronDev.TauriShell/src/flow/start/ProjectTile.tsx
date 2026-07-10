import { forwardRef } from 'react';
import type { ProjectSummary } from '../../api/types';
import type { ProjectTileReadiness } from './projectEntryTypes';
import { readinessLabel } from './projectEntryTypes';

interface ProjectTileProps {
  project: ProjectSummary;
  readiness: ProjectTileReadiness | undefined;
  isOpening: boolean;
  onOpen: () => void;
}

function statusClass(readiness: ProjectTileReadiness | undefined): string {
  if (readiness === undefined || readiness.kind === 'loading') {
    return 'fl-project-tile--unavailable';
  }

  if (readiness.kind === 'error') {
    return 'fl-project-tile--unavailable';
  }

  return readiness.readiness.isReady ? 'fl-project-tile--ready' : 'fl-project-tile--setup';
}

export const ProjectTile = forwardRef<HTMLButtonElement, ProjectTileProps>(function ProjectTile(
  { project, readiness, isOpening, onOpen },
  ref
) {
  const projectId = project.id ?? -1;
  const name = project.name ?? `Project ${projectId}`;
  const label = readinessLabel(readiness);
  const blockedCount =
    readiness?.kind === 'loaded' && !readiness.readiness.isReady ? readiness.readiness.blockedStates.length : 0;

  return (
    <button
      ref={ref}
      type="button"
      className={`fl-project-tile ${statusClass(readiness)}`}
      aria-label={`Open ${name}. ${label}`}
      aria-describedby={`flow-project-path-${projectId}`}
      data-testid={`flow.chooser.project.${projectId}`}
      disabled={isOpening}
      onClick={onOpen}
    >
      <span className="fl-project-tile__name">{name}</span>
      <span id={`flow-project-path-${projectId}`} className="fl-project-tile__path" title={project.localPath ?? undefined}>
        {project.localPath ?? 'No repository path set'}
      </span>
      <span className="fl-project-tile__status" data-testid={`flow.chooser.readiness.${projectId}`} aria-live="polite">
        {isOpening ? 'Opening...' : label}
      </span>
      {readiness?.kind === 'error' ? <span className="fl-project-tile__hint">Open setup to retry.</span> : null}
      {blockedCount > 0 ? (
        <span className="fl-project-tile__hint">
          {blockedCount} item{blockedCount === 1 ? '' : 's'}
        </span>
      ) : null}
    </button>
  );
});
