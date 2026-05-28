import type { ProjectSummary } from '../api/types';
import { CommandButton } from './CommandButton';

interface ProjectSelectorProps {
  projects: ProjectSummary[];
  selectedProjectId: number | null;
  isBusy?: boolean;
  onSelectProject: (projectId: number) => void;
}

export function ProjectSelector({
  projects,
  selectedProjectId,
  isBusy = false,
  onSelectProject
}: ProjectSelectorProps) {
  const selectableProjects = projects.filter((project) => Number.isFinite(project.id));

  return (
    <section className="project-selector" data-testid="project.selector">
      {selectableProjects.length === 0 ? (
        <div className="project-selector__empty" data-testid="project.selector.empty">
          <h3>No projects found</h3>
          <p>Create or import a project. For LocalTest, check the seed data and rerun the reset script.</p>
        </div>
      ) : (
        <div className="project-selector__list">
          {selectableProjects.map((project) => {
            const projectId = project.id ?? 0;
            const isSelected = projectId === selectedProjectId;
            return (
              <article
                key={projectId}
                className={`project-selector__item ${isSelected ? 'project-selector__item--selected' : ''}`.trim()}
                data-testid="project.option"
              >
                <div>
                  <p className="eyebrow">Project {projectId}</p>
                  <h3>{project.name ?? `Project ${projectId}`}</h3>
                  {project.description ? <p>{project.description}</p> : null}
                  {project.localPath ? <code>{project.localPath}</code> : null}
                </div>
                <CommandButton
                  type="button"
                  variant={isSelected ? 'secondary' : 'primary'}
                  disabled={isBusy || isSelected}
                  onClick={() => onSelectProject(projectId)}
                  testId={`project.option.select.${projectId}`}
                >
                  {isSelected ? 'Selected' : 'Select Project'}
                </CommandButton>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
