interface ProjectSetupHeaderProps {
  projectName: string;
  repositoryPath: string;
  statusLabel: string;
  statusTone: 'ready' | 'attention' | 'unavailable';
  onBackToProjects: () => void;
}

export function ProjectSetupHeader({
  projectName,
  repositoryPath,
  statusLabel,
  statusTone,
  onBackToProjects
}: ProjectSetupHeaderProps) {
  return (
    <header className="fl-project-setup__header">
      <button className="fl-btn fl-project-setup__back" type="button" onClick={onBackToProjects}>
        Back to Projects
      </button>
      <div className="fl-project-setup__identity">
        <p className="fl-plabel">IronDev / {projectName}</p>
        <h1 className="fl-h1">Project setup</h1>
        <p className="fl-project-setup__path" title={repositoryPath}>
          {repositoryPath || 'No repository path configured'}
        </p>
      </div>
      <span className={`fl-project-setup__status fl-project-setup__status--${statusTone}`}>
        {statusLabel}
      </span>
    </header>
  );
}
