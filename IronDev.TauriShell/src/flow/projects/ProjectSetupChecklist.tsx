import type { ProjectSetupCheckModel } from './projectSetupModel';

interface ProjectSetupChecklistProps {
  checks: ProjectSetupCheckModel[];
  currentCheckCode?: string | null;
}

const statusMark: Record<ProjectSetupCheckModel['status'], string> = {
  complete: 'OK',
  attention: '!',
  checking: '...',
  unavailable: '!'
};

export function ProjectSetupChecklist({ checks, currentCheckCode }: ProjectSetupChecklistProps) {
  return (
    <section className="fl-project-setup__checklist" aria-labelledby="project-setup-list-title">
      <h2 id="project-setup-list-title">Setup</h2>
      <div className="fl-project-setup__rows">
        {checks.map((check, index) => (
          <div
            className={`fl-project-setup__row fl-project-setup__row--${check.status}`}
            data-testid={`flow.projectSetup.row.${check.code}`}
            key={`${check.code}-${index}`}
          >
            <span className="fl-project-setup__row-mark" aria-hidden="true">
              {statusMark[check.status]}
            </span>
            <span className="fl-project-setup__row-copy">
              <strong>{check.label}</strong>
              <span>
                {check.statusLabel}
                {check.code === currentCheckCode ? ' - current step' : ''}
              </span>
            </span>
          </div>
        ))}
      </div>
    </section>
  );
}
